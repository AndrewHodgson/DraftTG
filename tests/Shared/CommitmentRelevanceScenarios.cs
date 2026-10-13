using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.TestScenarios;

/// <summary>Controlled what-if inputs; all calculations use the production engines. Never live promotion evidence.</summary>
internal static class CommitmentRelevanceScenarios
{
    internal sealed record Entry(Card Card, double? Rate = .56, int Games = 10000);
    internal static Entry Card(string id,string cost,double rate=.56,string type="Creature",IEnumerable<ManaKind>? produced=null,int manaValue=3)
    {
        var colors = cost.Where("WUBRG".Contains).Distinct().Select(c => (MagicColor)"WUBRG".IndexOf(c));
        return new(new(CardIdentifier.Create(id),id,new(colors),CardRarity.Common,CardSetCode.Create("WOE"),CollectorNumber.Create(id))
        { GameplayMetadata = new(type=="Land"?0:manaValue,cost,type,producedMana:produced) },rate);
    }
    internal static Entry[] Pool(int colored, int blueFixing=0, bool gold=false) => Enumerable.Range(0,colored)
        .Select(i => Card($"rg-{i}",gold ? "{1}{R}{G}" : i%2==0 ? "{2}{R}" : "{2}{G}"))
        .Concat(Enumerable.Range(0,blueFixing).Select(i => Card($"fix-{i}","",.56,"Land",[ManaKind.Blue,ManaKind.Red]))).ToArray();
    internal static Entry[] OpenPool(int count) => Enumerable.Range(0,count).Select(i=>Card($"open-{i}","{2}{"+"WUBRG"[i%5]+"}")).ToArray();

    internal static ContextualPickScoreResult Score(IReadOnlyList<Entry> pack,IReadOnlyList<Entry> pool,ContextualPickScoreVersion version=ContextualPickScoreVersion.V1_6_2)
    {
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(pool.Count/14+1),PickNumber.Create(pool.Count%14+1)),pack.Select(e=>e.Card.Identifier)),
            new(pool.Select((e,i)=>new DraftPick(new(PackNumber.Create(i/14+1),PickNumber.Create(i%14+1)),e.Card.Identifier))),DraftFormat.BestOfOne);
        var all=pool.Concat(pack).DistinctBy(e=>e.Card.Identifier).ToArray();
        var catalog=new CardCatalog(all.Select(e=>e.Card));
        var statistics=new LimitedCardStatisticsCatalog(all.Select(e=>new LimitedCardStatistics(e.Card.Identifier,e.Games,GameInHandWinRate:e.Rate)));
        var environment=new LimitedCardStatisticsCatalog([new(CardIdentifier.Create("environment"),1000000,GameInHandWinRate:.56)]);
        var statistical=new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack,statistics,environment);
        var colors=new ContextualRecommendationEngine().Recommend(snapshot,catalog,statistical);
        var lanes=new LaneContextualRecommendationEngine().Recommend(snapshot,colors,DraftPackObservationHistory.Empty,LimitedStatisticsFormat.PremierDraft);
        var archetype=new ArchetypeRecommendationEngine().Recommend(lanes,null,new("WOE",LimitedStatisticsFormat.PremierDraft));
        return new ContextualPickScoreEngine(new(version:version)).Recommend(snapshot,catalog,archetype,statistics,environment,
            new(snapshot.DraftedPool,DraftPoolCompleteness.Complete));
    }
}
