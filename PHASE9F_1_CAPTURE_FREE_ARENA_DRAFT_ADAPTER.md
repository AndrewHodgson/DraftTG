# Phase 9F.1 — Capture-Free Windows Arena Draft Adapter Foundation

Date: 2026-10-13. Tested tree: base commit `a526df46a498f97558ede291a2caccf74351aa9f` plus the uncommitted Phase 9F.1 changes and preserved pre-existing 9E.2B failure-analysis changes. Output directory: `bin/Phase9F1/` (isolated from the running desktop app).

**Result: the 9F.1 foundation proof succeeded.** The read-only debug command independently traversed the live Quick Draft object graph and returned 14 ordered offered GrpIds and `list` view mode. This establishes the requested one-shot capability on one explicit build; it does not establish transition robustness or production readiness. WGC remains production localization authority. Contextual scoring and `contextual-pick-score-v1.6` / `pick-score-tiers-v2` were not changed by this phase.

## 1. Scope

Windows x64 only, read-only process inspection, one offered-pack snapshot. No screenshots, capture, image/artwork matching, OCR, comparator sorting, overlay badges, right-deck/sideboard geometry, post-draft localization, or target execution. No macOS memory reader. GrpIds are returned in `_allCardViews` order, with no metadata sort reconstruction.

Successful observations are in [live-quick-draft-read.json](docs/evidence/phase9f1/live-quick-draft-read.json) and [the final-binary recheck](docs/evidence/phase9f1/live-quick-draft-final-read.json). They are repeated reads of one offered pack, not independent pack/session coverage. They are memory evidence for 9F.1, not qualifying deterministic-placement evidence for the Phase 9E.2B promotion gate.

## 2. Architecture

The adapter lives in `DraftTG.ArenaIntegration/Memory/Windows` and uses only .NET platform libraries. It does not depend on Avalonia, Application, Domain, Data, scoring, log state, or capture.

| File | Responsibility |
| --- | --- |
| `ArenaProcessDiscovery.cs` | Shared existing `MTGA` discovery convention; caller disposes `Process` results |
| `Memory/ArenaDraftMemorySnapshot.cs` | Immutable snapshot/status/build identity contracts, no native addresses required by consumers |
| `Memory/ArenaDraftMemoryFormatter.cs` | Text diagnostics; failure output does not display partial offered cards |
| `Memory/Windows/ArenaProcessReader.cs` | Read-only handle, architecture, modules, bounded reads, readable-region checks, disposal |
| `Memory/Windows/RemoteMemory.cs` | Narrow typed primitives, pointer/count/string bounds, structured failures |
| `Memory/Windows/ArenaMemoryCompatibility.cs` | Exact-build allowlist and separate versioned Mono layout |
| `Memory/Windows/ArenaMonoRuntime.cs` | Passive bootstrap, loaded-image checks, initialized class/field/list metadata |
| `Memory/Windows/ArenaDraftObjectGraph.cs` | Current scene, view, offered-list traversal, consistency checks |
| `Memory/Windows/ArenaDraftMemoryReader.cs` | Fail-closed orchestration; failures discard the pack |
| `DraftTG.App/ArenaMemoryDraftCommand.cs` | Debug CLI only; returns before Avalonia lifetime initialization |

`Program.cs` adds an early command branch. The sole production capture edit replaces `Process.GetProcessesByName("MTGA")` with the shared discovery helper performing exactly that call. No localization lifecycle or production provider wiring changes. The existing Player.log and Arena database discovery remain independent; neither is needed by this command. Existing raw memory-audit scans are not used as typed object evidence.

## 3. Public / independent source provenance

Windows process semantics come from Microsoft's [access-rights documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights), [ReadProcessMemory](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory), [VirtualQueryEx](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex), [IsWow64Process2](https://learn.microsoft.com/en-us/windows/win32/api/wow64apiset/nf-wow64apiset-iswow64process2), and [EnumProcessModulesEx](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-enumprocessmodulesex).

Mono semantic references are public Unity Mono source, pinned at `75711b4363fa58f05f953b3f95400d4d61c0ca35`: [domain.c](https://github.com/Unity-Technologies/mono/blob/75711b4363fa58f05f953b3f95400d4d61c0ca35/mono/metadata/domain.c), [assembly.c](https://github.com/Unity-Technologies/mono/blob/75711b4363fa58f05f953b3f95400d4d61c0ca35/mono/metadata/assembly.c), [class-private-definition.h](https://github.com/Unity-Technologies/mono/blob/75711b4363fa58f05f953b3f95400d4d61c0ca35/mono/metadata/class-private-definition.h), [class-internals.h](https://github.com/Unity-Technologies/mono/blob/75711b4363fa58f05f953b3f95400d4d61c0ca35/mono/metadata/class-internals.h), [metadata-internals.h](https://github.com/Unity-Technologies/mono/blob/75711b4363fa58f05f953b3f95400d4d61c0ca35/mono/metadata/metadata-internals.h), [object.h](https://github.com/Unity-Technologies/mono/blob/75711b4363fa58f05f953b3f95400d4d61c0ca35/mono/metadata/object.h), [object-internals.h](https://github.com/Unity-Technologies/mono/blob/75711b4363fa58f05f953b3f95400d4d61c0ca35/mono/metadata/object-internals.h), and [mono-internal-hash.h](https://github.com/Unity-Technologies/mono/blob/75711b4363fa58f05f953b3f95400d4d61c0ca35/mono/utils/mono-internal-hash.h).

That public source commit is a semantic reference, **not a claimed exact source revision of the installed native DLL**. The profile's actual offsets and globals were independently corroborated by offline PE/export/disassembly inspection of the exact locally installed DLL hash. Exported getters and their lookup call paths were read as bytes, never executed. No Untapped code, binary, proprietary constants, or runtime target invocation was used.

Local game types, field names, inheritance, value/reference semantics, and getter IL were inspected using the existing `tools/ArenaManagedStaticAudit` PEReader/MetadataReader tool. Target assemblies were never loaded into the CLR for execution. Reproduce representative inspection:

```powershell
$managed = 'C:\Program Files (x86)\Steam\steamapps\common\MTGA\MTGA_Data\Managed'
dotnet run --project .\tools\ArenaManagedStaticAudit -- inventory $managed
dotnet run --project .\tools\ArenaManagedStaticAudit -- dump "$managed\Core.dll" '^WrapperController$|^SceneLoader$|DraftContentController$|DraftPackHolder$|^DraftPackCardView$|^MetaCardView$|DraftDeckView$|^CardCollection$|^CardCollectionItem$'
dotnet run --project .\tools\ArenaManagedStaticAudit -- dump "$managed\SharedClientCore.dll" '^GreClient.CardData.CardData$|^GreClient.CardData.CardPrintingData$|^Wotc.Mtga.Cards.Database.CardPrintingRecord$'
```

The companion offline script [MonoRuntimeProfileAudit.py](tools/ArenaManagedStaticAudit/MonoRuntimeProfileAudit.py) emits the independently located accessor/helper instructions only for the known DLL hash. It requires `pefile` and `capstone` in an audit-only Python environment. These packages are not DraftTG dependencies.

```powershell
python .\tools\ArenaManagedStaticAudit\MonoRuntimeProfileAudit.py 'C:\Program Files (x86)\Steam\steamapps\common\MTGA\MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll'
```

## 4. Windows process permissions

`OpenProcess(0x00000410, false, pid)` requests exactly `PROCESS_QUERY_INFORMATION (0x400) | PROCESS_VM_READ (0x10)`. No privilege elevation/debug privilege, write, operation, create-thread, or all-access capability. `SafeProcessHandle` owns deterministic disposal; discovery `Process` wrappers are also disposed. Module values returned by PSAPI are identifiers, not separately owned handles.

Only native imports in the reader: `OpenProcess`, `IsWow64Process2`, `ReadProcessMemory`, `VirtualQueryEx`, `EnumProcessModulesEx`, `GetModuleFileNameEx`, and `GetModuleInformation`. Module enumeration uses `LIST_MODULES_ALL (3)` and caps the array at 2,048 modules. Win32 error 5 maps to `AccessDenied`; other native read errors become `TransientReadFailure`.

## 5. MTGA build fingerprint

Installation/process facts:

- Process name: `MTGA`.
- Executable: `C:\Program Files (x86)\Steam\steamapps\common\MTGA\MTGA.exe`.
- Architecture: AMD64/x64 (`IMAGE_FILE_MACHINE_AMD64 = 0x8664`). The live process is checked with `IsWow64Process2`.
- UnityPlayer file version: `6000.3.14.14060607` (Unity 6000.3.14).
- Mono: `MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll` under the installation root.
- Managed directory: `MTGA_Data\Managed` under the installation root.

| File | SHA256 | MVID |
| --- | --- | --- |
| `MTGA.exe` | `E22C729EEF75383888CB3D26A8CB4C2372BC6ACDAE7D7D9215C5020BA257FA37` | native |
| `UnityPlayer.dll` | `BBDF3E73522223EA45C3A54049B716F9DA3E632994636734C5D53326AA2C36D0` | native |
| `mono-2.0-bdwgc.dll` | `69DD7CA153D9BD718ED6176C1E6FF065721A9408964384A421602F7D5527A294` | native |
| `Core.dll` | `947441EECC9A9231503AF434AD7970E6AF0BC2C56148D56991BBA5B6C76458E6` | `304dcc50-070c-4610-9196-b6124619943f` |
| `SharedClientCore.dll` | `FF02A80282F36C45D6A639C6DC5013EFF16406BCE6EB0EF9985B59E1C3244B33` | `5028378c-e315-416c-92ef-a565601d5b8d` |
| `Assembly-CSharp.dll` | `9B155EAAF58561B739AF6AC175E4CF3E59094E901D256B880912306F6ED4BE42` | `dfb5e0ff-0b6c-4d9f-aa38-10f3cbf5ce2e` |
| `mscorlib.dll` | `6C76E06E6D44CF0F39BF59E6A90770968B70093AD4C1572DFBC5E46E431C5FBF` | `41d29b35-2f6a-475a-b1bf-7c6628b82790` |

Machine-readable fingerprint: [supported-build.json](docs/evidence/phase9f1/supported-build.json). Code authority is `ArenaMemoryCompatibility.Supported`; the JSON records the tested identity. All components must match exactly. Paths may differ across installations; hashes, MVIDs, architecture, and version may not. Unknown builds stop after safe process/module identification and local-file inspection, before runtime/object reads. Loaded-image GUIDs are additionally checked against each fingerprinted MVID, preventing an updated on-disk assembly from being silently substituted for an older loaded image.

## 6. Mono compatibility profile and every versioned offset

Profile: `unity-mono-x64-69dd7ca1-arena-2026-10-13-v1`. Only valid for the fingerprint above. These are **native runtime** layout constants; no game or `List<T>` managed-field offsets are hardcoded.

All hexadecimal offsets below are relative to the named structure; RVAs are relative to the loaded Mono module base. The native disassembly RVAs identify the independently inspected installed binary instructions. Public source supplies structure semantics.

| Constant / member | Offset / value | Independent provenance |
| --- | --- | --- |
| Root-domain pointer global | RVA `0x73FF80` | `mono_get_root_domain` at `0xA6040`: RIP-relative load at `0xA6040`, next IP `0xA6047` + displacement `0x699F39`; public `domain.c` root accessor |
| Loaded-assemblies GSList global | RVA `0x73FF90` | `mono_assembly_foreach` load at `0xB53C8`: next IP `0xB53CF` + displacement `0x68ABC1`; public `assembly.c` |
| GSList `data`, `next` | `0`, `8` | Public assembly iteration / GLib list semantics, corroborated native iteration at `0xB5390` |
| `MonoAssembly.image` | `0x60` | `mono_assembly_get_image` getter `0xB56A0` |
| `MonoImage.name`, `guid` | `0x30`, `0x58` | `mono_image_get_name` `0x14D3E0`, `mono_image_get_guid` `0x14D400` |
| `MonoImage.class_cache` | `0x4D0` | TypeDef lookup path `0xC5480` calls internal hash lookup with `image + 0x4D0` at `0xC5513`; public `metadata-internals.h` class cache |
| Internal hash `size`, `num_entries`, `table` | `0x18`, `0x1C`, `0x20` | Public `mono-internal-hash.h`; installed lookup `0x71310` and resize path |
| Class-cache `next_class_cache` | `0x108` | Native next-link callback `0x1454E0` computes `class + 0x108`; public `MonoClassDef` |
| `MonoClass.rank`, `class_kind` | `0x1A`, `0x1B` | Exported `mono_class_get_rank` at `0xC11B0`; public class layout and kind dispatch in field-count helper `0xCF900` |
| Class kinds DEF / GTD / GINST / ARRAY | `1 / 2 / 3 / 5` | Public `MonoTypeKind`, native dispatch/allocation paths at `0xCF900`, `0xC5480`, `0xC4DC0` |
| `MonoClass.instance_size` | `0x1C` | `mono_class_instance_size` `0xBAE00`, returned load at `0xBAE14` |
| Class metadata bitfield | `0x28`; fields initialized `0x80000`, dynamic type `0x100000` | Public flag semantics; setup-fields readiness check `0xC4E19`, get-fields special-type check; uninitialized/dynamic types rejected |
| `MonoClass.parent` | `0x30` | `mono_class_get_parent` `0xC1190` |
| `MonoClass.image` | `0x40` | `mono_class_get_image` `0xC1120` |
| `MonoClass.name`, `name_space` | `0x48`, `0x50` | `mono_class_get_name` `0xC11C0`, namespace getter `0xC11D0` |
| `MonoClass.type_token` | `0x58` | Class-cache key callback `0x1454D0`; TypeDef construction writes at `0xC567A` |
| `MonoClass.vtable_size` | `0x5C` | Static-field accessor `mono_vtable_get_static_field_data` `0x1C94E0` |
| `MonoClass.fields` | `0x98` | `mono_class_get_fields` `0xC12B0` iterator path |
| `MonoClass.runtime_info` | `0xD0` | Already-initialized vtable path in `mono_class_vtable`, at `0x1C60A7` |
| `MonoClassGenericInst.generic_class` | `0xF0` | Field-count helper `0xCF938`, setup fields `0xC4E02`; public generic-instance layout |
| `MonoGenericClass.container_class` | `0` | Native helper follows first pointer at `0xCF93F`; public generic-class definition |
| `MonoClassDef.field_count` | `0x100` | Field-count helper load at `0xCF961`, public ClassDef layout; GINST uses its GTD count |
| `MonoClassField` stride | `0x20` | Get-fields iterator increments by `0x20`; public four-member structure with x64 alignment |
| Field `type`, `name`, `parent`, `offset` | `0`, `8`, `0x10`, `0x18` | Public field definition; exported name/parent/offset getters `0xC1920`, `0xC1CA0`, `0xC1DF0` |
| `MonoType.attrs` / static attribute | `8` / mask `0x10` | Public MonoType / CLI field-attribute layout; live static vs instance fields independently corroborated |
| `MonoDomain.domain_id` | `0x94` | `mono_domain_get_id` getter `0xA7E00` |
| Runtime-info `max_domain`, `domain_vtables` | `0` (UInt16), `8` | Public `MonoClassRuntimeInfo`, installed initialized-vtable path; domain ID bounds checked |
| Object `vtable`, vtable `klass` | `0`, `0` | `mono_object_get_class` `0x1D34C0`; public object/vtable layouts |
| Vtable static-data flag | byte `0x30`, mask `4` | `mono_vtable_get_static_field_data` `0x1C94E0` |
| Vtable slots / static-data pointer | `0x48 + vtable_size * 8` | Same exported static-data accessor; no class initialization invoked |
| Pointer width / object header | `8` / `0x10` | x64 fingerprint and public `MonoObject` two-pointer layout; inline value fields subtract the object header |
| Array `bounds`, `max_length`, `vector` | `0x10`, `0x18`, `0x20` | Public `object-internals.h`, `mono_array_length` `0x1D7FB0`; native-sized length, rank-one vectors only |
| String `length`, `chars` | `0x10`, `0x14` | Public object layout, `mono_string_length` `0x1D7FA0`, `mono_string_chars` `0x1D7F90` |

TypeDef tokens are obtained from exact-fingerprint local managed metadata, not stored as game-layout constants. Class lookup enumerates only the image's bounded class cache, checks token/name/namespace/image, and rejects cache count/link changes. Missing initialized metadata is a decline: the reader never calls a Mono setup function to populate it.

## 7. Managed object graph

The final root is independently supported by local IL: `SceneLoader.GetSceneLoader` calls `WrapperController.get_Instance`, then `get_SceneLoader`. The implementation reads their backing fields passively; it does not execute these methods.

```text
Core: WrapperController.<Instance>k__BackingField (static)
  -> <SceneLoader>k__BackingField
  -> SceneLoader.<CurrentNavContent>k__BackingField
  -> Wotc.Mtga.Wrapper.Draft.DraftContentController (_active)
     -> _activeDeckView -> DraftDeckView._isColumnView
     -> _draftPackHolder -> DraftPackHolder._allCardViews
        -> List<DraftPackCardView> (_items, _size, _version)
        -> each DraftPackCardView, in stored list order
        -> inherited MetaCardView._visualCard
        -> SharedClientCore: GreClient.CardData.CardData._printing
        -> GreClient.CardData.CardPrintingData.Record (INLINE value type)
        -> Wotc.Mtga.Cards.Database.CardPrintingRecord.GrpId (UInt32)
```

`DraftPackCardView` inherits `CDCMetaCardView`, which inherits `MetaCardView`; inherited field lookup follows the runtime parent chain. Logical count evidence comes from `DraftPackHolder._packCollection -> CardCollection._itemList -> CardCollectionItem.<Quantity>k__BackingField`, summing quantities so duplicate card identities remain valid.

Representative local field declarations: Wrapper singleton `0x04001E1B`, SceneLoader backing field `0x04001E20`, current navigation content `0x04001B5C`, draft holder `0x04004A82`, active deck view `0x04004A8E`, column flag `0x04004ADA`, offered views `0x04004B22`, visual card `0x04001067`, printing `0x0400038D`, inline Record `0x040003CB`, GrpId `0x04000F24`. These tokens document static inspection; they are not runtime offsets.

An initial live attempt found no SceneLoader in Pantry service references and correctly returned `ObjectGraphIncomplete` with no cards. The narrower WrapperController root replaced that approach. No service dictionary traversal remains in the adapter.

## 8. Snapshot consistency rules

- Only the current navigation controller may qualify. Require its exact Core image/type and active flag; reject SceneLoader loading and pack-holder initializing/animating states.
- Require holder, active view, offered list, every view/visual/printing object, and initialized field metadata. Validate expected runtime type/image at each graph stage.
- Offered list count must be 1–32, inside its array capacity. Require rank-one zero-based arrays, valid aligned object/vtable/class pointers, and unique view-object pointers. Duplicate GrpIds are legal.
- Require every occurrence to resolve a nonzero GrpId within positive Int32 range. Never return partial ordering on failure.
- Require summed logical offered-item quantities to equal the view count. Verify logical-list version, entries, and quantities again afterward.
- Read offered count/items/version before traversal; verify them after traversal. Re-read every ordered view pointer, `_visualCard`, `_printing`, and GrpId.
- Re-read wrapper singleton, SceneLoader, current controller, active flag, holder, list, logical collection, active view, view mode, loading and animation flags before accepting.
- Snapshot identity includes observed scene/holder/list pointers, list version and count. It is diagnostic identity for this process instance, not a durable draft/session ID.

This is not an atomic process snapshot: the game remains running, and mutations that change then revert between reads cannot be ruled out. The guards detect observed inconsistency and fail closed; later transition validation is still necessary.

## 9. Failure modes

Statuses: `Success`, `NotRunning`, `UnsupportedPlatform`, `UnsupportedBuild`, `DraftSceneNotActive`, `AccessDenied`, `MetadataUnavailable`, `TransientReadFailure`, `ObjectGraphIncomplete`, `InvalidPackState`. Failure snapshots have an empty offered-card list, no view/pack identity, and an exact diagnostic. If an active draft was established before a subsequent decline, `IsDraftSceneActive` records that fact without claiming a successful pack.

Unknown or unreadable build files cannot authorize runtime traversal. Missing Mono/managed metadata, uninitialized classes, stale/deallocated objects, process exit, changing lists/anchors, invalid booleans/counts/IDs, and empty waiting/completed packs decline. Multiple MTGA processes require an explicit `--pid`; an arbitrary non-MTGA PID is rejected.

Per-read cap: 65,536 bytes. User-address range: `0x10000..0x00007FFFFFFFFFFF`, with overflow/end checks. Aggregate remote-read budget: 16 MiB and 15 seconds from attach. Strings: C strings at most 4,096 bytes (normal metadata names 512); managed UTF-16 strings at most 4,096 code units. Class buckets cap 8,192, entries 65,536; fields cap 1,024; inheritance depth 32; assembly-chain nodes 1,024. No unbounded raw-memory scan or retry loop. Each region must be committed, readable, and not guarded; a partial native read is discarded.

## 10. CLI usage and actual live evidence

From the repository root, build without disturbing the running desktop binary:

```powershell
dotnet build .\DraftTG.sln --no-restore -p:OutputPath=bin/Phase9F1/
dotnet .\src\DraftTG.App\bin\Phase9F1\DraftTG.App.dll --arena-memory-draft-once
```

Both `--arena-memory-draft` and `--arena-memory-draft-once` perform one read and exit. Optional `--pid <MTGA PID>` selects among multiple game processes; `--json` prints the full machine-readable snapshot/fingerprint. No polling or implicit desktop startup. Exit codes: 0 for successful pack, inactive scene or not running; 1 for invalid CLI options; 2 for declined/unsupported reads. Check `Status` as well as the exit code.

For saved diagnostic output:

```powershell
dotnet .\src\DraftTG.App\bin\Phase9F1\DraftTG.App.dll --arena-memory-draft-once --json | Set-Content -Encoding utf8 .\artifacts\arena-memory-draft-once.json
```

**Live validation actually performed:** 2026-10-13, user-reopened Quick Draft, PID 46632, supported fingerprint. The first run before reopening returned `NotRunning`, with zero bytes read. The unsuccessful service-root attempt returned no cards. Repeated subsequent complete reads independently returned the same 14 identities and list version, without user/game interaction by the agent:

```text
Arena memory: connected
Draft scene: active
View: list
Pack count: 14
Ordered offered cards:
  0  GrpId=106401
  1  GrpId=106292
  2  GrpId=106451
  3  GrpId=106486
  4  GrpId=106498
  5  GrpId=106347
  6  GrpId=106364
  7  GrpId=106240
  8  GrpId=106289
  9  GrpId=106327
  10  GrpId=106369
  11  GrpId=106376
  12  GrpId=106418
  13  GrpId=106423
```

The full text formatter additionally prints the complete `Build` JSON, runtime profile, PID, identity, status, timestamp, elapsed milliseconds and bytes read. [Exact observed text output](docs/evidence/phase9f1/live-quick-draft-read.txt) is saved from the text run at `2026-10-13T13:20:40.1005844Z` (403.2 ms). The saved initial JSON snapshot at `2026-10-13T13:10:51.8682489Z` took 419.1 ms and read 398,007 bytes; identity was `scene:1B26743F540/holder:1B26743F380/list:1B273F93D50/version:14/count:14`. After the final consistency guard and full-suite run, the final binary again returned `Success`, the same order/mode/identity, at `2026-10-13T13:25:15.0689533Z`: 399.6 ms, 398,024 bytes. The initial completed read took 448.9 ms. These timings include identification, hashing and bootstrap, not only offered-list traversal. Repeating the command later may return different identities/counts/state as Arena advances.

No pack/pick number or durable draft identifier was retrieved in this phase; the live count does not establish P1P1 specifically. No names are resolved, and the optional Player.log comparison command is deferred. Neither is necessary to obtain authoritative GrpIds directly from the ordered view list.

## 11. Tests and build

Added **13 focused test cases**, using small memory fixtures for four loaded image identities, one generic List/array and three field descriptors; no entire MTGA process mock. Coverage: exact permissions; address/count/read bounds; module selection; all fingerprint components and zero reads on unknown build; loaded MVID mismatch; C/UTF-16 strings; dynamic generic-field offsets; array capacity/duplicate view pointers; version changes; complete occurrence requirements; success/failure CLI formatting and order retention.

Executed:

```powershell
dotnet build .\DraftTG.sln --no-restore -p:OutputPath=bin/Phase9F1/
dotnet test .\DraftTG.sln --no-build --no-restore -p:OutputPath=bin/Phase9F1/ --logger 'trx;LogFilePrefix=phase9f1' --results-directory .\artifacts\phase9f1-tests
```

Build: **0 warnings, 0 errors**. Full solution: **1,246 passed, 0 failed, 0 skipped** (Domain 56; Data 160; ArenaIntegration 140; Application 274; RecommendationEngine 329; App 287). Existing 9E.2B/scoring tests remain intact. TRX files are in the ignored `artifacts/phase9f1-tests` directory.

## 12. Known limitations

One build, one Quick Draft, one 14-card offered state, `list` mode physically observed. Column-mode output is covered by formatting tests, not live mode validation. P1P2, smaller packs, pack transitions, moving/resizing, process restart while reading, leaving/re-entering draft and completed-draft transitions are unvalidated. The game was reopened between the initial audit and successful reads, but this is not a restart robustness test of an in-flight reader.

The reader's count bounds permit 1–32 cards; this is a memory plausibility guard, not a deterministic geometry envelope or evidence that all counts have been physically validated. No window size/DPI/coordinates are read or inferred. Future builds are unsupported until independently inspected and explicitly profiled. Native layouts may differ despite similar Unity version strings; exact hashes are required.

## 13. Security / read-only guarantees

No write-capable process handle, write API, remote allocation, remote thread, debugger attachment, injection, privilege adjustment, target Mono invocation or managed method execution exists in this path. `ReadProcessMemory` reads remote memory into local buffers only. Static assembly/disassembly audits inspect local files only. The command exits before desktop/capture initialization. Target state remains unsynchronized and unmodified; errors and races cause availability declines. This guarantee is about the implemented capabilities, not a claim of game policy approval.

## 14. What remains for 9F.2

Do not start 9F.2 automatically. Before considering any provider/overlay integration, perform a read-only physical matrix with user-driven Arena actions: P1P1/P1P2, pack transitions, naturally encountered <=12/14-card packs, list/column modes, move/resize, process restart, leaving/re-entering draft and completed-draft/deckbuilder transitions. Save CLI JSON per observation and record the physical step externally. Confirm correct availability declines during transitions and no stale offered order afterward.

The root-domain/metadata bootstrap blocker was resolved independently; no known blocker remains to the narrow one-shot proof. Remaining blockers to production use are broad live consistency validation, additional build compatibility, durable session/pack identity, and an explicitly reviewed integration design. Optional log identity comparison must remain diagnostic and must not replace memory order. Existing WGC remains authoritative until a separate architecture decision is explicitly authorized.

## 15. What remains for in-draft deck / sideboard localization

No deck/sideboard identities, transforms, rectangles, DPI behavior or badge placement are implemented. A later separately authorized phase must establish the right-side view graph, occurrences, live display transforms, client-coordinate conversion and generation/invalidation behavior independently. Post-draft deckbuilder work is separate again. Reading `_activeDeckView._isColumnView` here supplies only a mode flag, not deck geometry or overlay authority.

**Recommendation:** accept 9F.1 as a debug-only supported-build foundation with actual live offered-pack retrieval. Preserve production paths and scoring. Stop here; schedule the physical transition matrix only when 9F.2 is explicitly requested.
