using DraftTG.ArenaIntegration;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>Exact IDs resolve independently. Only five ordinary gameplay basics may cross printings.</summary>
public sealed class PreferredBasicPrintingReconciler(ArenaCardResolver resolver,CardCatalog catalog)
{
    public ArenaOfferedCardIdentity Reconcile(uint pod,uint visible)
    {
        ArenaOfferedCardIdentity Fail(string reason)=>new(pod,visible,null,false,false,reason);
        if(pod==0||visible==0||pod>int.MaxValue||visible>int.MaxValue)return Fail("Invalid printing ID");
        var logical=resolver.Resolve(ArenaCardIdentifier.Create((int)pod));var rendered=resolver.Resolve(ArenaCardIdentifier.Create((int)visible));
        if(logical.Status!=ArenaCardResolutionStatus.Resolved||rendered.Status!=ArenaCardResolutionStatus.Resolved)
            return Fail("Both exact printing IDs must resolve without ambiguity");
        var a=catalog.Find(logical.CardIdentifier!);var b=catalog.Find(rendered.CardIdentifier!);
        if(a is null||b is null)return Fail("Owned card metadata missing");
        if(pod==visible)return new(pod,visible,a.Identifier.Value,true,false,"Identical exact Arena printing");
        if(!Ordinary(a)||!Ordinary(b)||a.Name!=b.Name||a.Colors!=b.Colors||!a.GameplayMetadata.Equals(b.GameplayMetadata))
            return Fail("Different printing IDs are supported only for complete, equivalent ordinary basic-land metadata");
        return new(pod,visible,"ordinary-basic:"+a.Name,true,true,"Independently resolved ordinary basic; full normalized gameplay metadata agrees; pod printing retained for history/stats");
    }
    private static bool Ordinary(Card c)
    {
        var m=c.GameplayMetadata;
        if(!Enum.TryParse<BasicLandType>(c.Name,out var type)||!Enum.IsDefined(type))return false;
        var mana=type switch{BasicLandType.Plains=>ManaKind.White,BasicLandType.Island=>ManaKind.Blue,BasicLandType.Swamp=>ManaKind.Black,
            BasicLandType.Mountain=>ManaKind.Red,_=>ManaKind.Green};
        return m.Layout==CardLayout.Normal&&m.Faces.Count==0&&m.CardTypes is {IsKnown:true,IsBasic:true,Types:CardType.Land}
            &&m.CardTypes.BasicLandType==type&&m.TypeLine=="Basic Land — "+c.Name&&m.ManaValue==0&&m.ManaCost==string.Empty
            &&m.OracleText is not null&&m.UnrecognizedManaSymbols.Count==0
            &&m.ColorsKnown&&c.Colors.Equals(ColorSet.Colorless)&&m.ProducedMana is {Count:1}&&m.ProducedMana[0]==mana
            &&m.Keywords.Count==0&&m.Power is null&&m.Toughness is null;
    }
}
