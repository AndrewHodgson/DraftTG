using System.Globalization;
using System.Text.Json;

namespace DraftTG.ArenaIntegration;

/// <summary>Pure translation from one raw source event to zero or more Arena draft facts.</summary>
public sealed class ArenaDraftLogParser
{
    public IReadOnlyList<ArenaDraftLogEvent> Parse(ArenaLogSourceEvent sourceEvent)
    {
        if (sourceEvent is ArenaLogSourceEvent.SourceReset)
        {
            return [new ArenaDraftLogEvent.SourceReset()];
        }

        var line = ((ArenaLogSourceEvent.Line)sourceEvent).Text;
        if (IsIncomingBotDraftResponseMarker(line)) return [];
        var kind = Classify(line);
        if (kind is null) return [];

        var root = ParseOuterJson(line);
        return kind.Value switch
        {
            RecordKind.DraftNotify => [ParseDraftNotify(root)],
            RecordKind.HumanP1P1 => [ParseHumanP1P1(root)],
            RecordKind.HumanPick => [ParseHumanPick(root)],
            RecordKind.BotDraftStatus => ParseBotDraftStatus(root),
            RecordKind.BotDraftPick => [ParseBotDraftPick(root)],
            RecordKind.EventJoin => ParseEventJoin(root),
            RecordKind.HumanCompletion => [ParseHumanCompletion(root)],
            RecordKind.DeckSelection => ParseDeckSelection(root),
            RecordKind.CourseSnapshot => ParseCourseSnapshot(root),
            _ => []
        };
    }

    private static ArenaDraftLogEvent ParseDraftNotify(JsonElement root)
    {
        var fields = Embedded(root, "payload") ?? root;
        var draftIdentifier = RequiredDraftIdentifier(OptionalText(fields, "draftId"));
        var pack = RequiredInteger(fields, "SelfPack");
        var pick = RequiredInteger(fields, "SelfPick");
        var cards = RequiredText(fields, "PackCards");

        return new ArenaDraftLogEvent.PackPresented(
            new ArenaDraftPackPresentation(
                draftIdentifier,
                HumanCoordinate(pack, pick),
                ParseCommaSeparatedCards(cards)));
    }

    private static ArenaDraftLogEvent ParseHumanP1P1(JsonElement root)
    {
        JsonElement fields;
        var request = Embedded(root, "request");
        if (request is { } requestValue)
        {
            fields = Embedded(requestValue, "Payload") ?? requestValue;
        }
        else
        {
            fields = Embedded(root, "Payload") ?? root;
        }

        var pack = RequiredInteger(fields, "PackNumber");
        var pick = RequiredInteger(fields, "PickNumber");
        return new ArenaDraftLogEvent.PackPresented(
            new ArenaDraftPackPresentation(
                OptionalDraftIdentifier(OptionalText(fields, "DraftId")),
                HumanCoordinate(pack, pick),
                ParseCardArray(fields, "CardsInPack")));
    }

    private static ArenaDraftLogEvent ParseHumanPick(JsonElement root)
    {
        var fields = Embedded(root, "request") ?? root;
        var pack = RequiredInteger(fields, "Pack");
        var pick = RequiredInteger(fields, "Pick");
        return new ArenaDraftLogEvent.PickSubmitted(
            new ArenaDraftPickSubmission(
                RequiredDraftIdentifier(OptionalText(fields, "DraftId")),
                HumanCoordinate(pack, pick),
                ParseSelectedCards(fields, "GrpIds", "GrpId")));
    }

    private static IReadOnlyList<ArenaDraftLogEvent> ParseBotDraftStatus(JsonElement root)
    {
        var fields = ResolveBotContainer(root);
        var draftIdentifier = OptionalDraftIdentifier(OptionalText(fields, "DraftId", "draftId"));
        var eventName = Meaningful(OptionalText(fields, "EventName", "eventName"));
        if (string.Equals(OptionalText(fields, "DraftStatus"), "Completed", StringComparison.OrdinalIgnoreCase))
        {
            var completed = new List<ArenaDraftLogEvent>();
            AddQuickDraftPoolSnapshot(completed, fields, eventName, draftIdentifier, null, completed: true);
            completed.Add(new ArenaDraftLogEvent.DraftCompleted(new ArenaDraftCompletion(eventName, draftIdentifier)));
            return completed;
        }

        var events = new List<ArenaDraftLogEvent>();
        if (eventName is not null && ClassifyDraftMode(eventName) is { } mode)
        {
            events.Add(new ArenaDraftLogEvent.DraftStarted(
                new ArenaDraftStart(eventName, mode, draftIdentifier)));
        }

        if (fields.TryGetProperty("DraftPack", out _))
        {
            var pack = RequiredInteger(fields, "PackNumber");
            var pick = RequiredInteger(fields, "PickNumber");
            AddQuickDraftPoolSnapshot(
                events,
                fields,
                eventName,
                draftIdentifier,
                BotCoordinate(pack, pick));
            events.Add(new ArenaDraftLogEvent.PackPresented(
                new ArenaDraftPackPresentation(
                    draftIdentifier,
                    BotCoordinate(pack, pick),
                    ParseCardArray(fields, "DraftPack"))));
        }

        if (events.Count == 0)
        {
            throw Error(
                ArenaDraftLogParseErrorKind.UnsupportedPayloadShape,
                "BotDraft status");
        }
        return events;
    }

    private static void AddQuickDraftPoolSnapshot(
        ICollection<ArenaDraftLogEvent> events,
        JsonElement fields,
        string? eventName,
        ArenaDraftIdentifier? draftIdentifier,
        ArenaDraftCoordinate? coordinate,
        bool completed = false)
    {
        if (eventName is null
            || ClassifyDraftMode(eventName)?.Kind != ArenaDraftModeKind.Quick
            || (!completed && OptionalInteger(fields, "NumCardsToPick") != 1)
            || !fields.TryGetProperty("PickedCards", out var pickedCards))
        {
            return;
        }
        if (pickedCards.ValueKind != JsonValueKind.Array)
        {
            throw Error(
                ArenaDraftLogParseErrorKind.MalformedCardList,
                "PickedCards");
        }

        var identifiers = ParseCardArray(fields, "PickedCards", allowEmpty: true);
        events.Add(new ArenaDraftLogEvent.PickedCardsObserved(
            new ArenaPickedCardsSnapshot(draftIdentifier, coordinate, identifiers)));
    }

    private static ArenaDraftLogEvent ParseBotDraftPick(JsonElement root)
    {
        var fields = FirstEmbedded(root, "request", "response", "Payload") ?? root;
        var draftIdentifier = OptionalDraftIdentifier(OptionalText(fields, "DraftId", "draftId"));
        if (string.Equals(OptionalText(fields, "DraftStatus"), "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return new ArenaDraftLogEvent.DraftCompleted(
                new ArenaDraftCompletion(
                    Meaningful(OptionalText(fields, "EventName", "eventName")),
                    draftIdentifier));
        }

        var pickInfo = Embedded(fields, "PickInfo")
            ?? throw Missing("PickInfo");
        var pack = RequiredInteger(pickInfo, "PackNumber");
        var pick = RequiredInteger(pickInfo, "PickNumber");
        return new ArenaDraftLogEvent.PickSubmitted(
            new ArenaDraftPickSubmission(
                draftIdentifier,
                BotCoordinate(pack, pick),
                ParseSelectedCards(pickInfo, "CardIds", "CardId")));
    }

    private static IReadOnlyList<ArenaDraftLogEvent> ParseEventJoin(JsonElement root)
    {
        var fields = Embedded(root, "request") ?? root;
        var eventName = Meaningful(OptionalText(fields, "EventName", "eventName"))
            ?? throw Missing("EventName");
        var mode = ClassifyDraftMode(eventName);
        if (mode is null) return [];

        return [new ArenaDraftLogEvent.DraftStarted(
            new ArenaDraftStart(
                eventName,
                mode,
                OptionalDraftIdentifier(OptionalText(fields, "DraftId", "draftId")))
            {
                EntryRequestIdentifier = root.TryGetProperty("request", out _)
                    && fields.TryGetProperty("EntryCurrencyType", out _) && fields.TryGetProperty("EntryCurrencyPaid", out _)
                    ? Meaningful(OptionalText(root, "id")) : null
            })];
    }

    private static ArenaDraftLogEvent ParseHumanCompletion(JsonElement root)
    {
        var fields = FirstEmbedded(root, "response", "request", "payload") ?? root;
        return new ArenaDraftLogEvent.DraftCompleted(
            new ArenaDraftCompletion(
                Meaningful(OptionalText(fields, "EventName", "eventName")),
                OptionalDraftIdentifier(OptionalText(fields, "DraftId", "draftId"))));
    }

    private static JsonElement ResolveBotContainer(JsonElement root)
    {
        var container = FirstEmbedded(root, "request", "response", "Payload") ?? root;
        return Embedded(container, "Payload") ?? container;
    }

    private static IReadOnlyList<ArenaDraftLogEvent> ParseDeckSelection(JsonElement root)
    {
        // A module name nested in Courses, ModulePayload or a deck summary is not this response.
        if (!string.Equals(OptionalText(root, "CurrentModule"), "DeckSelect", StringComparison.Ordinal)) return [];
        var fields = Embedded(root, "Payload");
        if (fields is not { } payload
            || !string.Equals(OptionalText(payload, "Result"), "Success", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(OptionalText(payload, "DraftStatus"), "Completed", StringComparison.OrdinalIgnoreCase)) return [];
        var eventName = Meaningful(OptionalText(payload, "EventName"));
        var mode = eventName is null ? null : ClassifyDraftMode(eventName);
        if (mode is null) return [];
        return [new ArenaDraftLogEvent.DraftCompleted(new(eventName,
            OptionalDraftIdentifier(OptionalText(payload, "DraftId", "draftId")))
        {
            Origin = ArenaDraftCompletionOrigin.DeckSelection,
            Mode = mode,
            FinalPickedCards = mode.Kind == ArenaDraftModeKind.Quick && OptionalInteger(payload, "NumCardsToPick") == 1
                && payload.TryGetProperty("PickedCards", out _)
                ? ParseCardArray(payload, "PickedCards", allowEmpty: true) : null
        })];
    }

    /// <summary>
    /// EventGetCoursesV2 lists every course. After an Arena restart it is the only surviving evidence of a
    /// completed draft awaiting deck construction. Only an unambiguous single Premier/Traditional/Quick course in
    /// DeckSelect with a full 3x14 CardPool qualifies; anything else emits nothing.
    /// </summary>
    private static IReadOnlyList<ArenaDraftLogEvent> ParseCourseSnapshot(JsonElement root)
    {
        if (!root.TryGetProperty("Courses", out var courses) || courses.ValueKind != JsonValueKind.Array) return [];
        var candidates = new List<ArenaDraftLogEvent>();
        foreach (var course in courses.EnumerateArray())
        {
            if (course.ValueKind != JsonValueKind.Object) continue;
            try
            {
                if (!string.Equals(OptionalText(course, "CurrentModule"), "DeckSelect", StringComparison.Ordinal)) continue;
                var eventName = Meaningful(OptionalText(course, "InternalEventName"));
                var mode = eventName is null ? null : ClassifyDraftMode(eventName);
                if (mode?.Kind is not (ArenaDraftModeKind.Premier or ArenaDraftModeKind.Traditional or ArenaDraftModeKind.Quick)) continue;
                if (!course.TryGetProperty("CardPool", out _)) continue;
                var pool = ParseCardArray(course, "CardPool", allowEmpty: true);
                candidates.Add(new ArenaDraftLogEvent.DraftCompleted(new(eventName, null)
                {
                    Origin = ArenaDraftCompletionOrigin.CourseSnapshot,
                    Mode = mode,
                    CourseCardPool = pool
                }));
            }
            catch (ArenaDraftLogParseException) { } // A malformed unrelated course never becomes draft evidence.
        }
        return candidates.Count == 1 ? candidates : [];
    }

    private static JsonElement ParseOuterJson(string line)
    {
        var start = line.IndexOf('{');
        var end = line.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw Error(
                ArenaDraftLogParseErrorKind.MalformedOuterJson,
                "Relevant Arena record did not contain a complete JSON object.");
        }

        try
        {
            using var document = JsonDocument.Parse(line[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("The outer payload was not an object.");
            }
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw Error(ArenaDraftLogParseErrorKind.MalformedOuterJson, error.Message, inner: error);
        }
    }

    private static JsonElement? FirstEmbedded(JsonElement parent, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (parent.TryGetProperty(field, out _)) return Embedded(parent, field);
        }
        return null;
    }

    private static JsonElement? Embedded(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Object) return value.Clone();
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Error(
                ArenaDraftLogParseErrorKind.MalformedNestedJson,
                $"'{field}' was neither an object nor a string-encoded object.",
                field);
        }

        try
        {
            using var document = JsonDocument.Parse(value.GetString()!);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("The nested payload was not an object.");
            }
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw Error(
                ArenaDraftLogParseErrorKind.MalformedNestedJson,
                error.Message,
                field,
                inner: error);
        }
    }

    private static ArenaCardIdentifierList ParseCommaSeparatedCards(string value)
    {
        var parts = value.Split(',', StringSplitOptions.None);
        if (parts.Length == 0) throw MalformedCards(value);

        var identifiers = new List<ArenaCardIdentifier>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0
                || !int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                throw MalformedCards(value);
            }
            identifiers.Add(CreateCardIdentifier(number, trimmed));
        }
        return new ArenaCardIdentifierList(identifiers);
    }

    private static ArenaCardIdentifierList ParseCardArray(
        JsonElement parent,
        string field,
        bool allowEmpty = false)
    {
        if (!parent.TryGetProperty(field, out var array)) throw Missing(field);
        if (array.ValueKind != JsonValueKind.Array
            || (!allowEmpty && array.GetArrayLength() == 0))
        {
            throw MalformedCards(field);
        }

        return new ArenaCardIdentifierList(array.EnumerateArray().Select(ParseWireCardIdentifier));
    }

    private static ArenaCardIdentifierList ParseSelectedCards(
        JsonElement parent,
        string pluralField,
        string singularField)
    {
        if (parent.TryGetProperty(pluralField, out _)) return ParseCardArray(parent, pluralField);
        if (parent.TryGetProperty(singularField, out var single))
        {
            return new ArenaCardIdentifierList([ParseWireCardIdentifier(single)]);
        }
        throw Missing(pluralField);
    }

    private static ArenaCardIdentifier ParseWireCardIdentifier(JsonElement value)
    {
        string text;
        int number;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number))
        {
            text = number.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            text = value.GetString()!;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                throw Error(ArenaDraftLogParseErrorKind.InvalidArenaCardIdentifier, text);
            }
        }
        else
        {
            throw Error(ArenaDraftLogParseErrorKind.InvalidArenaCardIdentifier, value.GetRawText());
        }
        return CreateCardIdentifier(number, text);
    }

    private static ArenaCardIdentifier CreateCardIdentifier(int value, string text) =>
        ArenaCardIdentifier.TryCreate(value, out var identifier)
            ? identifier!
            : throw Error(ArenaDraftLogParseErrorKind.InvalidArenaCardIdentifier, text);

    private static ArenaDraftIdentifier RequiredDraftIdentifier(string? value)
    {
        if (value is null) throw Missing("DraftId");
        return OptionalDraftIdentifier(value)
            ?? throw Error(ArenaDraftLogParseErrorKind.InvalidDraftIdentifier, value);
    }

    private static ArenaDraftIdentifier? OptionalDraftIdentifier(string? value)
    {
        if (value is null) return null;
        return ArenaDraftIdentifier.TryCreate(value, out var identifier)
            ? identifier
            : throw Error(ArenaDraftLogParseErrorKind.InvalidDraftIdentifier, value);
    }

    private static ArenaDraftCoordinate HumanCoordinate(int pack, int pick) =>
        ArenaDraftCoordinate.TryCreate(pack, pick, out var coordinate)
            ? coordinate!
            : throw InvalidCoordinate(pack, pick);

    private static ArenaDraftCoordinate BotCoordinate(int pack, int pick)
    {
        if (pack < 0 || pick < 0 || pack == int.MaxValue || pick == int.MaxValue)
        {
            throw InvalidCoordinate(pack, pick);
        }
        return HumanCoordinate(pack + 1, pick + 1);
    }

    private static int RequiredInteger(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out var value)) throw Missing(field);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        throw Error(ArenaDraftLogParseErrorKind.UnsupportedPayloadShape, $"'{field}' was not an integer.", field);
    }

    private static int? OptionalInteger(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }
        throw Error(
            ArenaDraftLogParseErrorKind.UnsupportedPayloadShape,
            $"'{field}' was not an integer.",
            field);
    }

    private static string RequiredText(JsonElement parent, string field) =>
        OptionalText(parent, field) ?? throw Missing(field);

    private static string? OptionalText(JsonElement parent, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (!parent.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            if (value.ValueKind != JsonValueKind.String)
            {
                throw Error(
                    ArenaDraftLogParseErrorKind.UnsupportedPayloadShape,
                    $"'{field}' was not a string.",
                    field);
            }
            return value.GetString();
        }
        return null;
    }

    private static string? Meaningful(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ArenaDraftMode? ClassifyDraftMode(string eventName)
    {
        var normalized = eventName.ToLowerInvariant();
        if (normalized.Contains("sealed", StringComparison.Ordinal)) return null;
        if (normalized.Contains("picktwo", StringComparison.Ordinal)
            || normalized.Contains("pick_two", StringComparison.Ordinal)) return ArenaDraftMode.PickTwo;
        if (normalized.Contains("premierdraft", StringComparison.Ordinal)
            || normalized.Contains("premier_draft", StringComparison.Ordinal)) return ArenaDraftMode.Premier;
        if (normalized.Contains("traddraft", StringComparison.Ordinal)
            || normalized.Contains("traditionaldraft", StringComparison.Ordinal)
            || normalized.Contains("traditional_draft", StringComparison.Ordinal)) return ArenaDraftMode.Traditional;
        if (normalized.Contains("quickdraft", StringComparison.Ordinal)
            || normalized.Contains("quick_draft", StringComparison.Ordinal)
            || normalized.Contains("botdraft", StringComparison.Ordinal)) return ArenaDraftMode.Quick;
        return normalized.Contains("draft", StringComparison.Ordinal)
            ? ArenaDraftMode.Unknown(eventName)
            : null;
    }

    private static RecordKind? Classify(string line)
    {
        if (line.Contains("Draft.Notify", StringComparison.Ordinal)) return RecordKind.DraftNotify;
        if (line.Contains("EventPlayerDraftMakePick", StringComparison.Ordinal)
            || line.Contains("Event_PlayerDraftMakePick", StringComparison.Ordinal)) return RecordKind.HumanPick;
        if (line.Contains("BotDraftDraftPick", StringComparison.Ordinal)
            || line.Contains("BotDraft_DraftPick", StringComparison.Ordinal)) return RecordKind.BotDraftPick;
        if (line.Contains("BotDraftDraftStatus", StringComparison.Ordinal)
            || line.Contains("BotDraft_DraftStatus", StringComparison.Ordinal)) return RecordKind.BotDraftStatus;
        if (line.StartsWith("{\"Courses\":[", StringComparison.Ordinal)
            && line.Contains("\"DeckSelect\"", StringComparison.Ordinal)
            && line.Contains("\"CardPool\"", StringComparison.Ordinal)) return RecordKind.CourseSnapshot;
        if (line.Contains("\"CurrentModule\"", StringComparison.Ordinal)
            && line.Contains("\"DeckSelect\"", StringComparison.Ordinal)
            && line.Contains("\"Payload\"", StringComparison.Ordinal)) return RecordKind.DeckSelection;
        if (line.Contains("\"CurrentModule\"", StringComparison.Ordinal)
            && line.Contains("\"BotDraft\"", StringComparison.Ordinal)
            && line.Contains("\"Payload\"", StringComparison.Ordinal)) return RecordKind.BotDraftStatus;
        if (line.Contains("DraftCompleteDraft", StringComparison.Ordinal)) return RecordKind.HumanCompletion;
        if (line.Contains("EventJoin", StringComparison.Ordinal)
            || line.Contains("Event_Join", StringComparison.Ordinal)) return RecordKind.EventJoin;
        if (line.Contains("CardsInPack", StringComparison.Ordinal)) return RecordKind.HumanP1P1;
        return null;
    }

    private static bool IsIncomingBotDraftResponseMarker(string line)
    {
        foreach (var name in new[] { "BotDraftDraftStatus", "BotDraft_DraftStatus", "BotDraftDraftPick", "BotDraft_DraftPick" })
        {
            var markerStart = line.IndexOf($"<== {name}(", StringComparison.Ordinal);
            if (markerStart < 0) continue;
            var marker = line[markerStart..].Trim();
            if (marker.EndsWith(')') && !marker.Contains('{', StringComparison.Ordinal)
                && !marker.Contains('}', StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static ArenaDraftLogParseException Missing(string field) =>
        Error(ArenaDraftLogParseErrorKind.MissingRequiredField, field, field);

    private static ArenaDraftLogParseException MalformedCards(string detail) =>
        Error(ArenaDraftLogParseErrorKind.MalformedCardList, detail);

    private static ArenaDraftLogParseException InvalidCoordinate(int pack, int pick) =>
        new(
            ArenaDraftLogParseErrorKind.InvalidCoordinate,
            $"{pack}/{pick}",
            pack: pack,
            pick: pick);

    private static ArenaDraftLogParseException Error(
        ArenaDraftLogParseErrorKind kind,
        string detail,
        string? field = null,
        int? pack = null,
        int? pick = null,
        Exception? inner = null) =>
        new(kind, detail, field, pack, pick, inner);

    private enum RecordKind
    {
        DraftNotify,
        HumanP1P1,
        HumanPick,
        BotDraftStatus,
        BotDraftPick,
        EventJoin,
        HumanCompletion,
        DeckSelection,
        CourseSnapshot
    }
}
