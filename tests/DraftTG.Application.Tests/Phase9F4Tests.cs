using System.Text.Json;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RatingAudit;

namespace DraftTG.Application.Tests;

public sealed class Phase9F4Tests
{
    private static ArenaDeckViewMemory Deck(float normalized=1)
    {
        var offset=(1-normalized)*4;
        ArenaDeckEntryMemory Row(int collection,uint id,double y,int quantity=1,string area="Main")=>
            new(collection,id,quantity,area,null,"fixture") {Geometry=new(new(5,y+offset,3,1),collection,true,"fixture")};
        return new([Row(0,101,1),Row(1,102,3,2),Row(2,103,-3),Row(0,102,3,1,"Sideboard")],normalized,0,false,null)
        {MainGeometry=new(new(5,0,3,4),new(5,-4+offset,3,8),new(-8,0,16,9),1,1,true,false,"fixture")};
    }
    [Fact]
    public void MainRowsUseActualWorldOrderAndGroupedQuantityWithViewportProjection()
    {
        var result=CaptureFreeDeckLayout.Map(Deck(),1600,900,"list",true);
        Assert.True(result.IsAvailable);Assert.Equal(new[]{1,0,2},result.Rows.Select(r=>r.CollectionIndex));
        Assert.Equal(new uint[]{102,101,103},result.Rows.Select(r=>r.GrpId));
        Assert.Equal(2,result.Rows[0].Quantity);Assert.Equal(1300,result.Rows[0].Rectangle.X);
        Assert.Equal(500,result.Rows[0].Rectangle.Y);Assert.Equal(100,result.Rows[0].Rectangle.Height);
        Assert.All(result.Rows,r=>Assert.Equal("Main",r.Holder));Assert.True(result.Rows[2].Culled);
        Assert.False(CaptureFreeDeckLayout.Map(Deck(),1600,900,"list",true,"Sideboard").IsAvailable);
    }
    [Fact]
    public void TopMiddleBottomScrollUseNativeDisplacementAndCullOrClipRows()
    {
        foreach(var scroll in new[]{1f,.5f,0f})
        {
            var result=CaptureFreeDeckLayout.Map(Deck(scroll),1600,900,"list",true);
            Assert.True(result.IsAvailable);Assert.Equal((1-scroll)*400,result.ScrollPixels);
            Assert.Equal(result.ScrollPixels,result.NormalizedScrollPixels);
        }
        var partial=Deck() with {Entries=[Deck().Entries[0] with {Geometry=new(new(5,-.5,3,1),0,true,"fixture")}]};
        Assert.True(CaptureFreeDeckLayout.Map(partial,1600,900,"list",true).Rows[0].Clipped);
        Assert.False(CaptureFreeDeckLayout.Map(Deck(.5f) with {MainScroll=0},1600,900,"list",true).IsAvailable);
        Assert.False(CaptureFreeDeckLayout.Map(Deck() with {MainGeometry=null},1600,900,"list",true).IsAvailable);
        Assert.False(CaptureFreeDeckLayout.Map(Deck() with {MainGeometry=Deck().MainGeometry! with {Scrolling=true}},1600,900,"list",true).IsAvailable);
        Assert.False(CaptureFreeDeckLayout.Map(Deck(),900,900,"list",true).IsAvailable);
    }
    private static (PreferredBasicPrintingReconciler Policy,CardCatalog Catalog) Basics(bool conflicting=false,bool ambiguous=false,bool missing=false)
    {
        var records=new[]{new {id="a",arena_id=101,name="Swamp",set="fra",collector_number="1",rarity="common",colors=Array.Empty<string>()},
            new {id="b",arena_id=ambiguous?101:102,name="Swamp",set="usg",collector_number="2",rarity="common",colors=Array.Empty<string>()}};
        var data=new ScryfallCardCatalogDecoder().DecodeCatalogData(JsonSerializer.Serialize(records));
        var metadata=new CardGameplayMetadata(0,"","Basic Land — Swamp",oracleText:missing?null:"({T}: Add {B}.)",producedMana:[ManaKind.Black]);
        var catalog=new CardCatalog(data.Catalog.Cards.Select(c=>c with {GameplayMetadata=c.Identifier.Value=="b"&&conflicting?
            new(0,"","Basic Land — Swamp",oracleText:"different",producedMana:[ManaKind.Black]):metadata}));
        return (new(new ArenaCardResolver(data),catalog),catalog);
    }
    [Fact]
    public void BasicPrintingProofRequiresIndependentExactResolutionAndFullMetadata()
    {
        var (policy,_)=Basics();Assert.True(policy.Reconcile(101,101).Accepted);
        var result=policy.Reconcile(101,102);Assert.True(result.Reconciled);Assert.Equal((uint)101,result.PodGrpId);
        Assert.Equal((uint)102,result.VisibleGrpId);Assert.Equal("ordinary-basic:Swamp",result.LogicalIdentity);
        Assert.False(Basics(conflicting:true).Policy.Reconcile(101,102).Accepted);
        Assert.False(Basics(ambiguous:true).Policy.Reconcile(101,102).Accepted);
        Assert.False(Basics(missing:true).Policy.Reconcile(101,102).Accepted);
        Assert.False(policy.Reconcile(101,999).Accepted);
    }
    [Fact]
    public void PreferredPrintingMappingPreservesPodMultiplicityAndDeclinesCompetingPrintings()
    {
        var (policy,_)=Basics();var result=ArenaOfferedPrintingReconciliation.Match([101,101],[102,102],[102,102],policy.Reconcile);
        Assert.Equal(new uint[]{101,101},result.Select(r=>r.PodGrpId));
        Assert.Throws<InvalidDataException>(()=>ArenaOfferedPrintingReconciliation.Match([101],[102,102],[102,102],policy.Reconcile));
        ArenaOfferedCardIdentity Proof(uint a,uint b)=>new(a,b,"same",true,a!=b,"fixture");
        Assert.Throws<InvalidDataException>(()=>ArenaOfferedPrintingReconciliation.Match([1,2],[3,4],[3,4],Proof));
        Assert.Equal(new uint[]{2,1},ArenaOfferedPrintingReconciliation.Match([1,2],[3,1],[3,1],Proof).Select(r=>r.PodGrpId));
    }
    [Fact]
    public void StatReviewGroupsOnlyProvenIdentitiesRetainsRowsAndExcludesBasicDefectNoise()
    {
        var card=Basics().Catalog.Cards[0];
        var row=RatingAuditClassifier.Create(card,101,ArenaCardResolutionStatus.Resolved,null,null,null,.55,null)
            with {IsBasicLand=false,ProvenLogicalIdentity="oracle:a",Q=.9,AlternateGames=3000};
        var second=row with {ArenaGrpId=102};var basic=row with {ArenaGrpId=103,IsBasicLand=true};
        var grouped=StatRiskReview.Group([second,basic,row]);Assert.Single(grouped);Assert.Equal(new[]{101,102},grouped[0].GrpIds);
        Assert.Equal(.4,grouped[0].PriorShift!.Value,10);Assert.Equal("High",grouped[0].Risk);
        Assert.Equal(2,StatRiskReview.Group([row with {ProvenLogicalIdentity=null},second with {ProvenLogicalIdentity=null}]).Length);
        Assert.Equal("Medium",StatRiskReview.Risk(1500,.1,true));Assert.Equal("Low",StatRiskReview.Risk(3000,.1,true));
        Assert.Equal(grouped[0].LogicalIdentity,StatRiskReview.Group([row,second])[0].LogicalIdentity);
    }
}
