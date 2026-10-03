# Phase 7.5 report

GIH now comes only from `ever_drawn_win_rate`; `GameInHandGameCount` comes only from `ever_drawn_game_count`. Generic `win_rate` and `game_count` remain independent Data DTO/cache fields and are never exposed as GIH. OH WR, drawn WR, drawn improvement, ALSA and ATA retain their mappings. Opening-hand and drawn sample counts are also parsed and validated independently. `mtga_id` was not used in the current DTO or identity path and remains unused.

Missing GIH displays an em dash, with an asterisk only if a known GIH sample is below the existing 500 threshold. Unknown sample size produces no asterisk. Badge dimensions, calibration geometry and passive interactions remain unchanged.

The control rail status panel reports GIH available / pack slots, low GIH sample count, GIH unavailable, ALSA available / pack slots and source format/expansion. Duplicate copies count separately; history does not count. Low-sample and unavailable categories can overlap. No coverage appears while loading; stale pack coverage clears on transitions.

Cache schema 2 automatically rejects unversioned or incompatible cache entries. Old caches serialized a reduced DTO, losing the ever-drawn fields, so reparsing could not recover GIH. Rejected entries cannot become stale fallback after a failed refresh. Successful loads replace them at the existing path. The observed local `WOE_QuickDraft.json` has no schema version and will be rejected on its next load; it was not manually deleted or changed.

The existing successful-empty-environment PremierDraft fallback is unchanged. Missing GIH in a populated dataset triggers neither a metric substitution nor a new format fallback.

## Validation

- `dotnet build DraftTG.sln`: 0 warnings, 0 errors.
- `dotnet test DraftTG.sln --no-build`: 431 passed, 0 failed, 0 skipped.
- 16 added test cases: complete fixture and cache round trip; full provider-to-presentation mapping and slot coverage; 5 cache incompatibility cases, including offline rejection; 4 independent invalid-field cases; 5 badge cases for missing/low/boundary samples.
- Existing tests retained, with GIH fixture fields and explicit sample property names corrected.
- Fixture proves 57.5% / 900 rather than overall 51.0% / 5,000, null GIH despite populated overall WR, unknown GIH sample, low GIH sample, ALSA without GIH, and complete secondary metrics.
- No Phase 8 scores, ranking, recommendations or automatic picks exist in the implementation.
- No running Arena/DraftTG process was observed; live current-pack coverage was unavailable. Fixture coverage: GIH 4 / 6, low sample 2, unavailable 2, ALSA 5 / 6 (includes duplicate Alpha; excludes history).

## Files changed for Phase 7.5

- `src/DraftTG.Data/SeventeenLandsCardRatingsClient.cs`
- `src/DraftTG.Data/SeventeenLandsRatings.cs`
- `src/DraftTG.RecommendationEngine/LimitedCardStatistics.cs`
- `src/DraftTG.Application/LimitedStatisticsMapper.cs`
- `src/DraftTG.Application/LimitedStatisticsUpdate.cs`
- `src/DraftTG.App/MainWindowViewModel.cs`
- `src/DraftTG.App/RailPanelView.axaml`
- `tests/Fixtures/17lands-phase7-5.json` (new)
- `tests/DraftTG.Data.Tests/SeventeenLandsCardRatingsClientTests.cs`
- `tests/DraftTG.Data.Tests/DraftTG.Data.Tests.csproj`
- `tests/DraftTG.Application.Tests/LimitedStatisticsTests.cs`
- `tests/DraftTG.Application.Tests/DraftTG.Application.Tests.csproj`
- `tests/DraftTG.RecommendationEngine.Tests/LimitedStatisticsTests.cs`
- `tests/DraftTG.App.Tests/MainWindowViewModelTests.cs`
- `tests/DraftTG.App.Tests/OverlayPresentationTests.cs`
- `ARCHITECTURE.md`, `ROADMAP.md`, `PHASE7_5_REPORT.md` (new)

## Workspace recovery

Before implementation, 91 tracked workspace files contained only zero bytes, including the solution and source files. Those files were restored byte-for-byte from intact local HEAD `ba7afef`. The user's existing `global.json` edit was preserved. The Git index is also corrupt and was left untouched; review used a separate temporary index initialized from HEAD. Git status with the original index still requires repair. The first build encountered a sandbox restriction on Avalonia's build-service log; the exact requested build passed with approved access to that log.
