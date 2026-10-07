# Untapped localization audit (read-only research tool)

Isolated Windows x64 tool used for [UNTAPPED_DRAFTSMITH_LOCALIZATION_AUDIT.md](../../UNTAPPED_DRAFTSMITH_LOCALIZATION_AUDIT.md). It is deliberately absent from `DraftTG.sln` and is not production code. It never injects, hooks, patches, writes process memory, sends input, focuses/moves/resizes windows or modifies Arena/Untapped files. Every experiment that needed a window change was performed by the user.

Build and run from the repository root (.NET 10 SDK, Windows Desktop targeting pack):

```powershell
dotnet build tools/UntappedLocalizationAudit/UntappedLocalizationAudit.csproj
dotnet tools/UntappedLocalizationAudit/bin/Debug/net10.0-windows10.0.19041.0/UntappedLocalizationAudit.dll <command> --out artifacts/untapped-audit/<run>
```

The process is per-monitor-DPI-aware (V2), so every rectangle is in physical pixels.

## Commands

| Command | What it does | Access it needs |
|---|---|---|
| `topology [--tag t]` | All top-level windows in Z order; full styles/ex-styles, owner, DWM frame/cloak, layered attributes, display affinity, DPI, monitor; child windows of Arena and Untapped; monitor modes; foreground; cursor. | Window manager queries only. |
| `uia [--hwnd 0x..] [--slow --seconds s --max n]` | UI Automation raw-view tree. Default is one cached subtree request; `--slow` walks node by node with a budget. | UIA client (no patterns invoked). **The cached walk can hang on the Chromium overlay**; target Arena or use `--slow`. |
| `shot [--rect x,y,w,h] [--file f]` | Composited screen crop (BitBlt + CAPTUREBLT) of Arena's window rectangle. | Screen DC. |
| `watch --seconds s --interval ms [--shots ms]` | Change log of Arena/Untapped window rectangles, Player.log length; per-process CPU every 100 ms. | Window queries, `PROCESS_QUERY_LIMITED_INFORMATION`. |
| `trigger --seconds s [--uia]` | Waits for the user to bring Arena to the foreground, then logs overlay Z order/topmost at ~4 ms and saves timed captures. | As above. |
| `session --max s --frame ms` | Recorder for user experiments (move, hover, pick): geometry/Z order/cursor at ~4 ms, JPEG frames while Arena is foreground and something changes, tails Untapped `log.log` and Player.log. Stop by creating `<out>/STOP`. | As above; reads both logs with full sharing. |
| `geom --max s` | Geometry recorder for user-performed resolution changes. On every Arena foreground switch it saves full-resolution PNGs at +1/+30 ms ("arenaonly", before Untapped raises its overlay) and +0.7/+2.5 s ("overlay"). Overlay Z order is logged around each grab, along with client-size changes and a topology snapshot. Stop with `<out>/STOP`. | Window queries, screen DC. |
| `handles [--files]` | System handle table (`SystemExtendedHandleInformation`). Process/File handles owned by Untapped processes are **duplicated into this process only to call `GetProcessId` / `GetFinalPathNameByHandle`**, then closed. A duplicated Arena handle briefly carries the same rights as Untapped's (e.g. `VM_READ`); it is never used to read memory. Synchronous-pipe access masks are skipped and each name query has a 300 ms timeout. | `PROCESS_DUP_HANDLE` on Untapped processes. |
| `modules` | Loaded-module paths of Untapped processes. | `QUERY_INFORMATION|VM_READ` on Untapped (standard module enumeration). |
| `arena-modules` | Loaded-module paths of MTGA, filtered to foreign (non-Windows, non-Arena) DLLs, to detect injected modules. | `QUERY_INFORMATION|VM_READ` on MTGA (standard module enumeration; no other reads). |

Python helpers (standard library + Pillow):

| Script | Purpose |
|---|---|
| `compare_orders.py <Player.log> <scry ids>` | Player.log order vs Untapped Scry order vs Arena-DB prediction (uses `tools/ArenaSortOrderProbe`). |
| `score_untapped_orders.py <drafts.json>` | Treats Untapped's stored per-pick `DraftPack` orders as candidate display orders and scores the Phase 9E hypothesis family. Offline; never writes DraftTG's ledger. |
| `measure_cards.py <png> <capX> <capY> <clientX> <clientY> <bands>` | First-pass card rectangles from luminance scanlines (superseded by `measure_grid.py`). |
| `measure_grid.py <png> <capX> <capY> <clientX> <clientY> [--json f]` | Precise card rectangles: edges are the boundary of Arena's uniform RGB (38,38,38) card holders, as medians of many scanlines. Cards whose glow hides the holder are skipped. |
| `badges_vs_slots.py <arenaonly.png> <overlay.png> <grid.json>` | Untapped strip/shield rectangles relative to each measured card, in px and in units of s. |
| `fit_geometry.py grid.json:W:H ... [--predict WxH,...]` | Joint least-squares fit of the candidate slot-geometry models (fixed, uniform by W or H, independent X/Y, centre/left/right anchors, canvas match search). Prints per-size and per-slot residuals and pre-registered predictions. |
| `overlay_components.py <without.png> <with.png> <offX> <offY>` | Overlay elements as connected components of a with/without-overlay difference. |
| `follow_latency.py <session-events.log>` | Overlay-follow latency during window drags. |

## Outputs and privacy

Outputs go to the ignored `artifacts/untapped-audit/` directory. Screenshots show the Arena screen name; JSON/logs can contain local paths. Do not commit or share them unreviewed. Account identifiers from Untapped's logs are never copied into the report.
