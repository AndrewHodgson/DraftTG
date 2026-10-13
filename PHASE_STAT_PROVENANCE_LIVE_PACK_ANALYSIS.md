# Real Phase 9F.2 pack: card statistics and scoring provenance

Analysis date: 2026-10-13. Base commit: `d1d09f6b216f23b8e0ce69a77afc552e84b9a014`, with the existing uncommitted Phase 9F.2 implementation. Diagnostic-only additions are described below. `contextual-pick-score-v1.6`, `pick-score-tiers-v2`, capture-free localization, memory reading, slot geometry, and production authority were not changed during this investigation.

## Finding

The reported discrepancy is reproduced. Uldaros Theorix has a correctly resolved **existing** FRA QuickDraft provider row, but that row reports null GIH and null ALSA. Its GIH sample count is 371. DraftTG preserves these nulls and estimates intrinsic quality with the static neutral prior `Q = 0.5`, producing **25 EST**. Desperate Futurescribe has usable GIH, receives `Q = 0.6021321854690532`, and scores **30.00447708798361**, displayed as **30**, rank 1.

This is primarily **sparse exact-format provider data combined with a fallback-quality modeling limitation**. No identity mapping, name normalization, printing, wrong-format query, or ingestion defect was found for this pack. P1P1 contextual signals are neutral, so a lane, archetype, or projected-deck penalty does not explain the reversal. The fallback cannot distinguish an unsampled bomb from an unsampled weak card.

## Reconstructed evidence and historical limits

The exact native order was recovered from `artifacts/phase9f2-live.jsonl`, stable generation 2 at `2026-10-13T14:55:05.8743181+00:00`, run `4af75fef2a7c4c50a63373037650389a`. Course ID: `d913d359-55e3-4a94-aee7-03259a8ff48c` (memory course ID, not a populated bot draft ID). Event: `QuickDraft_FRA_20261008`; P1P1; 14 cards; 1600×900 client at DPI 96. It agrees with the earlier initial-overlay smoke ledger's pack. The raw Player.log BotDraft status independently supplies the same multiset, zero picked cards, and QuickDraft context; its order differs from the native display order. The replay retains the native order and uses the existing production log parser, state engine, printing resolver, snapshot adapter, provider cache reader, statistical mapper, and scoring pipeline.

The frozen [regression fixture](tests/Fixtures/phase9f2-live-pack-stat-provenance.json) contains the selected stable memory record, the corresponding final badge record, raw provider cache envelope and all 290 rows, all decoder inputs for the 14 printings, all mapped environment statistics, per-card raw rows, identity provenance, current v1.6 breakdowns, ranks and unrounded scores. Every reconstructed printing UUID was checked against the live renderer record. The pack has empty pool/history; there is no missing earlier-pick reconstruction. No screenshots or new live memory reads were used in this investigation; replay does not count as live validation evidence.

The cache was fetched before both live observations, and its fields reproduce the reported GIH/ALSA and both scores. However, the live ledger did not hash the statistics payload at score time. Consequently, a byte-identical historical in-memory response cannot be independently proved. That limitation is retained in the fixture. The source ledger can continue growing; the fixture freezes the records and hash read during this investigation.

Frozen input SHA-256:

- QuickDraft provider cache: `4AA17E20D560A6C17D21A90CCAFD676352B575363A6AA08B0FF51ADDFD11CC1F`.
- Scryfall bulk cache: `D44936533BB112D7F004ACF8939224B759E7BCBD450DB60F387B83B048539B52`.
- Ledger, saved log and Arena database hashes: fixture top-level `SourceLedgerSha256`, `SourceLogSha256`, `ArenaDatabaseSha256`.
- The live Windows build's baseline and binary hashes are preserved in [tested-build.json](docs/evidence/phase9f2/tested-build.json). The diagnostic replay uses the complete cached Scryfall import, preserving catalog-wide ArenaId and front-face alias ambiguity checks.

## Exact ordered pack and identity join

All 14 cards have **no direct Scryfall ArenaId mapping** in this cache. All resolve through the existing `ArenaDatabaseSetCollector` bridge: exact Arena expansion/collector → exactly one Scryfall printing, with Arena English-title agreement. This is identity fallback, independently of whether quality is DIRECT or ESTIMATED. All printings are ordinary FRA set entries, not a missed bonus/special-sheet mapping. For the two multiface cards, Arena names the front face and Scryfall names the complete card. The existing structured front-face statistical fallback succeeds for both.

Indices are zero-based native display slots. Unless separately stated, Arena name, Scryfall canonical name and 17Lands lookup key are the same text in the Name column. **Every lookup has one raw provider row.** `Fatehold Chronologist // Peer Review` and `Konstrari Improviser // Soul Tether` have the explicit different Arena/key values shown below.

| Index | GrpId | Scryfall canonical name | Set / collector | Arena name / 17Lands key if different | Quality provenance |
|---:|---:|---|---|---|---|
| 0 | 106401 | Uldaros Theorix | fra / 159 | same | ESTIMATED |
| 1 | 106292 | Rise of the Deathbringer | fra / 63 | same | ESTIMATED |
| 2 | 106451 | Tomik, Orzhov Lawmage | fra / 206 | same | DIRECT |
| 3 | 106486 | Yargle, Glutton of Urborg | fra / 241 | same | ESTIMATED |
| 4 | 106498 | Tomik, Izzet Sparkmage | fra / 253 | same | DIRECT |
| 5 | 106347 | Restore with Empathy | fra / 112 | same | DIRECT |
| 6 | 106364 | Desperate Futurescribe | fra / 129 | same | DIRECT |
| 7 | 106240 | Memory Trap | fra / 15 | same | DIRECT |
| 8 | 106289 | Rampart Hunter | fra / 60 | same | ESTIMATED |
| 9 | 106327 | Tether Technician | fra / 94 | same | DIRECT |
| 10 | 106369 | Fatehold Chronologist // Peer Review | fra / 133 | Fatehold Chronologist | FRONT_FACE_FALLBACK |
| 11 | 106376 | Konstrari Improviser // Soul Tether | fra / 139 | Konstrari Improviser | FRONT_FACE_FALLBACK |
| 12 | 106418 | Medic's Kitesail | fra / 173 | same | DIRECT |
| 13 | 106423 | Formidable Commons | fra / 178 | same | DIRECT |

`FRONT_FACE_FALLBACK` denotes a safe statistical name alias with measured GIH, not estimated quality. The printing identity remains its exact Scryfall UUID. There is no unresolved or conflicting provider-name join in the pack. The fixture contains every UUID, raw Arena title, source flag, canonical name, collector and resolved key.

## Exact provider response and missing fields

Source: `%LOCALAPPDATA%\DraftTG\limited-data\17lands\FRA_QuickDraft_ALL_TIME_v3.json`. Schema 3; expansion FRA; requested/source format enum 2 = QuickDraft; endpoint `/api/card_data`; ALL_TIME; no colors/player-group/date filters. Fetched `2026-10-12T20:42:52.4662422+00:00`, inside the 24-hour TTL during the live sessions. Exact request:

`https://www.17lands.com/api/card_data?expansion=FRA&event_type=QuickDraft&time_period=ALL_TIME`

The diagnostic reads that cache through the production client with time frozen just after its fetch, and with a rejecting HTTP handler. It makes zero network requests and does not refresh or overwrite production caches. `ever_drawn_win_rate` maps to GIH, `ever_drawn_game_count` to GIH sample, and `avg_seen` to ALSA. Overall `win_rate` is a different metric and does not substitute for GIH. 17Lands distinguishes in-draft ALSA from in-game GIH; these can have different availability. See [17Lands metric definitions](https://www.17lands.com/metrics_definitions).

The following are **raw cached provider values**, not just final CardStatistics values. GIH is shown as the rate fraction used in scoring; null means an explicit JSON null.

| Index | GIH | GIH games | ALSA |
|---:|---:|---:|---:|
| 0 | null | 371 | null |
| 1 | null | 403 | 1.2216495 |
| 2 | 0.52450469 | 959 | 3.6274873 |
| 3 | null | 100 | 9.163836 |
| 4 | 0.51589242 | 1636 | 5.5189117 |
| 5 | 0.58125 | 1440 | 5.7759032 |
| 6 | 0.5907767 | 2060 | 3.5256707 |
| 7 | 0.57374493 | 3207 | 2.5933783 |
| 8 | null | 454 | 8.5912915 |
| 9 | 0.52405322 | 977 | 7.7451437 |
| 10 | 0.5816474 | 4152 | 3.5980262 |
| 11 | 0.56556522 | 2875 | 3.6580356 |
| 12 | 0.54887675 | 1647 | 7.1124198 |
| 13 | 0.58724832 | 894 | 4.0915 |

**Uldaros GIH —:** category **B**, raw row exists but `ever_drawn_win_rate = null`, alongside a nonzero low sample of 371. The provider, not DraftTG, has withheld the rate. Low-sample suppression is the supported inference: in this exact dataset every non-null GIH has at least 500 samples, and every sub-500 GIH sample has a null rate. The response does not contain an explicit suppression-reason field, so the provider's private decision is not proven. DraftTG requires a valid rate and positive count; it does **not** discard a non-null GIH merely because n < 500. Its 500-game low-sample badge marker and 500-equivalent-game shrinkage prior are separate UI/model choices.

**Uldaros ALSA —:** category **B**, raw `avg_seen = null`; `avg_pick` is also null. The response exposes no draft-seen/picked counts or field suppression reason. We can definitively locate the absence upstream of parsing/mapping, but cannot prove whether this particular null is from too few seen instances, missing draft records, or another provider rule. GIH's 371 is a gameplay sample and cannot establish an ALSA draft sample. It would be incorrect to assert “zero ALSA samples” or a precise ALSA threshold.

**Rise / Yargle / Rampart GIH —:** each is category **B**, with respectively 403 / 100 / 454 GIH games and explicit null rates. None has zero reported GIH games. Their existing `avg_seen` values survive parsing and display unchanged. There is no name mismatch, wrong printing, DFC failure, bonus-sheet failure, wrong-format request, or cache parser failure for these cards. Sparse GIH and populated ALSA are consistent with their distinct metric samples.

Uldaros also has overall `game_count = 735`, `win_rate = 0.61904762`, `play_rate = 0.8876811594202898`, opening-hand count 115 and drawn count 256, with OH/GD/IIH rates null. These are retained in the fixture. Its available overall rate must not be mislabeled as GIH. Rise has overall rate `0.56264237` over 878 overall games, Rampart `0.48525214` over 1051, and Yargle null overall rate over 228. All four cards have exact provider rows and other input evidence; “no provider row” would be a wrong diagnosis.

## Does 17Lands have data for Uldaros?

Yes, in both preserved format snapshots, with different completeness. QuickDraft has the partial row above. The independent **older PremierDraft** cache fetched `2026-10-05T19:44:35.1684847+00:00` has GIH `0.69034167`, n = 3717, ALSA `1.7372032`, ATA `1.681458`. Its data is frozen in [premier-comparison.json](docs/evidence/stat-provenance/premier-comparison.json) with original cache hash and context. Futurescribe's PremierDraft row has GIH `0.588429`, n = 15487, ALSA `4.4829672`.

That is independent evidence that treating Uldaros as intrinsically average may understate its strength. It is not the input used by the observed QuickDraft score. The current service deliberately requests exact-format data and never silently substitutes PremierDraft when a QuickDraft row is sparse. The event/set/format query is correct. No fresh provider response was substituted into the historical reproduction; these conclusions are about the preserved snapshots, not an assertion of today's live endpoint values.

## Exact contextual-pick-score-v1.6 reproduction

The environment baseline is the GIH-sample-weighted mean over valid mapped whole-environment rows, **B = 0.5704692518601339**. It is not a mean of these 14 cards. Phase 8 shrinks a usable card rate g with sample n toward B using k = 500:

`g_adjusted = (n*g + 500*B) / (n+500)`; statistical evidence weight `n / (n+500)`.

For measured cards, `Q = SoftLimit(0.5 + (g_adjusted-B)/0.16, knee=0.35)`. For unavailable GIH with a valid environment baseline, the existing engine sets `Q = 0.5`. At this P1P1:

- Completed picks and coordinate progress are zero. Stage weights are **Q 0.70, L 0.20, A 0.10, D 0** for every card.
- **R = 1**, **Q_eff = Q**, **L = A = D = 0.5** for every card. These common values complete the per-card breakdown in the table below.
- Reported playability is **1** for every card, the engine's early-draft placeholder. Deck impact is unavailable, no optimizer runs, and projected pair is **null** for every card. That placeholder is not evidence of actual projected-deck inclusion.
- No active pair data or previous lane observations affect P1P1. ALSA, ATA, play rate, OH/GD/IIH, rarity and Oracle metadata do not change intrinsic Q here. Role metadata is reported, but D has zero weight.
- Calibration gain is 1.4. All candidates here lie in the linear region of both soft limits. Exact score = `50 * (0.5 + 1.4*(0.7*Q + 0.2*L + 0.1*A - 0.5))`, equivalently `25 + 49*(Q-0.5)` up to floating-point arithmetic. Display rounding is half away from zero.

| Index | Q (= Q_eff) | Exact unrounded 0–50 score | Display | Final rank |
|---:|---:|---:|---:|---:|
| 0 | 0.5 | 24.999999999999996 | 25 EST | 6 |
| 1 | 0.5 | 24.999999999999996 | 25 EST | 7 |
| 2 | 0.31117197213901504 | 15.747426634811736 | 16 | 12 |
| 3 | 0.5 | 24.999999999999996 | 25 EST | 8 |
| 4 | 0.23874152351597872 | 12.198334652282957 | 12 | 14 |
| 5 | 0.5500137800303071 | 27.450675221485056 | 27 | 4 |
| 6 | 0.6021321854690532 | 30.00447708798361 | 30 | 1 |
| 7 | 0.5177115925859027 | 25.86786803670924 | 26 | 5 |
| 8 | 0.5 | 24.999999999999996 | 25 EST | 9 |
| 9 | 0.3081056908964509 | 15.597178853926094 | 16 | 13 |
| 10 | 0.5623544592066914 | 28.05536850112788 | 28 | 3 |
| 11 | 0.4738905711150282 | 23.720637984636383 | 24 | 10 |
| 12 | 0.3964751671994632 | 19.927283192773697 | 20 | 11 |
| 13 | 0.5672546938533019 | 28.29547999881179 | 28 | 2 |

The four estimated cards tie exactly on score, Q and absent statistical weight; native pack index breaks that tie. Uldaros's rank 6 is not evidence that the engine measured it as better than Rise, Yargle or Rampart. Ranking uses the unrounded value before display rounding, explaining other displayed ties such as rank 2/3 at 28.

**Uldaros numerically:** no adjusted GIH; Q = 0.5; R = 1; neutral L/A/D; weighted value = 0.5 apart from floating-point summation; calibration = 0.5; score ≈ 25. No rarity, mana cost, Oracle strength, ALSA, archetype-derived quality or alternative-format performance upgrades Q. The mythic is assigned the same intrinsic estimate as every card without usable GIH. This neutral prior is deliberately conservative but can compress exceptional cards to the same score as average unknown cards. It does not specifically estimate replacement-level quality, and does not prove this card is weak.

**Futurescribe numerically:** g = 0.5907767, n = 2060; weight = 2060/2560 = **0.8046875**; adjusted GIH = **0.5868104015351824**. Adjusted excess over B = **0.0163411496750485**, or about 1.6341 percentage points. Q = **0.6021321854690532**; weighted value = **0.5714925298283372**; calibrated value = **0.6000895417596722**; score = **30.00447708798361**. Its entire gain over neutral 25 is **5.0044770879836085 quality points**; lane, archetype, deck need and soft-limit contributions are zero. ALSA 3.5256707 is displayed but does not produce those five points.

Full raw statistical inputs, shrinkage results, scores, roles, evidence sources, contributions, reasons, stage weights and projected-pair diagnostics for all 14 cards are in the fixture's `Expected` records. The CLI prints these without truncating precision.

## Untapped comparison and recommendation

The supplied request reports Untapped ordering Uldaros above Futurescribe (approximately 54 versus 48). No screenshot binary was supplied for this investigation, and no proprietary formula was inspected or reproduced. This is external reported evidence of a suspicious ordering, not a numeric calibration target. The separate PremierDraft snapshot supports the concern without making the two format samples interchangeable.

**Smallest immediate correction:** expose the already available provenance in diagnostics: “provider row exists; GIH unavailable; n=371; ALSA explicitly null; neutral format prior.” The new frozen-card CLI provides that explanation. Normal production badges were not changed. Do not label Uldaros “no provider row,” and do not treat its identity bridge as the cause of missing GIH.

**Smallest subsequent model change worth evaluating, subject to explicit approval:** an explicitly labeled, conservatively shrunk alternate-format quality prior for exact-format rows without usable GIH, with exact-format measurements retaining precedence. Keep the actual QuickDraft GIH/ALSA display null; disclose source format, fetch time and reliability of any borrowed quality. Do not replace the entire environment, silently use overall win rate as GIH, invent rarity-based bomb bonuses, or globally retune Q/L/A/D constants to solve this particular case. Comparison against additional real sparse-data packs is needed before choosing the prior and its strength. That change is a recommendation only and is not implemented.

This pack does **not** justify a general contextual coefficient correction in v1.6.1: it demonstrates unavailable quality input and the existing neutral fallback, not an erroneous application of measured quality or contextual weights. A reviewed change to fallback-quality semantics would warrant a new scoring version; the earlier Phase 9E.2B late-pick findings remain independent and should be evaluated separately. Better diagnostic labels alone do not warrant a scoring version bump. Merely waiting for the exact QuickDraft sample to grow may also change this outcome without a model change; the current snapshot cannot predict when that will occur.

## Diagnostic additions, commands and validation

Files added/changed by this investigation:

- `tools/DraftTG.PickScoreAudit/Program.cs`: dispatches two additional diagnostic commands.
- `tools/DraftTG.PickScoreAudit/LivePackProvenance.cs`: offline evidence reconstruction/freezing and per-card inspection.
- `tests/Fixtures/phase9f2-live-pack-stat-provenance.json`: exact ordered real pack, raw provider inputs and frozen v1.6 expectations.
- `tests/DraftTG.Application.Tests/LivePackStatProvenanceTests.cs`: one targeted real-pack regression exercising the production provider DTO/cache validator, null preservation, printing/name provenance, safe front-face aliases, all 14 scores/ranks and unchanged versions, with network forbidden.
- `tests/DraftTG.Application.Tests/DraftTG.Application.Tests.csproj`: copies the fixture to test output.
- `docs/evidence/stat-provenance/premier-comparison.json`: independent frozen older-format comparison, not used for reproduction.
- This report. Existing uncommitted Phase 9F.2 files were left as they were.

From the repository root, display Uldaros's frozen diagnostic record:

```powershell
dotnet run --project .\tools\DraftTG.PickScoreAudit -- --card-stat-provenance .\tests\Fixtures\phase9f2-live-pack-stat-provenance.json 106401
```

Use `106364` for Futurescribe. This command reads the frozen investigation artifact, not the current live provider cache. To independently reconstruct the same historical pack from a saved log and ledger, writing a new artifact:

```powershell
dotnet run --project .\tools\DraftTG.PickScoreAudit -- --live-pack-provenance .\artifacts\phase9f2-live.jsonl .\artifacts\phase-stat-provenance-player.log .\artifacts\phase-stat-provenance-replay.json
```

The freezing command is intentionally scoped to the investigated FRA P1P1, requires the exact raw cache to predate the observation, validates log pack/pool/context, and verifies the replayed printing identities against the recorded badges. It uses the locally installed read-only Arena card database; if local caches/database change later, use the committed frozen fixture for historical inspection. The saved log path above was copied read-only from the open Player.log; the tool expects a saved file and does not attach to Arena.

Validation completed:

- `dotnet build DraftTG.sln`: passed, 0 warnings, 0 errors.
- PickScoreAudit diagnostic build and full offline reconstruction: passed, all 14 identities checked, 290 provider rows loaded, 0 network requests; both reported scores reproduced.
- Per-card provenance command for Uldaros: passed, exact row/sample/nulls and EST reason printed.
- `dotnet test DraftTG.sln --no-build`: **1266 passed, 0 failed, 0 skipped** (Domain 56, Data 160, ArenaIntegration 146, Application 280, RecommendationEngine 329, App 295). TRX results are in `artifacts/stat-provenance-tests`.
- One new targeted regression; no scoring correction, capture-free changes, or authority promotion implemented. Stop here pending an explicitly approved fix scope.
