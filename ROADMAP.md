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

## Phase 9 - Contextual Draft Evaluation (not started)

Add draft-context/pool-aware evaluation, including likely colors and archetypes from drafted cards and signals. Represent uncertainty rather than forcing premature conclusions. Phase 8 contains none of this contextual logic.

## Phase 10 — Recommendation Explanations

Explain the most important factors behind each recommendation in concise user-facing language. Keep explanations traceable to engine inputs and rules.

## Phase 11 — Production Overlay Refinements

Refine the cross-platform Avalonia overlay for production recommendations. Address opt-in native click-through behavior, permissions, focus, and positioning carefully.

## Phase 12 — Deck Builder

Recommend a final 40-card deck from the completed draft pool, including lands and sideboard decisions. Make every inclusion and exclusion inspectable.
