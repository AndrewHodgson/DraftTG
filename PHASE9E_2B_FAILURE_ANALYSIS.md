# Phase 9E.2B failure analysis

Analysis date: 2026-10-12 (America/Chicago). No new draft, capture, promotion evidence or scoring calibration change was performed.

Live build: `1.0.0+449eab11ae1b9f6d29953fc8e3cb4ed845db57f2`, MVID `f91b5307-b7c6-4734-9bbb-85234488e2ce`. Draft scope: `eaf5816cd8fb811a69ba001241680841`; runtime session: `0bd92f4fccd6484c91cbc34654ff8097`. Diagnostic changes are based on repository commit `a526df4` plus this working-tree patch. The user-updated live validation report and original ledgers/Player.log were preserved.

Build: `dotnet build DraftTG.sln -p:BaseOutputPath=bin/Phase9E2BFailure/` ? 0 warnings, 0 errors. Targeted tests: 5 passed. Full solution: `dotnet test DraftTG.sln --no-build -p:BaseOutputPath=bin/Phase9E2BFailure/ -m:1 -- xUnit.ParallelizeTestCollections=false` ? **1,233 passed, 0 failed, 0 skipped** (Domain 56; Data 160; ArenaIntegration 127; Application 274; App 287; RecommendationEngine 329).

Evidence: local append-only localization/order ledgers, the existing Player.log, cached Scryfall catalog and Arena database, exact cached FRA QuickDraft statistics fetched at `2026-10-12T20:42:52.4662422Z`, reference-file timestamps, and production code. No application trace file or saved pixel frame from this live run was found in the inspected app-data/temp locations. Old audit screenshots were not substituted for this session. The native draft identifier is absent from the parsed QuickDraft state; matching recorded offered-card multisets and chronology ties Player.log to this runtime scope. The offline parser reports one malformed relevant outer-JSON record at line 296, before any current pack; it does not prevent recovery of the target packs.

## 1. Supported 13/14-card localization failure

**Established result:** all nine supported attempts had a non-unanimous comparator prediction before verification. Zero promotion eligibility is therefore correct, independently of pixels. Two P2P1 attempts did fully verify one 14-card candidate; ?0 eligible? and the old ?0 verifier failures on eligible attempts? must not be read as ?the verifier never worked.?

The session has 28 live attempts, 9 at supported counts and sizes, 19 unsupported-count attempts. Every observed DPI is 96. No 15-card observation exists. WGC remains authority, and no deterministic placement was made.

| Attempt | UTC captured | Pack/pick | Cards/client | Pre-verification comparator | WGC | Fast candidate accepted slots | Capture/full/fast ms | Frame admission age ms | Exact decline |
|---|---|---|---|---|---|---|---|---|---|
| S1 | 20:43:26.316 | P1P1 | 14, 1920x1080 | 15 orders; 400 rules | 14/14 | not run | 129.2/868.7/0.0 | 5367.6 | manual intervention during this pack generation; candidate limit exceeded (15 > 8) |
| S2 | 20:45:07.054 | P1P2 | 13, 1920x1080 | 2 orders; 55 rules | 0/13 | 0/13/0/13 | 27.2/565.2/661.8 | 3321.5 | 0 fully verified candidate orders |
| S3 | 20:45:13.110 | P1P2 | 13, 1920x1080 | 2 orders; 55 rules | 12/13 | 12/13/10/13 | 29.8/649.7/712.4 | 1395.1 | 0 fully verified candidate orders |
| S4 | 20:45:17.157 | P1P2 | 13, 1920x1080 | 2 orders; 55 rules | 12/13 | 12/13/10/13 | 28.5/627.4/737.1 | 1412.2 | 0 fully verified candidate orders |
| S5 | 20:47:08.514 | P1P2 | 13, 1600x900 | 2 orders; 55 rules | 0/13 | 0/13/0/13 | 36.0/471.0/478.9 | 998.1 | 0 fully verified candidate orders |
| S6 | 20:47:12.223 | P1P2 | 13, 1600x900 | 2 orders; 55 rules | 12/13 | 12/13/10/13 | 24.6/652.0/679.5 | 1369.8 | 0 fully verified candidate orders |
| S7 | 20:47:16.258 | P1P2 | 13, 1600x900 | 2 orders; 55 rules | 12/13 | 12/13/10/13 | 23.6/674.6/683.8 | 1395.2 | 0 fully verified candidate orders |
| S8 | 20:52:15.164 | P2P1 | 14, 1600x900 | 2 orders; 55 rules | 12/14 | 14/14/12/14 | 27.2/760.7/765.3 | 3916.3 | order discriminating (2 predicted orders) |
| S9 | 20:52:21.226 | P2P1 | 14, 1600x900 | 2 orders; 55 rules | 12/14 | 14/14/12/14 | 27.3/766.0/736.0 | 1541.1 | order discriminating (2 predicted orders) |

**Comparator diagnosis.** Replaying the order ledger only up to each frozen prediction timestamp reproduces all nine recorded candidate-order sets exactly. The numeric forensic artifact below contains every ordered identity, survivor rule name and sort key; no majority vote is used.

- S1/P1P1: empty pre-pack ledger, 400 hypotheses, 15 distinct orders. The eight-order cap declines the entire fast attempt. Calibration during this pack marks manual involvement. Full WGC independently matches 14/14 and teaches the order ledger; the observer does not promote an arbitrary initial order.
- S2?S7/P1P2: 55 surviving rules produce two orders (44 versus 11), differing only at zero-based slots 6 and 7: Infinite Coursework (106257) versus Semester Foreseer // Peer Review (106265). Both have rarity 3 and color order 1. Their creature sort keys differ (1 versus 0), CMC keys differ (3 versus 4), and their titles/collector numbers retain Coursework-first order. Creature-prioritizing surviving rules put Foreseer first. This is unresolved comparator evidence, not an identity alias or a stale prediction.
- S8?S9/P2P1: 55 surviving rules produce two orders (54 versus 1), differing at slots 6 and 7: Hexhaven Battalion (106237) versus Predictive Preparations (106241). Rarity 3, color order 0 and creature key 1 tie; CMC is 6 versus 2. The sole `rarity>color>creaturesFirst>cmc` survivor reverses them. Unique pixel verification accepts the first order, but it was not unanimous beforehand. Both attempts correctly remain non-promotional.

**Pixel diagnosis.** S2 and S5 fail broadly: neither full WGC nor either candidate verifies the pack. S2 was captured only 470 ms after pack acceptance. S5 is the first capture after the 1920x1080?1600x900 resize. Their failure is a scene/registration/geometry/timing ambiguity, not a proven stale-generation fault: every recorded admission is current-generation, and no rejection was logged. Without those pixels, animation, hover/occlusion, incorrect scene sampling or another registration failure cannot be distinguished.

S3/S4/S6/S7 verify the same 12 nonbasic cards at confidence approximately 0.98?0.995 and strong margins. Forest (106535) has expected score 0 and negative margin in both candidates; its failure reason does **not** report a missing exact-printing template. WGC also misses Forest. Its reference exists in the local cache. These facts establish failed pixel matching for the Forest occurrence, but do not prove whether Arena showed another basic-land art treatment or whether reference/artwork-region semantics differ. Do not skip Forest or lower thresholds to manufacture full-pack success.

For the matched subset in S3/S4, the common holder/artwork centre offset is approximately (+0.241,+3.947) px and max scatter 0.968 px. S6/S7: (-0.011,+3.155) px, max scatter 1.493 px. S8/S9: (-0.024,+3.412) px, max scatter 1.222 px. These are derived comparisons using the first candidate (the unique pixel winner on settled frames), not promotion evidence. They argue against a gross deterministic-grid translation/scale error for the successfully matched cards. Missing-card rectangles cannot be validated by this subset.

S8/S9 verify **14/14** in the first candidate: Wrath of the Bloodmane passes at 0.988165/margin 0.440376; Murmuring Volume at 0.980933/margin 0.430984. Full WGC omits both. This establishes a recall failure in the full-matcher localization path on pixels that the existing bounded fixed-slot verifier can identify; it does not establish which global proposal/assignment/registration decision caused the omission. The wrong comparator candidate fails precisely the two swapped occurrences.

**Failure counters.** All 37 reported localization/shadow failures have the same crop-bounds message. There are 29 in this runtime and 8 in the earlier startup runtime `ccc08328b843438f9f2b78399bc63c6c`. There are no recorded ShadowFailure events. The 29 crop errors finish before the first successful capture; they are a calibration prerequisite failure repeatedly logged by the two-second watchdog, not 29 verifier crashes or a failure throughout later smaller packs. Historical invalid calibration coordinates were not logged, so the exact offending edge cannot be recovered from the subsequently saved calibration.

**Timing.** Admission age starts at capture, so the 250 ms pre-capture settle interval is outside this measurement. It includes reference indexing/downloads, full matching, frame/reference copying, shadow queue wait, strict verification and UI admission. Upper-middle median is 1126.049 ms (the existing report convention); max is 5367.575 ms. Max WGC time is 868.685 ms. At the maximum-age S1 frame, capture?verification-start is 5356.556 ms, fast work is 0, and the final UI interval is about 11 ms. All 14 small reference JPEGs were written between 20:43:28.280 and 20:43:30.792 UTC, after the 20:43:26.317 capture and before admission. Together with the code?s per-image 150 ms download delay and first-use bulk-file indexing, this establishes cold reference acquisition on an already captured frame as a major first-frame latency source. It is not explained by pack stabilization or strict-verifier queue work alone.

On S3/S4/S6/S7, capture?verification-start is roughly 0.67?0.70 s and verification is 0.68?0.74 s; these two serial recognition passes explain most of their 1.37?1.41 s admission age. Other cold-pack attempts spend 2?3 s before fast verification. Historical timestamps cannot precisely separate reference I/O from copying, queueing and UI dispatch, and age alone does not prove stale pixels caused missing badges. New stage timestamps now make those costs separable.

**Smallest recommended fixes (not implemented):** prewarm the existing reference cache before acquiring the frame that will be matched, or reacquire a fresh synchronized frame after cold reference preparation, preserving generation cancellation; diagnose Forest?s exact art/scene before altering registration; replay the P2P1 WGC candidate audit on its pixels before changing proposal search; suppress/deduplicate repeated unchanged calibration-prerequisite diagnostics after preserving the first complete bounds snapshot. Keep unsupported counts unsupported. Keep WGC authoritative.

**Diagnostics implemented:** opt-in full-matcher audit with assigned/best candidates, scores, competing scores, proposed bounds and rejection reasons; crop/anchor and frame dimensions/hash; exact copied reference identities; candidate-specific verifier client rectangles; reference/matcher start/end, observer queue and copy durations; saved calibration/client geometry on crop failure. The copied reference key set is frozen with the frame, so a later pack cannot rewrite diagnostic reference availability. Session filtering and supported-attempt/fast-winner counts were added to the existing summary; no evidence source or pipeline was replaced.

**Tests needed:** exact live-pixel offline regressions for Forest and P2P1 once the pixels are available; pack animation/hover controls; cold-reference preparation followed by a generation change; invalid calibration edge cases without changing scoring. Existing and extended strict-admission tests cover detailed diagnostics and the unchanged 0.94/0.07 gates. Offline data does not contribute to the live promotion gate.

Full numeric derivative: [supported-attempt forensics](tests/Fixtures/phase9e2b-supported-attempt-forensics.json). Coordinates below are physical client pixels, `(x,y,width,height)`, zero-based slot indices. Predicted holder rectangles are reconstructed with the existing supported model; discriminating candidates never supplied production rectangles. WGC rectangles and confidence are directly recorded. Fast cells show expected score / ambiguity margin, PASS or the exact rejection reason. `n/a` means not measured, not zero confidence.

### S1: P1P1, 1920x1080

Frame 2026-10-12T20:43:26.3168402+00:00; admitted 2026-10-12T20:43:31.6844155+00:00; capture generation 5, request 1, pack generation 1. All current-generation checks passed; manual-history marker True.

Candidate orders before verification:
- C1: `106356,106383,106385,106449,106481,106514,106231,106240,106273,106306,106320,106335,106371,106427`.
- C2: `106356,106383,106385,106481,106449,106514,106306,106335,106231,106371,106240,106320,106273,106427`.
- C3: `106356,106449,106481,106514,106385,106383,106231,106240,106273,106306,106320,106335,106371,106427`.
- C4: `106356,106383,106385,106449,106481,106514,106231,106306,106240,106273,106320,106335,106371,106427`.
- C5: `106356,106449,106514,106383,106385,106481,106273,106320,106335,106371,106240,106306,106231,106427`.
- C6: `106356,106449,106514,106383,106385,106481,106273,106335,106371,106320,106306,106240,106231,106427`.
- C7: `106356,106449,106481,106514,106385,106383,106240,106231,106273,106320,106306,106335,106371,106427`.
- C8: `106356,106449,106481,106514,106385,106383,106231,106306,106240,106273,106320,106335,106371,106427`.
- C9: `106356,106383,106385,106481,106449,106514,106306,106231,106335,106371,106240,106320,106273,106427`.
- C10: `106356,106449,106514,106385,106383,106481,106273,106320,106335,106371,106240,106306,106231,106427`.
- C11: `106356,106449,106514,106383,106385,106481,106306,106231,106273,106320,106335,106371,106240,106427`.
- C12: `106356,106449,106514,106383,106385,106481,106273,106320,106335,106371,106306,106240,106231,106427`.
- C13: `106356,106449,106514,106385,106383,106481,106306,106231,106273,106320,106335,106371,106240,106427`.
- C14: `106356,106449,106514,106383,106385,106481,106306,106231,106273,106335,106371,106320,106240,106427`.
- C15: `106356,106449,106514,106385,106383,106481,106273,106320,106335,106371,106306,106240,106231,106427`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Fateshaper Aspirant (106231 / 0) | 6 / (477.00,455.01,169.65,232.06) | (470.27,447.72,183.55,256.29) / 0.991815 | not run: candidate limit exceeded (15 > 8) |
| Compel Brutality (106335 / 1) | 11 / (477.00,722.60,169.65,232.06) | (470.72,716.24,183.59,254.39) / 0.991723 | not run: candidate limit exceeded (15 > 8) |
| Ferocity of the Hunt (106371 / 2) | 12 / (673.14,722.60,169.65,232.06) | (665.68,714.71,184.98,258.37) / 0.991344 | not run: candidate limit exceeded (15 > 8) |
| No Admittance (106320 / 3) | 10 / (280.85,722.60,169.65,232.06) | (275.00,715.59,182.48,254.37) / 0.994798 | not run: candidate limit exceeded (15 > 8) |
| Memory Trap (106240 / 4) | 7 / (673.14,455.01,169.65,232.06) | (666.16,446.67,184.01,259.47) / 0.992947 | not run: candidate limit exceeded (15 > 8) |
| Unsummon (106273 / 5) | 8 / (869.29,455.01,169.65,232.06) | (858.58,444.81,189.27,262.79) / 0.975179 | not run: candidate limit exceeded (15 > 8) |
| Chandra's Emberling (106306 / 6) | 9 / (1065.43,455.01,169.65,232.06) | (1059.79,451.45,180.92,246.97) / 0.987917 | not run: candidate limit exceeded (15 > 8) |
| Prudent Fateseer // Peer Review (106385 / 7) | 2 / (673.14,187.42,169.65,232.06) | (1060.97,178.56,180.49,258.09) / 0.988351 | not run: candidate limit exceeded (15 > 8) |
| Primal Witchstalker (106383 / 8) | 1 / (477.00,187.42,169.65,232.06) | (274.36,447.38,184.05,255.97) / 0.994161 | not run: candidate limit exceeded (15 > 8) |
| Aerid Konstrari (106356 / 9) | 0 / (280.85,187.42,169.65,232.06) | (274.85,178.71,183.09,256.26) / 0.989381 | not run: candidate limit exceeded (15 > 8) |
| Yoshimaru, Scrappy Stray (106514 / 10) | 5 / (280.85,455.01,169.65,232.06) | (862.19,182.34,184.78,247.85) / 0.988678 | not run: candidate limit exceeded (15 > 8) |
| Teyo, Diamondblade Mage (106481 / 11) | 4 / (1065.43,187.42,169.65,232.06) | (668.02,182.88,180.85,245.71) / 0.966461 | not run: candidate limit exceeded (15 > 8) |
| Teyo, Lightshield Expert (106449 / 12) | 3 / (869.29,187.42,169.65,232.06) | (475.36,178.73,176.51,256.94) / 0.968964 | not run: candidate limit exceeded (15 > 8) |
| Innovative Commons (106427 / 13) | 13 / (869.29,722.60,169.65,232.06) | (862.26,720.95,184.01,243.57) / 0.978018 | not run: candidate limit exceeded (15 > 8) |

Classification: candidate-cap + manual-history exclusion; WGC complete.

### S2: P1P2, 1920x1080

Frame 2026-10-12T20:45:07.0540082+00:00; admitted 2026-10-12T20:45:10.3755099+00:00; capture generation 9, request 2, pack generation 3. All current-generation checks passed; manual-history marker False.

Candidate orders before verification:
- C1: `106452,106456,106287,106488,106409,106231,106257,106265,106294,106296,106306,106335,106535`.
- C2: `106452,106456,106287,106488,106409,106231,106265,106257,106294,106296,106306,106335,106535`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Solve for Disappointment (106296 / 0) | 9 / (1065.43,455.01,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 9: 0.353649/-0.434668 score 0.354 below 0.94; margin -0.435 below 0.07<br>C2 slot 9: 0.353649/-0.434668 score 0.354 below 0.94; margin -0.435 below 0.07 |
| Compel Brutality (106335 / 1) | 11 / (477.00,722.60,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 11: 0.000000/0.000000 score 0.000 below 0.94; margin 0.000 below 0.07<br>C2 slot 11: 0.000000/0.000000 score 0.000 below 0.94; margin 0.000 below 0.07 |
| Infinite Coursework (106257 / 2) | 6 / (477.00,455.01,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 6: 0.000000/-0.750689 score 0.000 below 0.94; margin -0.751 below 0.07<br>C2 slot 7: 0.000000/-0.767198 score 0.000 below 0.94; margin -0.767 below 0.07 |
| Fateshaper Aspirant (106231 / 3) | 5 / (280.85,455.01,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 5: 0.000000/-0.777093 score 0.000 below 0.94; margin -0.777 below 0.07<br>C2 slot 5: 0.000000/-0.777093 score 0.000 below 0.94; margin -0.777 below 0.07 |
| Chandra's Emberling (106306 / 4) | 10 / (280.85,722.60,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 10: 0.610726/-0.187624 score 0.611 below 0.94; margin -0.188 below 0.07<br>C2 slot 10: 0.610726/-0.187624 score 0.611 below 0.94; margin -0.188 below 0.07 |
| Semester Foreseer // Peer Review (106265 / 5) | 7 / (673.14,455.01,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 7: 0.000000/-0.767198 score 0.000 below 0.94; margin -0.767 below 0.07<br>C2 slot 6: 0.000000/-0.750689 score 0.000 below 0.94; margin -0.751 below 0.07 |
| Screeching Soulbreaker (106294 / 6) | 8 / (869.29,455.01,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 8: 0.300802/-0.352394 score 0.301 below 0.94; margin -0.352 below 0.07<br>C2 slot 8: 0.300802/-0.352394 score 0.301 below 0.94; margin -0.352 below 0.07 |
| Woodwork Prodigy // Soul Tether (106409 / 7) | 4 / (1065.43,187.42,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 4: 0.000000/-0.585618 score 0.000 below 0.94; margin -0.586 below 0.07<br>C2 slot 4: 0.000000/-0.585618 score 0.000 below 0.94; margin -0.586 below 0.07 |
| Multiply by Zero (106287 / 8) | 2 / (673.14,187.42,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 2: 0.000000/-0.784539 score 0.000 below 0.94; margin -0.785 below 0.07<br>C2 slot 2: 0.000000/-0.784539 score 0.000 below 0.94; margin -0.785 below 0.07 |
| Arni, Humble Scribe (106456 / 9) | 1 / (477.00,187.42,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 1: 0.000000/-0.765767 score 0.000 below 0.94; margin -0.766 below 0.07<br>C2 slot 1: 0.000000/-0.765767 score 0.000 below 0.94; margin -0.766 below 0.07 |
| Arni, Renowned Champion (106488 / 10) | 3 / (869.29,187.42,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 3: 0.668388/-0.129962 score 0.668 below 0.94; margin -0.130 below 0.07<br>C2 slot 3: 0.668388/-0.129962 score 0.668 below 0.94; margin -0.130 below 0.07 |
| Way of the Healer (106452 / 11) | 0 / (280.85,187.42,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 0: 0.000000/-0.689695 score 0.000 below 0.94; margin -0.690 below 0.07<br>C2 slot 0: 0.000000/-0.689695 score 0.000 below 0.94; margin -0.690 below 0.07 |
| Forest (106535 / 12) | 12 / (673.14,722.60,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 12: 0.000000/-0.279492 score 0.000 below 0.94; margin -0.279 below 0.07<br>C2 slot 12: 0.000000/-0.279492 score 0.000 below 0.94; margin -0.279 below 0.07 |

Classification: broad pixel/scene failure, underlying cause unresolved.

### S3: P1P2, 1920x1080

Frame 2026-10-12T20:45:13.110747+00:00; admitted 2026-10-12T20:45:14.5058443+00:00; capture generation 9, request 3, pack generation 3. All current-generation checks passed; manual-history marker False.

Candidate orders before verification:
- C1: `106452,106456,106287,106488,106409,106231,106257,106265,106294,106296,106306,106335,106535`.
- C2: `106452,106456,106287,106488,106409,106231,106265,106257,106294,106296,106306,106335,106535`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Solve for Disappointment (106296 / 0) | 9 / (1065.43,455.01,169.65,232.06) | (1059.34,449.60,180.92,250.65) / 0.977641 | C1 slot 9: 0.989771/0.467616 PASS<br>C2 slot 9: 0.989771/0.467616 PASS |
| Compel Brutality (106335 / 1) | 11 / (477.00,722.60,169.65,232.06) | (470.72,716.24,183.59,254.39) / 0.991723 | C1 slot 11: 0.994480/0.653909 PASS<br>C2 slot 11: 0.994480/0.653909 PASS |
| Infinite Coursework (106257 / 2) | 6 / (477.00,455.01,169.65,232.06) | (470.72,448.01,183.59,253.74) / 0.990828 | C1 slot 6: 0.991145/0.622375 PASS<br>C2 slot 7: 0.269755/-0.724385 score 0.270 below 0.94; margin -0.724 below 0.07 |
| Fateshaper Aspirant (106231 / 3) | 5 / (280.85,455.01,169.65,232.06) | (274.85,447.78,183.09,254.38) / 0.991529 | C1 slot 5: 0.993478/0.613407 PASS<br>C2 slot 5: 0.993478/0.613407 PASS |
| Chandra's Emberling (106306 / 4) | 10 / (280.85,722.60,169.65,232.06) | (275.82,719.30,180.85,246.92) / 0.988492 | C1 slot 10: 0.990101/0.441801 PASS<br>C2 slot 10: 0.990101/0.441801 PASS |
| Semester Foreseer // Peer Review (106265 / 5) | 7 / (673.14,455.01,169.65,232.06) | (666.37,447.78,183.59,254.38) / 0.992639 | C1 slot 7: 0.994140/0.614070 PASS<br>C2 slot 6: 0.306795/-0.687345 score 0.307 below 0.94; margin -0.687 below 0.07 |
| Screeching Soulbreaker (106294 / 6) | 8 / (869.29,455.01,169.65,232.06) | (863.22,448.15,180.85,254.42) / 0.990145 | C1 slot 8: 0.995286/0.578403 PASS<br>C2 slot 8: 0.995286/0.578403 PASS |
| Woodwork Prodigy // Soul Tether (106409 / 7) | 4 / (1065.43,187.42,169.65,232.06) | (1062.06,182.14,176.95,249.82) / 0.978649 | C1 slot 4: 0.989581/0.644517 PASS<br>C2 slot 4: 0.989581/0.644517 PASS |
| Multiply by Zero (106287 / 8) | 2 / (673.14,187.42,169.65,232.06) | (667.34,182.89,180.42,247.65) / 0.983496 | C1 slot 2: 0.991930/0.623159 PASS<br>C2 slot 2: 0.991930/0.623159 PASS |
| Arni, Humble Scribe (106456 / 9) | 1 / (477.00,187.42,169.65,232.06) | (470.51,180.51,184.01,253.07) / 0.982171 | C1 slot 1: 0.982626/0.693104 PASS<br>C2 slot 1: 0.982626/0.693104 PASS |
| Arni, Renowned Champion (106488 / 10) | 3 / (869.29,187.42,169.65,232.06) | (861.35,180.77,184.97,252.37) / 0.990690 | C1 slot 3: 0.990613/0.442313 PASS<br>C2 slot 3: 0.990613/0.442313 PASS |
| Way of the Healer (106452 / 11) | 0 / (280.85,187.42,169.65,232.06) | (274.58,178.00,183.57,260.02) / 0.991150 | C1 slot 0: 0.989417/0.661647 PASS<br>C2 slot 0: 0.989417/0.661647 PASS |
| Forest (106535 / 12) | 12 / (673.14,722.60,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 12: 0.000000/-0.481162 score 0.000 below 0.94; margin -0.481 below 0.07<br>C2 slot 12: 0.000000/-0.481162 score 0.000 below 0.94; margin -0.481 below 0.07 |

Classification: Forest pixel match failed; candidate C2 also swaps Coursework/Foreseer; geometry agrees on matched subset.

### S4: P1P2, 1920x1080

Frame 2026-10-12T20:45:17.1573791+00:00; admitted 2026-10-12T20:45:18.5695954+00:00; capture generation 9, request 4, pack generation 3. All current-generation checks passed; manual-history marker False.

Candidate orders before verification:
- C1: `106452,106456,106287,106488,106409,106231,106257,106265,106294,106296,106306,106335,106535`.
- C2: `106452,106456,106287,106488,106409,106231,106265,106257,106294,106296,106306,106335,106535`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Solve for Disappointment (106296 / 0) | 9 / (1065.43,455.01,169.65,232.06) | (1059.34,449.60,180.92,250.65) / 0.977641 | C1 slot 9: 0.989771/0.467616 PASS<br>C2 slot 9: 0.989771/0.467616 PASS |
| Compel Brutality (106335 / 1) | 11 / (477.00,722.60,169.65,232.06) | (470.72,716.24,183.59,254.39) / 0.991723 | C1 slot 11: 0.994480/0.653909 PASS<br>C2 slot 11: 0.994480/0.653909 PASS |
| Infinite Coursework (106257 / 2) | 6 / (477.00,455.01,169.65,232.06) | (470.72,448.01,183.59,253.74) / 0.990828 | C1 slot 6: 0.991145/0.622375 PASS<br>C2 slot 7: 0.269755/-0.724385 score 0.270 below 0.94; margin -0.724 below 0.07 |
| Fateshaper Aspirant (106231 / 3) | 5 / (280.85,455.01,169.65,232.06) | (274.85,447.78,183.09,254.38) / 0.991529 | C1 slot 5: 0.993478/0.613407 PASS<br>C2 slot 5: 0.993478/0.613407 PASS |
| Chandra's Emberling (106306 / 4) | 10 / (280.85,722.60,169.65,232.06) | (275.82,719.30,180.85,246.92) / 0.988492 | C1 slot 10: 0.990101/0.441801 PASS<br>C2 slot 10: 0.990101/0.441801 PASS |
| Semester Foreseer // Peer Review (106265 / 5) | 7 / (673.14,455.01,169.65,232.06) | (666.37,447.78,183.59,254.38) / 0.992639 | C1 slot 7: 0.994140/0.614070 PASS<br>C2 slot 6: 0.306795/-0.687345 score 0.307 below 0.94; margin -0.687 below 0.07 |
| Screeching Soulbreaker (106294 / 6) | 8 / (869.29,455.01,169.65,232.06) | (863.22,448.15,180.85,254.42) / 0.990145 | C1 slot 8: 0.995286/0.578403 PASS<br>C2 slot 8: 0.995286/0.578403 PASS |
| Woodwork Prodigy // Soul Tether (106409 / 7) | 4 / (1065.43,187.42,169.65,232.06) | (1062.06,182.14,176.95,249.82) / 0.978649 | C1 slot 4: 0.989581/0.644517 PASS<br>C2 slot 4: 0.989581/0.644517 PASS |
| Multiply by Zero (106287 / 8) | 2 / (673.14,187.42,169.65,232.06) | (667.34,182.89,180.42,247.65) / 0.983496 | C1 slot 2: 0.991930/0.623159 PASS<br>C2 slot 2: 0.991930/0.623159 PASS |
| Arni, Humble Scribe (106456 / 9) | 1 / (477.00,187.42,169.65,232.06) | (470.51,180.51,184.01,253.07) / 0.982171 | C1 slot 1: 0.982626/0.693104 PASS<br>C2 slot 1: 0.982626/0.693104 PASS |
| Arni, Renowned Champion (106488 / 10) | 3 / (869.29,187.42,169.65,232.06) | (861.35,180.77,184.97,252.37) / 0.990690 | C1 slot 3: 0.990613/0.442313 PASS<br>C2 slot 3: 0.990613/0.442313 PASS |
| Way of the Healer (106452 / 11) | 0 / (280.85,187.42,169.65,232.06) | (274.58,178.00,183.57,260.02) / 0.991150 | C1 slot 0: 0.989417/0.661648 PASS<br>C2 slot 0: 0.989417/0.661648 PASS |
| Forest (106535 / 12) | 12 / (673.14,722.60,169.65,232.06) | not identified; no rectangle/confidence recorded | C1 slot 12: 0.000000/-0.481162 score 0.000 below 0.94; margin -0.481 below 0.07<br>C2 slot 12: 0.000000/-0.481162 score 0.000 below 0.94; margin -0.481 below 0.07 |

Classification: Forest pixel match failed; candidate C2 also swaps Coursework/Foreseer; geometry agrees on matched subset.

### S5: P1P2, 1600x900

Frame 2026-10-12T20:47:08.5141737+00:00; admitted 2026-10-12T20:47:09.5122534+00:00; capture generation 10, request 5, pack generation 3. All current-generation checks passed; manual-history marker False.

Candidate orders before verification:
- C1: `106452,106456,106287,106488,106409,106231,106257,106265,106294,106296,106306,106335,106535`.
- C2: `106452,106456,106287,106488,106409,106231,106265,106257,106294,106296,106306,106335,106535`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Solve for Disappointment (106296 / 0) | 9 / (887.86,379.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 9: 0.130627/-0.255093 score 0.131 below 0.94; margin -0.255 below 0.07<br>C2 slot 9: 0.130627/-0.255093 score 0.131 below 0.94; margin -0.255 below 0.07 |
| Compel Brutality (106335 / 1) | 11 / (397.50,602.17,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 11: 0.000000/-0.344700 score 0.000 below 0.94; margin -0.345 below 0.07<br>C2 slot 11: 0.000000/-0.344700 score 0.000 below 0.94; margin -0.345 below 0.07 |
| Infinite Coursework (106257 / 2) | 6 / (397.50,379.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 6: 0.000000/0.000000 score 0.000 below 0.94; margin 0.000 below 0.07<br>C2 slot 7: 0.000000/-0.245351 score 0.000 below 0.94; margin -0.245 below 0.07 |
| Fateshaper Aspirant (106231 / 3) | 5 / (234.04,379.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 5: 0.000000/-0.416584 score 0.000 below 0.94; margin -0.417 below 0.07<br>C2 slot 5: 0.000000/-0.416584 score 0.000 below 0.94; margin -0.417 below 0.07 |
| Chandra's Emberling (106306 / 4) | 10 / (234.04,602.17,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 10: 0.000000/-0.416584 score 0.000 below 0.94; margin -0.417 below 0.07<br>C2 slot 10: 0.000000/-0.416584 score 0.000 below 0.94; margin -0.417 below 0.07 |
| Semester Foreseer // Peer Review (106265 / 5) | 7 / (560.95,379.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 7: 0.096642/-0.148709 score 0.097 below 0.94; margin -0.149 below 0.07<br>C2 slot 6: 0.000000/-0.214609 score 0.000 below 0.94; margin -0.215 below 0.07 |
| Screeching Soulbreaker (106294 / 6) | 8 / (724.41,379.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 8: 0.000000/-0.308205 score 0.000 below 0.94; margin -0.308 below 0.07<br>C2 slot 8: 0.000000/-0.308205 score 0.000 below 0.94; margin -0.308 below 0.07 |
| Woodwork Prodigy // Soul Tether (106409 / 7) | 4 / (887.86,156.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 4: 0.000000/-0.147972 score 0.000 below 0.94; margin -0.148 below 0.07<br>C2 slot 4: 0.000000/-0.147972 score 0.000 below 0.94; margin -0.148 below 0.07 |
| Multiply by Zero (106287 / 8) | 2 / (560.95,156.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 2: 0.000000/-0.199398 score 0.000 below 0.94; margin -0.199 below 0.07<br>C2 slot 2: 0.000000/-0.199398 score 0.000 below 0.94; margin -0.199 below 0.07 |
| Arni, Humble Scribe (106456 / 9) | 1 / (397.50,156.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 1: 0.000000/-0.193089 score 0.000 below 0.94; margin -0.193 below 0.07<br>C2 slot 1: 0.000000/-0.193089 score 0.000 below 0.94; margin -0.193 below 0.07 |
| Arni, Renowned Champion (106488 / 10) | 3 / (724.41,156.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 3: 0.236676/-0.176843 score 0.237 below 0.94; margin -0.177 below 0.07<br>C2 slot 3: 0.236676/-0.176843 score 0.237 below 0.94; margin -0.177 below 0.07 |
| Way of the Healer (106452 / 11) | 0 / (234.04,156.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 0: 0.000000/-0.385721 score 0.000 below 0.94; margin -0.386 below 0.07<br>C2 slot 0: 0.000000/-0.385721 score 0.000 below 0.94; margin -0.386 below 0.07 |
| Forest (106535 / 12) | 12 / (560.95,602.17,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 12: 0.356497/-0.057022 score 0.356 below 0.94; margin -0.057 below 0.07<br>C2 slot 12: 0.356497/-0.057022 score 0.356 below 0.94; margin -0.057 below 0.07 |

Classification: broad pixel/scene failure, underlying cause unresolved.

### S6: P1P2, 1600x900

Frame 2026-10-12T20:47:12.2236985+00:00; admitted 2026-10-12T20:47:13.5935296+00:00; capture generation 10, request 6, pack generation 3. All current-generation checks passed; manual-history marker False.

Candidate orders before verification:
- C1: `106452,106456,106287,106488,106409,106231,106257,106265,106294,106296,106306,106335,106535`.
- C2: `106452,106456,106287,106488,106409,106231,106265,106257,106294,106296,106306,106335,106535`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Solve for Disappointment (106296 / 0) | 9 / (887.86,379.18,141.38,193.38) | (882.85,375.40,150.71,206.32) / 0.958285 | C1 slot 9: 0.979645/0.455425 PASS<br>C2 slot 9: 0.979645/0.455425 PASS |
| Compel Brutality (106335 / 1) | 11 / (397.50,602.17,141.38,193.38) | (391.76,596.64,152.93,210.82) / 0.991819 | C1 slot 11: 0.991026/0.647521 PASS<br>C2 slot 11: 0.991026/0.647521 PASS |
| Infinite Coursework (106257 / 2) | 6 / (397.50,379.18,141.38,193.38) | (392.16,374.86,152.89,209.40) / 0.986650 | C1 slot 6: 0.986822/0.612493 PASS<br>C2 slot 7: 0.256427/-0.733755 score 0.256 below 0.94; margin -0.734 below 0.07 |
| Fateshaper Aspirant (106231 / 3) | 5 / (234.04,379.18,141.38,193.38) | (228.05,374.66,153.66,210.32) / 0.988350 | C1 slot 5: 0.988748/0.620933 PASS<br>C2 slot 5: 0.988748/0.620933 PASS |
| Chandra's Emberling (106306 / 4) | 10 / (234.04,602.17,141.38,193.38) | (228.38,599.47,152.93,205.95) / 0.988324 | C1 slot 10: 0.990128/0.442664 PASS<br>C2 slot 10: 0.990128/0.442664 PASS |
| Semester Foreseer // Peer Review (106265 / 5) | 7 / (560.95,379.18,141.38,193.38) | (554.74,375.02,153.69,206.95) / 0.986304 | C1 slot 7: 0.990182/0.622368 PASS<br>C2 slot 6: 0.298810/-0.691373 score 0.299 below 0.94; margin -0.691 below 0.07 |
| Screeching Soulbreaker (106294 / 6) | 8 / (724.41,379.18,141.38,193.38) | (719.48,374.95,150.65,208.88) / 0.985149 | C1 slot 8: 0.992551/0.577578 PASS<br>C2 slot 8: 0.992551/0.577578 PASS |
| Woodwork Prodigy // Soul Tether (106409 / 7) | 4 / (887.86,156.18,141.38,193.38) | (884.70,152.12,147.77,208.10) / 0.976905 | C1 slot 4: 0.985163/0.675135 PASS<br>C2 slot 4: 0.985163/0.675135 PASS |
| Multiply by Zero (106287 / 8) | 2 / (560.95,156.18,141.38,193.38) | (556.50,152.74,149.92,203.61) / 0.977914 | C1 slot 2: 0.990806/0.616477 PASS<br>C2 slot 2: 0.990806/0.616477 PASS |
| Arni, Humble Scribe (106456 / 9) | 1 / (397.50,156.18,141.38,193.38) | (391.78,152.09,152.89,207.14) / 0.978738 | C1 slot 1: 0.980186/0.691417 PASS<br>C2 slot 1: 0.980186/0.691417 PASS |
| Arni, Renowned Champion (106488 / 10) | 3 / (724.41,156.18,141.38,193.38) | (717.92,150.18,154.08,212.42) / 0.989951 | C1 slot 3: 0.989274/0.441809 PASS<br>C2 slot 3: 0.989274/0.441809 PASS |
| Way of the Healer (106452 / 11) | 0 / (234.04,156.18,141.38,193.38) | (228.37,148.96,152.89,214.49) / 0.990038 | C1 slot 0: 0.984733/0.676308 PASS<br>C2 slot 0: 0.984733/0.676308 PASS |
| Forest (106535 / 12) | 12 / (560.95,602.17,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 12: 0.000000/-0.482111 score 0.000 below 0.94; margin -0.482 below 0.07<br>C2 slot 12: 0.000000/-0.482111 score 0.000 below 0.94; margin -0.482 below 0.07 |

Classification: Forest pixel match failed; candidate C2 also swaps Coursework/Foreseer; geometry agrees on matched subset.

### S7: P1P2, 1600x900

Frame 2026-10-12T20:47:16.2589076+00:00; admitted 2026-10-12T20:47:17.6541361+00:00; capture generation 10, request 7, pack generation 3. All current-generation checks passed; manual-history marker False.

Candidate orders before verification:
- C1: `106452,106456,106287,106488,106409,106231,106257,106265,106294,106296,106306,106335,106535`.
- C2: `106452,106456,106287,106488,106409,106231,106265,106257,106294,106296,106306,106335,106535`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Solve for Disappointment (106296 / 0) | 9 / (887.86,379.18,141.38,193.38) | (882.85,375.40,150.71,206.32) / 0.958285 | C1 slot 9: 0.979645/0.455425 PASS<br>C2 slot 9: 0.979645/0.455425 PASS |
| Compel Brutality (106335 / 1) | 11 / (397.50,602.17,141.38,193.38) | (391.76,596.64,152.93,210.82) / 0.991819 | C1 slot 11: 0.991026/0.647521 PASS<br>C2 slot 11: 0.991026/0.647521 PASS |
| Infinite Coursework (106257 / 2) | 6 / (397.50,379.18,141.38,193.38) | (392.16,374.86,152.89,209.40) / 0.986650 | C1 slot 6: 0.986822/0.612493 PASS<br>C2 slot 7: 0.256427/-0.733755 score 0.256 below 0.94; margin -0.734 below 0.07 |
| Fateshaper Aspirant (106231 / 3) | 5 / (234.04,379.18,141.38,193.38) | (228.05,374.66,153.66,210.32) / 0.988350 | C1 slot 5: 0.988748/0.620933 PASS<br>C2 slot 5: 0.988748/0.620933 PASS |
| Chandra's Emberling (106306 / 4) | 10 / (234.04,602.17,141.38,193.38) | (228.38,599.47,152.93,205.95) / 0.988324 | C1 slot 10: 0.990128/0.442664 PASS<br>C2 slot 10: 0.990128/0.442664 PASS |
| Semester Foreseer // Peer Review (106265 / 5) | 7 / (560.95,379.18,141.38,193.38) | (554.74,375.02,153.69,206.95) / 0.986304 | C1 slot 7: 0.990182/0.622368 PASS<br>C2 slot 6: 0.298810/-0.691373 score 0.299 below 0.94; margin -0.691 below 0.07 |
| Screeching Soulbreaker (106294 / 6) | 8 / (724.41,379.18,141.38,193.38) | (719.48,374.95,150.65,208.88) / 0.985149 | C1 slot 8: 0.992551/0.577578 PASS<br>C2 slot 8: 0.992551/0.577578 PASS |
| Woodwork Prodigy // Soul Tether (106409 / 7) | 4 / (887.86,156.18,141.38,193.38) | (884.70,152.12,147.77,208.10) / 0.976905 | C1 slot 4: 0.985163/0.675135 PASS<br>C2 slot 4: 0.985163/0.675135 PASS |
| Multiply by Zero (106287 / 8) | 2 / (560.95,156.18,141.38,193.38) | (556.50,152.74,149.92,203.61) / 0.977914 | C1 slot 2: 0.990806/0.616477 PASS<br>C2 slot 2: 0.990806/0.616477 PASS |
| Arni, Humble Scribe (106456 / 9) | 1 / (397.50,156.18,141.38,193.38) | (391.78,152.09,152.89,207.14) / 0.978738 | C1 slot 1: 0.980186/0.691417 PASS<br>C2 slot 1: 0.980186/0.691417 PASS |
| Arni, Renowned Champion (106488 / 10) | 3 / (724.41,156.18,141.38,193.38) | (717.92,150.18,154.08,212.42) / 0.989951 | C1 slot 3: 0.989274/0.441809 PASS<br>C2 slot 3: 0.989274/0.441809 PASS |
| Way of the Healer (106452 / 11) | 0 / (234.04,156.18,141.38,193.38) | (228.37,148.96,152.89,214.49) / 0.990038 | C1 slot 0: 0.984733/0.676308 PASS<br>C2 slot 0: 0.984733/0.676308 PASS |
| Forest (106535 / 12) | 12 / (560.95,602.17,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 12: 0.000000/-0.482111 score 0.000 below 0.94; margin -0.482 below 0.07<br>C2 slot 12: 0.000000/-0.482111 score 0.000 below 0.94; margin -0.482 below 0.07 |

Classification: Forest pixel match failed; candidate C2 also swaps Coursework/Foreseer; geometry agrees on matched subset.

### S8: P2P1, 1600x900

Frame 2026-10-12T20:52:15.164756+00:00; admitted 2026-10-12T20:52:19.0810441+00:00; capture generation 62, request 27, pack generation 29. All current-generation checks passed; manual-history marker False.

Candidate orders before verification:
- C1: `106310,106455,106471,106328,106343,106513,106237,106241,106259,106329,106335,106407,106419,106422`.
- C2: `106310,106455,106471,106328,106343,106513,106241,106237,106259,106329,106335,106407,106419,106422`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Wrath of the Bloodmane (106329 / 0) | 9 / (887.86,379.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 9: 0.988165/0.440376 PASS<br>C2 slot 9: 0.988165/0.440376 PASS |
| Murmuring Volume (106419 / 1) | 12 / (560.95,602.17,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 12: 0.980933/0.430984 PASS<br>C2 slot 12: 0.980933/0.430984 PASS |
| Compel Brutality (106335 / 2) | 10 / (234.04,602.17,141.38,193.38) | (227.61,596.78,154.47,210.31) / 0.992540 | C1 slot 10: 0.991976/0.532899 PASS<br>C2 slot 10: 0.991976/0.532899 PASS |
| Predictive Preparations (106241 / 3) | 7 / (560.95,379.18,141.38,193.38) | (555.14,372.26,152.89,213.68) / 0.986293 | C1 slot 7: 0.989345/0.524978 PASS<br>C2 slot 6: 0.000000/-0.989345 score 0.000 below 0.94; margin -0.989 below 0.07 |
| Whiplash Wordsmith // Vicious Verse (106407 / 4) | 11 / (397.50,602.17,141.38,193.38) | (392.92,595.81,151.38,214.53) / 0.986295 | C1 slot 11: 0.979441/0.520259 PASS<br>C2 slot 11: 0.979441/0.520259 PASS |
| Mindseeker Oculus (106259 / 5) | 8 / (724.41,379.18,141.38,193.38) | (718.74,372.26,152.15,213.68) / 0.989756 | C1 slot 8: 0.991578/0.581888 PASS<br>C2 slot 8: 0.991578/0.581888 PASS |
| Hexhaven Battalion (106237 / 6) | 6 / (397.50,379.18,141.38,193.38) | (392.48,376.70,152.01,204.94) / 0.983916 | C1 slot 6: 0.988122/0.438173 PASS<br>C2 slot 7: 0.000000/-0.989345 score 0.000 below 0.94; margin -0.989 below 0.07 |
| Hunter's Axe (106343 / 7) | 4 / (887.86,156.18,141.38,193.38) | (882.87,151.91,150.65,207.94) / 0.989947 | C1 slot 4: 0.991478/0.532402 PASS<br>C2 slot 4: 0.991478/0.532402 PASS |
| Violent Echoes (106328 / 8) | 3 / (724.41,156.18,141.38,193.38) | (719.32,153.99,150.74,202.73) / 0.982271 | C1 slot 3: 0.991598/0.443809 PASS<br>C2 slot 3: 0.991598/0.443809 PASS |
| Draconic Visitor (106310 / 9) | 0 / (234.04,156.18,141.38,193.38) | (228.00,151.86,153.69,209.27) / 0.987764 | C1 slot 0: 0.985293/0.541430 PASS<br>C2 slot 0: 0.985293/0.541430 PASS |
| Yuriko, Hope from the Shadows (106471 / 10) | 2 / (560.95,156.18,141.38,193.38) | (555.13,150.04,152.93,211.37) / 0.985087 | C1 slot 2: 0.985087/0.584915 PASS<br>C2 slot 2: 0.985087/0.584915 PASS |
| Yuriko, Blade of the Mighty (106455 / 11) | 1 / (397.50,156.18,141.38,193.38) | (392.78,152.08,150.65,208.12) / 0.968726 | C1 slot 1: 0.975385/0.690388 PASS<br>C2 slot 1: 0.975385/0.690388 PASS |
| Way of the Wildspeaker (106513 / 12) | 5 / (234.04,379.18,141.38,193.38) | (227.42,374.47,154.85,210.84) / 0.994942 | C1 slot 5: 0.987819/0.523452 PASS<br>C2 slot 5: 0.987819/0.523452 PASS |
| Fatehold Annex (106422 / 13) | 13 / (724.41,602.17,141.38,193.38) | (718.12,595.70,153.70,215.58) / 0.994448 | C1 slot 13: 0.992733/0.596870 PASS<br>C2 slot 13: 0.992733/0.596870 PASS |

Classification: comparator still discriminating; C1 strictly verifies all 14, WGC omits two nonbasic occurrences.

### S9: P2P1, 1600x900

Frame 2026-10-12T20:52:21.2267668+00:00; admitted 2026-10-12T20:52:22.7678478+00:00; capture generation 62, request 28, pack generation 29. All current-generation checks passed; manual-history marker False.

Candidate orders before verification:
- C1: `106310,106455,106471,106328,106343,106513,106237,106241,106259,106329,106335,106407,106419,106422`.
- C2: `106310,106455,106471,106328,106343,106513,106241,106237,106259,106329,106335,106407,106419,106422`.

| Expected identity (Arena ID / pack index) | C1 slot / predicted holder | WGC rectangle / confidence | Fast per candidate |
|---|---|---|---|
| Wrath of the Bloodmane (106329 / 0) | 9 / (887.86,379.18,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 9: 0.988165/0.440376 PASS<br>C2 slot 9: 0.988165/0.440376 PASS |
| Murmuring Volume (106419 / 1) | 12 / (560.95,602.17,141.38,193.38) | not identified; no rectangle/confidence recorded | C1 slot 12: 0.980933/0.430984 PASS<br>C2 slot 12: 0.980933/0.430984 PASS |
| Compel Brutality (106335 / 2) | 10 / (234.04,602.17,141.38,193.38) | (227.61,596.78,154.47,210.31) / 0.992540 | C1 slot 10: 0.991976/0.532899 PASS<br>C2 slot 10: 0.991976/0.532899 PASS |
| Predictive Preparations (106241 / 3) | 7 / (560.95,379.18,141.38,193.38) | (555.14,372.26,152.89,213.68) / 0.986293 | C1 slot 7: 0.989345/0.524978 PASS<br>C2 slot 6: 0.000000/-0.989345 score 0.000 below 0.94; margin -0.989 below 0.07 |
| Whiplash Wordsmith // Vicious Verse (106407 / 4) | 11 / (397.50,602.17,141.38,193.38) | (392.92,595.81,151.38,214.53) / 0.986295 | C1 slot 11: 0.979441/0.520259 PASS<br>C2 slot 11: 0.979441/0.520259 PASS |
| Mindseeker Oculus (106259 / 5) | 8 / (724.41,379.18,141.38,193.38) | (718.74,372.26,152.15,213.68) / 0.989756 | C1 slot 8: 0.991578/0.581888 PASS<br>C2 slot 8: 0.991578/0.581888 PASS |
| Hexhaven Battalion (106237 / 6) | 6 / (397.50,379.18,141.38,193.38) | (392.48,376.70,152.01,204.94) / 0.983916 | C1 slot 6: 0.988122/0.438173 PASS<br>C2 slot 7: 0.000000/-0.989345 score 0.000 below 0.94; margin -0.989 below 0.07 |
| Hunter's Axe (106343 / 7) | 4 / (887.86,156.18,141.38,193.38) | (882.87,151.91,150.65,207.94) / 0.989947 | C1 slot 4: 0.991478/0.532402 PASS<br>C2 slot 4: 0.991478/0.532402 PASS |
| Violent Echoes (106328 / 8) | 3 / (724.41,156.18,141.38,193.38) | (719.32,153.99,150.74,202.73) / 0.982271 | C1 slot 3: 0.991598/0.443809 PASS<br>C2 slot 3: 0.991598/0.443809 PASS |
| Draconic Visitor (106310 / 9) | 0 / (234.04,156.18,141.38,193.38) | (228.00,151.86,153.69,209.27) / 0.987764 | C1 slot 0: 0.985293/0.541430 PASS<br>C2 slot 0: 0.985293/0.541430 PASS |
| Yuriko, Hope from the Shadows (106471 / 10) | 2 / (560.95,156.18,141.38,193.38) | (555.13,150.04,152.93,211.37) / 0.985087 | C1 slot 2: 0.985087/0.584915 PASS<br>C2 slot 2: 0.985087/0.584915 PASS |
| Yuriko, Blade of the Mighty (106455 / 11) | 1 / (397.50,156.18,141.38,193.38) | (392.78,152.08,150.65,208.12) / 0.968726 | C1 slot 1: 0.975385/0.690388 PASS<br>C2 slot 1: 0.975385/0.690388 PASS |
| Way of the Wildspeaker (106513 / 12) | 5 / (234.04,379.18,141.38,193.38) | (227.42,374.47,154.85,210.84) / 0.994942 | C1 slot 5: 0.987819/0.523452 PASS<br>C2 slot 5: 0.987819/0.523452 PASS |
| Fatehold Annex (106422 / 13) | 13 / (724.41,602.17,141.38,193.38) | (718.12,595.70,153.70,215.58) / 0.994448 | C1 slot 13: 0.992733/0.596870 PASS<br>C2 slot 13: 0.992733/0.596870 PASS |

Classification: comparator still discriminating; C1 strictly verifies all 14, WGC omits two nonbasic occurrences.

## 2. Missing production badge/root-cause analysis

**Established first observed failure:** WGC match output omits offered occurrences. Unsupported deterministic count is not a reason to suppress a production badge. Across 28 attempts, only three full WGC results exist (P1P1, the second P1P6 attempt, and P1P8); 25 are partial or empty. The nonbasic omissions below are real matcher omissions, not assumed missing statistics. Some retries regress from more matches to fewer. No first-run UI-admission/render snapshots were recorded, so a matcher omission is a potential missing badge at stage 2, not proof of every physical box the user saw.

| Pack/pick (chronological attempts) | WGC matched counts | Missing identities by attempt |
|---|---|---|
| P1P1 | 14 | A1: none |
| P1P2 | 0 ? 12 ? 12 ? 0 ? 12 ? 12 | A1: Solve for Disappointment (106296); Compel Brutality (106335); Infinite Coursework (106257); Fateshaper Aspirant (106231); Chandra's Emberling (106306); Semester Foreseer // Peer Review (106265); Screeching Soulbreaker (106294); Woodwork Prodigy // Soul Tether (106409); Multiply by Zero (106287); Arni, Humble Scribe (106456); Arni, Renowned Champion (106488); Way of the Healer (106452); Forest (106535)<br>A2: Forest (106535)<br>A3: Forest (106535)<br>A4: Solve for Disappointment (106296); Compel Brutality (106335); Infinite Coursework (106257); Fateshaper Aspirant (106231); Chandra's Emberling (106306); Semester Foreseer // Peer Review (106265); Screeching Soulbreaker (106294); Woodwork Prodigy // Soul Tether (106409); Multiply by Zero (106287); Arni, Humble Scribe (106456); Arni, Renowned Champion (106488); Way of the Healer (106452); Forest (106535)<br>A5: Forest (106535)<br>A6: Forest (106535) |
| P1P3 | 11 ? 0 ? 8 | A1: Island (106529)<br>A2: Greenhouse Propagator (106338); Vinelasher Adept (106354); Sphinx's Approach (106268); Graft Surgeon (106235); Heartstring Puller (106317); Rampart Hunter (106289); Surgical Precision (106247); Vigorbloom Vanguard // Seed Suture (106403); Pia, Aether Ascetic (106509); Pia, Determined Rebuilder (106495); Way of the Deathbringer (106483); Island (106529)<br>A3: Graft Surgeon (106235); Vigorbloom Vanguard // Seed Suture (106403); Pia, Aether Ascetic (106509); Island (106529) |
| P1P4 | 10 | A1: Mountain (106532) |
| P1P5 | 9 ? 9 ? 6 | A1: Swamp (106530)<br>A2: Swamp (106530)<br>A3: Graft Surgeon (106235); Greenhouse Propagator (106338); Fateshaper Aspirant (106231); Swamp (106530) |
| P1P6 | 0 ? 9 | A1: Keeper of the Quiet Hour (106416); Theorix Metamage // Omit Variables (106397); Charge the Sanctum (106360); Budding Insurgent (106332); Chandra's Emberling (106306); Academic Ascent (106226); Hexhaven Dueling Arena (106426); Tomik, Orzhov Lawmage (106451); Vigorbloom Annex (106439)<br>A2: none |
| P1P7 | 7 ? 4 | A1: Island (106528)<br>A2: Fateshaper Aspirant (106231); Greenhouse Propagator (106338); Graft Surgeon (106235); Island (106528) |
| P1P8 | 7 | A1: none |
| P1P9 | 0 ? 5 ? 5 | A1: Fateshaper Aspirant (106231); Ferocity of the Hunt (106371); Chandra's Emberling (106306); Prudent Fateseer // Peer Review (106385); Yoshimaru, Scrappy Stray (106514); Innovative Commons (106427)<br>A2: Prudent Fateseer // Peer Review (106385)<br>A3: Prudent Fateseer // Peer Review (106385) |
| P1P10 | 4 ? 0 | A1: Forest (106535)<br>A2: Infinite Coursework (106257); Semester Foreseer // Peer Review (106265); Screeching Soulbreaker (106294); Arni, Renowned Champion (106488); Forest (106535) |
| P1P11 | 2 | A1: Vinelasher Adept (106354); Island (106529) |
| P1P12 | 2 | A1: Mountain (106532) |
| P2P1 | 12 ? 12 | A1: Wrath of the Bloodmane (106329); Murmuring Volume (106419)<br>A2: Wrath of the Bloodmane (106329); Murmuring Volume (106419) |

The observed P1P13/P1P14 acceptance events have no recorded matched-frame result. No per-card claim about those packs is made. P1P8 has all seven WGC matches, including Unsummon and Arcane Amphisbaena; its recommendation problem is separate from localization.

| Stage | Evidence and conclusion | Historical limit |
|---|---|---|
| 1. Offered Player.log identity | Production parser recovers the current packs; their offered multisets match the runtime ledger. Every omitted identity listed above is present. | No user screenshot identifies which omitted occurrence corresponded to each physically missing box. |
| 2. Full WGC identity/rectangle | Recorded per-occurrence WGC output proves exactly which occurrences were not identified. P2P1 proves two nonbasic cards pass strict known-slot pixels but are omitted by full WGC. | Original rejected candidate scores/proposal choices were not recorded; they cannot be reverse-inferred from an omitted match. |
| 3. Catalog identity | The production Arena resolver and local DB bridge resolve all recovered packs. The complete adapter snapshots would be unavailable otherwise; replay metadata names each identity. | Catalog/cache provenance is preserved in the fixture; no new external card lookup was used. |
| 4. Contextual scoring | Offline production pipeline produces results for all recovered offered occurrences, including estimated-quality cards. Exact P1P8 scores match both displayed examples. | The actual first-run timing of loading/loaded/pair-stat updates was not recorded. Offline reproduction cannot prove every historical UI score update was applied. |
| 5. Badge model | `OverlayViewModel.RefreshPack` creates one model per presentation (up to the existing 14-card UI cap), without filtering basic lands or unavailable GIH/score. The diagnostic test shows a matched card with no score still has a placed badge. | No historical model-creation snapshot exists; this is code-path evidence, not a reconstruction of actual Avalonia objects. |
| 6. Placement acceptance | `PlaceAll` only places an automatically matched occurrence and clears an unmatched occurrence. `ApplyAutomaticLocalization` replaces the previous same-pack result, so a weaker/empty retry can retire earlier good badges if admitted. | The first ledger observes recognition before UI application, so the actual accepted/rejected outcome was not recorded. |
| 7. Render visibility | XAML binds badge-root visibility to `IsPlaced`; absence of statistics does not hide the rating box. Window/ancestor visibility and clipping can independently hide a matched badge. | No historical visual tree, compositor frame or capture proves which matched badges actually rendered. |

**Root cause established:** missing full-matcher occurrences block automatic badge placement at the recognition/placement boundary. This is demonstrated for specific nonbasic cards, most strongly P2P1 Wrath of the Bloodmane and Murmuring Volume, and is independent of the deterministic <=12 decline. The exact search/registration reason for these omissions remains unresolved. A second established production policy permits weaker same-pack retries to erase previously recognized badges; the log sequence P1P3 `11?0?8` and P1P7 `7?4` exposes this risk. Historical UI acceptance is needed before asserting which user-visible loss was caused by that policy.

**Diagnostics implemented:** `BadgeScoringSnapshot`, `ProductionBadgePipeline`, `ProductionPlacementAdmission`, and `BadgeRenderSnapshot` in the existing opt-in ledger. Per expected occurrence they distinguish catalog/identity consistency, score presence and full component object, actual badge model, matcher identification, placement, canvas bounds, visual-root creation, ancestor visibility, measured bounds and intersection with the overlay surface. Render snapshots are delayed until the UI background queue after application; they are layout/visibility evidence, not a claim to observe GPU compositor pixels or external occlusion. They do not change visibility or placement.

**Unresolved:** the identities/times of every user-observed missing box; WGC rejected scores and seed bounds on the old frames; exact basic-land art; whether every partial retry was admitted; clipping/visibility of matched badges; animations/hover state. No app log or retained frame supplies these missing facts. Do not blame scoring, statistics, or deterministic geometry by elimination without these observations.

**Smallest recommended fix:** use the added full-matcher audit to isolate P2P1 seed/proposal/assignment rejection and repair that WGC path with exact-frame offline regression tests. Separately evaluate retaining already verified same-pack/same-geometry occurrences across an inferior retry, with pick/resize/DPI invalidation still mandatory; do not blindly merge across geometry generations or retain obsolete cards. The current patch intentionally implements neither behavior change. Enable the existing `DRAFTTG_LOCALIZATION_DEBUG_DIRECTORY` only when reproducing an existing failing pack to retain pixels locally; offline frames still cannot count as live gate evidence. No additional draft is requested.

**Tests needed:** an exact-frame WGC recall regression for the two P2P1 cards; same-geometry retry regression versus mandatory retirement on accepted pick/resize/DPI; render visibility/clipping integration test. Added stage-audit test confirms a missing match versus absent score can be distinguished and capture leaves badge positions unchanged. Existing movement, retirement and stale-generation tests remain passing.

## 3. P1P8 RG-vs-Unsummon contextual scoring reproduction

**Exact real state:** P1P8, seven offered cards, seven recovered pool cards, complete pool. The canonical green card name is **Arcane Amphisbaena**. The replay uses FRA **QuickDraft**, not the older PremierDraft cache. The QuickDraft cache timestamp precedes the session?s first pack; its exact raw Unsummon 58.395415% GIH/ALSA 5.613781 and green 57.150625%/3.7472598 match the user?s display transcription.

Recovered chronological picks before P1P8:
- P1P1: Aerid Konstrari.
- P1P2: Woodwork Prodigy // Soul Tether.
- P1P3: Pia, Determined Rebuilder.
- P1P4: No Admittance.
- P1P5: Greenhouse Propagator.
- P1P6: Keeper of the Quiet Hour.
- P1P7: Konstrari Charm.

Environment baseline: `0.5704692518601339`. Draft progress: 7/42. Stage weights: Q 0.6835131284347649, L 0.2, A 0.10412171789130878, D 0.01236515367392632. Calibration/tier versions remain `contextual-pick-score-v1.6` / `pick-score-tiers-v2`.

| Offered identity | Quality evidence | Raw GIH / sample / ALSA | Q | R | Q_eff | L | A | D | Playable likelihood | Exact score ?50 | Display | Rank |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Blessed Ghoul (106358, bb975803-9bf2-401e-9414-d272df314398) | Direct card statistics | 0.566 / 2415 / 6.268 | 0.474950164682 | 1.000000000000 | 0.474950164682 | 0.489310654874 | 0.500000000000 | 0.100000000000 | 0.000000000000 | 23.305592474027 | 23 | 4 |
| Cryotheory Adept (106252, 9ba1f7ce-3404-4932-9795-22967707f762) | Direct card statistics | 0.559 / 2531 / 7.606 | 0.442556738704 | 1.000000000000 | 0.442556738704 | 0.520588769106 | 0.500000000000 | 0.100000000000 | 0.000000000000 | 22.193592838112 | 22 | 5 |
| Arcane Amphisbaena (106330, 1bf923c4-f0b7-4271-978c-fd2e79fe1cc8) | Direct card statistics | 0.572 / 3678 / 3.747 | 0.505705599507 | 1.000000000000 | 0.505705599507 | 0.501192599526 | 0.500000000000 | 0.744275080813 | 1.000000000000 | 25.501120969070 | 26 | 2 |
| Hallway Heckler // Vicious Verse (106315, 7e324816-552f-455d-97c4-5ea6b26d2e6e) | Direct card statistics | 0.535 / 1487 / 7.748 | 0.335531673421 | 1.000000000000 | 0.335531673421 | 0.492466606614 | 0.500000000000 | 0.509427220477 | 1.000000000000 | 17.033554094693 | 17 | 6 |
| Unsummon (106273, ddad9f16-52d5-49de-82b0-b1a5294a9c44) | Direct card statistics | 0.584 / 6980 / 5.614 | 0.578646882534 | 1.000000000000 | 0.578646882534 | 0.520588769106 | 0.500000000000 | 0.100000000000 | 0.000000000000 | 28.704950835170 | 29 | 1 |
| Way of the Necromancer (106484, a0ff9689-ea49-4fff-b37c-4abbaeb0f73d) | Estimated: unusable GIH | n/a / 225 / 7.796 | 0.500000000000 | 1.000000000000 | 0.500000000000 | 0.496441369880 | 0.500000000000 | 0.100000000000 | 0.000000000000 | 24.603954875454 | 25 | 3 |
| Dedicated Commons (106420, 3223e5db-5cc4-42f9-ae9e-ff58abc7c390) | Direct card statistics | 0.514 / 669 / 4.881 | 0.298738618507 | 1.000000000000 | 0.298738618507 | 0.500000000000 | 0.500000000000 | 0.100000000000 | 1.000000000000 | 15.024239942319 | 15 | 7 |

**Why 29 versus 26:** `25 + 3.762932370555 (quality) + 0.288242767485 (lane) + 0 (archetype) ? 0.346224302870 (deck need) = 28.704950835170`, displayed 29, rank 1 for Unsummon. Green: `25 + 0.272989651803 + 0.016696393360 + 0 + 0.211434923907 = 25.501120969070`, displayed 26, rank 2. Neither ties nor integer rounding reverses their order; the exact difference is 3.203829866100 points. Their soft-limit contributions are zero.

**Root cause established:** the evaluator correctly projects RG and gives Unsummon playable likelihood 0, RG color fit -0.15, no maindeck membership and deck need 0.1. The green card is RG-playable at likelihood 1 and deck need 0.744275080813. But `QualityRelevanceStartPicks = 10` forces R=1 at seven picks regardless of playability; Q_eff therefore equals Q even for this unplayable blue card. D has only 1.2365% stage weight, so its correct off-color penalty contributes only -0.346 points. Stronger direct GIH quality contributes +3.763 points and a modest blue late-card lane signal +0.288 points. Final composition barely uses the correctly detected playability/color commitment at this stage.

This is a quality-relevance/stage-composition defect relative to the requested contextual behavior, not a failure to recover the pool, project RG, identify an off-color card, compute deck fit, or resolve the statistics printing. The lane component is deliberately a separate openness signal: Unsummon?s late arrival contributes blue open evidence about 0.250225; blue support is about 0.082355 and L about 0.520589. It is not evidence that the current deck has pivoted to blue. Archetype A is neutral (0.5) for all seven cards. Color fit is exposed but is not a separate weighted final-score component. The sparse RG shell is correctly incomplete; this is not an accidental finished-deck projection.

**Full projected pair/deck, cut-line and explanation breakdown:** each candidate has one viable projected pair, RG, blend weight 1. The base seven-spell shell consists of the recovered pool and has average quality 0.5673296310068339. Green is added as an eighth spell, average quality 0.5678361956036361. Off-color cards cannot enter it; Hallway Heckler adds the eighth spell and Dedicated Commons enters the mana base. These remain incomplete shells, not finished decks.

### Blessed Ghoul

Evidence identity: bb975803-9bf2-401e-9414-d272df314398; quality source 1 / availability 0; adjusted Phase-8 value 0.5664612782092854; color fit -0.15; exact score 23.305592474026962; display 23; final rank 4.

Contributions from neutral 25: Quality -1.198532391339 (weight 0.683513128434765); Lane -0.149650831764 (weight 0.200000000000000); Archetype +0.000000000000 (weight 0.104121717891309); Deck Need -0.346224302870 (weight 0.012365153673926); Soft limit +0.000000000000 (weight 0.000000000000000).

Deck impact: playability 0, membership 0, D 0.1, margin None, real cut line None, replacement/future line 0.5474692518601338, compares-with-future False, projected quality delta 0, structure gate 0. Open/contested slots 16/16; 34 remaining picks, expected supply 27.200000000000003, coverage 1.7000000000000002, supply urgency 0.43333333333333324. Before/after pair RG/RG; projected structure {'Spells': 7, 'Creatures': 5, 'EarlyPlays': 2, 'HighCost': 0, 'Removal': 1.5}; structure confidence 0; creature/early/removal/top-end need 0/0/0/0.

Projected RG: quality 0.5673296310068339, 7 spells, shortfall 0, blend weight 1, eligibility 0, candidate in selected deck False; cut-line margin None. Selected deck: 1? Greenhouse Propagator; 1? Aerid Konstrari; 1? Pia, Determined Rebuilder; 1? No Admittance; 1? Keeper of the Quiet Hour; 1? Woodwork Prodigy // Soul Tether; 1? Konstrari Charm.

Explanation text (existing score diagnostics):

```text
Pick Score: 23 / 50; Model: contextual-pick-score-v1.6; Measured
Exact score 23.31; tier marginal (pick-score-tiers-v2)
Pick-score ordinal rank: 4; progress 0.167; color fit -0.15; Phase 8 data weight 0.828
Quality raw 0.475; relevance R 1 (playability 0); effective 0.475
Quality 0.475; Lane 0.489; Archetype 0.5; Deck Need 0.1
Weights Q 68 %, L 20 %, A 10 %, D 1 %
Contributions from neutral 25 (before clamp/rounding):
Quality: -1.20 points
Lane: -0.15 points
Archetype: 0.00 points
Deck Need: -0.35 points
Soft limit: 0.00 points
Deck: available True; included False; replacement none; quality delta 0.0000; creatures 0; early 0; high 0; mana 0; pair RG -> RG; pair changed False; relaxation changed False; incomplete shell True
Deck fit: cut-line margin n/a; graded membership 0; real cut line ; virtual cut line 0.5475; best-pair weight 1 of 1 viable pair(s)
Future supply: open slots 16; contested slots 16; remaining picks 34; expected playables 27.2; coverage 1.7; urgency 0.43; structure gate 0; structural need 0 at confidence 0; D 0.1
Supply profile: FRA (calibrated from FRA PremierDraft red-green card availability)
Evidence:
Intrinsic quality: average · Direct — 17Lands GIH
Playability: realistic for your projected decks
Lane: neutral (no clear signal)
Archetype: neutral (no clear signal)
Deck need: weak (D 0.10)
  - Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).
Roles: Creature, EarlyPlay; Structured metadata plus high-confidence Oracle rules.
Reasons:
Near-average intrinsic GIH performance.
- Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).
· Incomplete projected shell, not a finished 40-card deck.
```

### Cryotheory Adept

Evidence identity: 9ba1f7ce-3404-4932-9795-22967707f762; quality source 1 / availability 0; adjusted Phase-8 value 0.5612783300528099; color fit -0.15; exact score 22.193592838111602; display 22; final rank 5.

Contributions from neutral 25: Quality -2.748425626504 (weight 0.683513128434765); Lane +0.288242767485 (weight 0.200000000000000); Archetype +0.000000000000 (weight 0.104121717891309); Deck Need -0.346224302870 (weight 0.012365153673926); Soft limit +0.000000000000 (weight 0.000000000000000).

Deck impact: playability 0, membership 0, D 0.1, margin None, real cut line None, replacement/future line 0.5474692518601338, compares-with-future False, projected quality delta 0, structure gate 0. Open/contested slots 16/16; 34 remaining picks, expected supply 27.200000000000003, coverage 1.7000000000000002, supply urgency 0.43333333333333324. Before/after pair RG/RG; projected structure {'Spells': 7, 'Creatures': 5, 'EarlyPlays': 2, 'HighCost': 0, 'Removal': 1.5}; structure confidence 0; creature/early/removal/top-end need 0/0/0/0.

Projected RG: quality 0.5673296310068339, 7 spells, shortfall 0, blend weight 1, eligibility 0, candidate in selected deck False; cut-line margin None. Selected deck: 1? Greenhouse Propagator; 1? Aerid Konstrari; 1? Pia, Determined Rebuilder; 1? No Admittance; 1? Keeper of the Quiet Hour; 1? Woodwork Prodigy // Soul Tether; 1? Konstrari Charm.

Explanation text (existing score diagnostics):

```text
Pick Score: 22 / 50; Model: contextual-pick-score-v1.6; Measured
Exact score 22.19; tier marginal (pick-score-tiers-v2)
Pick-score ordinal rank: 5; progress 0.167; color fit -0.15; Phase 8 data weight 0.835
Quality raw 0.443; relevance R 1 (playability 0); effective 0.443
Quality 0.443; Lane 0.521; Archetype 0.5; Deck Need 0.1
Weights Q 68 %, L 20 %, A 10 %, D 1 %
Contributions from neutral 25 (before clamp/rounding):
Quality: -2.75 points
Lane: +0.29 points
Archetype: 0.00 points
Deck Need: -0.35 points
Soft limit: 0.00 points
Deck: available True; included False; replacement none; quality delta 0.0000; creatures 0; early 0; high 0; mana 0; pair RG -> RG; pair changed False; relaxation changed False; incomplete shell True
Deck fit: cut-line margin n/a; graded membership 0; real cut line ; virtual cut line 0.5475; best-pair weight 1 of 1 viable pair(s)
Future supply: open slots 16; contested slots 16; remaining picks 34; expected playables 27.2; coverage 1.7; urgency 0.43; structure gate 0; structural need 0 at confidence 0; D 0.1
Supply profile: FRA (calibrated from FRA PremierDraft red-green card availability)
Evidence:
Intrinsic quality: average · Direct — 17Lands GIH
Playability: realistic for your projected decks
Lane: neutral (no clear signal)
Archetype: neutral (no clear signal)
Deck need: weak (D 0.10)
  - Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).
Roles: Creature, EarlyPlay; Structured metadata plus high-confidence Oracle rules.
Reasons:
Near-average intrinsic GIH performance.
- Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).
· Incomplete projected shell, not a finished 40-card deck.
```

### Arcane Amphisbaena

Evidence identity: 1bf923c4-f0b7-4271-978c-fd2e79fe1cc8; quality source 1 / availability 0; adjusted Phase-8 value 0.5713821477812511; color fit 0.1625; exact score 25.501120969070218; display 26; final rank 2.

Contributions from neutral 25: Quality +0.272989651803 (weight 0.683513128434765); Lane +0.016696393360 (weight 0.200000000000000); Archetype +0.000000000000 (weight 0.104121717891309); Deck Need +0.211434923907 (weight 0.012365153673926); Soft limit +0.000000000000 (weight 0.000000000000000).

Deck impact: playability 1, membership 0.7865644544667174, D 0.7442750808127223, margin 0.023912895921117228, real cut line None, replacement/future line 0.5474692518601338, compares-with-future True, projected quality delta 0.0005065645968022059, structure gate 0.7677491110219381. Open/contested slots 16/16; 34 remaining picks, expected supply 27.200000000000003, coverage 1.7000000000000002, supply urgency 0.43333333333333324. Before/after pair RG/RG; projected structure {'Spells': 7, 'Creatures': 5, 'EarlyPlays': 2, 'HighCost': 0, 'Removal': 1.5}; structure confidence 0.3888888888888889; creature/early/removal/top-end need 0/0/0/0.

Projected RG: quality 0.5678361956036361, 8 spells, shortfall 0, blend weight 1, eligibility 1, candidate in selected deck True; cut-line margin 0.023912895921117228. Selected deck: 1? Greenhouse Propagator; 1? Aerid Konstrari; 1? Pia, Determined Rebuilder; 1? No Admittance; 1? Keeper of the Quiet Hour; 1? Woodwork Prodigy // Soul Tether; 1? Konstrari Charm; 1? Arcane Amphisbaena.

Explanation text (existing score diagnostics):

```text
Pick Score: 26 / 50; Model: contextual-pick-score-v1.6; Measured
Exact score 25.50; tier solid (pick-score-tiers-v2)
Pick-score ordinal rank: 2; progress 0.167; color fit 0.163; Phase 8 data weight 0.88
Quality raw 0.506; relevance R 1 (playability 1); effective 0.506
Quality 0.506; Lane 0.501; Archetype 0.5; Deck Need 0.744
Weights Q 68 %, L 20 %, A 10 %, D 1 %
Contributions from neutral 25 (before clamp/rounding):
Quality: +0.27 points
Lane: +0.02 points
Archetype: 0.00 points
Deck Need: +0.21 points
Soft limit: 0.00 points
Deck: available True; included True; replacement none; quality delta +0.0005; creatures 1; early 1; high 0; mana 0; pair RG -> RG; pair changed False; relaxation changed True; incomplete shell True
Deck fit: cut-line margin +2.39 pp; graded membership 0.787; real cut line ; virtual cut line 0.5475 (used); best-pair weight 1 of 1 viable pair(s)
Future supply: open slots 16; contested slots 16; remaining picks 34; expected playables 27.2; coverage 1.7; urgency 0.43; structure gate 0.768; structural need 0 at confidence 0.39; D 0.744
Supply profile: FRA (calibrated from FRA PremierDraft red-green card availability)
Evidence:
Intrinsic quality: average · Direct — 17Lands GIH
Playability: realistic for your projected decks
Lane: neutral (no clear signal)
Archetype: neutral (no clear signal)
Deck need: strong (D 0.74)
  + Fills an open slot: 2.4 pp above the expected later filler (54.7 %).
  + Deck still needs 16 playables with only 34 picks remaining.
Roles: Creature, EarlyPlay; Structured metadata plus high-confidence Oracle rules.
Reasons:
Near-average intrinsic GIH performance.
+ Fills an open slot: 2.4 pp above the expected later filler (54.7 %).
+ Deck still needs 16 playables with only 34 picks remaining.
· Incomplete projected shell, not a finished 40-card deck.
```

### Hallway Heckler // Vicious Verse

Evidence identity: 7e324816-552f-455d-97c4-5ea6b26d2e6e; quality source 1 / availability 0; adjusted Phase-8 value 0.5441543196074821; color fit 0.2875; exact score 17.033554094693450; display 17; final rank 6.

Contributions from neutral 25: Quality -7.869138229995 (weight 0.683513128434765); Lane -0.105467507406 (weight 0.200000000000000); Archetype +0.000000000000 (weight 0.104121717891309); Deck Need +0.008159832094 (weight 0.012365153673926); Soft limit +0.000000000000 (weight 0.000000000000000).

Deck impact: playability 1, membership 0.4549191338627917, D 0.5094272204765126, margin -0.003314932252651781, real cut line None, replacement/future line 0.5474692518601338, compares-with-future True, projected quality delta -0.0028969139249189757, structure gate 0.45865794906243024. Open/contested slots 16/16; 34 remaining picks, expected supply 27.200000000000003, coverage 1.7000000000000002, supply urgency 0.43333333333333324. Before/after pair RG/RG; projected structure {'Spells': 7, 'Creatures': 5, 'EarlyPlays': 2, 'HighCost': 0, 'Removal': 1.5}; structure confidence 0.3888888888888889; creature/early/removal/top-end need 0/0/0/0.

Projected RG: quality 0.5644327170819149, 8 spells, shortfall 0, blend weight 1, eligibility 1, candidate in selected deck True; cut-line margin -0.003314932252651781. Selected deck: 1? Konstrari Charm; 1? Aerid Konstrari; 1? Woodwork Prodigy // Soul Tether; 1? Keeper of the Quiet Hour; 1? No Admittance; 1? Pia, Determined Rebuilder; 1? Hallway Heckler // Vicious Verse; 1? Greenhouse Propagator.

Explanation text (existing score diagnostics):

```text
Pick Score: 17 / 50; Model: contextual-pick-score-v1.6; Measured
Exact score 17.03; tier marginal (pick-score-tiers-v2)
Pick-score ordinal rank: 6; progress 0.167; color fit 0.288; Phase 8 data weight 0.748
Quality raw 0.336; relevance R 1 (playability 1); effective 0.336
Quality 0.336; Lane 0.492; Archetype 0.5; Deck Need 0.509
Weights Q 68 %, L 20 %, A 10 %, D 1 %
Contributions from neutral 25 (before clamp/rounding):
Quality: -7.87 points
Lane: -0.11 points
Archetype: 0.00 points
Deck Need: +0.01 points
Soft limit: 0.00 points
Deck: available True; included True; replacement none; quality delta -0.0029; creatures 1; early 0; high 0; mana 0; pair RG -> RG; pair changed False; relaxation changed True; incomplete shell True
Deck fit: cut-line margin -0.33 pp; graded membership 0.455; real cut line ; virtual cut line 0.5475 (used); best-pair weight 1 of 1 viable pair(s)
Future supply: open slots 16; contested slots 16; remaining picks 34; expected playables 27.2; coverage 1.7; urgency 0.43; structure gate 0.459; structural need 0 at confidence 0.39; D 0.509
Supply profile: FRA (calibrated from FRA PremierDraft red-green card availability)
Evidence:
Intrinsic quality: below average · Direct — 17Lands GIH
Playability: realistic for your projected decks
Lane: neutral (no clear signal)
Archetype: neutral (no clear signal)
Deck need: neutral (D 0.51)
  - Fills an open slot, but later picks should supply better (0.3 pp below the expected filler 54.7 %).
  + Deck still needs 16 playables with only 34 picks remaining.
Roles: Creature; Structured metadata plus high-confidence Oracle rules.
Reasons:
Below-baseline intrinsic GIH performance.
- Fills an open slot, but later picks should supply better (0.3 pp below the expected filler 54.7 %).
+ Deck still needs 16 playables with only 34 picks remaining.
· Incomplete projected shell, not a finished 40-card deck.
```

### Unsummon

Evidence identity: ddad9f16-52d5-49de-82b0-b1a5294a9c44; quality source 1 / availability 0; adjusted Phase-8 value 0.5830527530655168; color fit -0.15; exact score 28.704950835170411; display 29; final rank 1.

Contributions from neutral 25: Quality +3.762932370555 (weight 0.683513128434765); Lane +0.288242767485 (weight 0.200000000000000); Archetype +0.000000000000 (weight 0.104121717891309); Deck Need -0.346224302870 (weight 0.012365153673926); Soft limit +0.000000000000 (weight 0.000000000000000).

Deck impact: playability 0, membership 0, D 0.1, margin None, real cut line None, replacement/future line 0.5474692518601338, compares-with-future False, projected quality delta 0, structure gate 0. Open/contested slots 16/16; 34 remaining picks, expected supply 27.200000000000003, coverage 1.7000000000000002, supply urgency 0.43333333333333324. Before/after pair RG/RG; projected structure {'Spells': 7, 'Creatures': 5, 'EarlyPlays': 2, 'HighCost': 0, 'Removal': 1.5}; structure confidence 0; creature/early/removal/top-end need 0/0/0/0.

Projected RG: quality 0.5673296310068339, 7 spells, shortfall 0, blend weight 1, eligibility 0, candidate in selected deck False; cut-line margin None. Selected deck: 1? Greenhouse Propagator; 1? Aerid Konstrari; 1? Pia, Determined Rebuilder; 1? No Admittance; 1? Keeper of the Quiet Hour; 1? Woodwork Prodigy // Soul Tether; 1? Konstrari Charm.

Explanation text (existing score diagnostics):

```text
Pick Score: 29 / 50; Model: contextual-pick-score-v1.6; Measured
Exact score 28.70; tier solid (pick-score-tiers-v2)
Pick-score ordinal rank: 1; progress 0.167; color fit -0.15; Phase 8 data weight 0.933
Quality raw 0.579; relevance R 1 (playability 0); effective 0.579
Quality 0.579; Lane 0.521; Archetype 0.5; Deck Need 0.1
Weights Q 68 %, L 20 %, A 10 %, D 1 %
Contributions from neutral 25 (before clamp/rounding):
Quality: +3.76 points
Lane: +0.29 points
Archetype: 0.00 points
Deck Need: -0.35 points
Soft limit: 0.00 points
Deck: available True; included False; replacement none; quality delta 0.0000; creatures 0; early 0; high 0; mana 0; pair RG -> RG; pair changed False; relaxation changed False; incomplete shell True
Deck fit: cut-line margin n/a; graded membership 0; real cut line ; virtual cut line 0.5475; best-pair weight 1 of 1 viable pair(s)
Future supply: open slots 16; contested slots 16; remaining picks 34; expected playables 27.2; coverage 1.7; urgency 0.43; structure gate 0; structural need 0 at confidence 0; D 0.1
Supply profile: FRA (calibrated from FRA PremierDraft red-green card availability)
Evidence:
Intrinsic quality: average · Direct — 17Lands GIH
Playability: realistic for your projected decks
Lane: neutral (no clear signal)
Archetype: neutral (no clear signal)
Deck need: weak (D 0.10)
  - Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).
Roles: EarlyPlay; Structured metadata plus high-confidence Oracle rules.
Reasons:
Near-average intrinsic GIH performance.
- Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).
· Incomplete projected shell, not a finished 40-card deck.
```

### Way of the Necromancer

Evidence identity: a0ff9689-ea49-4fff-b37c-4abbaeb0f73d; quality source 3 / availability 1; adjusted Phase-8 value None; color fit -0.15; exact score 24.603954875454026; display 25; final rank 3.

Contributions from neutral 25: Quality +0.000000000000 (weight 0.683513128434765); Lane -0.049820821676 (weight 0.200000000000000); Archetype +0.000000000000 (weight 0.104121717891309); Deck Need -0.346224302870 (weight 0.012365153673926); Soft limit +0.000000000000 (weight 0.000000000000000).

Deck impact: playability 0, membership 0, D 0.1, margin None, real cut line None, replacement/future line 0.5474692518601338, compares-with-future False, projected quality delta 0, structure gate 0. Open/contested slots 16/16; 34 remaining picks, expected supply 27.200000000000003, coverage 1.7000000000000002, supply urgency 0.43333333333333324. Before/after pair RG/RG; projected structure {'Spells': 7, 'Creatures': 5, 'EarlyPlays': 2, 'HighCost': 0, 'Removal': 1.5}; structure confidence 0; creature/early/removal/top-end need 0/0/0/0.

Projected RG: quality 0.5673296310068339, 7 spells, shortfall 0, blend weight 1, eligibility 0, candidate in selected deck False; cut-line margin None. Selected deck: 1? Greenhouse Propagator; 1? Aerid Konstrari; 1? Pia, Determined Rebuilder; 1? No Admittance; 1? Keeper of the Quiet Hour; 1? Woodwork Prodigy // Soul Tether; 1? Konstrari Charm.

Explanation text (existing score diagnostics):

```text
Pick Score: 25 / 50 (Estimated); Model: contextual-pick-score-v1.6; EstimatedMissingStatistics
Exact score 24.60; tier solid (pick-score-tiers-v2)
Pick-score ordinal rank: 3; progress 0.167; color fit -0.15; Phase 8 data weight 
Quality raw 0.5; relevance R 1 (playability 0); effective 0.5
Quality 0.5; Lane 0.496; Archetype 0.5; Deck Need 0.1
Weights Q 68 %, L 20 %, A 10 %, D 1 %
Contributions from neutral 25 (before clamp/rounding):
Quality: 0.00 points
Lane: -0.05 points
Archetype: 0.00 points
Deck Need: -0.35 points
Soft limit: 0.00 points
Deck: available True; included False; replacement none; quality delta 0.0000; creatures 0; early 0; high 0; mana 0; pair RG -> RG; pair changed False; relaxation changed False; incomplete shell True
Deck fit: cut-line margin n/a; graded membership 0; real cut line ; virtual cut line 0.5475; best-pair weight 1 of 1 viable pair(s)
Future supply: open slots 16; contested slots 16; remaining picks 34; expected playables 27.2; coverage 1.7; urgency 0.43; structure gate 0; structural need 0 at confidence 0; D 0.1
Supply profile: FRA (calibrated from FRA PremierDraft red-green card availability)
Evidence:
Intrinsic quality: Estimated — this card's 17Lands row has no usable GIH win rate; neutral format prior used
Playability: realistic for your projected decks
Lane: neutral (no clear signal)
Archetype: neutral (no clear signal)
Deck need: weak (D 0.10)
  - Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).
Roles: EarlyPlay; Structured metadata plus high-confidence Oracle rules.
Reasons:
Estimated intrinsic quality (neutral format prior): this card's 17Lands row has no usable GIH win rate.
- Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).
· Incomplete projected shell, not a finished 40-card deck.
```

### Dedicated Commons

Evidence identity: 3223e5db-5cc4-42f9-ae9e-ff58abc7c390; quality source 1 / availability 0; adjusted Phase-8 value 0.5382674308212719; color fit 0; exact score 15.024239942318646; display 15; final rank 7.

Contributions from neutral 25: Quality -9.629535754811 (weight 0.683513128434765); Lane +0.000000000000 (weight 0.200000000000000); Archetype +0.000000000000 (weight 0.104121717891309); Deck Need -0.346224302870 (weight 0.012365153673926); Soft limit +0.000000000000 (weight 0.000000000000000).

Deck impact: playability 1, membership 1, D 0.1, margin None, real cut line None, replacement/future line 0.5474692518601338, compares-with-future False, projected quality delta 0, structure gate 0. Open/contested slots 16/16; 34 remaining picks, expected supply 27.200000000000003, coverage 1.7000000000000002, supply urgency 0.43333333333333324. Before/after pair RG/RG; projected structure {'Spells': 7, 'Creatures': 5, 'EarlyPlays': 2, 'HighCost': 0, 'Removal': 1.5}; structure confidence 0; creature/early/removal/top-end need 0/0/0/0.

Projected RG: quality 0.5673296310068339, 7 spells, shortfall 0, blend weight 1, eligibility 1, candidate in selected deck True; cut-line margin None. Selected deck: 1? No Admittance; 1? Keeper of the Quiet Hour; 1? Dedicated Commons; 1? Konstrari Charm; 1? Greenhouse Propagator; 1? Aerid Konstrari; 1? Woodwork Prodigy // Soul Tether; 1? Pia, Determined Rebuilder.

Explanation text (existing score diagnostics):

```text
Pick Score: 15 / 50; Model: contextual-pick-score-v1.6; Measured
Exact score 15.02; tier marginal (pick-score-tiers-v2)
Pick-score ordinal rank: 7; progress 0.167; color fit 0; Phase 8 data weight 0.572
Quality raw 0.299; relevance R 1 (playability 1); effective 0.299
Quality 0.299; Lane 0.5; Archetype 0.5; Deck Need 0.1
Weights Q 68 %, L 20 %, A 10 %, D 1 %
Contributions from neutral 25 (before clamp/rounding):
Quality: -9.63 points
Lane: 0.00 points
Archetype: 0.00 points
Deck Need: -0.35 points
Soft limit: 0.00 points
Deck: available True; included True; replacement none; quality delta 0.0000; creatures 0; early 0; high 0; mana 0; pair RG -> RG; pair changed False; relaxation changed False; incomplete shell True
Deck fit: cut-line margin n/a; graded membership 1; real cut line ; virtual cut line 0.5475; best-pair weight 1 of 1 viable pair(s)
Future supply: open slots 16; contested slots 16; remaining picks 34; expected playables 27.2; coverage 1.7; urgency 0.43; structure gate 0; structural need 0 at confidence 0; D 0.1
Supply profile: FRA (calibrated from FRA PremierDraft red-green card availability)
Evidence:
Intrinsic quality: below average · Direct — 17Lands GIH
Playability: realistic for your projected decks
Lane: neutral (no clear signal)
Archetype: neutral (no clear signal)
Deck need: weak (D 0.10)
  - Selected only as a basic substitute: no measured colour shortfall to fix, so no fixing value.
Roles: Land, DirectManaSource, Fixing; Structured metadata plus high-confidence Oracle rules.
Reasons:
Below-baseline intrinsic GIH performance.
- Selected only as a basic substitute: no measured colour shortfall to fix, so no fixing value.
· Incomplete projected shell, not a finished 40-card deck.
```

**Unresolved questions:** historical UI lane/pair-provider arrival timing was not recorded, although the exact display and direct statistics reproduce with the recovered chronology. How early relevance should respond to a strong small pool versus an intentionally flexible/partial pool is a product/model decision. Do not replace this with a global fixed off-color penalty or retune display calibration without comparative tests.

**Smallest recommended correction:** a v1.6.1 investigation is warranted. Make early quality relevance respond to strong recovered color commitment and zero realistic projected playability, while retaining exploration for weak/unknown pools. Keep intrinsic Q, raw GIH, tier thresholds, display calibration and WGC authority unchanged. Do not choose constants or implement the correction in this phase. The before-fix fixture intentionally asserts the observed failure; a subsequently approved correction should keep these real inputs and update expected contextual outcomes deliberately.

**Deterministic fixture:** [real P1P8 inputs](tests/Fixtures/phase9e2b-p1p8-rg-failure.json), frozen local card metadata, all eight offered-pack snapshots up to P1P8, chronological picks, exact pool, participant/environment stats, original log SHA-256, baseline, expected exact scores, and the full unrounded component/explanation/projected-deck breakdown for every offered card. [Reproduction test](tests/DraftTG.Application.Tests/LiveP1P8FailureReproductionTests.cs) reproduces all seven ranks/scores offline and asserts P=0 but R=1 for Unsummon. The optional projection-audit callback is read-only; a test compares deck impact with/without it and finds identical numerical outputs. No live ledger entry is written.

Reproduce using the existing score audit tool (no network or live capture):

```powershell
dotnet run --project tools/DraftTG.PickScoreAudit -- --live-failure 'C:\Users\Graphics0\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log' artifacts/phase9e2b-failure
```

The command writes ignored local `reproduction.json` (all recovered-pack component/projection details plus pre-verification comparator audit) and `fixture-input.json`. It checks the exact target runtime?s recorded offered multisets, rejects absent P1P8, reports parser warnings, and asserts zero network requests. The tracked compact fixture above makes the regression independent of Player.log rotation and local caches.

Session-specific summary without rewriting the user?s existing live report:

```powershell
dotnet .\src\DraftTG.App\bin\Phase9E2BFailure\Debug\net10.0-windows10.0.19041.0\DraftTG.App.dll --live-localization-validation-summary --build-id f91b5307-b7c6-4734-9bbb-85234488e2ce --session-id 0bd92f4fccd6484c91cbc34654ff8097
```

**Next step:** do not collect another paid/live draft yet. First use the retained real-state scoring regression to review a narrowly scoped v1.6.1 proposal, and diagnose WGC recall/reference handling with exact-frame evidence from an existing failing state or retained pixels. This patch supplies diagnostics and an offline reproduction only. It does not change score constants, deterministic support, verifier thresholds, production localization authority or the promotion decision.
