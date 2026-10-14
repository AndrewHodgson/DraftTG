# Phase 9F.4 Physical Validation

## Pack

The representative matrix is **incomplete**. No broader physical qualification is claimed. The tested windowed list client is 1600×900, 96 DPI. The current live draft is `761fac89-894a-4de8-afa2-4507fc4eb55f`.

| Check | Result | Evidence |
| --- | --- | --- |
| 3 cards | Confirmed in preserved 9F.3 | P1P12; “All three aligned” |
| 1 card | Confirmed | P1P14, Terminal Criticism |
| 14 cards | Confirmed | P2P1, including preferred Forest printing |
| 10 cards | Pending | No user observation yet |
| 5 cards | Pending | No user observation yet |
| Second size at 100% | Pending | 1280×720 or 1920×1080 requested |
| Move without resize | Pending | No completed live move event/user observation yet |
| Resize retirement/redisplay | Pending | Implemented/tested; physical confirmation required |
| Accepted-pick retirement | Pending | The P1P14 timed overlay had already exited before the pick |
| Next-pack redisplay | Pending for running transition | P2P1 aligned after restart, not evidence of automatic transition |
| Maximize/restore | Unvalidated | Unsupported aspect/fullscreen states must suppress |
| 125%/150% DPI | Unvalidated | Transform exists; ordinary deck markers remain gated to 96 DPI |

The user confirmed: “Yes both old Pack 1 pick 14 aligned and so does pack 2 outlines.” The earlier observation “Old outline disapears but no new outline. Did the app close?” was diagnosed as expiry of the timed session. It is not counted as successful pick retirement.

## Deck

| Check | Result | Scope |
| --- | --- | --- |
| Few cards; actual order/names/vertical anchors | Confirmed | P1P12, eight main groups / nine copies |
| Grouped duplicate ×2 | Confirmed | One grouped Saheeli row, quantity two |
| 10+ main rows | Pending | Total picked cards is not a count of visible main rows |
| Top/middle/bottom scroll | Pending | Current list is shorter than its viewport |
| Rapid scroll and badge retirement | Pending | Tests do not establish physical behavior |
| Pick update | Pending | No confirmed running transition |
| Resize | Pending | No second-size confirmation yet |
| Main/side transition | Unsupported for display | Side geometry is diagnostic only |
| Compact Q marker appearance | Not separately confirmed | Debug bounds are validated; ordinary marker placement is conservatively gated |

The user confirmed: “All main rows aligned, including ×2.” The supported ordinary marker scope is limited to the confirmed non-scrollable 1600×900 / 96-DPI main list. Larger/scrollable lists, sideboard, other sizes and DPI remain debug-only until qualified.

For each remaining deck check, require identity, quantity, vertical anchor, scroll following, removal of culled rows and absence of stale badges. Do not mark a checklist item complete solely from native geometry, automated tests or an unchanged ledger.

[Machine confirmation](docs/evidence/phase-9f4/physical-confirmation.json). The previous Phase 9F.3 artifacts remain unchanged.
