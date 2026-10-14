# Rating and Stats QA — Phase 9F.3

## 1. Executive summary

`DraftTG.RatingAudit` audits existing production evidence without tuning or acquiring research data. Latest **FRA QuickDraft** audit: **432 Arena printing IDs**, 281 exact-primary, 111 alternate-format priors, 40 neutral cases, zero missing/ambiguous identities. Twenty-five missing-stat rows are basic-land printings; fifteen other neutral printing rows have null GIH. No Untapped ratings were supplied or imported; comparisons are unavailable, not zero agreement.

Production `contextual-pick-score-v1.6.2`, Q/L/A/D/R, commitment, k500, FRA prior, tiers and deck builder are unchanged. No V2 study, corpus acquisition/replay or MH3/DFT evaluation occurred.

## 2. Production Q/stat pipeline

Reuse the production Scryfall JSONL loader, Arena resolver/database bridge, `SeventeenLandsCardRatingsClient` cache validation, `LimitedStatisticsMapper`, `SeventeenLandsStatisticalIdentity`, `StatisticalRecommendationEngine`, `NormalizeQuality`, and `SparsePrimaryQualityPrior`. The diagnostic does not replace these with research Q formulas.

Environment baseline is the existing count-weighted valid-GIH environment. Direct adjusted rate is `(n*p + 500*b)/(n+500)`; Q uses the frozen normalization. Primary GIH needs a valid rate and positive sample. A null GIH does not become usable merely because a count exists. Missing environment gives unavailable Q; known cards with an environment and no usable evidence retain neutral Q=.5.

FRA Quick→Premier prior remains narrow: primary row must exist, be independently resolved and lack usable GIH; alternate exact-format/name evidence and valid baselines must satisfy the existing contract. Reliability=.9, alternate sample weight=n/(n+500), effective quality=b_primary + .9*weight*(p_alternate-b_alternate), clipped to [0,1]. Missing/ambiguous primary joins do not acquire this prior. No generalized cross-format fallback or prior added to usable primary rows.

## 3. Set/format coverage and scope

CLI supports `--set FRA --format QuickDraft`, `PremierDraft`, `TradDraft` using the existing production format enum. Unsupported formats reject, rather than being silently relabeled. MH3/DFT are excluded from this QA command.

Scope: all distinct non-token/non-rebalanced Arena GrpIds in the **exact selected set**, including alternate art/collector printings and basics. Here: 432 IDs, 407 nonbasic printing IDs, 285 distinct names. This is an Arena printing inventory, **not a claim that every printing is draftable or equally likely in a booster**. Bonus/sheet cards from another set need their own explicit inventory scope; no broad name-based set fallback is used. Printing names do not prove original booster collation.

## 4. Exact-primary coverage

| Category | Printing IDs |
| --- | ---: |
| ExactPrimary, usable GIH and n≥500 | 281 |
| SparsePrimary, usable GIH and n<500 | 0 |
| AlternateFormatPrior | 111 |
| NullGIH, neutral | 15 |
| MissingStatRow, neutral | 25 |
| Total | 432 |

Usable direct primary coverage: **65.05%**. Including the authorized alternate prior: **90.74%**. Counts are disjoint coverage states; null flags below overlap prior states. All printings of the same card are retained; these are not 432 independent card-quality estimates.

## 5. Sparse/fallback coverage

Sparse-primary-with-prior count is **zero**: that category is not an additional production policy. Usable low-count primary evidence already receives k500 shrinkage; only unusable primary GIH can invoke the existing FRA alternate prior. The 111 alternate rows remain explicitly estimated, with alternate format/GIH/count/baseline/sample weight/reliability/prior provenance recorded. They must not be presented as measured primary GIH.

The first audit used the October 12 stale caches and found 223 direct / 158 alternate / 51 neutral. The normal live overlay subsequently refreshed production caches at **2026-10-14T20:01:18.5299959Z**, producing 281 / 111 / 40 on the latest audit. This is evidence freshness, not changed scoring. The audit itself uses a local HTTP-intercepting handler and never downloads data. Latest source/cache/catalog/database hashes and fetched time are in [summary](docs/evidence/rating-audit/summary.json).

## 6. Neutral/missing cards

There are forty neutral printing rows. Fifteen have null GIH and no qualified alternate measurement: Loyal Tutor, Precise Redaction, Identity Echo, Gardenize, Omnipresence, Entrust the Spark and Tam, the Possibility (multiple printings). Twenty-five basic-land printings have no provider stat row: Plains, Island, Swamp, Mountain, Forest. Missing basic-land GIH is expected inventory coverage, not evidence of a scoring defect or a failed nonbasic identity join.

Full rows and risk reasons are under ignored `artifacts/rating-audit/cards.json`; compact aggregates and high-risk examples are under `docs/evidence/rating-audit/`. Unknown Q/baselines/counts remain JSON null. The tool never fills absent data with invented measurements.

## 7. Identity problems

Zero missing Arena/Scryfall identities and zero ambiguous identities in this selected inventory. Exact statistical-name/front-face production rules are reused. Conflicting provider rows and ambiguous aliases receive explicit ambiguity flags; missing card identity remains separate from missing statistics. No case-insensitive/partial name join or arbitrary printing choice was added.

The separate live opening-pack Swamp printing-ID discrepancy concerns **memory pod vs visible printing**, not a missing FRA statistical identity. It causes localization suppression and is documented in the localization report. Resolving a QA name does not weaken that exact memory contract.

## 8. Null GIH/sample issues

126 printing rows have a present primary stat row with null GIH: 111 receive the authorized prior; fifteen stay neutral. Missing sample-count flags: zero. A low primary count and null primary GIH may coexist. Count is not proof that the provider published a usable rate.

Risk examples include Cruel Calculations and Codie, Ravenous Codex (primary n=17), Extrapolate the Impossible (n=20), Karn, Gilded Guardian (n=27), plus the neutral cards above. These are QA inspection priorities, not claims their intrinsic ratings are wrong. The machine summary contains the top 25 printing-level risk rows and exact reasons.

## 9. Current-pack diagnostic

The P1P12 live capture-free overlay emitted `RatingAudit` from the actual accepted production update, rather than reconstructing draft scores offline. Set/format, generation, baseline, GrpId, resolved identity, Q/GIH/count, provenance/prior, final contextual value/score and rank are tied to the current reconciled pack. Loaded/stale previous-pack updates cannot populate it.

Baseline **0.5681364169465147**, source Live during the measured overlay:

| Card | GrpId | Q | GIH | Games | Exact score / 50 | Rank |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hexhaven Dueling Arena | 106426 | .256353 | 49.666667% | 600 | 12.770167 | 3 |
| Ferocity of the Hunt | 106371 | .330497 | 53.809729% | 4646 | 16.020178 | 2 |
| Medic's Kitesail | 106418 | .355607 | 54.055103% | 2577 | 18.860483 | 1 |

All three: DirectPrimary / ExactName, no alternate prior. Earlier probe ledgers stored `FinalCurrentScore` as raw 0–1 ContextualValue; the final recorder explicitly labels scales and emits displayed 0–50 score, exact 0–50 score, and 0–1 ContextualValue separately. Ranking uses exact values; rounding is presentation only. The historical ledger is retained, not relabeled as a new live run.

## 10. External comparison methodology

CSV template: `tools/DraftTG.RatingAudit/untapped-rating-reference.csv`. Required headers: Set, Format, CardName, ArenaGrpId (blank allowed), UntappedRating, UntappedTier (blank allowed), Source, ObservedAt, Notes. Sources: Manual, Screenshot, VisibleUI, PublicExport. Supply one logical card row per exact set/format, using observed visible ratings and a real date. No auto-population, private endpoints, authentication/entitlement bypass, binary extraction, formula copying or tuning.

Primary comparison is **intrinsic Q**, not DraftTG's contextual 0–50 score. Higher rating is assumed better; midrank ties and percentile ranks remove numeric scale differences. Spearman and top/bottom percentile diagnostics use the matched reference subset. True set-wide percentile interpretation requires a complete reference inventory; partial samples are explicitly labeled. Q from missing evidence remains flagged, rather than being treated as measured.

## 11. Untapped comparison coverage

No legitimate user-visible rating reference was supplied for this run. **Zero cards compared.** Header-only schemas are available. No proprietary data was extracted or invented, and Untapped is not a runtime dependency. [Comparison status](docs/evidence/rating-audit/comparison.json).

## 12. Rank correlation

Unavailable (JSON null), not zero. With actual reference rows, the tool computes Spearman using tie-aware midrank percentiles; constant or singleton samples decline correlation. Set/format mismatch and ambiguous/no-usable comparison identity are explicitly rejected.

## 13. Large disagreements

No observed disagreements exist without reference data. The tool reports the top 25 absolute percentile gaps, GIH/count/fallback/coverage and possible explanation. A ≥.30 percentile-gap diagnostic threshold is reported; it is not fitted to observed external grades or used for scoring. The percentile scope stays explicit.

## 14. Data gaps and secondary pack comparison

Explanations include MissingStats, SparseStats, FormatMismatch, IdentityIssue and Unknown. They are possible data-gap associations, not proof that an external rating is correct or that a gap causally explains the difference. Review null/identity/fallback problems as DATA QA before proposing model changes.

For a manually observed pack, use `untapped-pack-reference.csv`, including the exact recorded PackKey and every offered GrpId. `--pack-reference CSV --pack-ledger JSONL` requires a complete scored pack and legitimate same-pack observations. It emits Top1 agreement, Top3 overlap, Spearman and ≥3-rank differences, together with production provenance. It is labeled a **secondary static-grade versus contextual-score comparison**. Tied Top1/Top3 ordering uses observed occurrence order and is labeled; Spearman preserves ties. No partial pack comparison is allowed. Current pack agreement is unavailable because no reference was provided.

## 15. Unresolved disagreements

No external discrepancies can be asserted from this run. Remaining data risks are explicit null primary rates, alternate-format estimates, inventory scope beyond booster collation, and localization's preferred-printing contract. None authorizes score tuning.

## 16. Commands and recommendations

```powershell
Set-Location 'C:\Users\Graphics0\Documents\Projects\DraftTG'
dotnet build DraftTG.sln
dotnet .\tools\DraftTG.RatingAudit\bin\Debug\net10.0\DraftTG.RatingAudit.dll --set FRA --format QuickDraft --output .\artifacts\rating-audit
# Same production cache semantics, alternate supported formats:
dotnet .\tools\DraftTG.RatingAudit\bin\Debug\net10.0\DraftTG.RatingAudit.dll --set FRA --format PremierDraft --output .\artifacts\rating-audit-premier
# Optional legitimate user-filled reference:
dotnet .\tools\DraftTG.RatingAudit\bin\Debug\net10.0\DraftTG.RatingAudit.dll --set FRA --format QuickDraft --reference .\my-visible-ratings.csv --output .\artifacts\rating-comparison
# Inspect an accepted live pack from the overlay ledger:
dotnet .\tools\DraftTG.RatingAudit\bin\Debug\net10.0\DraftTG.RatingAudit.dll --set FRA --format QuickDraft --pack-ledger .\artifacts\phase9f3\live.jsonl --output .\artifacts\rating-audit
```

Retain production scoring. Review the fifteen nonbasic neutral printing rows and narrow prior provenance; do not treat basics or publisher-suppressed low samples as incorrect identity. Gather a limited legitimate external sample only if useful, preserving its source/scale/format. Finish localization's physical matrix and native deck transform work before calling that portion complete. Build/test/golden evidence is in `docs/evidence/phase-9f3/validation.json`. No later phase begins automatically.
