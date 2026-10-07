# Arena managed static audit (Phase 9E.2A.2)

An isolated read-only metadata/CIL reader and pure draft-sort reference. It uses
the installed .NET SDK APIs; no ILSpy, Cecil, Unity asset tool or global install
is required. Target assemblies are opened with `File.OpenRead` and `PEReader`;
they are never loaded or executed. Framework reflection only enumerates the
tool runtime's `OpCodes`. The Application project reference allows direct
comparison with the current hypothesis model; the application and solution
do not reference this tool.

See [the static audit](../../ARENA_DRAFT_SORT_STATIC_AUDIT.md) for the proven
selection, method tokens, limitations and results. `inspected-build.json`
contains only file metadata/hashes for the inspected installation, not binaries.
Keep raw IL dumps and exported DB analysis under ignored `artifacts/`.

```powershell
dotnet build tools/ArenaManagedStaticAudit/ArenaManagedStaticAudit.csproj
$taskTool = 'tools/ArenaManagedStaticAudit/bin/Debug/net10.0/ArenaManagedStaticAudit.dll'
$taskManaged = 'C:/Program Files (x86)/Steam/steamapps/common/MTGA/MTGA_Data/Managed'
dotnet $taskTool self-test
dotnet $taskTool inventory $taskManaged
dotnet $taskTool types "$taskManaged/Core.dll" 'DraftPack|CardSorter'
dotnet $taskTool dump "$taskManaged/SharedClientCore.dll" '^Wotc.Mtga.Cards.SortTypeFilters$'
dotnet $taskTool field-data "$taskManaged/SharedClientCore.dll" 04001615 16
dotnet $taskTool calls "$taskManaged/Core.dll" 'SortTypeFilters::DraftPack'
dotnet $taskTool resources "$taskManaged/Core.dll"
```

`field-data` tokens apply only to the recorded assembly hash; obtain the token
again with `dump` after an update. Regexes select types or full method names.
`calls` searches decoded operands (including fields), not only call opcodes.
Manifest resources do not enumerate external Unity prefab bundles.

Replay uses only the existing sanitized numeric fixtures, saved verified audit
orders, the current local DB and an optional read-only ledger. It also exports
the current ANA/FRA/USG/WOE/WOT draft universe to compare functional classes.
The exporter never reads Untapped storage. Missing ledger files mean zero rows;
malformed supplied rows cause failure, rather than silently counting them.
No observation is written to the production ledger.

```powershell
New-Item -ItemType Directory -Force artifacts/arena-static-sort | Out-Null
python tools/ArenaManagedStaticAudit/export_evidence.py --db 'C:/Program Files (x86)/Steam/steamapps/common/MTGA/MTGA_Data/Downloads/Raw/Raw_CardDatabase_b8e6eb972e40c139da666ec805aa8a2b.mtga' --output artifacts/arena-static-sort/reference-input.json --ledger "$env:LOCALAPPDATA/DraftTG/localization/order-evidence.jsonl"
dotnet $taskTool reference artifacts/arena-static-sort/reference-input.json
```

The pure comparator accepts occurrence records and an injected title comparer.
Replay uses en-US localizations and host .NET en-US comparison. This does not
prove identical host/Unity Mono collation for every Unicode string or prove the
live language setting. It does prove the reference agrees on the available
English packs, including their numeric DB keys. Ten focused executable tests
cover priorities, color selection/ranking, collation, duplicates, null titles
and the difference from the current model's final GrpId tie. They run through
`self-test`, separately from the unchanged 996 solution tests.
