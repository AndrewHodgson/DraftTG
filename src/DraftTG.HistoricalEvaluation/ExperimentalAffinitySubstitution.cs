using System.IO.Compression;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation;

public sealed record ResearchAffinityState(double Fit,double Confidence,bool Applicable=true);
public sealed record AffinityPackPrediction(string Set,string Format,string Draft,int Pack,int Pick,
    Dictionary<string,Dictionary<string,ResearchAffinityState>> Models);

/// <summary>Offline pick-time affinity stream; no future build or outcome fields.</summary>
public sealed class ExperimentalAffinitySubstitution : IDisposable
{
    public const string Version="research-a-phase-d-v1";
    private readonly StreamReader _reader;
    private readonly HashSet<(string,string,string,int,int)> _seen=[];
    public ExperimentalAffinitySubstitution(string path)
    {
        Stream stream=File.OpenRead(path);
        if(path.EndsWith(".gz",StringComparison.Ordinal))stream=new GZipStream(stream,CompressionMode.Decompress);
        _reader=new(stream);
    }
    public AffinityPackPrediction Read(PickTimeState input)
    {
        var row=HistoricalJson.Read<AffinityPackPrediction>(_reader.ReadLine()??throw new InvalidDataException("Missing affinity prediction."));
        var key=(row.Set,row.Format,row.Draft,row.Pack,row.Pick);
        if(key!=(input.SetCode,input.Format,input.DraftId,input.PackNumber,input.PickNumber)||!_seen.Add(key))
            throw new InvalidDataException("Future, duplicate or mismatched affinity prediction rejected.");
        if(!row.Models.Keys.Order().SequenceEqual(new[]{"A","B","C"}))throw new InvalidDataException("Exactly three offline affinity families required.");
        var ids=input.OfferedCards.Select(c=>c.Card.CanonicalId).Where(i=>i is not null).Distinct().Order().ToArray();
        foreach(var model in row.Models.Values)
        {
            if(!model.Keys.Order().SequenceEqual(ids))throw new InvalidDataException("Affinity offered identities differ.");
            foreach(var value in model.Values)Validate(value);
        }
        return row;
    }
    public static HistoricalReplayResult Apply(HistoricalReplayResult baseline,Dictionary<string,ResearchAffinityState> states,string family)
    {
        if(family is not ("A" or "B" or "C"))throw new ArgumentException("Offline affinity family required.");
        var model=Version+"/"+family;
        var cards=baseline.Cards.Select(c=>states.TryGetValue(c.CardIdentifier.Value,out var state)
            ?Substitute(c,state,model):c with {ModelVersion=model}).ToArray();
        var ranked=cards.Where(c=>c.ContextualValue is not null).OrderByDescending(c=>c.ContextualValue).ThenByDescending(c=>c.QualityComponent)
            .ThenByDescending(c=>c.StatisticalDataWeight).ThenBy(c=>c.PackIndex).ToArray();
        if(ranked.Length>=2)for(var i=0;i<ranked.Length;i++)
        {var index=Array.FindIndex(cards,c=>c.PackIndex==ranked[i].PackIndex);cards[index]=ranked[i] with {CurrentPackRank=i+1,IsContextPick=i==0};}
        return baseline with {ModelVersion=model,Cards=cards,OptimizerCalls=0,TotalMilliseconds=0};
    }
    public static ContextualPickScore Substitute(ContextualPickScore baseline,ResearchAffinityState state,string model)
    {
        Validate(state);
        if(!model.StartsWith(Version+"/",StringComparison.Ordinal))throw new ArgumentException("Explicit offline version required.");
        if(!state.Applicable||state.Confidence==0)return baseline with {ModelVersion=model,
            Reasons=baseline.Reasons.Append("Experimental A declined; existing A retained.").ToArray()};
        var affinity=baseline.ArchetypeComponent+state.Confidence*(state.Fit-baseline.ArchetypeComponent);
        var weighted=baseline.Weights.Quality*(baseline.EffectiveQualityComponent??.5)+baseline.Weights.Lane*baseline.LaneComponent
            +baseline.Weights.Archetype*affinity+baseline.Weights.DeckNeed*baseline.DeckNeedComponent;
        var value=baseline.ContextualValue is null?(double?)null:ContextualPickScoreCalibration.Calibrate(weighted);
        var linear=.5+ContextualPickScoreCalibration.DefaultGain*(weighted-.5);
        var contributions=baseline.Contributions.Select(c=>c.Component=="Archetype"
            ?c with {Value=affinity,PointsFromNeutral=50*ContextualPickScoreCalibration.DefaultGain*baseline.Weights.Archetype*(affinity-.5)}
            :c.Component=="Soft limit"?c with {Value=linear,PointsFromNeutral=50*((value??.5)-linear)}:c).ToArray();
        return baseline with {ArchetypeComponent=affinity,ContextualValue=value,CurrentPackRank=null,IsContextPick=false,ModelVersion=model,
            Contributions=contributions,Reasons=baseline.Reasons.Append("Offline A-only substitution; Q/L/D, weights, R, commitment, sparse prior and optimizer projections frozen.").ToArray()};
    }
    private static void Validate(ResearchAffinityState s)
    {
        if(!double.IsFinite(s.Fit)||s.Fit is <0 or >1||!double.IsFinite(s.Confidence)||s.Confidence is <0 or >1)
            throw new InvalidDataException("Affinity fit/confidence must be finite and bounded.");
    }
    public void Complete(){if(_reader.ReadLine() is not null)throw new InvalidDataException("Unconsumed future affinity records.");}
    public void Dispose()=>_reader.Dispose();
}
