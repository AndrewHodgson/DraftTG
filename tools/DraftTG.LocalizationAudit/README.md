# Read-only localization audit prototype

This Windows x64 developer tool is isolated from the app and is deliberately absent from `DraftTG.sln`. It does not implement `ICardVisualLocator` or place badges. It references the existing Arena parser/state engine without modifying them.

Run from the repository root with the .NET 10 SDK and Windows Desktop targeting pack:

```powershell
dotnet build tools/DraftTG.LocalizationAudit/DraftTG.LocalizationAudit.csproj
dotnet tools/DraftTG.LocalizationAudit/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.LocalizationAudit.dll snapshot --output artifacts/localization-audit/observation-a
dotnet tools/DraftTG.LocalizationAudit/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.LocalizationAudit.dll memory --output artifacts/localization-audit/observation-a
dotnet tools/DraftTG.LocalizationAudit/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.LocalizationAudit.dll duplicate --output artifacts/localization-audit/observation-a
```

Use a normal interactive console on the same desktop as Arena. The Codex sandbox desktop can enumerate Arena handles yet cannot read their window properties; that is a probe environment error, not evidence about the game's UI provider. In the audit, outside-sandbox execution on `Default` was needed for `snapshot` and `duplicate`. Administrative privileges are not a tool requirement. Exactly one `MTGA` process must be running.

Use a new output directory for each observation. JSON files are overwritten when a command is repeated. An image path in an older JSON file is not evidence that a later run saved a new image. Do not reuse a screenshot as current merely because its filename exists.

## Commands and bounds

- `snapshot`: native window/parent relationships, UIA raw-view tree, module inventory, and selected managed PE metadata. UIA traversal is limited to 512 nodes/eight seconds between provider calls. A hung provider call can exceed that soft limit; terminate only the audit process if it hangs. Reading PE metadata does not load game assemblies or call game methods. SHA-256/MVID values describe this build, not supported memory layouts.
- `memory`: only `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` (`0x410`). Searches the current log-derived pack IDs, plus shifted negative controls, as Int32LE/ASCII/UTF16LE. Runtime writable PE sections seed one-hop OS region selection; private regions are not assumed to be Mono heap objects. Limits: 48 MiB requested reads, 8,192 region queries, eight seconds between operations, sixteen selected private-region samples of at most 2 MiB each, and 32 saved addresses per ID/encoding. A missing current pack causes a recorded skip. No raw memory or neighboring strings are persisted. A hit does not identify a UI object.
- `duplicate`: D3D11/DXGI Desktop Duplication, selected output adapter, two-second acquisition budget, single unrotated SDR output, at most eight million crop pixels. Acquires only through standard OS APIs. Only Arena center is copied to CPU/saved; a five-point window/PID guard rejects observed occlusion. Pointer-only updates cannot validate pixels; uniform crops are rejected. No fullscreen desktop image is saved. The sampled guard cannot prove absence of all occlusion. This is an audit crop, not production calibration or pack/frame synchronization.

Each command records parser/state snapshots before/after. `SemanticUnchanged` means those replayed states compare equal; it does **not** establish rendering freshness, a valid current pack, or a production generation token. The bounded 64 MiB log replay can fail on unsupported or conflicting records; the redacted trace reports fact kinds, coordinates, card IDs and error kinds. It does not silently infer a pack from screenshots.

Exit code zero means the experiment ran and wrote JSON. Inspect `Result.Success`, `Result.Skipped`, errors, and semantic state to assess it. Fatal setup exceptions return one. The commands do not claim card recognition or correct card rectangles.

For a second observation, let the user make their normal pick, then run the same commands into another directory. Never make picks, focus the game, or navigate on their behalf. Deck-builder inspection requires the user to open it during their normal workflow.

The tool never injects, hooks, writes target memory/files, requests debug privilege, invokes UIA actions, calls game internals, or sends input. Outputs stay in the existing ignored `artifacts/` directory. They can include draft/pool cards, module paths, process addresses and screenshots; review them before sharing. Whole Player.log files, account/session tokens and game assemblies are not copied.

Results and architecture gates: [LOCALIZATION_ARCHITECTURE_AUDIT.md](../../LOCALIZATION_ARCHITECTURE_AUDIT.md).
