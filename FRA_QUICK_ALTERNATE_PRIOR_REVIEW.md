# FRA QuickDraft Alternate-Prior Review — Phase 9F.4

Offline audit of the owned ALL_TIME caches, primary fetched 2026-10-14T20:01:18.5299959+00:00. Primary baseline 56.8136%; Premier baseline 56.1555%. Production `contextual-pick-score-v1.6.2`, k500, .9 FRA reliability, all other components and tiers remain unchanged. No historical replay, holdout evaluation or provider acquisition occurred.

Grouping requires the owned Scryfall oracle ID plus complete normalized gameplay metadata agreement and colors; no name-only grouping. Printing-specific evidence is retained. Logical counts refer to this audit population, not booster collation.

**56 proven logical cards / 111 printings use the existing authorized prior.** Duplicated printings are not independent quality observations. Quantiles use sorted floor-index empirical values, one row per logical card; full per-printing evidence remains exported.

| Logical-card weighted measure | N | Min | P25 | Median | P75 | Max |
| --- | --- | --- | --- | --- | --- | --- |
| PrimaryN | 56 | 17.000000 | 131.000000 | 225.000000 | 323.000000 | 494.000000 |
| AlternateN | 56 | 505.000000 | 2072.000000 | 3612.000000 | 5756.000000 | 12049.000000 |
| AlternateDelta | 56 | -0.117654 | -0.065135 | -0.030071 | -0.000278 | 0.097453 |
| Q | 56 | 0.037748 | 0.232763 | 0.350098 | 0.498536 | 0.948727 |
| Shift | 56 | -0.462252 | -0.267237 | -0.149902 | -0.001464 | 0.448727 |

High: alternate n <500, |Q−.5|≥.35, or uncertain identity. Medium: otherwise n <2000 or |Q−.5|≥.20. Low: otherwise. These prospective QA triage rules were recorded before ranking; they are not reliability parameters or scoring changes.

Logical risk buckets: {'High': 9, 'Medium': 20, 'Low': 27}. High is a review priority, not proof of a bad score. The sample effect remains n/(n+500), with .9 format reliability; Q is the existing quality normalization of the resulting value. The exported shift is Q_with_prior−.5, not a rerun of contextual pick score, draft-win improvement, or a causal quality estimate.

## Top 20 absolute shifts

| Logical card | GrpIds | Primary n | Alternate n | Alt GIH − baseline | Q | Q − .5 | Risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Way of the Necromancer | 106484 | 336 | 2656 | -11.7654 pp | 0.037748 | -0.462252 | High |
| Flickering Hound | 106232, 107898 | 252 | 3086 | -10.6273 pp | 0.050119 | -0.449881 | High |
| Overwrite the Multiverse | 106288, 107880 | 439 | 6877 | +9.7453 pp | 0.948727 | +0.448727 | High |
| Aerid Konstrari | 106356, 107886, 107930 | 400 | 5718 | +8.6749 pp | 0.922329 | +0.422329 | High |
| Loot, the Anomaly | 106477 | 457 | 3103 | -8.1697 pp | 0.110552 | -0.389448 | High |
| Yargle, Goliath of Otaria | 106470 | 283 | 1261 | -9.6051 pp | 0.117300 | -0.382700 | High |
| Flourishing Grapple | 106336, 107911 | 178 | 689 | -11.7433 pp | 0.120554 | -0.379446 | High |
| Samut, Tyrant of Naktamun | 106465, 107841 | 103 | 2165 | -8.3495 pp | 0.121553 | -0.378447 | High |
| Codie, Ravenous Codex | 106413, 107935 | 17 | 805 | -10.3170 pp | 0.142227 | -0.357773 | High |
| Germinate Recruits | 106234, 107873 | 45 | 1207 | -8.6824 pp | 0.154667 | -0.345333 | Medium |
| Kwia Vigorbloom | 106378, 107891, 107933 | 469 | 5288 | +6.4580 pp | 0.831880 | +0.331880 | Medium |
| Kindred Judgment | 106238, 107861 | 494 | 11846 | +5.8823 pp | 0.817480 | +0.317480 | Medium |
| Roiling Canopy | 106432, 107919 | 51 | 1700 | -7.0967 pp | 0.191536 | -0.308464 | Medium |
| Yargle, Glutton of Urborg | 106486 | 166 | 552 | -10.1410 pp | 0.200686 | -0.299314 | Medium |
| Pyre Rhymer // Molten Tide | 106323, 106324 | 314 | 5456 | -5.6057 pp | 0.211153 | -0.288847 | Medium |
| Gideon's Memorial | 106443, 107831 | 313 | 5403 | -5.5910 pp | 0.212145 | -0.287855 | Medium |
| Frostbite Pyromental | 106372, 107872 | 140 | 3013 | -5.6079 pp | 0.229453 | -0.270547 | Medium |
| Solitary Cell | 106389, 107894 | 131 | 3293 | -5.4722 pp | 0.232763 | -0.267237 | Medium |
| Essence Burn | 106312, 107908 | 248 | 845 | -7.5165 pp | 0.234374 | -0.265626 | Medium |
| Puppet Crafting | 106346, 107912 | 96 | 2113 | -5.3273 pp | 0.257680 | -0.242320 | Medium |

## Largest positive shifts

| Logical card | GrpIds | Primary n | Alternate n | Alt GIH − baseline | Q | Q − .5 | Risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Overwrite the Multiverse | 106288, 107880 | 439 | 6877 | +9.7453 pp | 0.948727 | +0.448727 | High |
| Aerid Konstrari | 106356, 107886, 107930 | 400 | 5718 | +8.6749 pp | 0.922329 | +0.422329 | High |
| Kwia Vigorbloom | 106378, 107891, 107933 | 469 | 5288 | +6.4580 pp | 0.831880 | +0.331880 | Medium |
| Kindred Judgment | 106238, 107861 | 494 | 11846 | +5.8823 pp | 0.817480 | +0.317480 | Medium |
| Proctor of Potential | 106384, 107893 | 493 | 10331 | +4.2452 pp | 0.727771 | +0.227771 | Medium |
| Avatar of Burgeoning Echoes | 106357, 107887 | 436 | 5080 | +3.9232 pp | 0.700908 | +0.200908 | Medium |
| Seasoned Cryomancer | 106264, 107863 | 491 | 6653 | +3.1411 pp | 0.664334 | +0.164334 | Low |
| Shipwreck Marsh | 106434, 107925 | 206 | 10373 | +1.8219 pp | 0.597771 | +0.097771 | Low |
| Dark Matter Manipulator | 106281, 107879 | 285 | 8323 | +1.6482 pp | 0.587456 | +0.087456 | Low |
| Theorist's Proxy | 106271, 107878 | 428 | 12049 | +1.2186 pp | 0.565812 | +0.065812 | Low |
| Enlightened Confidant | 106230, 107860 | 271 | 4204 | +1.1946 pp | 0.560056 | +0.060056 | Low |
| Ingris Stingerquill | 106374, 107890, 107932 | 265 | 3612 | +0.7936 pp | 0.539210 | +0.039210 | Low |
| Stingcaster Mage | 106326, 107868 | 303 | 4405 | +0.0988 pp | 0.504989 | +0.004989 | Low |

## Largest negative shifts

| Logical card | GrpIds | Primary n | Alternate n | Alt GIH − baseline | Q | Q − .5 | Risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Way of the Necromancer | 106484 | 336 | 2656 | -11.7654 pp | 0.037748 | -0.462252 | High |
| Flickering Hound | 106232, 107898 | 252 | 3086 | -10.6273 pp | 0.050119 | -0.449881 | High |
| Loot, the Anomaly | 106477 | 457 | 3103 | -8.1697 pp | 0.110552 | -0.389448 | High |
| Yargle, Goliath of Otaria | 106470 | 283 | 1261 | -9.6051 pp | 0.117300 | -0.382700 | High |
| Flourishing Grapple | 106336, 107911 | 178 | 689 | -11.7433 pp | 0.120554 | -0.379446 | High |
| Samut, Tyrant of Naktamun | 106465, 107841 | 103 | 2165 | -8.3495 pp | 0.121553 | -0.378447 | High |
| Codie, Ravenous Codex | 106413, 107935 | 17 | 805 | -10.3170 pp | 0.142227 | -0.357773 | High |
| Germinate Recruits | 106234, 107873 | 45 | 1207 | -8.6824 pp | 0.154667 | -0.345333 | Medium |
| Roiling Canopy | 106432, 107919 | 51 | 1700 | -7.0967 pp | 0.191536 | -0.308464 | Medium |
| Yargle, Glutton of Urborg | 106486 | 166 | 552 | -10.1410 pp | 0.200686 | -0.299314 | Medium |
| Pyre Rhymer // Molten Tide | 106323, 106324 | 314 | 5456 | -5.6057 pp | 0.211153 | -0.288847 | Medium |
| Gideon's Memorial | 106443, 107831 | 313 | 5403 | -5.5910 pp | 0.212145 | -0.287855 | Medium |
| Frostbite Pyromental | 106372, 107872 | 140 | 3013 | -5.6079 pp | 0.229453 | -0.270547 | Medium |
| Solitary Cell | 106389, 107894 | 131 | 3293 | -5.4722 pp | 0.232763 | -0.267237 | Medium |
| Essence Burn | 106312, 107908 | 248 | 845 | -7.5165 pp | 0.234374 | -0.265626 | Medium |
| Puppet Crafting | 106346, 107912 | 96 | 2113 | -5.3273 pp | 0.257680 | -0.242320 | Medium |
| Return to the Light Realms | 106245, 107862 | 145 | 1019 | -6.4008 pp | 0.258467 | -0.241533 | Medium |
| Karn, Argent Defender | 106524, 107859 | 60 | 838 | -6.5135 pp | 0.270531 | -0.229469 | Medium |
| Cruel Calculations | 106251, 107875 | 17 | 732 | -6.8386 pp | 0.271447 | -0.228553 | Medium |
| Bloodline Recollector // Ancestral Craving | 106277, 106278 | 192 | 2646 | -4.6438 pp | 0.280302 | -0.219698 | Medium |

## Recommendation

Retain the narrow FRA prior unchanged. Review the extreme shifts and lower alternate samples using future authorized evidence; do not remove a prior solely for disagreement with intuition. There are no alternate samples below 500 in this population. Codie has primary n=17 and an extreme negative shift; primary n alone does not qualify GIH when the rate is null. Highest-risk rows and all 56 logical groups are in the machine evidence. No production scoring defect was established. Untapped comparison unavailable: no legitimate reference, zero matched cards, no correlation.

[Machine evidence](docs/evidence/phase-9f4/stat-risk-review.json); [input provenance](docs/evidence/phase-9f4/stat-inputs.json).
