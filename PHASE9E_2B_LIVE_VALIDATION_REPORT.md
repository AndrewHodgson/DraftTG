# Phase 9E.2B — Live deterministic localization validation

Promotion gate: **NOT PASSED**. Production authority: **WGC full matcher**.

Build ID tested (assembly MVID): `f91b5307-b7c6-4734-9bbb-85234488e2ce`. Build/commit metadata: 1.0.0+449eab11ae1b9f6d29953fc8e3cb4ed845db57f2.
Only this build's opt-in synchronized WGC records are included. Offline, legacy, synthetic/unspecified captures and other builds do not count.

Total live observation attempts: 28; eligible attempts: 0; fully verified promotion-counting distinct packs: 0.
Accepted pack generation events: 15; accepted-event card counts (including packs with no recognized frame): 14: 2; 13: 1; 12: 1; 11: 1; 10: 1; 9: 1; 8: 1; 7: 1; 6: 1; 5: 1; 4: 1; 3: 1; 2: 1; 1: 1
Fully verified attempts: 0; qualifying identified real drafts: 0; qualifying client sizes: 0.
Comparator/order mismatches: 0; geometry mismatches: 0; verifier failures on eligible attempts: 0.
False deterministic proposals against complete WGC evidence: 0; actual production deterministic placements: 0 (shadow only).
Fallback events: 28; unclean/missing fallback diagnostics: 0; localization/shadow failures requiring investigation: 37.
Unsupported observations: 19; rejected non-WGC/unsynchronized observation rows: 0; excluded ledger rows: 0; malformed rows: 0.

Draft scopes: eaf5816cd8fb811a69ba001241680841: 28 attempts, 0 verified packs

Runtime sessions: 0bd92f4fccd6484c91cbc34654ff8097: 28 attempts, 0 verified packs

Client sizes / DPI: 1600x900 / 96 DPI: 24 attempts, 0 verified packs; 1920x1080 / 96 DPI: 4 attempts, 0 verified packs

Card-count coverage: 10: 3 attempts, 0 verified packs; 11: 1 attempts, 0 verified packs; 12: 3 attempts, 0 verified packs; 13: 6 attempts, 0 verified packs; 14: 3 attempts, 0 verified packs; 3: 1 attempts, 0 verified packs; 4: 1 attempts, 0 verified packs; 5: 2 attempts, 0 verified packs; 6: 3 attempts, 0 verified packs; 7: 1 attempts, 0 verified packs; 8: 2 attempts, 0 verified packs; 9: 2 attempts, 0 verified packs

Fallback reasons: manual intervention during this pack generation: 1; 0 fully verified candidate orders: 6; card count 12 outside validated envelope: 3; card count 11 outside validated envelope: 1; card count 10 outside validated envelope: 3; card count 9 outside validated envelope: 2; card count 8 outside validated envelope: 2; card count 7 outside validated envelope: 1; card count 6 outside validated envelope: 3; card count 5 outside validated envelope: 2; card count 4 outside validated envelope: 1; card count 3 outside validated envelope: 1; order discriminating (2 predicted orders): 2

Stale-frame admission rejections: 0; asynchronous stale-result rejection events: 0; canceled localization attempts: 0 (cancellation alone is not proof of stale rejection).
Move without resize: 42 events; completed placement retained without full relocalization: 42.
Resize/identity/visibility/DPI invalidation: 1 events; size changes: 1; DPI changes: 0.
Accepted-pick badge retirement: 14 events; cleared context and zero placed badges: 14. Semantic-event/UI timestamps are recorded; Player.log emission latency is not measured.
CaptureMilliseconds: n=28, median=27.5 ms, max=129.2 ms
FullMatcherMilliseconds: n=28, median=537.5 ms, max=868.7 ms
VerificationMilliseconds: n=28, median=0.0 ms, max=765.3 ms
AdmissionAgeMilliseconds: n=28, median=1126.0 ms, max=5367.6 ms

Unsupported/unvalidated: <=12 and 15 cards; non-windowed clients; height outside 720–1080; aspect outside 1.70–1.78; unusual UI scaling. No deterministic geometry is extrapolated. DPI/display transitions require live verification.

Recommendation: retain WGC authority. Gather >=30 qualifying distinct packs across >=2 identified real drafts and >=2 client sizes; investigate every mismatch/failure and missing fallback diagnostic. Zero observations means no live validation, not proof of zero faults. Physical move, resize, pick retirement and stale-result behavior must be reviewed before proposing promotion.
