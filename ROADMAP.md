# DraftTG Roadmap

## Phase 0 — Architecture

Establish module boundaries, dependency direction, concurrency ownership, a minimal application shell, and independent Domain tests. Do not add product behavior.

## Phase 1 — Domain Model

Define platform-independent cards, packs, picks, draft state, and related invariants. Cover the model with focused unit tests.

## Phase 2 — Card Catalog

Add card metadata ingestion and lookup behind Domain-facing boundaries. Choose persistence only after the catalog's real access patterns are understood.

## Phase 3 — Arena Log Source

Locate and observe MTG Arena logs, emitting raw log additions without interpreting draft behavior. Keep filesystem concerns inside Arena Integration.

## Phase 4 — Arena Draft Log Parser

Parse supported Arena log events into typed Domain inputs. Add fixture-based tests and tolerate unknown or malformed events.

## Phase 4.5 — Cross-Platform .NET Migration

Migrate completed behavior to one shared C#/.NET 10 and Avalonia architecture for Windows and macOS. Preserve Domain, Data, Arena source, and parser behavior without introducing draft-session state.

## Phase 5 — Draft State Engine

Build the single owner of mutable draft-session state. Apply parsed events deterministically and expose immutable snapshots to consumers.

## Phase 6 — Live Draft Overlay MVP

Compose the production card-data and Arena-monitoring pipeline into a borderless, always-on-top Avalonia overlay. Present live pack, pick, and drafted-card state while keeping UI-thread state isolated and product logic outside views.

## Phase 7 — Statistical Card Data Integration (implemented)

Load whole-environment 17Lands Limited statistics after draft context is known, validate and cache for 24 hours, and map exact names to Scryfall-resolved Domain identities. Display raw GIH WR, ALSA, sample counts, low-sample warnings, and explicit source/fallback status asynchronously. Preserve offline tracking and Arena pack order. No scores, ranks, or recommendations. Live HOB Quick Draft statistics have since been physically validated on macOS. Phase 7.1 scopes mapping to known draft identities and removes global ambiguity banners.

## Phase 7.2 — Gameplay Overlay UX (implemented)

Replace the persistent central panel with an interactive control rail and a separate transparent card-badge layer. Add explicit dragging, isolated Windows/macOS click-through adapters, manual normalized-slot calibration, persisted geometry with display validation, and temporary status/history flyouts. Preserve Arena order and one shared runtime. Physical Arena click-through/alignment and Windows validation remain separate release checks.

## Phase 7.3 — Overlay interaction hotfix (implemented; physical acceptance pending)

Add eight native resize targets, an AppKit resizable-frame fallback for Avalonia 12.1.3's empty macOS resize method, explicit passive/calibration policies, and badge-only key/main focus vetoes. Save live geometry and restore it on reopen/cancel. Native macOS policy checks pass; physical Arena activation and native frame dragging remain unverified. Interior custom-handle resizing on macOS still requires an Avalonia backend fix. See PHASE7_3_REPORT.md.

## Phase 7.4 — Calibration exit and passive overlay (implemented)

Correct the macOS Retina desktop/render-scale mismatch that rejected valid saves. Share Save/Cancel cleanup, hide all calibration presentation, close the flyout, and restore borderless passive badges. Re-enter calibration from the rail icon with saved geometry. Native window/button smoke checks pass; physical Arena interaction is reported separately in PHASE7_4_REPORT.md.

Phase 7.5 corrects GIH to the provider ever-drawn metric and sample, rejects incompatible statistics caches automatically, and adds current-pack coverage in the control rail. No recommendation logic is added.

## Phase 8 - Basic Statistical Recommendation Engine (implemented)

Rank scorable pack occurrences by adjusted historical GIH using a weighted environment baseline and sample shrinkage: `b = sum(p_i*n_i)/sum(n_i)`, `s = (n*p + 500*b)/(n+500)`. The prior-equivalent 500 games is a configurable product choice. Show raw GIH alongside occurrence rank, a subtle gold Stats Pick border, and explicit scored/total coverage and baseline in the control rail. Preserve Arena order, duplicate slots, geometry and passive interaction. Missing/invalid GIH or sample data is unscored; fewer than two scored cards or no baseline produces no top pick. Calculation runs on a background worker with stale-pack/context rejection. ALSA is display only. No pool-aware scoring or automatic Arena input. Physical Windows/macOS Phase 8 acceptance remains unverified; see PHASE8_REPORT.md.

## Phase 8.1 - Current 17Lands Card Data API (implemented)

Use `/api/card_data` with `expansion`, exact `event_type` and `time_period=ALL_TIME`. Decode the current object envelope's `data` array using Data-only DTOs, preserve corrected GIH mappings, and automatically migrate to cache schema 3 with endpoint/format/period identity. Remove automatic Premier substitution. Reject empty, tiny and no-GIH datasets, preserving a valid same-source stale cache when available. Extend rail diagnostics with endpoint, period, row count and origin. Phase 8 scoring, badge UI and geometry are unchanged. Live WOE network validation succeeded; live Arena was not running. See PHASE8_1_REPORT.md.

## Phase 9A - Pool-Aware Color Commitment (implemented)

Compose the unchanged Phase 8 score with drafted-pool colors only. Each selected colored occurrence contributes 1/k evidence to each of its k colors; colorless contributes zero. Mean evidence is the sum across five colors divided by five. Raw support is `clamp((evidence_c - meanEvidence)/4, -1, +1)`; progress is `clamp(completed picks/14, 0, 1)`; effective support is raw support times progress. Colorless fit is zero, mono fit is its support, and multicolor fit is the minimum required-color support. Adjustment is `fit*0.025`; context value is the Phase 8 value plus adjustment clamped to [0,1]. These configurable product heuristics keep early picks flexible and allow large statistical gaps to justify pivots.

Rank scored occurrences by context value, Phase 8 value, sample (descending), then original index (ascending). Retain duplicates and pack order; no context score/rank for a Phase 8 unscored card, and no top pick with fewer than two scored cards. Badge rank/gold now follows Context Pick while raw GIH/ALSA and geometry stay unchanged. Show a distinct Stats Pick when different, effective colors, early progress, adjustment and coverage in the rail; detailed diagnostics are collapsed.

Capture immutable observations of actually seen packs by coordinate plus ordered identity, enrich their Phase 8 values when available, and complete selections from existing resolved Arena history. Replay is idempotent. Duplicate selections keep the known card and report an unknown slot when ambiguous. Expose partial observation coverage without inventing earlier packs. Observations do not influence Phase 9A scoring. Windows automated checks pass; Windows/macOS physical checks remain outstanding. See PHASE9A_REPORT.md.

## Phase 9B - Open Color Lane Detection (implemented)

Compose a separate final stage with unchanged Phase 8 and Phase 9A results. Extend existing observations with ALSA and their Phase 8 environment baseline. Card evidence is `clamp((pick-ALSA)/3,0,1) * clamp((Phase8Value-baseline)/0.04,0,1) * positionWeight`. The inclusive pick 4–8 ramp gives 0 before pick 4, 0.2 at pick 4 and 1 at pick 8+. Distribute evidence by 1/k across colored cards; colorless gives zero. Decay current/previous/two-back packs by 1.0/0.5/0.25. Derive relative support as `clamp((colorEvidence-meanEvidence)/2,-1,1)` and multiply by `clamp(meaningful distinct observed packs/4,0,1)`.

Candidate fit is neutral for colorless and the minimum required-color support for multicolor. Add `fit*0.020` for Premier/Traditional human drafts, `fit*0.010` for Quick Draft bots, or zero for unknown modes to the Phase 9A value, clamped to [0,1]. These are conservative tunable heuristics; a single pack has limited influence and large statistical gaps can still justify pivots. Missing Phase 8 scores remain unscored. Final ranking breaks ties by pool value, statistical value, sample (descending), then original slot (ascending).

Replay does not duplicate evidence and selecting a card preserves its original arrival signal. Current packs may contribute; quality never derives from contextual scores. Partial history is reported without invented packs, and new sessions reset lane state. Final rank/gold stays in the unchanged-size badges; the existing rail adds three-stage ranks, modest lane diagnostics and human/bot labels. The model infers relative evidence, not definitive open/closed colors. Automated validation passes with 21 focused new tests; Quick Draft and human-draft live validation and Windows/macOS physical checks remain outstanding. See PHASE9B_REPORT.md. Stop here; no Phase 9C/9D logic exists.

## Phase 9B.1 - Current-pack statistics association integrity (implemented; physical validation pending)

Replace positional UI joins with `(PackIndex, CardIdentifier)` keys and one immutable occurrence containing raw statistics and all three recommendation stages. Preserve active-card-first exact-name mapping and existing generation/revision/snapshot rejection; explicitly check immutable pack identity at the UI boundary. Derive rail names and badges from the same occurrence, retain Arena order separately from rank order, and cover real Hatching Plans/Scarecrow Guide values, late results, shrinking packs and duplicates. Eleven focused regressions bring the suite to 605 passing tests with zero warnings/errors. The live initiating event and physical Windows/macOS badge alignment remain unverified. See PHASE9B_1_REPORT.md. Stop here.

## Phase 9B.2 - Live passive badge bindings (binding work implemented; live placement unresolved)

Make the shipped CardOverlayWindow XAML read rank/GIH/ALSA directly from the immutable occurrence shared with the rail. Remove the passive overlay's dependency on the separate mutable row projection, reconcile wrappers by key, publish the complete snapshot, and expose diagnostics from the final rendered items. Seven focused tests bring the suite to 612 passing tests with zero warnings/errors. The live log lists Scarecrow Guide first and Hatching Plans last, opposite the reported visible endpoints; the existing log-index-to-geometric-slot assumption remains unverified and may explain the live swap. Windows inspection could not start despite Arena running. Do not claim the physical swap is fixed. See PHASE9B_2_REPORT.md. Stop here.

## Phase 9B.3 - Arena visual card placement (conservative mapping implemented)

Separate occurrence identity from calibrated visual slots. One observed pack provides no visual-order field and cannot establish a universal sort rule. Add explicit per-pack card-to-slot confirmation in the status panel, preserve the existing slot geometry, and hide unconfirmed badges with a diagnostic. Reset mappings on pack advance and reject stale selectors; keep confirmed placement during scoring updates. The actual P1P6 fixture maps Hatching Plans from log index 8 to visual slot 1 and Scarecrow Guide from log index 0 to visual slot 9 without changing their statistics/ranks. Seven focused tests cover the exact pack, invalid maps, duplicates, rank changes, all 14-to-1 sizes and existing layouts. All 619 tests pass with zero warnings/errors. Manual confirmation remains necessary for each pack; physical Windows/macOS acceptance is pending. See PHASE9B_3_REPORT.md. Stop here.

## Phase 9B.4 - Automatic localization (Windows implementation; live acceptance pending)

Known-pack artwork matching combines calibrated/compact geometry hypotheses, pixel refinement, exact occurrence assignment and conservative similarity/margin gates. Windows captures only the unobstructed Arena draft region; a window-relative anchor follows movement/resize/DPI. Phase 9B.5 removes the original foreground prerequisite. Pack/window changes schedule bounded attempts rather than a video loop. Small reference downloads/storage are capped, debug captures are disabled by default, and partial results prefill the manual fallback.

637 tests pass (18 new). Physical Windows validation is blocked by the inspection runtime failing to start. Mac capture remains unimplemented with a concrete ScreenCaptureKit/permission plan. Do not claim normal drafting accuracy or platform parity before physical acceptance. See PHASE9B_4_REPORT.md for research, benchmark limits, privacy, files and remaining validation. Stop here before archetypes.

## Phase 9B.5 - Windows capture runtime (implemented; live acceptance pending)

Fix the source-confirmed rail-focus gate that prevented acquisition before BitBlt was called. Rediscover MTGA's rendering HWND on Retry, separate focus from geometry generation, enforce an asynchronous configurable two-second frame timeout, and acquire the regional frame before loading references. Expanded diagnostics expose discovery, integrity, initialization, acquisition, crop and matcher stages with exception details. An explicit diagnostic action saves one temporary draft crop and a full text diagnostic, including failed attempts. Manual placement and the existing artwork matcher remain unchanged.

15 focused tests bring the suite to 652 passing tests; build warnings/errors are zero. Physical Windows capture, integrity/HWND verification, automatic localization and two consecutive picks remain blocked: the computer-use runtime cannot initialize (os error 3, including after reset). Do not mark this phase accepted based on synthetic tests. See PHASE9B_5_REPORT.md for the exact runtime trace and next live evidence. Stop here; no Phase 9C or macOS capture work.

## Live-frame artwork matcher audit (offline verified; live overlay validation pending)

The user's actual Phase 9B.5 crop/log establishes successful non-foreground Windows acquisition with matching Medium integrity. Reproduce the reported 0/9 with all nine correct best candidates: coarse artwork alignment produces .788-.907 scores below the .94 gate. Add bounded local artwork registration while keeping references, assignment and confidence/margin gates unchanged. The exact same PNG now identifies all nine correctly at .980-.996. Add a standalone offline developer command with full per-slot/candidate/rectangle/reference diagnostics and six live-frame regressions. All 658 tests pass; build warnings/errors are zero. Capture, parser, providers and all recommendation/statistics models remain unchanged. Validate the rebuilt live overlay on new packs/consecutive picks before broader acceptance. See ARTWORK_MATCHER_REPORT.md. No Phase 9C logic was started.

## Phase 9B.6 - Current pack/capture synchronization (implemented; live acceptance pending)

Both automatic localization and debug save already made new BitBlt copies; the missing defense was proof of visual freshness across pack transitions. Bind candidate snapshots, timestamps and semantic pack tokens to request-scoped acquisition. Add a configurable 250 ms settle, separate per-acquisition counters, previous-image/confirmed-art occupancy checks, three bounded captures with 150/250 ms spacing, stale result rejection, and fresh-frame debug pairs with unique pack/generation filenames and SHA256 pairing. Keep twenty generated pairs and save failure logs without old PNG reuse. Preserve the matcher/reference source bytes and all recommendation/statistics/parser behavior.

Fourteen focused regressions bring the suite to 672 passing tests; build warnings/errors are zero. Three consecutive live picks remain unvalidated because the Windows computer-use runtime fails to initialize with os error 3, including after reset. Timestamp/token ownership is explicit; conservative prior-content checks are not universal visual-state detection. See PHASE9B_6_REPORT.md for the source audit, defense limits and remaining physical acceptance. Phase 9C remains untouched.

## Phase 9B.7 - Windows Graphics Capture (complete; user-confirmed Windows validation)

Replace GDI as the default with one-shot HWND Windows.Graphics.Capture, free-threaded frame pool and bounded D3D11 regional readback. Keep GDI as an explicit developer option or opt-in initialization-only fallback. Preserve Phase 9B.6 defenses and the unchanged matcher/reference cache. Automatic localization and debug save share backend selection; diagnostics show backend, hashes, timing and ownership. Windows SDK types stay in App.Platform under conditional compilation; portable net10.0 build still passes. API floor is Windows 10 build 18362, with .NET 10 deployment support also required.

Actual live WGC captures now show the current P1P7 eight-card pack and the unchanged matcher identifies all 8/8 at the existing .94/.07 gates. Fifteen focused tests, including the exact safe live regression fixture, bring the suite to 687 passing tests; Windows and portable builds have zero warnings/errors. GDI comparison was blocked by its covering-window guard. Three consecutive picks and physical badge placement remain pending because Computer Use still fails with os error 3. See PHASE9B_7_REPORT.md. Stop here; Phase 9C remains untouched.

The Phase 9C request subsequently confirms Phase 9B.7 physically validated on Windows. That user-confirmed completion supersedes the historical pending status above; Phase 9C does not change capture or localization.

## Phase 9C - Set archetype and color-pair awareness (implemented; live recommendation validation pending)

Add a fourth inspectable stage after Stats, Pool and Lane, using curated set descriptions and empirical exact-format pair GIH. The validated embedded catalog initially contains all ten WOE archetypes from official Wizards design material; future sets add profile files. Unknown sets and unavailable pair evidence remain neutral. No text-derived synergy ratings are introduced.

Pair confidence is coverage * (0.75 + 0.25 * balance) * Phase 9A progress, with no repeated lane bonus. Active confidence must reach 0.35 and lead second place by 0.10; later picks can change the pair. Verify and use the actual 17Lands `colors` query in WUBRG order (GU -> UG), lazy-load at most three pair datasets per draft by default, and cache exact set/format/pair/ALL_TIME/schema/source identities with TTL, atomic writes and stale fallback.

Use the sample-weighted pair GIH baseline, a configurable 300-game prior toward Phase 8 strength, relative lift against both environments, a 0.04 lift scale, and at most +/-1.5 pp scaled by archetype confidence. Only one/two-color cards entirely inside the active pair are eligible. Missing or unscored evidence stays neutral/unscored. Final sorting retains previous-stage tie breaks and occurrence identity.

The rail exposes four ranks, archetype description/confidence, adjustment and pair GIH; passive badge metrics and placement remain unchanged while gold #1 uses final rank. Pair loads are asynchronous and stale callbacks cannot apply old affinity. Twenty-four focused new tests bring the suite to 711 passing tests, retaining all 687 existing tests. Windows and portable builds/tests pass with zero build warnings/errors. Phase 9C live Windows inspection is blocked by Computer Use initialization (os error 3); native macOS execution remains unvalidated. See [PHASE9C_REPORT.md](PHASE9C_REPORT.md). Stop after Phase 9C; Phase 9D remains unstarted.

The Phase 9B.8 request confirms Phase 9C is complete. That user confirmation supersedes its historical pending Windows validation status above.

## Phase 9B.8 - WGC long-running draft capture reliability (implemented; low-card Arena acceptance pending)

Serialize native capture through a lifecycle gate and dedicated MTA. Retain a healthy D3D device/capture item for unchanged validated geometry while closing fresh session/frame-pool resources after each request. Keep cancellation sources alive through late cleanup, guard request-local callbacks, preserve primary faults during teardown, retain last failure/success across resets, rediscover HWND on Retry, and expose exact stage/type/HRESULT/message plus ownership/state. Manual fallback and matcher, synchronization, provider and recommendation behavior are preserved.

Fourteen focused regressions bring the suite to 725 passing tests on Windows/portable targets with zero build warnings/errors. A real WGC fixture captured 1,000/1,000 frames with collected handles leveling around 600; the final 100-cycle run passed too. The historical late-draft initialization fault itself was not reproduced. Actual Arena 5/4/3/2-card validation and a fresh four-card debug pair remain pending because desktop inspection cannot initialize. No separate low-card matcher issue was established. See [PHASE9B_8_REPORT.md](PHASE9B_8_REPORT.md). Stop after Phase 9B.8; Phase 9D remains unstarted.

## Phase 9D - Successful / trophy deck evidence (implemented; scoring disabled by source audit)

Add provider-neutral successful-event criteria, immutable complete deck/available-pool copy counts, a deduplicated corpus, event-level pool-to-maindeck conversion, copy utilization, presence and average-copy diagnostics. Phase 9D wraps the unchanged Phase 9C result. Its adjustment is zero: 17Lands Play Rate weights games and copies and cannot baseline event-presence conversion. No scoring switch bypasses this decision.

Audit the actual internal `POST /api/trophies/` and nominated-build `GET /api/deck/draft/` JSON. WOE QuickDraft BG returned 60 recent summaries, 34 without splashes. The API explicitly restricts outside use; the provider fails closed before further deck requests or cache writes when it sees that notice. The public dataset catalog has WOE PremierDraft/TradDraft, but no WOE QuickDraft. No substitution or runtime bulk download occurs. The bounded adapter/corpus pipeline is ready for usable source data, but the current production WOE QuickDraft corpus is unavailable.

Lazy asynchronous loads use the active Phase 9C set/format/pair, a three-corpus draft budget and independent versioned TTL/atomic cache. Loading, failure and stale responses preserve recommendations. The existing rail exposes evidence/status, pre-trophy rank and disabled-scoring reasons; passive badge metrics/geometry remain unchanged. Twenty-three focused additions retain all 754 existing tests: 777 pass on Windows and portable .NET 10, with zero final build warnings/errors. Windows active-archetype corpus validation cannot be completed with the restricted source; native macOS execution remains unvalidated. See [PHASE9D_REPORT.md](PHASE9D_REPORT.md). Stop after Phase 9D; deck building, synergy and Phase 10 remain future work.

## Phase 10A — Draft Pool and Deck-Building Metadata Foundation (implemented)

Expose an immutable, provider-neutral owned-card inventory with counts and Complete/Partial/Unknown status, independent of exact pick history. Application adapts the state engine's authoritative merged multiset without summing history twice; the final pool remains available after completion and a new session replaces it. Import mana value/cost, central type flags, basic lands, useful face metadata, Oracle text, keywords, string P/T and structured mana production from the existing Scryfall raw cache. Preserve unknown values explicitly.

Describe multiplicity, overlapping color/type membership and fractional-safe nonland/creature curves in `DraftPoolAnalyzer`; show a compact collapsible rail panel and expanded entry diagnostics. No deck selection, role classification, mana optimization, recommendation or capture changes. Twenty-five focused additions retain all 777 existing tests: 802 pass on Windows and portable .NET 10, with zero final build warnings/errors. Real cached import of 118,469 printings succeeds without redownload. Native macOS execution and physical panel acceptance remain unvalidated. See [PHASE10A_REPORT.md](PHASE10A_REPORT.md).

## Phase 10B — Baseline 40-Card Deck Construction (implemented)

Construct one conservative two-color baseline from the owned multiset: 23 eligible spells, 17 lands, unlimited separately generated basics, and conserved main/sideboard copy counts. Recompute final Phase 9C context after completion; prefer a viable active pair, otherwise the highest viable pool-evidence pair. Strength uses Phase 8 overall value with optional matching active-pair affinity and explicit neutral/unscored fallback. A bounded deterministic optimizer enforces feasibility-adjusted creature, early-play and high-cost constraints. Separate nonbasic selection, primary-face pip demand and minimum-support/largest-remainder basics provide an advisory Baseline Mana Base.

Completed-only asynchronous construction and a collapsible counted deck/sideboard panel preserve draft badges and session isolation. Twenty-six focused additions retained all 802 prior tests: 828 passed on Windows and portable .NET 10 with zero warnings/errors. A genuine completed WOE pool audited as RG, 40 cards, 14 creatures and about 15 ms. The initial DeckSelect activation gap is resolved in Phase 10B.1 below. No capture/scoring/provider or selection-algorithm change was made. Native macOS and physical panel acceptance remain unverified. See [PHASE10B_REPORT.md](PHASE10B_REPORT.md).

## Phase 10B.1 — Live Draft Completion / DeckSelect Integration (implemented; physical acceptance pending)

Recognize the observed successful top-level DeckSelect/Completed payload through the canonical DraftCompleted event. Preserve authoritative recovered/exact PickedCards counts, compare final single-selection Quick Draft multiplicities, and allow a validated full final snapshot to recover missing inventory without invented chronology. Mismatches retain known cards, report both totals and make reliability/provisional status explicit. Generic deck, sideboard, Courses and CardPool records do not replace drafted inventory, with one exception: the narrow Premier/Traditional/Quick course-list completion added later (see the course-list completion section). Duplicate completion is idempotent; existing session/generation fences protect new drafts and asynchronous results.

Eleven focused additions retain all 828 existing tests: 839 pass on Windows and portable .NET 10 with zero warnings/errors. Real current-log replay through the production view model and coordinators automatically exposes a Complete 42-card pool and Ready RG 40-card baseline, with one completion transition, no warnings/conflicts and no HTTP requests. No manual Completed state or direct builder call is used. This replay is headless; physically observing a newly running build after a live final pick remains pending, as does native macOS execution. Phase 10B selection, Phase 8/9 scoring, providers and capture/localization are unchanged. See [PHASE10B_1_REPORT.md](PHASE10B_1_REPORT.md).

## Phase 10C — Multiple Two-Color Suggested Builds (implemented; physical acceptance pending)

Preserve the exact Phase 10B baseline as Recommended Build 1. Evaluate all ten pairs, run the shared constrained builder for every viable pair, and present up to three distinct pairs. Alternatives use a common overall Phase 8 spell-quality metric and lexicographic tie breaks; pair affinity never inflates cross-build comparison. Each proposal has independent inventory and mana allocation, visible coverage/relaxations, and counted spell/land differences. The rail selects one advisory build and retains its identity within the completed session.

The real completed 42-card WOE pool supports only RG and UG: two valid 40-card builds, with the prior RG lists unchanged. UG has ten creatures and exposes its creature-floor relaxation. Generation measured a 23.7 ms warm median with cached data, one existing final-RG pair load and zero additional pair loads or HTTP requests. Twenty-eight focused additions retain all 839 existing tests: 867 pass on Windows and portable targets with zero warnings/errors. Physical rail interaction and native macOS execution remain unvalidated. See [PHASE10C_REPORT.md](PHASE10C_REPORT.md).

## Phase 10D — Arena Deck-Builder Add/Remove Highlighting (audit and foundation in progress)

The October 4 audit found exact historical saved-deck main/sideboard/basic counts after EventSetDeckV3, but has not established an authoritative current unsaved editor source. Implemented the separate immutable observed-deck/revision model, saved-response parser/adapter, selected-build multiset differences and fail-closed saved/current distinction. Twenty focused additions retain all 867 previous tests: 887 pass on Windows and portable targets with zero build warnings/errors.

An explicit read-only Arena-only WGC capture command is available. The live audit observed EventLanding and later Home; a real completed deck-builder fixture and a user-performed edit observation are outstanding. No production deck-builder localization, outlines, comparison rail or live revision wiring has been implemented. Do not proceed with highlighting until the required semantic and visual audit is satisfied. Physical Windows acceptance and native macOS visual localization remain pending. See [PHASE10D_AUDIT.md](PHASE10D_AUDIT.md).

## Course-list draft completion after an Arena restart (fix)

**Root cause.** DraftTG stayed `Waiting for MTG Arena...` on a completed Premier Draft FRA deck builder because Arena had been restarted after the draft, and the rotated Player.log held no draft records. The only evidence was `EventGetCoursesV2`. Its `DeckSelect` module and `CardPool` are nested inside `Courses[]`, with no `Payload`, and Phase 10B.1 recognizes only the top-level DeckSelect/Completed payload written at the final pick. So the parser emitted nothing, the state engine stayed Idle, and Idle is rendered as Waiting. This was not specific to Premier Draft; a Quick Draft after a restart behaves the same way.

**Fix.** A top-level `{"Courses":[...]}` response now completes a draft only when:
- exactly one Premier, Traditional or Quick course is in `DeckSelect`
- that course has a full 3×14 `CardPool`
- the state engine is idle or is tracking the same event

The course list never replaces a live or different draft. Duplicates are preserved, and repeated course lists are idempotent. The fix does not change scoring, capture, localization or Phase 9E.1.

**Residual blocker** (since resolved). Scryfall had no FRA `arena_id`. The Arena local-database identity fallback below now resolves the pool 42/42 and activates Suggested Decks. Nine regression cases bring the suite to 929 passing tests.

## Arena local-database card identity fallback (implemented)

This fallback applies only when the Scryfall `arena_id` mapping is Missing. It maps the GrpId through Arena's read-only `Raw_CardDatabase` (`ExpansionCode` + `CollectorNumber`) to exactly one catalog printing, and resolves only when the name agrees with Arena's English title.

- Several printings stay Ambiguous; a zero match or a name mismatch stays Missing.
- Tokens and rebalanced cards are excluded.
- Direct mappings and direct ambiguities are untouched.
- Provenance appears in pool diagnostics.
- If the database is unavailable, resolution falls back to the existing Missing behavior.

The real FRA Premier pool resolves 42/42 through the fallback, producing a Ready RG baseline and Suggested Decks. Sixteen focused cases bring the suite to 945 passing tests with zero warnings/errors. Scoring, deck algorithms, capture, localization and Phase 9E.1 are unchanged.

## Phase 9E.1 — Arena display-order evidence collection (implemented; evidence gathering pending)

Read Arena's local `Raw_CardDatabase_*.mtga` strictly read-only. Port the review's 400-rule sort-key family into a pure `ArenaDisplayOrderModel`, which supports functional equivalence classes and Unanimous/Discriminating/Unavailable predictions. Record an IDs-only, versioned JSONL observation only from full high-confidence automatic placements; manual, partial, ambiguous and stale results are never recorded. Each pack is predicted before it is learned from. A diagnostic rail line and the `--order-evidence-summary` command report survivors, classes, contradictions and strict Gate-1 progress.

On the two real WOE packs, 120/400 rules survive in 20 classes. Placement, the matcher and recommendations are unchanged. Sixteen focused test methods (33 cases) bring the suite to 920 passing tests with zero warnings/errors. No live observations exist yet, so Gate 1 stands at 0/2 drafts and 0/80 observations.

Gate 1 requires 2 complete drafts, ≥ 80 observations, the listed coverage, two window sizes, and exactly one surviving class with zero contradictions. Do not start Phase 9E.2 until Gate 1 is satisfied and separately approved. If the family is refuted, abandon DB order. See [PHASE9E_1_REPORT.md](PHASE9E_1_REPORT.md).

## Later — Recommendation Explanations

Explain the most important factors behind each recommendation in concise user-facing language. Keep explanations traceable to engine inputs and rules.

Later deck work includes same-pair strategy variants, splashes, card roles, richer synergy, mana-source optimization and successful-deck composition analysis after their input semantics are established.

## Phase 11 — Production Overlay Refinements

Refine the cross-platform Avalonia overlay for production recommendations. Address opt-in native click-through behavior, permissions, focus, and positioning carefully.

Deck construction follows the Phase 10A–10D sequence above. Complete Phase 10D's current-editor and visible-tile audit before production highlighting; do not start Phase 10E or additional deck-generation heuristics.
