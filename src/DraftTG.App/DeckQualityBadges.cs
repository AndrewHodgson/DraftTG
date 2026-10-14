using DraftTG.Application;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App;

public sealed record DeckQualityBadge(double X,double Y,double Width,double Height,string Label);

internal static class DeckQualityBadges
{
    // Physical evidence is narrower than runtime geometry support. Scrollable lists and other DPI remain debug-only.
    internal static bool PhysicallyQualified(int width,int height,double dpi,CaptureFreeDeckLayoutResult layout)=>
        width==1600&&height==900&&dpi==96&&layout.IsAvailable&&layout.Content is {} content&&layout.Viewport is {} viewport
        &&content.Height<=viewport.Height+1;

    internal static IReadOnlyList<DeckQualityBadge> Create(CaptureFreeDeckLayoutResult layout,
        IReadOnlyDictionary<uint,CardIdentifier> identities,LimitedStatisticsLoadResult statistics,double scaling)
    {
        var ids=layout.Rows.Where(r=>identities.ContainsKey(r.GrpId)).Select(r=>identities[r.GrpId]).ToArray();
        if(ids.Length!=layout.Rows.Count||ids.Length==0)return [];
        var pack=new DraftPack(new(PackNumber.Create(1),PickNumber.Create(1)),ids);
        var recommendation=new StatisticalRecommendationEngine().Recommend(pack,statistics.Catalog,statistics.EnvironmentCatalog);
        if(recommendation.EnvironmentBaseline is not {} baseline)return [];
        var badges=new List<DeckQualityBadge>();
        foreach(var row in layout.Rows.Where(r=>r.Visible&&r.VisibleRectangle.Height>=18*scaling))
        {
            var id=identities[row.GrpId];var card=statistics.CardCatalog.Find(id);if(card is null)continue;
            var primary=statistics.Catalog.StatisticsFor(id);
            var prior=statistics.StatisticsResolutions.GetValueOrDefault(id)?.IsResolved==true?
                SparsePrimaryQualityPrior.TryCreate(card,primary,baseline,statistics.AlternateQualityEvidence.GetValueOrDefault(id)):null;
            var adjusted=recommendation.Cards[row.VisualIndex].AdjustedValue;
            var q=prior is not null?new ContextualPickScoreConfiguration().NormalizeQuality(prior.EffectiveQualityValue,baseline):
                adjusted is {} value?new ContextualPickScoreConfiguration().NormalizeQuality(value,baseline):.5;
            var rectangle=row.VisibleRectangle;
            badges.Add(new((rectangle.X+rectangle.Width)/scaling-48,rectangle.Y/scaling,48,18,
                "Q "+q.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)));
        }
        return badges;
    }
}
