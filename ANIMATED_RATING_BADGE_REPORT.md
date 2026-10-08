# Animated Mystic Rating Badge Background

The animated background and preceding visibility polish were implemented offline on 2026-10-07. Contextual Pick Score V1 now supplies the badge's numeric value and six animation tiers. Localization, capture, Player.log handling and deterministic authority remain unchanged. Phase 9E.2B was not started.

**Animation color is based on DraftTG card-quality tier, not Magic printed rarity.** The current source is `Presentation.PickScore.Score0To50`; the former binary background styling by `IsContextPick` is superseded. The existing best-card border still uses the recommendation flag and stays stationary.

## Current Pick Score tier mapping

| Score / quality tier | Primary / secondary accent | Glow / sparkle | Highlight / silver opacity | Sparkle multiplier |
|---|---|---|---|---|
| 0–9 Graphite | `#555D69` / `#727B88` | `#8792A3` / `#A3AFBF` | .11 / .14 | .65 |
| 10–19 Steel | `#6D798B` / `#8797A9` | `#A4B7CE` / `#CAD8EA` | .14 / .18 | .78 |
| 20–29 Silver | `#A3B0C1` / `#8797A9` | `#B9C6D8` / `#D6E2F2` | .18 / .18 | .88 |
| 30–39 Gold | `#BFA56D` / `#A78B68` | `#C5A568` / `#E4D3AC` | .20 / .19 | .93 |
| 40–44 Rich gold | `#E2BE64` / `#B58A60` | `#D8A24F` / `#F3DCA2` | .24 / .20 | 1.00 |
| 45–50 Premium bronze/amber | `#E1AE70` / `#BD8656` | `#DC9D57` / `#FFE0AC` | .26 / .20 | 1.00 |
| Unavailable | `#6D798B` / `#8797A9` | `#B9C6D8` / `#D6E2F2` | .14 / .18 | .88 |

All tiers keep base `#E6171B22` and neutral silver `#B9C6D8`. `MysticBadgeTierPalette.For(int? score)` contains only UI mapping; tier colors cannot influence engine scores. A Common scoring 47 gets premium treatment, and a poorly scoring Mythic gets the low tier. Existing rarity regressions now verify numeric scores and palettes, and a boundary test covers all six bands plus unavailable values.

The 86×42 badge now shows the prominent score, GIH and ALSA on three compact lines; ordinal rank remains in expanded diagnostics. The name, placement, rounded clip, text veil, five particles, motion cycles, shared 30 Hz clock and pause/static behavior are retained. The bounded cache holds seven treatments (~5.68 MB of raw pixels before renderer copies), generated once as used, with no per-frame blur. The 15-badge preview labels the actual score bands, includes long names, one score winner and an unavailable score, and still supports live motion capture and static/animated measurement. See [CONTEXTUAL_PICK_SCORE_REPORT.md](CONTEXTUAL_PICK_SCORE_REPORT.md) for score formulas, replay examples, final validation and current preview commands.

Current validation: **1,057 solution tests passed**, zero build warnings/errors. The final 15-badge animated probe measured 7.55% of one core / 0.314% machine CPU and 0.980 MB/s managed allocation, with 289 ticks and 4,335 background renders in 12.01 seconds. Paused pixels stayed identical, and pause/hidden clock shutdown passed. This remains a short whole-process offline measurement; no GPU FPS or live Arena claim is made. Current artifacts are under `artifacts/pick-score-v1/preview/`.

The sections below retain the earlier animation/visibility implementation and measurements as historical evidence. Their two-treatment palette, ordinal-primary layout and 1,027-test count describe the preceding pass, not the current Pick Score integration.

## Earlier smoke/sparkle implementation (historical)

The current visibility refinement adds **more pronounced feathered smoke, a stronger soft aura and five sparse sparkles** per badge. Wave core opacity is up **34.8%** and glow strength **25.5%** relative to the previous polish. Peak sparkle opacity rises from 0.46 to 0.72, sprite core alpha from 210 to 235, and sprite sizes vary slightly above the previous size. These parameters target roughly 1.5–2× sparkle prominence; perceived visibility depends on phase and background, rather than being an exact luminance multiplier. The flowing curves and 8/12/10-second motion remain unchanged. Context Picks retain warm gold/bronze smoke, amber glow and pale gold sparkles; other cards retain steel/silver smoke, cool silver-blue glow and silver-white sparkles.

`MysticRatingBackground` remains an ordinary Avalonia custom-drawn control. At first use of each quality treatment, the existing SkiaSharp dependency rasterizes the curves at 2× resolution with a **2.7-DIP Gaussian softness pass** and a broader **6-DIP glow pass**, increased from 2.2/5.5 to soften the stronger light. Blur and glow are **baked once into transparent textures**, rather than recomputed during animation. A tiny radial aura and four-point sparkle sprite are also generated once. All temporary canvases, paths, paints, filters, encoded streams and source bitmaps are disposed after construction. No new package or external image asset is needed. This uses Avalonia's [bitmap/custom drawing support](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering) and SkiaSharp's [Gaussian image filters](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skimagefilter?view=skiasharp-3.119).

The bounded cache holds only two style resources: six 456×148 smoke textures and two 24×24 sparkle sprites, approximately **1.62 MB of uncompressed pixel data**, excluding renderer/GPU copies. Cache size is unchanged by this refinement. All badges share those resources for the process lifetime. Each frame draws three translated images, five small sparkle images with value-type opacity/position/size state, and the fixed text veil. There is no application-created bitmap, blur filter, brush, path, random particle object or particle collection per frame.

The control sits behind the original rating text in a Grid inside the existing rounded Border. The nearly opaque charcoal background, recommendation border colors, rank/GIH/ALSA bindings, font styles and 42-DIP height remain intact. Original `Padding="3,2"` becomes the same content inset on the text StackPanel, letting the effect occupy the inner box without moving text or changing badge bounds. The recently added name line remains above the box, with its existing width, ellipsis and visibility behavior. Animation does not extend into or outside that separate name line.

A rounded clip uses radius 4 inside the original radius-5, one-DIP border. Smoke, glow and sparkles all stay inside this clip. A fixed translucent charcoal gradient across the center suppresses highlights behind text, and sparkles are anchored mainly near the outer/top/bottom portions of the badge. Text is drawn independently above this layer and is never blurred. The control cannot receive pointer hits or focus, and does not change the overlay's click-through implementation, window size or placement coordinates. This polish pass does not modify the shipped XAML layout or text styling.

## Earlier binary quality classification and accent mapping (superseded)

The existing passive badge has **two** strength treatments, selected by `CardBadgeViewModel.IsContextPick`: the recommended **Context Pick** uses gold border/rank styling, while **other cards** use gray. There is no existing five-level quality enum or numeric tier-threshold table in this checkout. These are recommendation-relative treatments, not absolute grades such as Low/Playable/Bomb. The implementation preserves that distinction instead of inventing thresholds or a new quality formula.

`CurrentPackCardPresentation.IsContextPick` already resolves the active Trophy → Archetype → Lane → Pool → Statistical recommendation flag. The effect binds to that same existing `IsContextPick` property used by `BorderColor` and `RankColor`, so a contextual recommendation takes priority over a raw-rate/statistical winner. No rate, rank, printed rarity, set or collector metadata is consulted by the animation styling.

`MysticBadgeTierPalette.For(bool isContextPick)` centralizes UI colors without leaking Avalonia types into Domain. Neutral silver and the principal gray/gold accents reuse the existing badge color values. The existing border remains static and unchanged.

Both treatments retain base **`#E6171B22`** and a shared neutral/silver current **`#B9C6D8`**. The exact mapping is:

| Existing quality treatment | Tier accent | Secondary accent | Tier highlight opacity | Neutral/silver opacity |
|---|---|---|---|---|
| Other cards (`IsContextPick=false`) | `#6D798B` cool steel/blue-gray | `#8797A9` silver-blue | 0.14 | 0.18 |
| Context Pick (`IsContextPick=true`) | `#E2BE64` warm muted gold | `#B58A60` bronze | 0.24 | 0.20 |

| Existing quality treatment | Glow tint | Sparkle tint | Sparkle intensity multiplier |
|---|---|---|---|
| Other cards | `#B9C6D8` cool silver-blue | `#D6E2F2` silver-white | 0.88 |
| Context Pick | `#D8A24F` soft amber | `#F3DCA2` pale gold | 1.00 |

The first band supplies neutral silver, the second supplies the tier accent, and the third supplies the secondary accent at 55% of tier opacity. The softened core applies a **1.24 multiplier** to the original opacities; the broad aura applies a **0.69 multiplier**. Higher core opacity improves separation from charcoal without sharpening the edges. Gradient edges fade to transparent before blurring. The original fixed dark veil limits center brightness. Missing/inconsistent recommendation data safely uses the existing non-pick treatment. Printed Rare with a low recommendation receives steel/silver; printed Common selected as the top recommendation receives gold/bronze. Changing only printed rarity never changes smoke, glow or sparkle treatment. The source of classification remains the exact same `IsContextPick` flag; the added intensity multiplier only restrains cool sparkle styling.

## Earlier motion-fix diagnosis (retained for reference)

The previous build was measured before changing the motion. It produced **292 shared ticks and 4,380 background renders across 15 badges in 12.01 seconds**. This rules out a stopped clock or missing redraw in the local preview.

| Question | Previous implementation finding |
|---|---|
| A. Is the clock advancing? | Yes. A monotonic Stopwatch advances and the shared DispatcherTimer ticks. |
| B. Do active badges receive updated time? | Yes. The clock invalidated all subscribers; each render read the advancing shared time directly. |
| C. Does each badge redraw? | Yes. 292 ticks produced 292 renders per badge in the baseline probe. |
| D. Does wave geometry depend on phase? | The cached cubic paths themselves are fixed; their drawn position depends on phase through sine/cosine translation matrices. This changes the rendered wave image. |
| E. Is preview animation disabled? | No. `IsAnimated` defaults to true; all 15 preview controls subscribed. Pause was only enabled deliberately during static measurements. |
| F. Is a transform/brush frozen once? | Brushes and paths are cached intentionally. Translation matrices were recomputed on every render; no animated transform was frozen. |

**Root cause of the weak apparent motion:** the principal gold/steel wave moved only **10 nominal DIPs end to end (11.6% of badge width) over a 12-second cycle**, with a maximum horizontal speed of only 2.62 DIPs/second. Broad, faint curves behind the fixed text veil made that small displacement hard to perceive. The previous verification checked ticks/renders and exported manually selected phases, which did not establish live clock-to-pixel motion. No stopped-timer failure was reproduced on this machine.

That motion fix increased displacement, made the tick → stored time → invalidation → render path explicit, and verified changing pixels sampled from the live clock. The current polish retains that fix, while replacing sharp vector edges with cached mist and aura textures.

## Motion and lifecycle

The principal gold/steel band now travels **48 nominal DIPs end to end, 55.8% of badge width, on an 8-second cycle**. Neutral silver counter-drifts 30 DIPs on a 12-second cycle, and the secondary accent drifts 20 DIPs on a 10-second cycle. Horizontal amplitudes are respectively 15/24/10 DIPs for silver/tier/secondary; vertical amplitudes are 1.8/2.4/1.4 DIPs. Sine/cosine cycles are continuous without endpoint resets and repeat jointly after 120 seconds. `occurrenceIndex * 1.37` seconds supplies deterministic phase variation, including duplicate card occurrences. The text, border and box stay stationary; badge position, scale and dimensions are unchanged.

Sparkles fade smoothly from zero to their individual peaks using a squared cosine envelope and five permuted **6.5–10-second cycles**, with distinct phases. Each has a stable, varied size and an intensity multiplier in **0.82–1.00**, making some particles softer and others more prominent. They drift only ±1.5 DIPs horizontally and ±0.8 DIPs vertically over unchanged 14/16-second cycles. Maximum sprite opacity is **0.72 for Context Picks / 0.634 for other cards**, before individual intensity, built-in alpha and the text veil. Five separated anchor positions lie near the top corners and lower perimeter, leaving the central text area quiet. There is no spawning, abrupt reset, random noise or synchronized flashing.

One shared UI `DispatcherTimer` requests updates at a nominal 30 Hz for all active badges. Each tick reads a monotonic Stopwatch once, calls each subscriber's `AdvanceFrame(seconds)` to store its current time, and calls `InvalidateVisual()`. Rendering derives smoke offsets and sparkle positions/opacities from that same time. There is no per-badge timer. Avalonia still allocates retained drawing commands internally; the measured whole-process allocation is reported below rather than claiming zero allocation.

Only attached, visible, animated controls subscribe. Ancestor visibility and minimized-window changes remove inactive badges; detachment removes subscriptions and ancestor listeners. The timer stops when the final subscriber leaves. The offline probe verified zero subscribers/ticks while paused and zero subscribers when the preview was hidden.

`IsAnimated=false` shows fixed, occurrence-varied smoke and sparkles without ticking. Sparkle drift and fading stop along with the waves; no secondary timer continues running. This preserves the existing static/reduced-motion integration point. Automatic OS reduced-motion detection remains unchanged and is not implemented; no Settings screen or normal-UI developer button was introduced.

If clock startup fails, animation falls back to static currents. If optional effect drawing/resources fail, that control disables its layer and stops subscribing; the original parent charcoal Border and independent rating text remain available. The effect is never a visibility gate for ratings.

## Files changed

- `src/DraftTG.App/MysticRatingBackground.cs`: cached softened smoke/glow, sparkle sprites/state and centralized visual tuning, retaining quality selection, shared clock, clipping and static/error behavior.
- `src/DraftTG.App/MysticBadgePreviewWindow.cs`: preview descriptions, live/static pixel verification for **both** treatments, and 4× interior detail exports. The existing isolated 15-badge route, pause/resume, diagnostics and shipped DataTemplate are retained.
- `tests/DraftTG.App.Tests/MysticRatingBackgroundTests.cs`: the existing eleven focused tests are retained. Sparkle checks now cover all five bounded positions, size variation, slow fade changes, premium/neutral intensity ordering and disabled-state constancy; no new test count is needed for this tuning pass.
- `ANIMATED_RATING_BADGE_REPORT.md`: this report.

`App.axaml.cs`, `CardOverlayWindow.axaml` and their preview route/background layering were established by the preceding implementation and are unchanged in this polish pass. Rating, recommendation, placement, localization, capture and deck-building code were not edited.

## Tuning constants

`MysticBadgeTuning` keeps the small visual controls in the App layer. Texture constants apply when resources are first generated (restart after changing them).

| Constant | Value / purpose |
|---|---|
| `WaveOpacity` | 1.24 × existing tier/neutral core opacity (was 0.92) |
| `GlowStrength` | 0.69 × corresponding band opacity (was 0.55) |
| `SmokeSoftness` / `GlowSoftness` | 2.7 / 6 DIPs, baked Gaussian sigma (was 2.2 / 5.5) |
| `SparkleCount` | 5 per badge (was 3) |
| `SparkleSizeMin` / `SparkleSizeMax` | 1.10–1.28 × 12-DIP sprite including transparent halo; luminous core about 2.9–3.3 DIPs (was 2.7) |
| `SparkleOpacity` | Maximum 0.72; fade reaches zero (was 0.46) |
| `SparkleIntensityMin` | 0.82; individual peak strengths range 0.82–1.00 |
| `SparkleFadeDurationMin` / `SparkleFadeDurationMax` | 6.5–10 seconds, permuted across particles |
| `MotionSpeed` | 1; retains the established wave speed |

## Offline preview

From the repository root, open the interactive preview:

```powershell
dotnet run --project src/DraftTG.App --no-build -- --badge-preview
```

It displays 15 original-size badges labeled by the actual two recommendation treatments: **Other cards** and **Context Pick**. Nine neutral examples and six recommended examples show representative names and currents. Columns three and five now show **long names in both styles**, with ellipsis above the badges, for a direct readability comparison. Preview card metadata is fixed fixture data and is never read by styling. It renders realistic sample rank/GIH/ALSA values without invoking scoring, bootstrapping a draft runtime, contacting services or requiring Arena. A preview-only button pauses/resumes all backgrounds. Developer diagnostics show Running/Paused/Stopped, subscriber count, ticks, phase and renders. They refresh at most once per second from the same shared clock; normal production badges have no diagnostics.

To capture three frames from the live clock, without a manually forced phase:

```powershell
dotnet run --project src/DraftTG.App --no-build -- --badge-preview --capture-motion artifacts/mystic-pronounced-frames
```

After warmup it captures relative times 0/1.5/3 seconds, including whole-preview PNGs and isolated interior PNGs for both treatments. `*-detail.png` renders the interiors at 4× for close inspection of smoke softness and sparkles. `motion.json` records actual elapsed/shared times, ticks, frame updates, live render counts, offsets and decoded-pixel SHA-256 hashes. This route fails unless each treatment produces three distinct interior images and the live clock/update/render counters advance, then closes its own preview.

To export samples and collect a short whole-process performance comparison:

```powershell
dotnet run --project src/DraftTG.App --no-build -- --badge-preview --measure-output artifacts/mystic-pronounced-after/measurement.json
```

The probe warms up, runs the same live 0/1.5/3-second capture, measures an 8-second static interval and a 12-second animated interval, compares paused interior pixels for both treatments 1.5 seconds apart, checks pause/hide clock shutdown, writes JSON and closes its own preview. Generated files remain under ignored `artifacts/`. Neither export nor image hashing runs during the performance measurement intervals or in production.

## Performance and visual observations

The final probe exited successfully on this Windows machine, in Debug with Avalonia 12.1.3 and **24 logical processors**, showing **15 animated badges**:

| Metric | Static | Animated |
|---|---|---|
| Measurement interval | 8.01 s | 12.01 s |
| CPU, fraction of one logical core | 0.39% | 9.11% |
| CPU, normalized across the machine | 0.016% | 0.379% |
| Managed allocation | 1.7 KB/s | 0.965 MB/s |
| Working set, start → end | 151.6 → 145.2 MB | 148.0 → 145.2 MB |
| Shared clock ticks / active subscribers | 0 / 0 | 288 / 15 |
| Per-badge frame updates / background renders | 0 / 0 | 4,320 / 4,320 |

The animation added about **0.36 percentage points of total CPU** over static drawing in this short sample. Each tick caused one update and one redraw per badge. A fresh pre-refinement run on the same machine used **0.336% machine CPU / 8.07% of one core**, **0.786 MB/s allocation**, and **24.32 Hz**. The refined run averaged **23.98 Hz** with a largest steady animated clock gap of **56.6 ms**, versus **53.9 ms** before refinement. Two additional sparkle draws raise whole-process allocation by approximately **0.18 MB/s (23%)**, and machine CPU by about **0.043 percentage points** in these samples. The implementation remains lightweight with similar dispatch cadence; the added particles are not free. Short measurements do not establish a general responsiveness or GPU result.

These are whole-process costs including the developer HUD, after texture warmup, rather than an isolated `InvalidateVisual` or GPU benchmark. MB/KB use decimal units. The bounded cache trades about 1.62 MB of texture pixels for avoiding per-frame blur. Working set decreased during both animated samples due to normal process/GC variation; this is not a long-duration memory-leak test. Timer cadence is below the requested 30 Hz and is not GPU presentation FPS. GPU utilization and subjective live display smoothness were not measured.

Live-clock whole-preview samples and 4× interior details were visually inspected. Both styles show fuller feathered mist and stronger diffuse glow; brighter sparkle points remain sparse, with distinct intensity/size/timing instead of noise. Rank, percentage and ALSA remain crisp at the original gameplay scale, and long names ellipsize in both styles. The crest/overlap and sparkle brightness change while names, text, borders and badge bounds stay fixed. The sampled Context Pick's primary X offset changed from −22.33 to −0.33 to +22.17 nominal DIPs; both treatments produced distinct decoded-pixel hashes at all three captures. Ticks advanced 46 → 83 → 117. Capture times start after warmup (shared phase about 2.1 seconds), not at process clock zero. Both paused interiors were pixel-identical after 1.5 seconds, and paused/hidden subscriber checks passed. The isolated detail images omit text/border intentionally for inspecting the effect; whole-preview images verify final text readability. No live Arena validation is claimed.

Artifacts: [pre-refinement measurement](artifacts/mystic-pronounced-before/measurement.json), [refined measurement](artifacts/mystic-pronounced-after/measurement.json), preview at [0 seconds](artifacts/mystic-pronounced-after/badge-preview-0.0.png), [1.5 seconds](artifacts/mystic-pronounced-after/badge-preview-1.5.png), [3 seconds](artifacts/mystic-pronounced-after/badge-preview-3.0.png), [static preview](artifacts/mystic-pronounced-after/badge-preview-static.png), [warm interior detail](artifacts/mystic-pronounced-after/wave-3.0-detail.png), and [cool interior detail](artifacts/mystic-pronounced-after/wave-other-1.5-detail.png).

## Tests and validation

Eleven tests cover the existing two-treatment quality palette (including glow/sparkle colors), missing-recommendation fallback, disabled/static motion, deterministic occurrence offsets, bounded continuous cycles, recommendation-derived quality/unchanged rating and placement values, and shipped effect layering/text bindings/name ellipsis. The preceding motion test calls the same `AdvanceFrame` entry point used by the clock and checks distinct t=0/1/2 render offsets and disabled-state constancy. The existing sparkle test was extended for all five particles, bounded position/opacity/size, distinct sizes, gradual per-tick fade changes, premium intensity ordering and static constancy. Existing regressions still prove a low-rated Rare receives the neutral treatment and a top-rated Common receives the premium treatment; varying printed rarity preserves palette, rank and displayed rate. No beauty assertions or screenshot golden tests were added. Both CLI preview modes exited successfully after rebuilding, and the measurement probe verified live and paused rendered pixels for both styles.

- `dotnet build DraftTG.sln`: **0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **1,027 passed**, 0 failed, 0 skipped. All existing tests remain passing; focused sparkle assertions were extended without adding redundant tests.
- Counts: Domain 56, RecommendationEngine 163, Data 160, ArenaIntegration 127, Application 246, App 275.
- Separate `ArenaManagedStaticAudit.dll self-test`: **10 passed**; reported separately from solution tests. The sort-reference implementation was not changed.
- `git diff --check`: no whitespace errors.

Stopped after the visual polish pass. No deterministic placement promotion, Phase 9E.2B work or unrelated feature work was undertaken.
