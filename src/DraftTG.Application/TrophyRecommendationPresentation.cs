using System.Globalization;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public sealed record TrophyDataStatus(bool IsLoading, SuccessfulDeckSource Source, string? Diagnostic = null)
{
    public static TrophyDataStatus Unavailable { get; } = new(false, SuccessfulDeckSource.Unavailable);
}

public static class TrophyRecommendationPresentation
{
    private static string Number(double? value, string format = "0.0") => value?.ToString(format, CultureInfo.InvariantCulture) ?? "unknown";
    public static string Summary(TrophyRecommendationResult? result, TrophyDataStatus status, string? topName, bool loading = false)
    {
        var text = ArchetypeRecommendationPresentation.Summary(result?.Archetype, topName, loading);
        if (loading || result is null) return text;
        var top = result.Cards.FirstOrDefault(c => c.IsContextPick);
        if (top is not null) text += $"\nArchetype rank: #{top.ArchetypeRecommendation.ContextualRank}";
        if (result.Archetype.Profile.Active is null) return text + "\nTrophy evidence: waiting for an active archetype";
        if (status.IsLoading) return text + "\nTrophy evidence: loading; using Archetype result";
        if (result.Evidence is not { } evidence)
            return text + "\nTrophy evidence unavailable" + (status.Diagnostic is { } diagnostic ? "\n" + diagnostic : "");
        text += $"\n17Lands trophy evidence: {evidence.Corpus.Samples.Count} recent {evidence.Corpus.Key.Pair.DisplayCode} {evidence.Corpus.Key.Format} decks"
            + (status.Source == SuccessfulDeckSource.StaleCache ? " (stale cache)" : "");
        if (top?.Evidence is { } card)
            text += $"\nCard seen in pool: {card.PoolDecks}; main-decked: {card.MainDeckDecksWithKnownPool}"
                + $"\nPool-to-maindeck conversion: {Number(card.MainDeckConversion * 100)}%";
        return text + "\nTrophy adjustment: 0.0 pp\nAdjustment disabled: incompatible baseline semantics";
    }

    public static string Diagnostics(TrophyRecommendationResult? result, TrophyDataStatus status)
    {
        if (result is null) return string.Empty;
        var config = result.Configuration;
        var text = $"Trophy model {config.ModelVersion}; minimum corpus {config.MinimumTrophyCorpusDecks}; minimum exposure {config.MinimumCardTrophyExposure}"
            + $"\nFull corpus confidence {config.FullCorpusConfidenceDecks}; full exposure confidence {config.FullCardExposureDecks}; per-draft corpus budget {config.MaxCorporaPerDraft}"
            + $"\nScoring disabled: {TrophyRecommendationResult.DisabledReason}\nSource: {status.Source}; {status.Diagnostic}";
        if (result.Evidence is { } evidence)
        {
            var corpus = evidence.Corpus; var source = corpus.Provenance;
            text += $"\nSource: {source.Source}; query mode: {source.QueryMode}; filter: {source.Query}"
                + $"\nRetrieved: {corpus.RetrievedAt:u}; returned events {source.ReturnedEvents}; source cap {source.SourceLimit}; requested cap {source.RequestedLimit}; eligible sample {corpus.Samples.Count}"
                + $"\nRepresentative policy: {source.RepresentativePolicy}; pool bases: {string.Join(", ", corpus.Samples.Select(s => s.PoolBasis).Distinct())}";
        }
        text += "\nBiased toward successful events, opened/drafted cards, recent samples and deck construction decisions; supplemental evidence, not a replacement for GIH.";
        return text + "\n" + string.Join("\n", result.Cards.Select(c =>
            $"Slot {c.PackIndex}: {c.CardIdentifier}; pool events {c.Evidence?.PoolDecks}; main events {c.Evidence?.MainDeckDecksWithKnownPool}; "
            + $"pool copies {c.Evidence?.PoolCopies}; main copies {c.Evidence?.MainDeckCopies}; conversion {Number(c.Evidence?.MainDeckConversion)}; "
            + $"copy utilization {Number(c.Evidence?.CopyUtilization)}; presence {Number(c.Evidence?.DeckPresenceRate)}; average copies {Number(c.Evidence?.AverageCopiesWhenPlayed)}; "
            + $"corpus confidence {Number(c.CorpusConfidence)}; exposure confidence {Number(c.CardExposureConfidence)}; combined {Number(c.CombinedConfidence)}; "
            + $"Phase 9C rank {c.ArchetypeRecommendation.ContextualRank}; final rank {c.FinalRank}; adjustment 0; {c.Diagnostic}"));
    }
}
