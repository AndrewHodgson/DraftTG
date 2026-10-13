"""Rebuild the Phase B report from immutable, already-released aggregate evidence. Never evaluate models."""
import collections,json,pathlib,statistics
from phase_b_core import verify_registration,digest
from phase_b_source import sha
OUT=pathlib.Path('docs/evidence/v2-phase-b')

def read(name):return json.loads((OUT/(name+'.json')).read_text(encoding='utf-8-sig'))
def pct(v):return 'unavailable' if v is None else f'{100*v:.4f}%'
def number(v):return f'{v:,}'
def table(headers,rows):return '\n'.join(['| '+' | '.join(headers)+' |','| '+' | '.join(['---']*len(headers))+' |']+['| '+' | '.join(str(x) for x in row)+' |' for row in rows])
def macro(rows,policy,sets,key='MAE'):
    values=collections.defaultdict(list)
    for r in rows:
        if r['set'] in sets and r['policy']==policy and r.get(key) is not None:values[r['set']].append(r[key])
    return statistics.mean(statistics.mean(v) for v in values.values())
def human(prefix,model):
    rows=read(prefix+'-human-q-ranking');n=sum(r['models'][model]['eligible'] for r in rows)
    return dict(eligible=n,top1=sum(r['models'][model]['top1']*r['models'][model]['eligible'] for r in rows)/n,
                MRR=sum(r['models'][model]['MRR']*r['models'][model]['eligible'] for r in rows)/n)

def make_summary():
    splits=verify_registration();release=read('holdout-release')
    if 'completedAt' not in release:raise ValueError('Final holdout not completed; report cannot claim final results')
    if release['selectionHash']!=digest(json.dumps(read('selection'),sort_keys=True)):raise ValueError('Released selection changed')
    if release['qualityResultSha256']!=sha(OUT/'holdout-result.json') or release['contextualResultSha256']!=sha(OUT/'holdout-production-replay.json'):
        raise ValueError('Released holdout result changed')
    q=read('qualification');datasets=q['datasets'];picks=[d['picks'] for d in datasets if d['picks']];games=[d['games'] for d in datasets if d['games']]
    d=read('report-diagnostics');s=read('selection');v=read('validation');h=read('holdout-result');t=read('training-result')
    current=h['macroMAE']['current-k500'];best=h['macroMAE'][s['selectedCandidate']]
    r=read('holdout-production-replay');metrics=r['Metrics']
    return dict(status='Research run finished; full Phase B qualification / production Q adoption blocked',
        reason='No legitimate multi-set Quick/Premier card-quality pairs; primary-only candidates fail Uldaros prior portability; selected A lowers contextual holdout human agreement',
        sets=len({x['set'] for x in datasets}),formats=sorted({x['format'] for x in datasets}),setFormatDatasets=len(datasets),
        pickDrafts=d['pickDrafts'],combinedDistinctDrafts=d['combinedDistinctDrafts'],gameDrafts=sum(x['sampledDrafts'] for x in games),
        importedPicks=sum(x['waterfall']['imported'] for x in picks),eligibleDecisions=sum(x['waterfall'].get('eligible',0) for x in picks),
        fullyEvaluatedQualityRankDecisions=human('training','A')['eligible']+human('holdout','A')['eligible'],
        identityOccurrences=sum(x['identityOccurrences'] for x in picks),resolvedOccurrences=sum(x['resolvedOccurrences'] for x in picks),
        identityResolution=sum(x['resolvedOccurrences'] for x in picks)/sum(x['identityOccurrences'] for x in picks),
        identityScope='Logical score identity; exact art/printing ambiguity remains explicit',
        calibration=[s for s,p in splits.items() if p=='Calibration'],development=[s for s,p in splits.items() if p=='Development'],holdout=[s for s,p in splits.items() if p=='Holdout'],
        selectedCandidate=s['selectedCandidate'],selectedPolicy=s['selectedPolicy'],candidateSpecifications=s['candidates'],
        developmentMAE=s['developmentMacroMAE'][s['selectedPolicy']],developmentCurrentMAE=s['developmentMacroMAE']['fixed-500'],
        holdoutMAE=best,holdoutCurrentMAE=current,absoluteImprovementPercentagePoints=100*(current-best),relativeImprovement=(current-best)/current,
        holdoutContextualEligible=metrics['contextual-pick-score-v1.6.2 | overall']['Eligible'],
        holdoutContextualTop1=metrics['contextual-pick-score-v1.6.2 | overall']['Top1'],
        holdoutCandidateContextualTop1=metrics['research-q-phase-b-v1/A | overall']['Top1'],
        qualifiedGames=sum(x['sampledGames'] for x in games),observedPrimaryBuilds=sum(x['builds'].get('observedPrimaryBuild',0) for x in games),
        provenFinalSubmittedDecks=0,crossFormatQualityPairs=0,productionChanged=False,phaseCStarted=False,
        validation=v,storageBytes=read('storage'),trainingRun=t,holdoutRun=h,
        recommendation='Keep v1.6.2 and its FRA-only alternate prior. Resolve missing-data/source-denominator compatibility and acquire permitted paired Quick/Premier evidence; use new untouched whole-set holdouts for any revised candidate. Do not implement Q or begin Phase C automatically.')

def summary():
    r=make_summary();output={k:r[k] for k in ('status','reason','sets','formats','setFormatDatasets','pickDrafts','combinedDistinctDrafts','importedPicks',
        'eligibleDecisions','identityResolution','identityScope','calibration','development','holdout','selectedPolicy','developmentMAE','developmentCurrentMAE',
        'holdoutMAE','holdoutCurrentMAE','absoluteImprovementPercentagePoints','holdoutContextualEligible','holdoutContextualTop1','holdoutCandidateContextualTop1',
        'qualifiedGames','observedPrimaryBuilds','provenFinalSubmittedDecks','crossFormatQualityPairs','productionChanged','phaseCStarted','recommendation')}
    output['candidates']=['A: shrunk baseline delta / k250','B: standardized shrunk delta / k250','C: disjoint OH/drawn decomposition / k250']
    output['tests']=dict(dotnetPassed=r['validation']['tests']['passed'],pythonPassed=r['validation']['pythonTests']['passed'],buildWarnings=0,buildErrors=0)
    output['storageGiB']=sum(r['storageBytes'].values())/1024**3
    print(json.dumps(output,indent=2))

def report():
    result=make_summary();q=read('qualification');datasets=q['datasets'];s=read('selection');v=read('validation');diag=read('report-diagnostics')
    baselines=read('training-baselines')+read('holdout-baselines');experiments=read('training-experiments');candidate=read('training-candidate-experiments');holdout=read('holdout-experiments')
    golden=read('golden-sanity');trainingReplay=read('training-production-replay');holdoutReplay=read('holdout-production-replay')
    index={(r['set'],r['format']):r for r in diag['datasets']}
    body=['# Contextual Pick Score V2 — Phase B: Set-Normalized Quality Research','']
    def section(title,text):body.extend(['## '+title,'',text,''])
    section('1. Executive conclusion',f'''**Do not adopt a production V2 Q model.** The research run and single final holdout release are finished, but full Phase B qualification is not achieved. The corpus scale target is met; legitimate multi-set Quick/Premier quality evidence is unavailable. Every primary-only candidate loses the existing Uldaros alternate prior, and selected candidate A reduces contextual holdout human agreement.

Candidate A (simple shrunk delta, k=250) improves disjoint-game holdout card MAE from {pct(result['holdoutCurrentMAE'])} to {pct(result['holdoutMAE'])}: {result['absoluteImprovementPercentagePoints']:.4f} percentage points, {pct(result['relativeImprovement'])} relative. This modest predictive gain is not evidence of better drafting or causal card power. No statistical significance or causal improvement is claimed.

Production `contextual-pick-score-v1.6.2`, its FRA-only prior, L/A/D, commitment, `pick-score-tiers-v2`, memory, localization and overlay behavior remain unchanged. The {v['baselineFilesVerified']} baseline source/fixture hashes match the Phase A freeze.

Tested build: commit `{v['testedCommit']}` plus the existing dirty working tree and Phase B changes; source fingerprint `{v['sourceFingerprint']}`. Score assembly SHA256 `{v['scoreAssemblySha256']}`. [Build/test evidence](docs/evidence/v2-phase-b/validation.json).''')
    rows=[]
    for d in datasets:
        p=d['picks'];g=d['games']
        rows.append([d['set'],d['format'],d['partition'],p['sampledDrafts'] if p else 0,p['waterfall']['imported'] if p else 0,p['waterfall'].get('eligible',0) if p else 0,g['sampledGames'] if g else 0])
    storage=result['storageBytes'];normal=next(value for key,value in storage.items() if 'normalized' in key);raw=sum(value for key,value in storage.items() if '\\raw\\' in key)
    import_speeds=[i['manifest']['RowsPerSecond'] for r in (trainingReplay,holdoutReplay) for i in r['Imports']]
    qualify_seconds=sum(d['picks']['seconds'] for d in datasets if d['picks'])
    section('2. Expanded corpus',f'''{result['sets']} sets, {result['setFormatDatasets']} set/format datasets, {', '.join(result['formats'])}; 2021–2024 release environments. {number(result['pickDrafts'])} pick-bearing drafts; {number(result['combinedDistinctDrafts'])} distinct set/format/draft groups across picks and games (overlap deduplicated), including {number(result['gameDrafts'])} game-bearing drafts. {number(result['importedPicks'])} imported decisions, {number(result['eligibleDecisions'])} conservative eligible decisions, and all {number(result['fullyEvaluatedQualityRankDecisions'])} eligible decisions evaluated for Q-only revealed-preference ranking. {number(result['qualifiedGames'])} qualified atomic/uniquely joined games. Actual frozen contextual replay is a separately identified subset of {trainingReplay['Decisions']+holdoutReplay['Decisions']:,} decisions / {trainingReplay['Drafts']+holdoutReplay['Drafts']} drafts, {trainingReplay['Metrics']['contextual-pick-score-v1.6.2 | overall']['Eligible']+result['holdoutContextualEligible']:,} ranked decisions.

{table(['Set','Format','Partition','Pick drafts','Imported','Eligible','Qualified games'],rows)}

Selection was fixed by calendar/mechanical diversity, not agreement with v1.6.2: snow/foretell, lessons/spells, dungeons, graveyard/vampires, artifacts/enchantments, three-color, domain, adventures, discovery, investigate, outlaws, typal and enchantment environments. Fast/slow/power labels were not fitted outcomes or selection filters. KHM has games but no Parquet picks; it remains registered, with zero pick rows. VOW Quick has picks but no permitted game table. No Traditional-as-Quick substitution.

Modern draft samples use the lowest outcome-independent SHA256 whole-draft IDs (700/dataset) over every source row group; modern games use 8,000 such drafts/dataset. Source incomplete/abandoned drafts stay represented; eligible picks weight observable decisions, not each drafter equally. The four 2021 game sources use original 20,000-row prefixes because Parquet lost distinguishing game keys. Those prefixes are not random, not full-format maturity, and can bias calibration. Public 17Lands users are a selected population. “Full” later means the entire qualified evidence sample, never the entire released dump.

Data attribution: [17Lands public datasets](https://www.17lands.com/public_datasets), transformed draft/newer-game tables from [TrevorJS/17lands-corpus](https://huggingface.co/datasets/TrevorJS/17lands-corpus/tree/766a56bbdb5ebe1ed4c0d3441df694c81ec9f774), pinned revision `766a56bbdb5ebe1ed4c0d3441df694c81ec9f774`, CC-BY-4.0 per pinned source README. Only bulk picks/pack/pool/games/game_cards and card metadata were acquired; no targets, embeddings, replay model outputs or private endpoints. All 72 pinned file bytes were SHA256-verified. Five original CSV spot checks covered 5,000 picks with zero missing rows or pack/pool/picked-name differences; this is not an exhaustive ETL proof. [Sources/hashes](docs/evidence/v2-phase-b/source-provenance.json), [source audit](docs/evidence/v2-phase-b/source-audit.json).

Raw Phase B disk: {raw/1024**3:.3f} GiB; normalized including replay stores: {normal/1024**2:.2f} MiB; all retained Phase B data/dependency/replay traces: {sum(storage.values())/1024**3:.3f} GiB. Individual source files are compressed Parquet; readers filter 131,072-row batches. Canonical JSONL/gzip reuses `HistoricalDraftDecision`, not a second scoring schema. Full contextual replay was bounded to eight preregistered hash-selected drafts/dataset because optimizer work and detailed JSONL scale much more heavily than Q-only ranking. Row-level data stays ignored.

Production replay: training {trainingReplay['Seconds']:.3f}s / {trainingReplay['DecisionsPerSecond']:.2f} decisions/s / {trainingReplay['PeakWorkingSetBytes']/1024**2:.2f} MiB peak; holdout {holdoutReplay['Seconds']:.3f}s / {holdoutReplay['DecisionsPerSecond']:.2f} decisions/s / {holdoutReplay['PeakWorkingSetBytes']/1024**2:.2f} MiB. Holdout quality processing {result['holdoutRun']['seconds']:.3f}s / {result['holdoutRun']['peakWorkingSetBytes']/1024**2:.2f} MiB peak (excludes subsequent contextual replay). Import manifests preserve parse/store rows/s, peak and hash-cache keys; full process timings include startup/preflight. A completed AFR import was reused after the logging fix with matching raw/catalog/split/options hashes. Training analysis/export finished, but the final Win32 telemetry write failed; wall time/peak are explicitly unavailable. The supported launcher supplies the 64-bit HANDLE prototype. Selection was not rerun. Existing older Phase A intermediate outputs remain ignored; no user files were cleaned up. [Storage](docs/evidence/v2-phase-b/storage.json), [run metadata](docs/evidence/v2-phase-b/training-result.json).''')
    body.extend([f"Canonical replay parse/store speed: {min(import_speeds):.2f}–{max(import_speeds):.2f} rows/s, excluding SHA preflight. Recorded draft qualification stages total {qualify_seconds:.3f}s ({result['importedPicks']/qualify_seconds:.2f} decisions/s); this includes source long-table scanning/joins and canonical gzip output, excludes metadata loading and separately rejected old-game attempts.",''])
    hashes=read('preregistration-hashes')
    section('3. Frozen split manifest',f'''Registered and committed before candidate analysis as **`d2cb723`**. Manifest SHA256 `{hashes['SplitSha256']}`; plan SHA256 `{hashes['PlanSha256']}`.

- Calibration: {', '.join(result['calibration'])}.
- Development: {', '.join(result['development'])}.
- Untouched final holdout: {', '.join(result['holdout'])}.

OTJ was already viewed in Phase A and was deliberately burned into development. No pick-level set split. Within each dataset, SHA256(`DraftTG-phase-b-v1/17LandsPublic/set/format/draft_id`) chooses disjoint evidence and target draft groups; all games in a draft stay together. Sampling hashes do not use outcomes. Qualification handled holdout schema, identity, count and missingness only; candidate performance/distributions were locked until selection.

Parameters were fitted on calibration; fitted fixed/adaptive/EB/decomposition families were chosen on development using set-macro card MAE averaged equally over six levels. Selected A/k250 was frozen at `{s['frozenAt']}`. The [original analytical selection](docs/evidence/v2-phase-b/selection-analysis-freeze.json) is preserved byte-for-byte. A later CLI progress/JSON parsing correction changed only the execution fingerprint; [correction evidence](docs/evidence/v2-phase-b/execution-correction.json) and a deep equality check confirm all formulas, parameters, missing rules and metrics unchanged. [Current selection](docs/evidence/v2-phase-b/selection.json).

The exclusive final release lock was consumed once before reading holdout outcomes and completed at `{read('holdout-release')['completedAt']}`. Both BLB and DSK were evaluated together, including the contextual subset, in that release. No post-holdout retuning. A consumed or failed release cannot be retried. BLB/DSK are now spent holdouts and must not be called untouched in another tuning study.''')
    identity=read('identity-summary');categories=collections.Counter()
    for d in datasets:
        if d['picks']:categories.update(d['picks']['identityCategories'])
    section('4. Identity coverage',f'''{number(result['resolvedOccurrences'])}/{number(result['identityOccurrences'])} offered/pool/label occurrences resolve: **{pct(result['identityResolution'])} logical score identity**, including basics. Score-relevant nonbasic coverage is also 100%. This improves Phase A’s 95.3959%, but is not 100% exact printing identification.

{identity['consensusLogicalIdentities']} set/source-name identities use explicit research logical IDs because all declared Arena printing candidates share one Oracle ID and identical gameplay fields/faces. Every original printing candidate remains in mapping provenance; no printing is selected arbitrarily. Single printing matches stay exact. Mixed printing rarity, where present, is not a quality input; neutral decoder metadata is not proof of original printing rarity. Rebalanced A- names stay separate, tokens/non-draft objects decline, and generic cross-set name fallback is prohibited. MKM List exceptions are limited to [publisher-enumerated Arena List names](https://magic.wizards.com/en/news/announcements/whats-new-on-the-list-for-murders-at-karlov-manor), including the documented Arena swaps; consensus is still required.

{table(['Observed identity category','Occurrences'],[(k,number(n)) for k,n in sorted(categories.items())])}

No unresolved vocabulary entries remain in this sample. DFC front names, split/adventure faces, basics, art variants, explicit sheets/guests and MKM List are classified. Token, genuinely conflicting Oracle/gameplay, rebalanced-name substitution, collector discrepancy, unavailable metadata and malformed-schema declines remain supported categories; zero occurrences does not prove those failure modes impossible. Collector/printing identity cannot be recovered from a name-only source. Helper, owned Scryfall cache and policy hashes are in [identity evidence](docs/evidence/v2-phase-b/identity-summary.json); the full mapping/candidate list remains in ignored normalized storage. Legacy strict Phase A resolver behavior is unchanged; the research catalog uses explicit proven logical identities.''')
    stages=['imported','validSchema','validOfferedPack','identitiesResolved','scoreInputsAvailable','atLeastTwoChoices','eligible'];total={k:sum(d['picks']['waterfall'].get(k,0) for d in datasets if d['picks']) for k in stages}
    waterfall=[]
    for d in datasets:
        if d['picks']:
            w=d['picks']['waterfall'];waterfall.append([d['set'],d['format']]+[number(w.get(k,0)) for k in stages]+[pct(w.get('eligible',0)/w['imported'])])
    section('5. Exclusion waterfall',f'''{table(['Set','Format','Imported','Schema','Pack','Identity','Inputs','≥2','Eligible','Eligible %'],waterfall)}

{table(['Stage','Remaining','% of imported'],[(k,number(total[k]),pct(total[k]/total['imported'])) for k in stages])}

Schema/pack/identity exclusions are zero here. Missing exact-format game evidence excludes all 29,348 VOW Quick decisions; supported-domain pick coordinates exclude historical pick 15 in 15-pick sets; singleton packs are excluded from comparative ranking. Source pool completeness requires count==global source index; unresolved pool cards decline eligibility. Those final reasons are disjoint in the waterfall. Per-set exact reason counts are in [qualification](docs/evidence/v2-phase-b/qualification.json). “Inputs available” means an exact-format usable environment plus complete identity/inventory and supported coordinates, not that every card has measured GIH; missing individual GIH remains explicit neutral Q. Actual contextual availability is separately verified in the 4,647-decision production subset. No truncated-pack rankings or hidden outcome filtering.''')
    buildrows=[]
    for d in datasets:
        if d['games']:
            g=d['games'];b=g['builds'];buildrows.append([d['set'],g['sampledGames'],b.get('observedConfigurations',0),b.get('gameDecksExactly40',0),b.get('observedPrimaryBuild',0),b.get('ambiguousModal40',0),b.get('openingObservedAndModalAgree',0)])
    section('6. Outcome/build coverage',f'''**{number(result['observedPrimaryBuilds'])} ObservedPrimaryBuilds**, versus 960 observed builds in the pilot. Definition: one uniquely most frequent observed main-deck configuration among exact 40-card game decks per draft. Tied modal configurations decline. Configuration identity uses every card quantity, not just build_index. This is stronger evidence of the commonly played main deck, not proof of the final submitted draft deck.

{table(['Set','Games','Configurations','40-card game decks','Primary builds','Modal ties','Opening-key/modal agreement'],buildrows)}

Individual observed game decks and sideboard counts remain available. Unique modal 40, opening-key observed deck, post-sideboard build, inferred primary build and final submitted deck are distinct. Match 1/game 1 gives an opening-key observation where available; old game numbering may reset, and missing initial games prevent a true pre-sideboard claim. **Zero proven final submitted main decks/sideboards**; no modal sideboard/full pool is invented. Source pick maindeck/sideboard rates and terminal supported Bo1 records stay analysis labels, never scoring inputs. Nonterminal records are reported records, not completed events.

Older Parquet draft/build/game joins were materially ambiguous; [qualification](docs/evidence/v2-phase-b/qualification.json) preserves rejected-key counts. Original wide CSV rows keep result and card counts atomic, with source row ordinal; they are not deduplicated by an ambiguous game number. Newer draft/build/match/game keys were unique in the sampled corpus. Observed-build association uses unique modal builds with ≥3 observed games, high observed game WR≥.6 vs lower; presence counts a card once per build, not copies. It is descriptive, selection-biased, and does not use final draft wins as a simplistic card-quality target. [Associations](docs/evidence/v2-phase-b/training-build-association.json).''')
    pos=read('position-audit')
    section('7. Derived seen/taken-position statistics',f'''{sum(r['cards'] for r in pos['datasets']):,} set/format/card feature rows. Each source zero-based pick becomes a **1-based pack-local pick**; global index is a separate source coordinate. Seen means/counts include offered multiplicity; Taken means/counts include actual picked names. Mean, median, complete frequency distribution, counts, and pack-specific Seen distributions exist in ignored `*.positions.json`. Portable means/medians/counts/CDF descriptors are in [derived positions](docs/evidence/v2-phase-b/derived-positions.json).

The audit checks histogram count conservation, weighted mean recomputation, positive 1-based indexes and pack-specific totals for every row. The five original-source pack/pool checks additionally validate underlying offered counts. Set normalization uses empirical midrank percentiles over per-card means and `SeenLaterThanSetExpectation = card_seen_mean - unweighted_set_card_seen_mean`; this expectation is explicit, not a new lane formula. Taken percentile is separate. Low Seen/Taken counts remain visible, not imputed.

These are `DerivedAverageSeenAt` / `DerivedAverageTakenAt`, not official ALSA/ATA. There is no licensed overlapping published aggregate in this corpus; correlation/error against official ALSA/ATA is **unavailable**, not zero. Current aggregate API explicitly disallows outside use, so it was not harvested into research. No position signal enters Q, production L, A, D or commitment. [Position audit](docs/evidence/v2-phase-b/position-audit.json).''')
    section('8. Available card-performance fields','''Source long game-card fields: `opening_hand`, `drawn`, `tutored`, `deck`, `sideboard`; game fields: `won`, source draft/build/match/game keys, rank, and `main_colors`/`splash_colors` where present. Original wide sources use those prefixes with each source card name. Draft sources supply `pack_card`, `pool`, `pick`, `pack_number`, `pick_number`, observed pick-maindeck/sideboard rates and reported event record/lifetime buckets.

| Research signal | Exact denominator / meaning |
| --- | --- |
| Game-presence GIH | deck>0 and (opening>0 OR drawn>0), at most one card/game outcome |
| GIH count | count of those unique card/game presences, not sum of copies |
| Copy-weighted GIH audit | sum(opening+drawn) and outcome-weighted same sum; not interchangeable |
| GP presence | deck>0, one card/game; separate copy-GP audit uses deck copies |
| OH presence | opening>0; may overlap drawn presence |
| Drawn presence | drawn>0; may overlap OH presence |
| Drawn-without-OH | drawn>0 AND opening==0, disjoint from OH-presence stratum |
| Never-seen style | deck>0 AND opening==0 AND drawn==0; tutored-only is not added silently |
| Derived IWD | game-presence GIH WR minus Never-seen WR; not declared official provider IWD |
| Pick/Seen counts | source offered/picked occurrences; not game counts |
| Color/rank strata | reported main_colors and source game rank; descriptive post-draft, not intrinsic inputs |

Only nonbasic resolved deck cards enter Q baseline/research; sideboard-only, basic-land and unresolved rows are counted as exclusions. Rates/counts are separately exported for each field. OH+Drawn counts can overlap, so marginal aggregate rates alone cannot reconstruct disjoint game-presence strata. Tutoring is audited but is not silently equated with drawing. Provider GIH/Games/count definitions must be bridged before deploying a k trained on unique presences. [Training fields](docs/evidence/v2-phase-b/training-card-features.json), [holdout fields](docs/evidence/v2-phase-b/holdout-card-features.json).''')
    baserows=[]
    for b in baselines:
        a=b['baseline'];unique=index[b['set'],b['format']]
        baserows.append([b['set'],a['cards'],number(a['observations']),pct(a['weighted']),pct(a['unweighted']),pct(unique['uniqueGameWinRate']),pct(a['spread']),pct(a['quantiles']['0.05']),pct(a['quantiles']['0.95'])])
    section('9. Set/format baselines',table(['Set','Cards','Card/game GIH observations','GIH weighted','Card unweighted','Unique-game WR','Card-rate SD','P05','P95'],baserows)+'''

Weighted GIH counts each observed card/game once, so a game can contribute several cards. Unweighted GIH averages card rates equally. Unique-game WR counts each game once. Copy-weighted GIH counts each opening/drawn copy; these are different denominators and have separate tables. Baselines and SD for predictions are computed from **evidence drafts only**, never target outcomes. Full qualified evidence+target baselines above are descriptive released-corpus distributions, not fitting inputs. Quantiles P05/P25/P50/P75/P95 and rarity distributions/counts are in [baselines](docs/evidence/v2-phase-b/training-baselines.json) and [holdout baselines](docs/evidence/v2-phase-b/holdout-baselines.json). [Unique-game/rank/color diagnostics](docs/evidence/v2-phase-b/report-diagnostics.json).''')
    section('10. Cross-set GIH distributions',f'''Observed GIH-weighted baselines range from {pct(min(b['baseline']['weighted'] for b in baselines))} to {pct(max(b['baseline']['weighted'] for b in baselines))}. A 58% card is therefore not the same relative strength in every environment. Unweighted card means can differ materially from occurrence-weighted baselines because rare/low-played cards receive much more influence when weighted equally.

Unweighted raw card-rate SD ranges from {pct(min(b['baseline']['spread'] for b in baselines))} to {pct(max(b['baseline']['spread'] for b in baselines))}; low-count extremes inflate it, notably in larger/bonus-sheet vocabularies. A percentile/CDF diagnostic was prepared and measured on calibration/development only after selection, against the common mature delta-Q reference; it preserves monotonic ranking but discards absolute effect size and needs a separate early-data distribution calibration. It was not selected as a fourth candidate or fitted on holdouts. Rarity appears only in stratification/sample diagnostics, never a quality bonus.

Win rates reflect player/deck/card selection, color strength, play decisions, draw timing and survivorship of decks people submit. Unique-game rank/main-color strata demonstrate observational dependence; unknown/lifetime buckets have uncertain temporal scope. No causal card-power claim, archetype clustering, synergy bonus or A formula follows from these diagnostics. Copy vs presence differences are retained per card and must not be washed out by a common name “GIH”.''')
    gridrows=[[f'fixed k={k}',pct(s['calibrationMacroMAE']['fixed-'+str(k)]),pct(s['developmentMacroMAE']['fixed-'+str(k)])] for k in (0,50,100,250,500,1000,2000)]
    gridrows += [[name,pct(s['calibrationMacroMAE'][name]),pct(s['developmentMacroMAE'][name])] for name in ('adaptive-250','adaptive-500','adaptive-1000','EB-calibration-median','decomposition-250')]
    section('11. Shrinkage experiments',f'''Primary metric: unweighted card MAE, first averaged equally over n=50/100/250/500/1000/full, then equally over whole sets. Weighted MAE, RMSE, Brier, fixed-bin calibration/ECE, bias, rank correlation and ≥2/5pp overestimation are secondary outputs. Target card rates require ≥100 observations; evidence requires ≥50. Targets and evidence are disjoint drafts.

{table(['Policy','Calibration MAE','Development MAE'],gridrows)}

Fixed k=250 wins calibration. Adaptive k=k0*sqrt(500/n) fits k0=250 but loses development; the simple moment EB estimate subtracts average binomial sampling variance from between-card variance, caps k to [0,2000], and freezes the calibration median at **{s['EBcalibrationMedian']:.6f}**. EB loses the fixed policy on development. The moment estimator is sensitive to small-count card variance; it is not a causal variance estimate. No set-specific holdout k was fitted. **Retain production k500:** observed improvement on unique-presence samples does not authorize halving the prior on differently counted provider data. [All grid/level results](docs/evidence/v2-phase-b/training-experiments.json).''')
    section('12. Multi-stat experiments',f'''Candidate C divides GIH games into mutually exclusive OH-presence and drawn-without-OH strata, shrinks each toward the evidence baseline with k/2, and mixes using their observed frequencies. Each outcome enters once. k=250 fits calibration; development MAE {pct(s['developmentMacroMAE']['decomposition-250'])} vs GIH-only {pct(s['developmentMacroMAE']['fixed-250'])}; holdout C {pct(result['holdoutRun']['macroMAE']['C'])} vs A {pct(result['holdoutMAE'])}. **GIH alone is slightly better here.**

OH+Drawn marginal rates overlap; GP is a mixture of seen/not-seen outcomes; IWD is an algebraic difference of already shared outcomes. They were audited, but adding GIH/OH/Drawn/GP/IWD as independent weighted “votes” was rejected as redundant. We did not fit a GP/IWD black-box combination or pretend to exhaust all distinct-signal models. The tested small decomposition does not justify extra complexity. Color/rank/build associations remain descriptive; conditional archetype strength belongs in later A research, not a rarity or intrinsic-Q bonus.''')
    section('13. Cross-format experiments','''**Zero legitimate paired Quick/Premier card-quality observations; no reliability estimate or successful multi-set experiment.** Only VOW Quick draft rows exist in the permitted bulk corpus, and its game table is absent. Public Premier/Traditional aggregates in the third-party repository are not a substitute for Quick, and undocumented provider-derived targets were not used. The current aggregate API carries an explicit outside-use prohibition; no such aggregate was imported into research.

The experiment contract supports same-set, different-format name/identity pairing; tests reject cross-set and same-format pairing. Planned comparators are neutral Q=.5, v1.6.1-style .9 reliability/k500 alternate prior, and generalized global/direction/sample reliability. Correlation, baseline offsets, residuals, GIH/Q MAE, sign accuracy and ≥2/5pp overestimation are **unavailable**, not zero. No direction-specific/global/set-specific/sample reliability is identified. Production FRA Quick→Premier .9/k500 remains narrow and unchanged. [Explicit blocked experiment](docs/evidence/v2-phase-b/cross-format-experiments.json). This Phase B success criterion is not met; permitted paired data is required before a general prior proposal.''')
    levelrows=[]
    for level in ('50','100','250','500','1000','full'):
        rows=[r for r in holdout if r['level']==level]
        levelrows.append([level,pct(macro(rows,'current-k500',result['holdout'])),pct(macro(rows,'A',result['holdout'])),f"{macro(rows,'B',result['holdout'],'QMAE'):.6f}",pct(macro(rows,'C',result['holdout'])),sum(r['cards'] for r in rows if r['policy']=='A'),sum(r['fullySupportedCards'] for r in rows if r['policy']=='A')])
    section('14. Sample-size simulation',table(['Requested n','Current MAE','A MAE','B Q-MAE','C MAE','Target-qualified cards','Actually reached n'],levelrows)+'''

Deterministic, nested card/game samples are ordered by SHA256(seed/sample/set/format/card/game-key); selection does not inspect outcomes. Requested n can exceed available observations, so actual minimum/median and fully supported counts are reported, not extrapolated. “Full” is all evidence-group observations in the bounded corpus. Target rates use the other draft group; they remain noisy mature-sample estimates. This is **sample-size simulation, not chronological backtest**, not full-dump/all-time performance. No exact historical statistics availability dates are asserted. Repeated cards in one game count once; all games from one draft stay on one side. [Final sparse metrics](docs/evidence/v2-phase-b/holdout-experiments.json).''')
    body.extend(['Environment baseline and spread use the full evidence group at every n; only the focal card sample is reduced. This models sparse card evidence with an established environment, not jointly uncertain first-week card rates/baselines/spreads. QMAE is an absolute error on the 0–1 Q scale, not win-rate percentage points.',''])
    section('15. Candidate Q specifications','''All three are `research-q-phase-b-v1`, offline scalar Q in (0,1), neutral .5 when required usable exact-set/format evidence is missing. Evidence provenance: public qualified game-presence observations; no rarity/position/player/deck-win feature. Baseline b is evidence-group card/game-weighted GIH; counts are unique card/game presences. No generalized alternate prior; this missing policy is a demonstrated portability failure for Uldaros.

| Candidate | Inputs and formula | Early / mature behavior |
| --- | --- | --- |
| A | p,n,b; p*= (n*p+250*b)/(n+250); Q=SoftLimit(.5+(p*-b)/.16,.35) | Shrinks sparse estimates toward neutral; converges toward observed relative rate |
| B | Same p*,b plus evidence-group unweighted card-rate SD; Q=SoftLimit(.5+(p*-b)/(4*max(.005,SD)),.35) | Normalizes set spread; noisy low-count extremes can dilute/amplify quality magnitude |
| C | Per-game disjoint OH vs drawn-without-OH outcomes; shrink each with k/2=125 and observed-frequency mix, then A delta normalization | Sparse strata are noisier; missing stratum has zero mixture mass; aggregate marginal rates alone cannot supply the required decomposition |

SoftLimit is linear within .5±.35 and asymptotically approaches 0/1 outside, with the frozen quality soft-limit shape. Format handling is exact set/format only. Missing baseline declines prediction rather than inventing an environment. QMAE uses a common mature delta-Q reference; B’s rate predictions equal A but its Q magnitude differs. Machine specifications and exact selected policy are in [selection](docs/evidence/v2-phase-b/selection.json). These are not production V2 weights/models.''')
    calrows=[]
    for name in ('current-k500','A','B','C'):calrows.append([name,pct(macro(candidate,name,result['calibration'])),f"{macro(candidate,name,result['calibration'],'QMAE'):.6f}"])
    section('16. Calibration results',table(['Model','Card MAE','Common-reference QMAE'],calrows)+'''

Nine registered calibration sets, including KHM games without picks. Per-set card counts, evidence/target counts, actual sparse sample sizes, metrics and exclusions are exported. Fixed/adaptive/decomposition k fits and EB calibration median use only these sets. Human revealed-preference agreement and stronger-build presence are secondary; known golden cards were not calibration targets. [Candidate experiments](docs/evidence/v2-phase-b/training-candidate-experiments.json).''')
    devrows=[]
    for name in ('current-k500','A','B','C'):devrows.append([name,pct(macro(candidate,name,result['development'])),f"{macro(candidate,name,result['development'],'QMAE'):.6f}"])
    section('17. Development results',table(['Model','Card MAE','Common-reference QMAE'],devrows)+f'''

LCI/MKM/OTJ choose among calibration-fitted families. A/k250 wins the preregistered primary objective; B shares identical probability predictions and loses the complexity tie, while C adds no improvement. The selected MAE is {pct(result['developmentMAE'])} vs current {pct(result['developmentCurrentMAE'])}. No choice was made from BLB/DSK performance. Q-only ranking on all eligible decisions and the contextual subset are separately exported; secondary agreement did not replace the primary objective.''')
    holdrows=[]
    for setcode in result['holdout']:
        for model in ('current-k500','A','B','C'):
            rows=[r for r in holdout if r['set']==setcode and r['policy']==model]
            holdrows.append([setcode,model,pct(statistics.mean(r['MAE'] for r in rows)),f"{statistics.mean(r['QMAE'] for r in rows):.6f}",pct(statistics.mean(r['overestimate5pp'] for r in rows)),pct(statistics.mean(r['signAccuracy'] for r in rows))])
    section('18. Final untouched holdout results',table(['Set','Model','Six-level MAE','QMAE','≥5pp overestimate','Sign accuracy'],holdrows)+f'''

Final set-macro MAE: current {pct(result['holdoutCurrentMAE'])}, A/B {pct(result['holdoutMAE'])}, C {pct(result['holdoutRun']['macroMAE']['C'])}. All inputs, targets, missingness/exclusions and sample-level behavior are traceable by set/source/normalized hash. {result['holdoutRun']['cardCount']} observed card rows; {result['holdoutRun']['evidenceGames']:,} evidence games and {result['holdoutRun']['targetGames']:,} target games. Missing card GIH uses neutral Q for pick-ranking; prediction metrics require evidence≥50/target≥100 and do not score nonexistent measurements. Q-only ranking includes {human('holdout','A')['eligible']:,} eligible decisions; contextual replay includes 648 decisions/16 drafts, {result['holdoutContextualEligible']} eligible.

The [single-use release](docs/evidence/v2-phase-b/holdout-release.json) records selected-input hash and final result hashes. No re-selection or retuning followed. Largest contextual regressions and source coordinates are in [holdout contextual comparisons](docs/evidence/v2-phase-b/holdout-production-replay.json).''')
    rankrows=[]
    for prefix,replay in [('training',trainingReplay),('holdout',holdoutReplay)]:
        for model in ('currentQ','A','B','C'):
            pure=human(prefix,model);name='contextual-pick-score-v1.6.2' if model=='currentQ' else 'research-q-phase-b-v1/'+model
            context=replay['Metrics'][name+' | overall'];rankrows.append([prefix,model,pure['eligible'],pct(pure['top1']),context['Eligible'],pct(context['Top1']),f"{context['MeanReciprocalRank']:.6f}"])
    comp=holdoutReplay['Comparisons'][0]
    section('19. Current v1.6.2 Q comparison',table(['Partition','Model','Q-only N','Q-only Top1','Contextual N','Contextual Top1','Contextual MRR'],rankrows)+f'''

The offline ablation uses the **actual production v1.6.2 result once**, substitutes only Q, and retains L/A/D, weights, source evidence tie breaks, deck optimizer result, commitment and already-computed R. Calibration/ranking reuses the production calibration function. Holding R fixed also prevents experimental Q from changing the quality-tail commitment calculation; this is an isolation experiment, not a fully rerun V2 engine.

Selected A’s contextual holdout Top1 declines from {pct(result['holdoutContextualTop1'])} to {pct(result['holdoutCandidateContextualTop1'])}. It changes {comp['RankingChanged']} orderings, improves actual-pick rank in {comp['ActualRankImproved']}, regresses in {comp['ActualRankRegressed']}. Human pick preference is not correctness; nevertheless this regression and the golden failure preclude adoption. Pure Q ranking improves modestly across the large corpus, while contextual composition can behave differently. Fixed-500 probability/Q is the offline current-Q comparator; production itself is unchanged. No unsupported claim of draft win improvement.''')
    sanityrows=[]
    for r in golden['P1P8']:
        for c,value in r['candidates'].items():sanityrows.append([r['name'],c,f"{value['Q']:.9f}",f"{value['R']:.9f}",f"{value['exactScore']:.6f}",value['rank']])
    section('20. Real regression checks',f'''The full suite includes **{v['goldenTestsInFullSuite']} frozen golden tests**, all passing. Production Uldaros stays Q={golden['Uldaros']['productionQ']:.12f}, exact score {golden['Uldaros']['productionExact']:.9f} (48 EST). The research primary-only missing rule makes **A/B/C Q=.5 and score 25**: an explicit **portability FAIL**, not a reason to retune on golden targets or holdouts. Its existing FRA-only prior is preserved in production.

Production Unsummon retains C=.9446064139941691, P=0, R=.10262390670553934, Q=.5786468825336437 and exact score 25.328185285150624, behind Arcane Amphisbaena at 25.501120969070218. The Q-only sensitivity keeps the same commitment/relevance:

{table(['Card','Candidate','Raw Q','Frozen R','Exact score','Pack rank'],sanityrows)}

A/B numbers here illustrate transporting the existing authorized fixture’s published GIH/count into the fitted normalization. Provider copy vs presence semantics are not proven equivalent, so these are sanity sensitivities, not new quality calibration evidence. C requires disjoint game strata unavailable in marginal aggregate fixtures and therefore declines to neutral. No new provider request, live session or golden-target fitting occurred. The recomposition audit exactly reproduces frozen production scores. [Golden sanity](docs/evidence/v2-phase-b/golden-sanity.json).''')
    section('21. Failure modes',f'''1. Multi-set Quick/Premier public game pairs unavailable: cross-format reliability and hold-one-format-out metrics remain unqualified.
2. Primary-only missing rule loses Uldaros’s narrow alternate prior; all candidates fail production portability.
3. A improves card prediction slightly but worsens contextual holdout revealed preference; improvement is not causal drafting benefit.
4. Card-game presence and copy-weighted provider counts differ; learned k250 cannot be transplanted silently.
5. 2021 original game sources are bounded prefixes; modern samples are whole-draft hash samples, not full all-time datasets. Selection/player/deck/color bias remains.
6. Target card rates are noisy; samples across cards share games. No independent-card confidence interval, exact chronology or statistical significance is claimed.
7. Low-count extremes inflate unweighted set SD; B’s normalized range changes contextual influence despite fixed component weights. Percentiles lose effect-size magnitude.
8. Printing-specific art/collector identities and potentially printing-specific rarity remain unresolved where logical consensus is used; no first-match printing claim.
9. Modal observed builds are not final submitted or proven pre-sideboard decks; incomplete game histories and modal ties decline.
10. Historical pick15/stage mismatch remains frozen; detailed source coordinates and exclusions stay visible. Missing pair/trophy stats and Arena slot order remain baseline limitations.
11. Training metadata write had a Win32 HANDLE prototype failure after analysis/selection completed; no time/memory was fabricated and no selection rerun. CLI import progress/JSON parsing was corrected before holdout, with immutable original selection and unchanged analytical parameters.

Validation: `dotnet build DraftTG.sln` passed, **0 warnings/errors**; `dotnet test DraftTG.sln --no-build` passed **{v['tests']['passed']:,}**, zero failed/skipped; **{v['pythonTests']['passed']}** targeted Python tests passed. Two added C# tests protect real Q-only substitution and duplicate/version rejection; ten grouped Python tests cover byte-level split/production freeze, whole-set isolation, weighting/shrinkage, position indexing, same-set format pairs, neutral missing data, outcome-independent deterministic samples, disjoint decomposition, single-use holdout lock and logical identity consensus/A- scope. No synthetic case inflation. [Validation](docs/evidence/v2-phase-b/validation.json).''')
    section('22. Recommendation for Phase C / V2 Q implementation','''**Retain production v1.6.2, k500 and the narrow FRA alternate prior. No winning candidate is installed. Phase C is not started.** Keep A/k250 as the simplest promising *unique-game-presence research* policy, not an approved production definition.

Next authorized work should resolve quality-evidence compatibility: preserve the existing successful missing-primary prior contract, establish provider copy/presence count transport, obtain permitted same-set Quick/Premier evidence across multiple sets, and test the revised contract on new whole-set holdouts. BLB/DSK are already spent. Only after that review should V2 Q integration be considered. Derived position descriptors may support separately authorized Phase C lane research; they do not justify a lane formula or automatic architecture change now.

Reproduce the original study pipeline on a fresh, separately identified research workspace with the same input hashes; published selection/final-release files intentionally prevent retuning or a second “untouched” release in this workspace. Do not delete/reset the consumed lock. The normal commands here are `summary` and `report`, which only read released aggregates.

```powershell
Set-Location 'C:\\Users\\Graphics0\\Documents\\Projects\\DraftTG'
python -m pip install --target .\\data\\historical\\cache\\phase-b\\python pyarrow==26.0.0
dotnet build DraftTG.sln
# Initial study only; acquisition uses public bulk sources and the owned Scryfall cache.
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b.py acquire
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b.py qualify
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b.py audit
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b.py train
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b.py replay
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b_golden.py
# Exactly one final release, after frozen selection; refuses an existing consumed lock.
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b.py final
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b_diagnostics.py
python .\\tools\\DraftTG.HistoricalEvaluation\\test_phase_b.py
dotnet test DraftTG.sln --no-build
# Safe after the published release: no model evaluation or parameter changes.
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b.py report
python .\\tools\\DraftTG.HistoricalEvaluation\\phase_b.py summary
```

`acquire` verifies pinned LFS SHA256 and caps data at 6 GiB; the original wide fallback reuses Phase A’s bounded public sampler. It requires the existing public card helper and owned Scryfall cache (hashes in identity evidence). `qualify` reuses cached dataset results; canonical replay imports reuse the existing store’s strict hash cache. Supported entry point `phase_b.py` configures Windows process telemetry without modifying analytical code. `evaluate --research-q FILE` on the existing C# evaluator is the labeled experimental Q-only ablation. Baselines/shrinkage/candidate metrics are produced by `train`; missing paired-data cross-format results remain explicit. Small source/feature/metric/specification outputs are under `docs/evidence/v2-phase-b/`; raw rows, full histogram/build mappings, SQLite and detailed occurrence traces remain ignored under `data/historical/`.

Data-derived metrics are attributable by set/format/card/count plus source file/normalized SHA and filtering rules. Evidence provenance distinguishes original atomic CSV from pinned Parquet transformations. A fresh verification run is not another newly untouched holdout study and must not retune using these released outcomes.''')
    target=pathlib.Path('V2_PHASE_B_SET_NORMALIZED_QUALITY_RESEARCH.md');target.write_text('\n'.join(body),encoding='utf-8')
    (OUT/'final-summary.json').write_text(json.dumps(result,indent=2));print(target)

if __name__=='__main__':report()
