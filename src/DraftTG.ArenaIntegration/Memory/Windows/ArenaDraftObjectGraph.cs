using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DraftTG.ArenaIntegration.Memory.Windows;

internal sealed record DraftGraphResult(string ViewMode, uint[] GrpIds, string Identity, string? DraftId, string? EventName,
    string? DraftKind, int? Pack, int? Pick, string? CourseId)
{ internal ArenaUiMemorySnapshot? Ui { get; init; } internal ArenaOfferedCardIdentity[] Printings {get;init;}=[]; }

internal sealed class ArenaDraftObjectGraph(IRemoteMemory memory, ArenaMonoRuntime runtime, string managedDirectory, ulong unityPlayerBase = 0,
    Func<uint,uint,ArenaOfferedCardIdentity>? printingReconciler = null)
{
    private const string DraftNamespace = "Wotc.Mtga.Wrapper.Draft";
    private readonly Dictionary<(string, string, string), ulong> classes = [];
    private bool? uiAssemblySupported;
    private ArenaUnityGeometryReader? unityGeometry;
    internal DraftGraphResult Read(Action? onActive = null, Action<ArenaUiMemorySnapshot>? onUi = null)
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
        var visibleIds=new uint[views.Length];var representedIds=new uint[views.Length];var printings=new ArenaOfferedCardIdentity[views.Length];
        var cardObjects = new List<(ulong Visual, ulong Printing,ulong Logical,ulong LogicalPrinting)>();
        for (var i = 0; i < views.Length; i++)
        {
            runtime.Expect(views[i], "Core", "", "DraftPackCardView");
            var visual = ArenaMonoRuntime.Require(runtime.Reference(views[i], "_visualCard"), "Visual card");
            runtime.Expect(visual, "SharedClientCore", "GreClient.CardData", "CardData");
            var printing = ArenaMonoRuntime.Require(runtime.Reference(visual, "_printing"), "Card printing");
            runtime.Expect(printing, "SharedClientCore", "GreClient.CardData", "CardPrintingData");
            var id = memory.U32(runtime.Address(printing, "Record") + (ulong)grpField.Offset - MonoLayout.ObjectHeader);
            if (id == 0 || id > int.MaxValue) throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "Invalid offered GrpId.");
            var logical=ArenaMonoRuntime.Require(runtime.Reference(views[i],"_cardData"),"Represented offered card");
            runtime.Expect(logical,"SharedClientCore","GreClient.CardData","CardData");
            var logicalPrinting=ArenaMonoRuntime.Require(runtime.Reference(logical,"_printing"),"Represented offered printing");
            runtime.Expect(logicalPrinting,"SharedClientCore","GreClient.CardData","CardPrintingData");
            var logicalId=memory.U32(runtime.Address(logicalPrinting,"Record")+(ulong)grpField.Offset-MonoLayout.ObjectHeader);
            visibleIds[i]=id;
            representedIds[i]=logicalId;
            printings[i]=printingReconciler?.Invoke(logicalId,id)??new(logicalId,id,null,logicalId==id,false,"Exact-ID-only reader; no owned equivalence proof supplied");
            if(printingReconciler is not null&&!printings[i].Accepted)Torn(printings[i].Reason);
            cards[i] = printingReconciler is null?id:logicalId;
            cardObjects.Add((visual, printing,logical,logicalPrinting));
        }
        var ui = ReadUi(holder, deckView);
        onUi?.Invoke(ui);
        // No target synchronization is possible. Re-read all observed anchors, list entries, and identities.
        // Duplicate card identities are legal; duplicate view-object pointers are not.
        runtime.CheckList(state);
        var podState = runtime.ListState(semantic.OfferedList, 32);
        var ids = runtime.CardIdentifiers(podState);
        if(printingReconciler is not null)
        {
            try {printings=ArenaOfferedPrintingReconciliation.Match(ids,representedIds,visibleIds,printingReconciler);cards=printings.Select(p=>p.PodGrpId).ToArray();}
            catch(InvalidDataException ex){Torn(ex.Message);}
        }
        if (!ids.Order().SequenceEqual(cards.Order())) Torn("pod offered identities disagree with visual views: " + string.Join(',', ids) + " vs " + string.Join(',', cards));
        runtime.CheckList(logicalState);
        if (!runtime.References(logicalState).SequenceEqual(logicalItems)) Torn();
        for (var i = 0; i < logicalItems.Length; i++)
            if (memory.I32(runtime.Address(logicalItems[i], "<Quantity>k__BackingField")) != quantities[i]) Torn();
        if (!runtime.References(state).SequenceEqual(views)) Torn();
        for (var i = 0; i < views.Length; i++)
        {
            var (visual, printing,logical,logicalPrinting) = cardObjects[i];
            if (runtime.Reference(views[i], "_visualCard") != visual || runtime.Reference(visual, "_printing") != printing
                || memory.U32(runtime.Address(printing, "Record") + (ulong)grpField.Offset - MonoLayout.ObjectHeader) != visibleIds[i]
                ||runtime.Reference(views[i],"_cardData")!=logical||runtime.Reference(logical,"_printing")!=logicalPrinting
                ||memory.U32(runtime.Address(logicalPrinting,"Record")+(ulong)grpField.Offset-MonoLayout.ObjectHeader)!=representedIds[i]) Torn();
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
            semantic.DraftId, semantic.EventName, semantic.Kind, semantic.Pack, semantic.Pick, courseId)
            { Ui = ui,Printings=printings };
    }
    // Optional diagnostics do not weaken or replace the established offered-pack consistency checks.
    private ArenaUiMemorySnapshot ReadUi(ulong holder, ulong deckView)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ArenaPackLayoutMemory? layout = null;
        ArenaDeckViewMemory? deck = null;
        string? reason = null;
        try
        {
            var address = runtime.Address(holder, "_currentPackLayoutData");
            var type = Class("Core", "", "DraftPackLayoutData");
            ulong Field(string name) => address + (ulong)runtime.Field(type, name).Offset - MonoLayout.ObjectHeader;
            var length = checked((int)(Field("RowCount") + 4 - address));
            var first = memory.Read(address, length);
            layout = new(memory.I32(Field("ColumnCount")), memory.I32(Field("RowCount")), Float(Field("Scale")),
                Float(Field("Offset")), Float(Field("Offset") + 4), "DraftPackHolder._currentPackLayoutData");
            if (layout.Columns is < 1 or > 32 || layout.Rows is < 0 or > 32 || !float.IsFinite(layout.Scale)
                || layout.Scale <= 0 || !float.IsFinite(layout.OffsetX) || !float.IsFinite(layout.OffsetY)
                || !first.SequenceEqual(memory.Read(address, length)))
                throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Pack layout changed/invalid during passive read.");
        }
        catch (ArenaMemoryException ex) { layout = null; reason = ex.Message; }
        var deckClock = System.Diagnostics.Stopwatch.StartNew();
        double identityMs=0,geometryMs=0;
        try
        {
            var main = runtime.Reference(deckView, "_mainHolderInstance");
            var side = runtime.Reference(deckView, "_sideHolderInstance");
            var entries = ReadDeckHolder(main, "Main").Concat(ReadDeckHolder(side, "Sideboard")).ToArray();
            identityMs=deckClock.Elapsed.TotalMilliseconds;
            var geometryClock=System.Diagnostics.Stopwatch.StartNew();
            ArenaDeckHolderGeometryMemory? mainGeometry=null,sideGeometry=null;
            string? geometryReason=null;
            unityGeometry ??= new(memory,runtime,managedDirectory,unityPlayerBase);
            unityGeometry.Begin();
            ArenaDeckHolderGeometryMemory? Geometry(ulong owner,string area)
            {
                try
                {
                    var tileList=runtime.ListState(runtime.Reference(owner,"_tiles"),256);var tileObjects=runtime.References(tileList);
                    if(tileObjects.Length==0)return null;
                    var holderGeometry=unityGeometry.Holder(owner,tileObjects[0]);
                    var rows=entries.Where(e=>e.Area==area).Select(e=>e with { Geometry=unityGeometry.Row(tileObjects[e.Index],e.Index) }).ToArray();
                    runtime.CheckList(tileList);unityGeometry.Check();
                    entries=entries.Select(e=>e.Area==area?rows.Single(r=>r.Index==e.Index):e).ToArray();
                    return holderGeometry;
                }
                catch(ArenaMemoryException ex) { geometryReason=(geometryReason is null?"":geometryReason+"; ")+area+": "+ex.Message;return null; }
            }
            mainGeometry=Geometry(main,"Main");sideGeometry=Geometry(side,"Sideboard");
            try {unityGeometry.Check();}
            catch(ArenaMemoryException ex){mainGeometry=sideGeometry=null;entries=entries.Select(e=>e with {Geometry=null}).ToArray();geometryReason=ex.Message;}
            geometryMs=geometryClock.Elapsed.TotalMilliseconds;
            var finalEntries=ReadDeckHolder(main,"Main").Concat(ReadDeckHolder(side,"Sideboard")).ToArray();
            if(!entries.Select(e=>(e.Area,e.Index,e.GrpId,e.Quantity)).SequenceEqual(finalEntries.Select(e=>(e.Area,e.Index,e.GrpId,e.Quantity))))
                throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure,"Deck identity/quantity changed during geometry traversal");
            if (runtime.Reference(deckView, "_mainHolderInstance") != main || runtime.Reference(deckView, "_sideHolderInstance") != side)
                throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Deck holder changed during read.");
            // Cached managed bounds are not client-pixel bounds. Suppress anchors until native transform mapping is proven.
            float? mainScroll = null, sideScroll = null; bool? expanded = null;
            if (uiAssemblySupported ??= UiAssemblySupported())
            {
                mainScroll = ReadScroll(main); sideScroll = ReadScroll(side);
                var toggle = runtime.Reference(deckView, "ShowSideboardToggle");
                if (toggle != 0 && runtime.ClassName(runtime.ObjectClass(toggle)) == "Toggle")
                {
                    var value = memory.Bool(runtime.Address(toggle, "m_IsOn"));
                    if (runtime.Reference(deckView, "ShowSideboardToggle") != toggle || memory.Bool(runtime.Address(toggle, "m_IsOn")) != value)
                        throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Sideboard toggle changed during read.");
                    expanded = value;
                }
            }
            deck = new(entries, mainScroll, sideScroll, expanded, geometryReason)
                { MainGeometry=mainGeometry,SideGeometry=sideGeometry };
        }
        catch (ArenaMemoryException ex) { deck = new([], null, null, null, ex.Message); }
        return new(layout, deck, reason, clock.Elapsed.TotalMilliseconds) { DeckReadMilliseconds = deckClock.Elapsed.TotalMilliseconds,
            DeckIdentityMilliseconds=identityMs,DeckGeometryMilliseconds=geometryMs };
    }
    private ArenaDeckEntryMemory[] ReadDeckHolder(ulong holder, string area)
    {
        ArenaMonoRuntime.Require(holder, area + " holder");
        if (runtime.ClassName(runtime.ObjectClass(holder)) != "DraftListMetaCardHolder")
            throw new ArenaMemoryException(ArenaMemoryStatus.ObjectGraphIncomplete, area + " holder type: " + runtime.ClassName(runtime.ObjectClass(holder)) + "; row path not qualified.");
        runtime.Expect(holder, "Core", DraftNamespace, "DraftListMetaCardHolder");
        var list = ArenaMonoRuntime.Require(runtime.Reference(holder, "_tiles"), area + " tiles");
        var state = runtime.ListState(list, 256);
        var tiles = runtime.References(state);
        var entries = new ArenaDeckEntryMemory[tiles.Length];
        var observed = new List<(ulong Visual, ulong Printing, int Quantity)>();
        var grp = runtime.Field(Class("SharedClientCore", "Wotc.Mtga.Cards.Database", "CardPrintingRecord"), "GrpId");
        for (var i = 0; i < tiles.Length; i++)
        {
            runtime.Expect(tiles[i], "Core", "", "ListMetaCardView_Expanding");
            var quantity = memory.I32(runtime.Address(tiles[i], "<Quantity>k__BackingField"));
            if (quantity is < 1 or > 250) throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Deck quantity inactive/invalid; no partial deck result.");
            var visual = ArenaMonoRuntime.Require(runtime.Reference(tiles[i], "_cardData"), "Deck represented card");
            runtime.Expect(visual, "SharedClientCore", "GreClient.CardData", "CardData");
            var printing = ArenaMonoRuntime.Require(runtime.Reference(visual, "_printing"), "Deck printing");
            runtime.Expect(printing, "SharedClientCore", "GreClient.CardData", "CardPrintingData");
            var id = memory.U32(runtime.Address(printing, "Record") + (ulong)grp.Offset - MonoLayout.ObjectHeader);
            if (id == 0 || id > int.MaxValue) throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "Invalid deck GrpId.");
            entries[i] = new(i, id, quantity, area, null, $"DraftContentController._activeDeckView._{(area == "Main" ? "main" : "side")}HolderInstance._tiles[{i}]._cardData._printing.Record.GrpId");
            observed.Add((visual, printing, quantity));
        }
        runtime.CheckList(state);
        if (runtime.Reference(holder, "_tiles") != list || !runtime.References(state).SequenceEqual(tiles))
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Deck tiles changed during read.");
        for (var i = 0; i < tiles.Length; i++)
        {
            var x = observed[i];
            if (runtime.Reference(tiles[i], "_cardData") != x.Visual || runtime.Reference(x.Visual, "_printing") != x.Printing
                || memory.I32(runtime.Address(tiles[i], "<Quantity>k__BackingField")) != x.Quantity
                || memory.U32(runtime.Address(x.Printing, "Record") + (ulong)grp.Offset - MonoLayout.ObjectHeader) != entries[i].GrpId)
                throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Deck identity/quantity changed during read.");
        }
        return entries;
    }
    private float Float(ulong address) => BitConverter.ToSingle(memory.Read(address, 4));
    private bool UiAssemblySupported()
    {
        using var file = File.OpenRead(Path.Combine(managedDirectory, "UnityEngine.UI.dll"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)) == "4BF50572EE01F1A62147142CC9750058EC01DFB3790B46BE9B42D207A4BD3797";
    }
    private float? ReadScroll(ulong holder)
    {
        var scrollbar = runtime.Reference(holder, "_scrollbar");
        if (scrollbar == 0) return null;
        if (runtime.ClassName(runtime.ObjectClass(scrollbar)) != "Scrollbar") return null;
        var value = Float(runtime.Address(scrollbar, "m_Value"));
        if (!float.IsFinite(value) || value is < 0 or > 1 || runtime.Reference(holder, "_scrollbar") != scrollbar
            || Float(runtime.Address(scrollbar, "m_Value")) != value)
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Scrollbar changed/invalid during read.");
        return value;
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
    private static void Torn(string? detail = null, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Draft scene/view/list/card identity changed during read; snapshot discarded. " + (detail ?? $"Guard line {line}"));
}

internal static class ArenaDraftSnapshotRules
{
    internal static void RequireComplete(int expectedCount, IReadOnlyList<uint> cards)
    {
        if (expectedCount is < 1 or > 32 || cards.Count != expectedCount || cards.Any(id => id == 0 || id > int.MaxValue))
            throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "Complete sane offered-pack identities required; no partial success.");
    }
}
