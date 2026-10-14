using DraftTG.ArenaIntegration.Memory;
using DraftTG.Application;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;
using DraftTG.RatingAudit;

namespace DraftTG.Application.Tests;

public sealed class Phase9F3Tests
{
    [Fact]
    public void ShrinkingPackUsesCompactFixedIndicesOnlyWithLiveLayoutEvidence()
    {
        var evidence = new ArenaPackLayoutMemory(5,0,43,196,-268,"DraftPackHolder._currentPackLayoutData");
        var opening = CaptureFreeDraftLayout.Map(1600,900,14,"list",true,evidence);
        for(var count=14;count>=1;count--)
        {
            var result=CaptureFreeDraftLayout.Map(1600,900,count,"list",true,evidence);
            Assert.True(result.IsAvailable);Assert.Equal(opening.Rectangles.Take(count),result.Rectangles);
        }
        Assert.False(CaptureFreeDraftLayout.Map(1600,900,12,"list",true,null).IsAvailable);
        Assert.False(CaptureFreeDraftLayout.Map(1600,900,14,"list",true,evidence with { Rows=3 }).IsAvailable);
        Assert.False(CaptureFreeDraftLayout.Map(1600,900,3,"list",true,evidence with { OffsetX=200 }).IsAvailable);
        Assert.False(CaptureFreeDraftLayout.Map(1600,900,8,"column",true,evidence).IsAvailable);
        Assert.False(CaptureFreeDraftLayout.Map(1600,900,15,"list",true,evidence).IsAvailable);
    }
    [Fact]
    public void UiGenerationRetiresOnResizeDpiAndScrollAndNeedsFreshRepeatedState()
    {
        var stable=new CaptureFreeUiStability();var now=DateTimeOffset.UnixEpoch;
        Assert.False(stable.Observe("1600/900/96/scroll0",now));
        Assert.False(stable.Observe("1600/900/96/scroll0",now.AddMilliseconds(100)));
        Assert.True(stable.Observe("1600/900/96/scroll0",now.AddMilliseconds(200)));
        var generation=stable.Generation;
        Assert.False(stable.Observe("1600/900/144/scroll0",now.AddMilliseconds(250)));Assert.True(stable.Generation>generation);
        Assert.True(stable.Observe("1600/900/144/scroll0",now.AddMilliseconds(450)));
        stable.Invalidate();Assert.False(stable.IsStable);
        Assert.False(stable.Observe("1280/720/144/scroll1",now.AddMilliseconds(500)));
        Assert.True(stable.Observe("1280/720/144/scroll1",now.AddMilliseconds(700)));
    }
    [Fact]
    public void AuditUsesProductionShrinkageAndNarrowPriorAndKeepsNullsExplicit()
    {
        var card=new Card(CardIdentifier.Create("card"),"Card",ColorSet.Colorless,CardRarity.Rare,CardSetCode.Create("FRA"),CollectorNumber.Create("1"));
        var primary=new LimitedCardStatistics(card.Identifier,1000,GameInHandWinRate:.6);
        var pack=new DraftPack(new(PackNumber.Create(1),PickNumber.Create(1)),[card.Identifier]);
        var recommendation=new StatisticalRecommendationEngine().Recommend(pack,new([primary]),new([new(card.Identifier,1000,GameInHandWinRate:.55)]));
        RatingAuditRow Row(LimitedCardStatistics? stat,CardRecommendation? rec=null,AlternateFormatQualityPrior? prior=null,
            ArenaCardResolutionStatus identity=ArenaCardResolutionStatus.Resolved,StatisticalIdentityResolution? mapping=null)=>
            RatingAuditClassifier.Create(card,101,identity,mapping,stat,rec,.55,prior);
        Assert.Equal(RatingCoverage.ExactPrimary,Row(primary,recommendation.Cards[0]).Coverage);
        Assert.Equal(RatingCoverage.SparsePrimary,Row(primary with { GameInHandGameCount=100 },recommendation.Cards[0]).Coverage);
        var nullGih=new LimitedCardStatistics(card.Identifier,100);
        var prior=SparsePrimaryQualityPrior.TryCreate(card,nullGih,.55,new(card.Identifier,new("FRA",LimitedStatisticsFormat.QuickDraft),
            new("FRA",LimitedStatisticsFormat.PremierDraft),.6,1000,.55,"Card",DateTimeOffset.UnixEpoch,"Cache","ALL_TIME"));
        Assert.NotNull(prior);Assert.Equal(RatingCoverage.AlternateFormatPrior,Row(nullGih,prior:prior).Coverage);
        Assert.Equal(RatingCoverage.NullGIH,Row(nullGih).Coverage);Assert.Equal(.5,Row(nullGih).Q);
        Assert.Equal(RatingCoverage.MissingStatRow,Row(null).Coverage);
        Assert.Equal(RatingCoverage.AmbiguousIdentity,Row(null,mapping:new(StatisticalIdentityKind.ConflictingProviderRows,"Card")).Coverage);
        Assert.Equal(.5,Row(null,identity:ArenaCardResolutionStatus.Ambiguous).Q);
        Assert.Equal(RatingCoverage.MissingIdentity,RatingAuditClassifier.Create(null,999,ArenaCardResolutionStatus.Missing,null,null,null,.55,null).Coverage);
    }
    [Fact]
    public void ReferenceComparisonIsScaleIndependentAndTieAware()
    {
        Assert.Equal(new[] {0d,.5,.5,1d},RatingRankComparison.Percentiles([1,2,2,3]));
        Assert.Equal(1,RatingRankComparison.Spearman([.2,.5,.8],[1,3,5]));
        Assert.Equal(-1,RatingRankComparison.Spearman([.2,.5,.8],[5,3,1]));
        Assert.Null(RatingRankComparison.Spearman([.5,.5],[1,2]));
        Assert.Throws<ArgumentException>(()=>RatingRankComparison.Percentiles([double.NaN]));
    }
}

