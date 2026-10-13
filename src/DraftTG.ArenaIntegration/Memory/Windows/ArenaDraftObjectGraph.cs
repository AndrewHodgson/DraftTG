using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DraftTG.ArenaIntegration.Memory.Windows;

internal sealed record DraftGraphResult(string ViewMode, uint[] GrpIds, string Identity, string? DraftId, string? EventName,
    string? DraftKind, int? Pack, int? Pick, string? CourseId);

internal sealed class ArenaDraftObjectGraph(IRemoteMemory memory, ArenaMonoRuntime runtime, string managedDirectory)
{
    private const string DraftNamespace = "Wotc.Mtga.Wrapper.Draft";
    private readonly Dictionary<(string, string, string), ulong> classes = [];
    internal DraftGraphResult Read(Action? onActive = null)
    {
        runtime.CheckRootDomain();
        var wrapperClass = Class("Core", "", "WrapperController");
        var wrapper = ArenaMonoRuntime.Require(runtime.StaticReference(wrapperClass, "<Instance>k__BackingField"), "WrapperController singleton");
        runtime.Expect(wrapper, "Core", "", "WrapperController");
        var loader = ArenaMonoRuntime.Require(runtime.Reference(wrapper, "<SceneLoader>k__BackingField"), "Current SceneLoader");
        runtime.Expect(loader, "Core", "", "SceneLoader");
        if (memory.Bool(runtime.Address(loader, "_isLoading")))
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "SceneLoader is loading.");
        var controller = runtime.Reference(loader, "<CurrentNavContent>k__BackingField");
        if (controller == 0 || runtime.ClassName(runtime.ObjectClass(controller)) != "DraftContentController")
            throw new ArenaMemoryException(ArenaMemoryStatus.DraftSceneNotActive, "Current navigation content is not a draft scene.");
        runtime.Expect(controller, "Core", DraftNamespace, "DraftContentController");
        if (!memory.Bool(runtime.Address(controller, "_active")))
            throw new ArenaMemoryException(ArenaMemoryStatus.DraftSceneNotActive, "Draft controller is inactive.");
        onActive?.Invoke();
        var limitedEvent = ArenaMonoRuntime.Require(runtime.Reference(controller, "_limitedEvent"), "Limited event");
        runtime.Expect(limitedEvent, "Core", "Wotc.Mtga.Events", "LimitedPlayerEvent");
        var pod = ArenaMonoRuntime.Require(runtime.Reference(limitedEvent, "<DraftPod>k__BackingField"), "Draft pod");
        var semantic = ReadSemantic(pod);
        var course = runtime.Reference(limitedEvent, "<CourseData>k__BackingField");
        var courseId = ReadCourse(course);
        var holder = ArenaMonoRuntime.Require(runtime.Reference(controller, "_draftPackHolder"), "Draft pack holder");
        runtime.Expect(holder, "Core", DraftNamespace, "DraftPackHolder");
        if (!memory.Bool(runtime.Address(holder, "_isInitialized")) || memory.Bool(runtime.Address(holder, "<IsAnimating>k__BackingField")))
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Draft pack holder is initializing/animating.");
        var deckView = ArenaMonoRuntime.Require(runtime.Reference(controller, "_activeDeckView"), "Active draft view");
        runtime.Expect(deckView, "Core", DraftNamespace, "DraftDeckView");
        var column = memory.Bool(runtime.Address(deckView, "_isColumnView"));
        var list = ArenaMonoRuntime.Require(runtime.Reference(holder, "_allCardViews"), "Ordered offered views");
        var state = runtime.ListState(list, 32);
        if (state.Count == 0) throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "No offered card views (waiting/picked/completed state).");
        var views = runtime.References(state);
        var collection = ArenaMonoRuntime.Require(runtime.Reference(holder, "_packCollection"), "Logical offered collection");
        runtime.Expect(collection, "Core", "", "CardCollection");
        var logicalList = ArenaMonoRuntime.Require(runtime.Reference(collection, "_itemList"), "Logical pack item list");
        var logicalState = runtime.ListState(logicalList, 32);
        var logicalItems = runtime.References(logicalState);
        var quantities = logicalItems.Select(item =>
        {
            runtime.Expect(item, "Core", "", "CardCollectionItem");
            var quantity = RemoteMemory.Count(memory.I32(runtime.Address(item, "<Quantity>k__BackingField")), 32, "Pack item quantity");
            if (quantity == 0) throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "Empty logical pack item.");
            return quantity;
        }).ToArray();
        if (quantities.Sum() != state.Count)
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Logical pack quantity disagrees with ordered view count.");
        var printingRecord = Class("SharedClientCore", "Wotc.Mtga.Cards.Database", "CardPrintingRecord");
        var grpField = runtime.Field(printingRecord, "GrpId");
        if (grpField.IsStatic) throw new ArenaMemoryException(ArenaMemoryStatus.MetadataUnavailable, "GrpId is unexpectedly static.");
        var cards = new uint[views.Length];
        var cardObjects = new List<(ulong Visual, ulong Printing)>();
        for (var i = 0; i < views.Length; i++)
        {
            runtime.Expect(views[i], "Core", "", "DraftPackCardView");
            var visual = ArenaMonoRuntime.Require(runtime.Reference(views[i], "_visualCard"), "Visual card");
            runtime.Expect(visual, "SharedClientCore", "GreClient.CardData", "CardData");
            var printing = ArenaMonoRuntime.Require(runtime.Reference(visual, "_printing"), "Card printing");
            runtime.Expect(printing, "SharedClientCore", "GreClient.CardData", "CardPrintingData");
            var id = memory.U32(runtime.Address(printing, "Record") + (ulong)grpField.Offset - MonoLayout.ObjectHeader);
            if (id == 0 || id > int.MaxValue) throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "Invalid offered GrpId.");
            cards[i] = id;
            cardObjects.Add((visual, printing));
        }
        // No target synchronization is possible. Re-read all observed anchors, list entries, and identities.
        // Duplicate card identities are legal; duplicate view-object pointers are not.
        runtime.CheckList(state);
        var podState = runtime.ListState(semantic.OfferedList, 32);
        var ids = runtime.CardIdentifiers(podState);
        if (!ids.Order().SequenceEqual(cards.Order())) Torn();
        runtime.CheckList(logicalState);
        if (!runtime.References(logicalState).SequenceEqual(logicalItems)) Torn();
        for (var i = 0; i < logicalItems.Length; i++)
            if (memory.I32(runtime.Address(logicalItems[i], "<Quantity>k__BackingField")) != quantities[i]) Torn();
        if (!runtime.References(state).SequenceEqual(views)) Torn();
        for (var i = 0; i < views.Length; i++)
        {
            var (visual, printing) = cardObjects[i];
            if (runtime.Reference(views[i], "_visualCard") != visual || runtime.Reference(visual, "_printing") != printing
                || memory.U32(runtime.Address(printing, "Record") + (ulong)grpField.Offset - MonoLayout.ObjectHeader) != cards[i]) Torn();
        }
        if (runtime.StaticReference(wrapperClass, "<Instance>k__BackingField") != wrapper || runtime.Reference(wrapper, "<SceneLoader>k__BackingField") != loader
            || memory.Bool(runtime.Address(loader, "_isLoading"))
            || runtime.Reference(loader, "<CurrentNavContent>k__BackingField") != controller
            || !memory.Bool(runtime.Address(controller, "_active"))
            || runtime.Reference(controller, "_draftPackHolder") != holder || runtime.Reference(holder, "_allCardViews") != list
            || runtime.Reference(controller, "_limitedEvent") != limitedEvent || runtime.Reference(limitedEvent, "<DraftPod>k__BackingField") != pod
            || ReadSemantic(pod) != semantic
            || runtime.Reference(limitedEvent, "<CourseData>k__BackingField") != course || ReadCourse(course) != courseId
            || runtime.Reference(holder, "_packCollection") != collection || runtime.Reference(collection, "_itemList") != logicalList
            || runtime.Reference(controller, "_activeDeckView") != deckView || memory.Bool(runtime.Address(deckView, "_isColumnView")) != column
            || !memory.Bool(runtime.Address(holder, "_isInitialized"))
            || memory.Bool(runtime.Address(holder, "<IsAnimating>k__BackingField"))) Torn();
        runtime.CheckList(state);
        runtime.CheckList(podState);
        if (!runtime.CardIdentifiers(podState).SequenceEqual(ids)) Torn();
        ArenaDraftSnapshotRules.RequireComplete(state.Count, cards);
        runtime.CheckRootDomain();
        return new(column ? "column" : "list", cards, $"scene:{controller:X}/pod:{pod:X}/holder:{holder:X}/list:{list:X}/version:{state.Version}/count:{state.Count}",
            semantic.DraftId, semantic.EventName, semantic.Kind, semantic.Pack, semantic.Pick, courseId);
    }
    private string? ReadCourse(ulong course)
    {
        if (course == 0) return null;
        runtime.Expect(course, "Core", "Wotc.Mtga.Events", "CourseData");
        // Local CourseData.Id is sequential System.Guid (mscorlib in the pinned fingerprint), 16 inline bytes.
        var id = new Guid(memory.Read(runtime.Address(course, "Id"), 16));
        return id == Guid.Empty ? null : id.ToString();
    }

    private (string? DraftId, string? EventName, string? Kind, int? Pack, int? Pick, ulong PickInfo, ulong OfferedList) ReadSemantic(ulong pod)
    {
        var kind = runtime.ClassName(runtime.ObjectClass(pod));
        if (kind is not ("BotDraftPod" or "HumanDraftPod"))
            throw new ArenaMemoryException(ArenaMemoryStatus.MetadataUnavailable, "Unsupported draft pod type.");
        runtime.Expect(pod, "Core", DraftNamespace, kind);
        var id = ArenaMonoRuntime.Require(runtime.Reference(pod, "<DraftId>k__BackingField"), "Draft ID");
        var draftId = memory.ManagedString(id, 256);
        if (string.IsNullOrWhiteSpace(draftId)) draftId = null;
        var eventName = ArenaMonoRuntime.Require(runtime.Reference(pod, "<InternalEventName>k__BackingField"), "Draft event name");
        if (kind == "HumanDraftPod")
        {
            var info = ArenaMonoRuntime.Require(runtime.Reference(pod, "_currentPickInfo"), "Human current pick info");
            runtime.Expect(info, "Wizards.Arena.Models", "Wizards.Unification.Models.Draft", "PickInfo");
            var humanPack = memory.I32(runtime.Address(info, "SelfPack"));
            var humanPick = memory.I32(runtime.Address(info, "SelfPick"));
            if (humanPack is < 1 or > 3 || humanPick is < 1 or > 32)
                throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Human pick-info coordinates outside active bounds.");
            return (draftId, memory.ManagedString(eventName, 256), "Human", humanPack, humanPick, info,
                ArenaMonoRuntime.Require(runtime.Reference(info, "PackCards"), "Human offered cards"));
        }
        var pack = memory.I32(runtime.Address(pod, "_currentPack"));
        var pick = memory.I32(runtime.Address(pod, "_currentPick"));
        if (pack is < 0 or > 2 || pick is < 0 or > 31)
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Bot pod pack/pick outside active bounds.");
        // Local BotDraftPod.GetCurrentDraftStateVisualData IL adds one to these fields for UI numbers.
        return (draftId, memory.ManagedString(eventName, 256), "Quick", pack + 1, pick + 1, 0,
            ArenaMonoRuntime.Require(runtime.Reference(pod, "_currentPackCards"), "Bot offered cards"));
    }

    private ulong Class(string assembly, string ns, string name)
    {
        if (classes.TryGetValue((assembly, ns, name), out var cached)) return cached;
        // Read TypeDef tokens from independently inspected, exact-fingerprint local metadata.
        using var stream = File.OpenRead(Path.Combine(managedDirectory, assembly + ".dll"));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var h in metadata.TypeDefinitions)
        {
            var t = metadata.GetTypeDefinition(h);
            if (metadata.GetString(t.Name) == name && metadata.GetString(t.Namespace) == ns)
                return classes[(assembly, ns, name)] = runtime.FindClass(assembly, ns, name, MetadataTokens.GetToken(h));
        }
        throw new ArenaMemoryException(ArenaMemoryStatus.MetadataUnavailable, $"Local type {assembly}:{ns}.{name} unavailable.");
    }
    private static void Torn() => throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Draft scene/view/list/card identity changed during read; snapshot discarded.");
}

internal static class ArenaDraftSnapshotRules
{
    internal static void RequireComplete(int expectedCount, IReadOnlyList<uint> cards)
    {
        if (expectedCount is < 1 or > 32 || cards.Count != expectedCount || cards.Any(id => id == 0 || id > int.MaxValue))
            throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "Complete sane offered-pack identities required; no partial success.");
    }
}
