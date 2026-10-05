using System.Text.Json;

namespace DraftTG.ArenaIntegration;

public sealed record ArenaDeckCardCount(ArenaCardIdentifier CardIdentifier, int Count);
public abstract record ArenaDeckLogEvent
{
    public sealed record SourceReset : ArenaDeckLogEvent;
    public sealed record SceneChanged(string Scene) : ArenaDeckLogEvent;
    public sealed record SavedDeckObserved(string EventName, IReadOnlyList<ArenaDeckCardCount> MainDeck,
        IReadOnlyList<ArenaDeckCardCount> Sideboard) : ArenaDeckLogEvent;
    public sealed record InvalidObservation(string Diagnostic) : ArenaDeckLogEvent;
}

/// <summary>Only audited scene records and direct server CourseDeck snapshots. No unsaved edit inference.</summary>
public sealed class ArenaDeckLogParser
{
    public IReadOnlyList<ArenaDeckLogEvent> Parse(ArenaLogSourceEvent source)
    {
        if (source is ArenaLogSourceEvent.SourceReset) return [new ArenaDeckLogEvent.SourceReset()];
        var line = ((ArenaLogSourceEvent.Line)source).Text;
        var scene = line.Contains("Client.SceneChange ", StringComparison.Ordinal);
        // Requests are proposals, not accepted saves. Nested course collections are historical saved data.
        if (!scene && (!line.TrimStart().StartsWith('{') || !line.Contains("\"CourseDeck\"", StringComparison.Ordinal))) return [];
        try
        {
            var start = line.IndexOf('{'); if (start < 0) return [];
            using var document = JsonDocument.Parse(line[start..]); var root = document.RootElement;
            if (scene)
            {
                if (!root.TryGetProperty("toSceneName", out var name) || name.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Scene record lacks a valid destination.");
                return [new ArenaDeckLogEvent.SceneChanged(name.GetString()!)];
            }
            if (!root.TryGetProperty("CourseDeck", out var deck)) return [];
            if (!root.TryGetProperty("InternalEventName", out var eventName) || eventName.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(eventName.GetString())) throw new InvalidDataException("Saved deck lacks an event identity.");
            var main = Cards(deck, "MainDeck"); var sideboard = Cards(deck, "Sideboard");
            foreach (var field in new[] { "CommandZone", "Companions" })
                if (deck.TryGetProperty(field, out var zone) && (zone.ValueKind != JsonValueKind.Array || zone.GetArrayLength() > 0))
                    throw new InvalidDataException("Unsupported deck zone; ordinary Limited main/sideboard counts required.");
            return [new ArenaDeckLogEvent.SavedDeckObserved(eventName.GetString()!, main, sideboard)];
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or OverflowException)
        { return [new ArenaDeckLogEvent.InvalidObservation("Deck observation unavailable: " + error.Message)]; }
    }

    private static IReadOnlyList<ArenaDeckCardCount> Cards(JsonElement deck, string field)
    {
        if (!deck.TryGetProperty(field, out var cards) || cards.ValueKind != JsonValueKind.Array || cards.GetArrayLength() > 300)
            throw new InvalidDataException($"'{field}' must be a bounded card/count array.");
        var result = new List<ArenaDeckCardCount>();
        foreach (var row in cards.EnumerateArray())
        {
            if (!row.TryGetProperty("cardId", out var id) || !id.TryGetInt32(out var number) || number < 1
                || !row.TryGetProperty("quantity", out var quantity) || !quantity.TryGetInt32(out var count) || count is < 1 or > 1000)
                throw new InvalidDataException("Deck card IDs and quantities must be positive bounded integers.");
            result.Add(new(ArenaCardIdentifier.Create(number), count));
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
