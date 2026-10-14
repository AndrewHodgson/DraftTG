# FRA QuickDraft Neutral Stat Review — Phase 9F.4

Offline audit of the owned ALL_TIME caches, primary fetched 2026-10-14T20:01:18.5299959+00:00. Primary baseline 56.8136%; Premier baseline 56.1555%. Production `contextual-pick-score-v1.6.2`, k500, .9 FRA reliability, all other components and tiers remain unchanged. No historical replay, holdout evaluation or provider acquisition occurred.

Grouping requires the owned Scryfall oracle ID plus complete normalized gameplay metadata agreement and colors; no name-only grouping. Printing-specific evidence is retained. Logical counts refer to this audit population, not booster collation.

**7 logical nonbasic cards / 15 printings remain neutral.** 25 basic-land missing-stat printings are excluded from nonbasic defects.

| Logical card | GrpIds | Primary row / n / GIH | Alternate row / n / GIH | Alternate baseline | Q | Risk |
| --- | --- | --- | --- | --- | --- | --- |
| Identity Echo | 106318, 107910 | True / 18 / null | True / 321 / null | 56.1555% | 0.5 | High |
| Loyal Tutor | 106239, 107927 | True / 26 / null | True / 223 / null | 56.1555% | 0.5 | High |
| Omnipresence | 106345, 107870 | True / 5 / null | True / 22 / null | 56.1555% | 0.5 | High |
| Entrust the Spark | 106367, 107889 | True / 6 / null | True / 145 / null | 56.1555% | 0.5 | High |
| Precise Redaction | 106262, 107901 | True / 74 / null | True / 253 / null | 56.1555% | 0.5 | High |
| Tam, the Possibility | 106521, 107856 | True / 9 / null | True / 144 / null | 56.1555% | 0.5 | High |
| Gardenize | 106337, 107884, 107929 | True / 27 / null | True / 320 / null | 56.1555% | 0.5 | High |

Both format rows exist for all seven logical cards, but both primary and alternate GIH are null. Positive game counts do not invent a win rate. The existing prior requires finite alternate GIH and a positive sample, so it correctly declines; production Q=.5 remains explicit. No production scoring defect is established.

High: alternate n <500, |Q−.5|≥.35, or uncertain identity. Medium: otherwise n <2000 or |Q−.5|≥.20. Low: otherwise. These prospective QA triage rules were recorded before ranking; they are not reliability parameters or scoring changes.

All seven require coverage review: Identity Echo, Loyal Tutor, Omnipresence, Entrust the Spark, Precise Redaction, Tam, the Possibility, and Gardenize. Recommendation for each: retain neutral Q and EST provenance, monitor normal authorized cache refreshes for usable exact-format GIH, and review newly available data without tuning to a desired rating. Null rates alone do not establish provider error or poor card quality.

Untapped comparison unavailable: no legitimate reference supplied, zero cards compared, correlation unavailable.

[Machine evidence](docs/evidence/phase-9f4/stat-risk-review.json); [input provenance](docs/evidence/phase-9f4/stat-inputs.json).
