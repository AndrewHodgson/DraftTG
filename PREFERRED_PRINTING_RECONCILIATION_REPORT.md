# Preferred-Printing Reconciliation — Phase 9F.4

Ordinary preferred basic-land printings now reconcile without changing scoring or substituting the rendered printing into draft history. Arbitrary nonbasic reprints remain unsupported.

## Exact identities and rule

| Case | Pod printing retained | Visible printing | Evidence |
| --- | --- | --- | --- |
| Swamp | 106530, FRA #285 | 11809, USG #340 | Earlier live mismatch; both exact IDs now pass owned-metadata proof |
| Forest | 106535, FRA #290 | 75903, ANA #20 | Live P2P1 succeeds; 14 outlines confirmed |

`LogicalIdentity` denotes the proven gameplay basic (`ordinary-basic:Swamp`, for example). `PodGrpId` retains the exact draft/statistical printing. `VisibleGrpId` retains the exact rendered printing. Ordinary identical-ID cases keep the owned catalog identifier as their logical identity. These diagnostics do not replace production statistical identity resolution.

Each exact ID must independently resolve without ambiguity in the existing Arena resolver and owned catalog. Different IDs qualify only for Plains, Island, Swamp, Mountain or Forest with complete, equal normalized gameplay metadata: normal single-face layout; known colorless colors; exact ordinary Basic Land type line and subtype; mana value zero; empty cost; exactly the corresponding produced mana; no power/toughness/keywords; equal Oracle text and all other normalized metadata, including unknown-symbol lists. Name agreement alone never suffices. Snow basics, Wastes, unknown fields, ambiguous IDs and differing gameplay decline.

## Independent pod occurrence reconciliation

The live P2P1 test showed that both `_cardData` and `_visualCard` can already contain the preferred-art ID. Reading `_cardData` alone therefore does not recover the draft-pod identity. The reader reconciles the independent pod occurrence multiset against the rendered order, separately verifies represented-versus-visible equivalence, reserves exact matches first, and requires one unique remaining pod printing candidate per differing occurrence. Counts and duplicates are conserved. Competing different pod printings decline rather than choosing an arbitrary printing.

All original scene/list/pod/count and end-of-read checks remain. Represented printing IDs and visible IDs are independently reread; transformed pod IDs do not replace the represented-ID staleness check. The watcher and UI stability key include printing provenance so a visible-printing change requires fresh stability.

## Live result and limitations

P2P1 initially failed closed with Forest 106535 versus 75903. After the independent pod correction, the complete 14-card pack passed reconciliation and the user confirmed its outlines. The earlier Swamp pair passes the actual owned metadata contract, but a newly rendered Swamp pack was not physically re-observed in this phase. No alternate-printing nonbasic spell has been observed/proven here; the rule is not broadened to reprinted spells or front-face substitutions.

Production capture-free runtime, one-shot memory command, watcher and UI debug command reuse the same policy. If catalog proof is unavailable, the reader remains exact-ID-only. Unknown process fingerprints still fail closed before object traversal. Pack provenance records both IDs, logical identity, acceptance/reconciliation flags and exact reason. The log/history/scoring pack keeps pod printings.

[Owned proof](docs/evidence/phase-9f4/printing-proof.json), [live summary](docs/evidence/phase-9f4/live-summary.json), [physical scope](PHASE_9F4_PHYSICAL_VALIDATION.md). No production scoring, memory write or capture authority was changed.
