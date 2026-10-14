using System.IO.Compression;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation;

public sealed record ResearchLaneState(double[] Colors,double Confidence,bool Applicable=true);
public sealed record LanePackPrediction(string Set,string Format,string Draft,int Pack,int Pick,
    Dictionary<string,ResearchLaneState> Models);

/// <summary>Offline, ordered pick-time lane stream. Labels/outcomes are not accepted.</summary>
public sealed class ExperimentalLaneSubstitution : IDisposable
{
    public const string Version="research-l-phase-c-v1";
    private readonly StreamReader _reader;
    private readonly Dictionary<(string Set,string Format,string Id),double> _alsa=[];
    private DraftPackObservationHistory _currentHistory=DraftPackObservationHistory.Empty;
    private string? _scope;
    public ExperimentalLaneSubstitution(string path,string alsa)
    {
        Stream stream=File.OpenRead(path);
        if(path.EndsWith(".gz",StringComparison.Ordinal))stream=new GZipStream(stream,CompressionMode.Decompress);
        _reader=new(stream);
        foreach(var row in HistoricalJson.Read<PositionRow[]>(File.ReadAllText(alsa)))
            if(!double.IsFinite(row.ALSA)||row.ALSA<=0||!_alsa.TryAdd((row.Set,row.Format,row.Id),row.ALSA))
                throw new InvalidDataException("Invalid/duplicate derived ALSA identity.");
    }
    public LanePackPrediction Read(PickTimeState input)
    {
        var row=HistoricalJson.Read<LanePackPrediction>(_reader.ReadLine()??throw new InvalidDataException("Missing lane prediction."));
        if((row.Set,row.Format,row.Draft,row.Pack,row.Pick)!=(input.SetCode,input.Format,input.DraftId,input.PackNumber,input.PickNumber))
            throw new InvalidDataException("Asynchronous/future/out-of-order lane prediction rejected.");
        if(!row.Models.Keys.Order().SequenceEqual(new[]{"A","B","C"}))throw new InvalidDataException("Exactly three frozen lane families required.");
        foreach(var value in row.Models.Values)Validate(value);
        return row;
    }
    public ResearchLaneState Current(PickTimeState input,HistoricalReplayResult baseline,CardCatalog catalog)
    {
        var scope=input.SetCode+"/"+input.Format+"/"+input.DraftId;
        if(_scope!=scope){_scope=scope;_currentHistory=DraftPackObservationHistory.Empty;}
        if(input.PickNumber>14)return new(new double[5],0);
        var position=new DraftPosition(PackNumber.Create(input.PackNumber),PickNumber.Create(input.PickNumber));
        var obs=baseline.ObservationHistory.Observations.FirstOrDefault(o=>o.Position==position);
        if(obs is not null)
        {
            var stats=new LimitedCardStatisticsCatalog(obs.Cards.DistinctBy(c=>c.CardIdentifier).Select(c=>
                new LimitedCardStatistics(c.CardIdentifier,c.GamesInHandSampleCount,GameInHandWinRate:c.RawGamesInHandWinRate,
                    AverageLastSeenAt:_alsa.GetValueOrDefault((input.SetCode,input.Format,c.CardIdentifier.Value)) is >0 and var a?a:null)));
            var env=new LimitedCardStatisticsCatalog(obs.EnvironmentBaseline is {} b
                ?new[]{new LimitedCardStatistics(obs.Cards[0].CardIdentifier,1,GameInHandWinRate:b)}:[]);
            var statistical=new StatisticalRecommendationEngine().Recommend(obs.Pack,stats,env);
            var history=new DraftHistory(input.PriorPicks.Where(p=>p.Pick<=14&&p.Card.Identifier is not null)
                .Select(p=>new DraftPick(new(PackNumber.Create(p.Pack),PickNumber.Create(p.Pick)),p.Card.Identifier!)));
            _currentHistory=_currentHistory.Observe(obs.Pack,catalog,history,statistical,stats);
        }
        // Actual production Phase9B implementation; derived evidence is an explicit offline input.
        var profile=LaneOpennessProfile.Analyze(_currentHistory,position);
        return new(profile.Colors.Select(c=>c.EffectiveSupport).ToArray(),profile.ObservationConfidence);
    }
    public static HistoricalReplayResult Apply(HistoricalReplayResult baseline,ResearchLaneState state,CardCatalog catalog,string family)
    {
        Validate(state);
        if(family is not ("A" or "B" or "C" or "current-derived"))throw new ArgumentException("Explicit offline lane model required.");
        var model=Version+"/"+family;
        if(!state.Applicable)return baseline with {ModelVersion=model,OptimizerCalls=0,TotalMilliseconds=0,
            Cards=baseline.Cards.Select(c=>c with {ModelVersion=model,Reasons=c.Reasons.Append("Experimental lane declined: qualified positional dataset unavailable; existing L retained.").ToArray()}).ToArray()};
        var cards=baseline.Cards.Select(c=>
        {
            var card=catalog.Find(c.CardIdentifier);var colors=card?.Colors??ColorSet.Colorless;
            var fit=colors.IsColorless||family!="current-derived"&&card?.GameplayMetadata.CardTypes.Contains(CardType.Land)==true
                ?0:colors.Colors.Min(color=>state.Colors[(int)color]);
            return Substitute(c,.5+.5*fit,model);
        }).ToArray();
        var ranked=cards.Where(c=>c.ContextualValue is not null).OrderByDescending(c=>c.ContextualValue).ThenByDescending(c=>c.QualityComponent)
            .ThenByDescending(c=>c.StatisticalDataWeight).ThenBy(c=>c.PackIndex).ToArray();
        if(ranked.Length>=2)for(var i=0;i<ranked.Length;i++)
        {var index=Array.FindIndex(cards,c=>c.PackIndex==ranked[i].PackIndex);cards[index]=ranked[i] with {CurrentPackRank=i+1,IsContextPick=i==0};}
        return baseline with {ModelVersion=model,Cards=cards,OptimizerCalls=0,TotalMilliseconds=0};
    }
    public static ContextualPickScore Substitute(ContextualPickScore baseline,double lane,string model)
    {
        if(!model.StartsWith(Version+"/",StringComparison.Ordinal)||!double.IsFinite(lane)||lane is <0 or >1)
            throw new ArgumentException("Offline version and bounded lane required.");
        var weighted=baseline.Weights.Quality*(baseline.EffectiveQualityComponent??.5)+baseline.Weights.Lane*lane
            +baseline.Weights.Archetype*baseline.ArchetypeComponent+baseline.Weights.DeckNeed*baseline.DeckNeedComponent;
        var value=baseline.ContextualValue is null?(double?)null:ContextualPickScoreCalibration.Calibrate(weighted);
        var linear=.5+ContextualPickScoreCalibration.DefaultGain*(weighted-.5);
        var contributions=baseline.Contributions.Select(c=>c.Component=="Lane"
            ?c with {Value=lane,PointsFromNeutral=50*ContextualPickScoreCalibration.DefaultGain*baseline.Weights.Lane*(lane-.5)}
            :c.Component=="Soft limit"?c with {Value=linear,PointsFromNeutral=50*((value??.5)-linear)}:c).ToArray();
        return baseline with {LaneComponent=lane,ContextualValue=value,CurrentPackRank=null,IsContextPick=false,ModelVersion=model,
            Contributions=contributions,Reasons=baseline.Reasons.Append("Offline L-only substitution; Q/A/D, weights, commitment, relevance, sparse prior and deck projections frozen.").ToArray()};
    }
    private static void Validate(ResearchLaneState state)
    {
        if(state.Colors.Length!=5||state.Colors.Any(x=>!double.IsFinite(x)||x is <-1 or >1)||!double.IsFinite(state.Confidence)||state.Confidence is <0 or >1)
            throw new InvalidDataException("Invalid lane state.");
    }
    public void Complete(){if(_reader.ReadLine() is not null)throw new InvalidDataException("Unconsumed future lane records.");}
    public void Dispose()=>_reader.Dispose();
    private sealed record PositionRow(string Set,string Format,string Id,double ALSA);
}
