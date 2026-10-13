using DraftTG.Application;

namespace DraftTG.App;

/// <summary>Read-only stage snapshots; missing statistics never imply a missing badge.</summary>
internal static class ProductionBadgeDiagnostics
{
    internal static object Capture(OverlayViewModel view, VisualPlacementContext context, CardVisualLocalizationResult? visual) => new
    {
        PackGeneration = context.Generation, Pack = context.Pack.Position.Pack.Value, Pick = context.Pack.Position.Pick.Value,
        CaptureRequestGeneration = visual?.CaptureRequestGeneration, view.ShowBadges,
        Cards = context.Pack.AvailableCardIdentifiers.Select((id, index) =>
        {
            var key = new CardOccurrenceKey(index, id);
            var presentation = view.Session.CurrentPackPresentations.FirstOrDefault(p => p.Key == key);
            var badge = view.Badges.FirstOrDefault(b => b.OccurrenceKey == key);
            return new
            {
                PackIndex = index, CardIdentity = id.Value, Offered = true,
                CatalogResolved = presentation?.Card is not null, Name = presentation?.CardName,
                IdentityConsistent = presentation?.IsIdentityConsistent, ScoringPending = presentation?.IsLoading,
                ScoreProduced = presentation?.PickScore is not null, Score = presentation?.PickScore,
                RawGih = presentation?.RawGIH, Alsa = presentation?.ALSA,
                FullMatcherIdentified = visual?.Matches.Any(m => m.Key == key),
                BadgeModelCreated = badge is not null, PlacementAccepted = badge?.IsPlaced,
                X = badge?.X, Y = badge?.Y, Width = badge?.Width
            };
        }).ToArray()
    };
}
