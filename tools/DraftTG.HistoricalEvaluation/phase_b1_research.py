"""Documented copy/draw units; whole-draft clusters, frozen calibration, no holdout reads."""
import bisect,collections,dataclasses,gzip,json,math,os,pathlib,statistics,sys,time
from phase_b1_core import *
sys.path.insert(0,'data/historical/cache/phase-b/python')
import pyarrow.parquet as pq
OLD=pathlib.Path('data/historical/normalized/phase-b-v1')

def provider_audit():
    guard(); cache=pathlib.Path(os.environ['LOCALAPPDATA'])/'DraftTG/limited-data/17lands'
    entries=[];snapshots=pathlib.Path('data/historical/cache/phase-b1/owned');snapshots.mkdir(parents=True,exist_ok=True)
    for file in sorted(cache.glob('*.json')):
        value=load(file);rows=value['Payload']['data'];target=snapshots/file.name
        if not target.exists():target.write_bytes(file.read_bytes())
        if sha(file)!=sha(target):raise ValueError('Owned cache changed since audit; explicit new cohort required')
        entries.append(dict(file=file.name,sha256=sha(file),bytes=file.stat().st_size,set=value['Expansion'],format=value['SourceFormat'],period=value['TimePeriod'],
            fetchedAt=value['FetchedAt'],rows=len(rows),countIdentityMatches=sum(r.get('ever_drawn_game_count',0)==r.get('opening_hand_game_count',0)+r.get('drawn_game_count',0) for r in rows)))
    semantics=dict(documentation='https://www.17lands.com/metrics_definitions',auditedProductionPath=[
        'DraftTG.Data/SeventeenLandsCardRatingsClient.cs: ever_drawn_win_rate -> EverDrawnWinRate; ever_drawn_game_count -> EverDrawnGameCount',
        'LimitedStatisticsMapper.Attach -> GameInHandWinRate/GameInHandGameCount unchanged',
        'StatisticalRecommendationEngine: occurrence-weighted environment baseline; (n*p+500*b)/(n+500)',
        'SparsePrimaryQualityPrior: FRA Quick <- Premier only, .9*n/(n+500) alternate delta, primary wins',
        'ContextualPickScore: Q=soft-limit(.5+(adjustedRate-primaryBaseline)/.16); commitment affects R, not raw provider GIH'],
        establishedCountSemantics=SEMANTIC,duplicateCopies='Multiple opening/draw instances weight the same game outcome repeatedly; redraw can exceed deck copies',
        changes=[dict(date='2021-03-29',before='GIH at most once per card/game'),dict(date='2022-02-27',backfill='2022-02-10',change='Remove returned-to-hand from draws; GP per deck copy'),
            dict(date='2022-10-16',backfill='2022-09-01',change='Remove tutored cards from drawn/GIH'),dict(date='2024-11-12',change='OH includes kept seven before bottoming, rather than start-turn-one hand')],
        compatibleUnitSets=list(COMPATIBLE),excludedFromModernFit=['KHM','STX','AFR','NEO','SNC','VOW'],
        currentOHCollectionEraNotReconstructed=True,sourceWholeGameValidityFilterCannotBeFullyReconstructed=True,
        ownedCaches=entries,networkAggregateRequests=0,
        bridge=dict(status='UNAVAILABLE',historicalOverlapSets=0,reason='Owned aggregates are FRA only; historical games are other sets/time cohorts. No same-cohort rate/count bridge or correlations claimed'),
        legitimateCrossFormatPairs=1,generalizedReliabilityQualified=False)
    dump(OUT/'provider-semantics.json',semantics);print('owned cache audit',len(entries),flush=True)

def dataset(s,mapping):
    allow(s);f='PremierDraft';base=DATA if s in ('ONE','MOM') else OLD;path=base/f'{s}.{f}.observations.parquet'
    counts=collections.defaultdict(collections.Counter);streams=collections.defaultdict(lambda:dict(evidence=[],target=[]));games=set();egames=set();tgames=set();excluded=collections.Counter()
    for batch in pq.ParquetFile(path).iter_batches(batch_size=65536):
        for r in batch.to_pylist():
            identity=mapping[s+'|'+r['card']]
            if not r['resolved'] or identity['identity']['CanonicalId'] is None:excluded['unresolved']+=1;continue
            if identity['category']=='basic lands':excluded['basic']+=1;continue
            if r['deck']<=0:excluded['sideboardOnly']+=1;continue
            weights=count_weights(r);c=counts[r['card']];won=r['won'];group='evidence' if evidence_group(s,f,r['draft']) else 'target'
            games.add(r['game']);(egames if group=='evidence' else tgames).add(r['game'])
            for key,n in weights.items():c[key]+=n;c[key+'Wins']+=n*won
            c['gpGames']+=1;c['multiDeckGames']+=r['deck']>1;c['ohDrawOverlap']+=r['opening']>0 and r['drawn']>0
            c['multipleSeenInstances']+=weights['copies']>1;c['redrawPossible']+=weights['copies']>r['deck'];c['tutoredInstances']+=r['tutored']
            c[group+'Copies']+=weights['copies'];c[group+'CopyWins']+=weights['copies']*won;c[group+'Presence']+=weights['presence'];c[group+'PresenceWins']+=weights['presence']*won
            if weights['presence']: streams[r['card']][group].append((r['game'],won,weights['copies']))
    if egames&tgames:raise ValueError('Draft/game split leakage')
    rows=[]
    for card,c in sorted(counts.items()):
        identity=mapping[s+'|'+card];n=c['presence'];copies=c['copies'];p=c['presenceWins']/n if n else None;rate=c['copiesWins']/copies if copies else None
        rows.append(dict(set=s,format=f,card=card,canonicalId=identity['identity']['CanonicalId'],rarity=identity['rarity'],counts=dict(c),
            presenceRate=p,copyRate=rate,countDifference=copies-n,countRatio=copies/n if n else None,rateDifference=rate-p if n and copies else None,
            averageDeckCopies=c['gp']/c['gpGames'],duplicateHeavy=c['multiDeckGames']/c['gpGames']>=.25,
            evidenceRate=c['evidenceCopyWins']/c['evidenceCopies'] if c['evidenceCopies'] else None,evidenceN=c['evidenceCopies'],evidenceGames=c['evidencePresence'],
            targetRate=c['targetCopyWins']/c['targetCopies'] if c['targetCopies'] else None,targetN=c['targetCopies'],targetGames=c['targetPresence']))
    evidenceN=sum(r['evidenceN'] for r in rows);targetN=sum(r['targetN'] for r in rows)
    b=sum(r['evidenceRate']*r['evidenceN'] for r in rows if r['evidenceN'])/evidenceN
    targetb=sum(r['targetRate']*r['targetN'] for r in rows if r['targetN'])/targetN
    rates=[];noise=[];ratios=[]
    for r in rows:
        obs=streams[r['card']]['evidence'];p,n=rate_units(obs)
        if n>0:
            neff=n*n/sum(x[2]**2 for x in obs);rates.append(p);noise.append(p*(1-p)/neff);ratios.append(n/neff)
    tau=max(0,statistics.pvariance(rates)-statistics.mean(noise));eb=min(2000,max(0,b*(1-b)/tau-1))*statistics.median(ratios) if tau>0 else 2000
    return dict(set=s,format=f,rows=rows,streams=streams,baseline=b,targetBaseline=targetb,ebK=min(2000,eb),games=len(games),evidenceGames=len(egames),targetGames=len(tgames),
        exclusions=dict(excluded),sourceHash=sha(path),sourceBytes=path.stat().st_size)

def comparisons(d):
    rows=[r for r in d['rows'] if r['counts']['presence']>=50]
    def compare(rs):
        if not rs:return dict(cards=0)
        m=metrics([r['copyRate'] for r in rs],[r['presenceRate'] for r in rs]);m.update(countRatio={str(p):quantile([r['countRatio'] for r in rs],p) for p in (.05,.5,.95)},meanDeckCopies=statistics.mean(r['averageDeckCopies'] for r in rs),
            countCorrelation=correlation([r['counts']['copies'] for r in rs],[r['counts']['presence'] for r in rs]))
        return m
    strata=dict(overall=compare(rows),duplicateHeavy=compare([r for r in rows if r['duplicateHeavy']]),mostlySingleton=compare([r for r in rows if not r['duplicateHeavy']]))
    for rarity in sorted({r['rarity'] for r in rows}):strata['rarity='+rarity]=compare([r for r in rows if r['rarity']==rarity])
    for lo,hi in ((50,250),(250,1000),(1000,1000000)):strata[f'presenceN={lo}-{hi}']=compare([r for r in rows if lo<=r['counts']['presence']<hi])
    return dict(set=d['set'],games=d['games'],sourceHash=d['sourceHash'],strata=strata,rows=d['rows'])

def experiments(d,eb):
    b=d['baseline'];qualified=[r for r in d['rows'] if r['evidenceGames']>=50 and r['targetGames']>=100];samples={}
    for r in qualified:
        key=d['set']+'/'+r['card'];ordered=sample_clusters(d['streams'][r['card']]['evidence'],'full',key);cumulative=[];total=0
        for x in ordered:total+=x[2];cumulative.append(total)
        samples[r['card']]={level:rate_units(ordered if level=='full' else ordered[:bisect.bisect_left(cumulative,level)+1]) for level in LEVELS}
    results=[]
    for name,k in [(str(k),k) for k in GRID]+[('EB-count-adjusted',eb)]:
        for level in LEVELS:
            estimates=[];targets=[];ns=[]
            for r in qualified:
                p,n=samples[r['card']][level];estimates.append((n*p+k*b)/(n+k));targets.append(r['targetRate']);ns.append(n)
            m=metrics(estimates,targets)
            if targets:m.update(QMAE=statistics.mean(abs(q(p,b)-q(t,d['targetBaseline'])) for p,t in zip(estimates,targets)),signAccuracy=statistics.mean((p>b)==(t>d['targetBaseline']) for p,t in zip(estimates,targets)))
            results.append(dict(set=d['set'],partition=load('V2_PHASE_B1_SPLIT_MANIFEST.json')['Sets'][d['set']],policy=name,k=k,level=str(level),actualUnitMinimum=min(ns) if ns else 0,actualUnitMedian=statistics.median(ns) if ns else 0,fullySupported=sum(n>=level for n in ns) if level!='full' else len(ns),**m))
    return results

def candidate_rows(d,k):
    result=[]
    for r in d['rows']:
        n=r['evidenceN'];p=r['evidenceRate'];b=d['baseline']
        current=q((n*p+500*b)/(n+500),b) if n else .5;candidate=q((n*p+k*b)/(n+k),b) if n else .5
        result.append(dict(set=d['set'],format=d['format'],card=r['card'],canonicalId=r['canonicalId'],currentQ=current,candidateQ=candidate,evidenceN=n,baseline=b,
            evidenceMode='primary' if n else 'neutral',countSemantics=SEMANTIC,alternatePrior='Available only with legitimate same-set alternate; no alternate historical table exists',sourceHash=d['sourceHash']))
    return result

def human(d,qrows):
    lookup={r['canonicalId']:r for r in qrows};acc=collections.defaultdict(collections.Counter);base=DATA if d['set'] in ('ONE','MOM') else OLD
    with gzip.open(base/f"{d['set']}.PremierDraft.decisions.jsonl.gz",'rt',encoding='utf-8') as stream:
        for line in stream:
            decision=json.loads(line);i=decision['Inputs']
            if evidence_group(d['set'],'PremierDraft',i['DraftId']):continue
            ids=[x['Card']['CanonicalId'] for x in i['OfferedCards'] for _ in range(x['Count'])];actual=decision['Label']['ActualPickedCard']['CanonicalId']
            if i['PickNumber']>14 or not i['PoolIsComplete'] or any(x['Card']['CanonicalId'] is None for x in i['PoolBeforePick']) or None in ids or len(ids)<2 or actual not in ids:continue
            for model in ('currentQ','candidateQ'):
                order=sorted(range(len(ids)),key=lambda index:(-lookup.get(ids[index],{}).get(model,.5),ids[index],index));rank=min(j+1 for j,index in enumerate(order) if ids[index]==actual)
                a=acc[model];a['n']+=1;a['top1']+=rank==1;a['top3']+=rank<=3;a['rr']+=1/rank;a['normalized']+=(rank-1)/(len(ids)-1)
    return dict(set=d['set'],models={m:dict(n=a['n'],top1=a['top1']/a['n'],top3=a['top3']/a['n'],MRR=a['rr']/a['n'],normalizedRank=a['normalized']/a['n']) for m,a in acc.items()})

def train():
    start=time.perf_counter();split=guard();mapping=load(DATA/'identity-map.json');cal=[dataset(s,mapping) for s in ('DMU','BRO','WOE')]
    eb=statistics.median(d['ebK'] for d in cal);results=[r for d in cal for r in experiments(d,eb)]
    def macro(partition,policy):return statistics.mean(r['mae'] for r in results if r['partition']==partition and r['policy']==policy)
    selected=min([str(k) for k in GRID]+['EB-count-adjusted'],key=lambda p:(macro('Calibration',p),p=='EB-count-adjusted'))
    k=eb if selected=='EB-count-adjusted' else int(selected)
    fingerprint=digest(json.dumps({str(p):sha(p) for p in sorted(pathlib.Path(__file__).parent.glob('phase_b1_*.py')) if p.name not in ('phase_b1_report.py','phase_b1_tests.py')},sort_keys=True))
    selection=dict(version=VERSION,k=k,policy=selected,ebCalibrationK=eb,selectionMetric='Equal-set/equal-six-level unweighted card MAE; calibration fits only',semantics=SEMANTIC,
        calibrationSets=['DMU','BRO','WOE'],developmentSets=['LCI','MKM','OTJ','ONE','MOM'],fingerprint=fingerprint,splitHash=sha('V2_PHASE_B1_SPLIT_MANIFEST.json'),
        limitations=['Modern unit compatible, but pre-2024-11-12 OH collection era differs from current provider','No matching provider historical cohort bridge','Copy instances sharing game outcomes are not independent Bernoulli trials'],
        alternatePrior=dict(reliability=.9,shrinkage=500,status='Reference policy only; generalized reliability unqualified'))
    path=OUT/'selection.json'
    if path.exists() and load(path)!=selection:raise ValueError('B.1 selection already frozen; no overwrite/retune')
    dump(path,selection);comparisons_out=[];baselines=[];qrows=[];humans=[]
    def export(d):
        comparisons_out.append(comparisons(d));baselines.append({k:v for k,v in d.items() if k not in ('rows','streams')});qr=candidate_rows(d,k);qrows.extend(qr);humans.append(human(d,qr))
        features=[]
        for r in d['rows']:
            c=r['counts'];n=r['evidenceN']
            features.append(dict(SourceIdentity=r['card'],SetCode=d['set'],PrimaryFormat='PremierDraft',GIHWinRate=r['evidenceRate'],GIHGames=n,
                ALSA=None,ATA=None,Period='ALL_TIME',Provenance='Public historical B.1 disjoint-draft copy/draw evidence; old OH collection era'))
        snapshots.append(dict(SetCode=d['set'],Format='PremierDraft',Regime='MatureFormatReplay',AvailableAt=None,Provenance='B.1 public evidence drafts only; documented copy/draw units',Cards=features))
        print('analyzed',d['set'],d['games'],flush=True)
    snapshots=[]
    for d in cal:export(d)
    cal.clear()
    for s in ('LCI','MKM','OTJ','ONE','MOM'):
        d=dataset(s,mapping);results.extend(experiments(d,eb));export(d)
    dump(OUT/'count-comparisons.json',comparisons_out);dump(OUT/'compatible-baselines.json',baselines);dump(OUT/'shrinkage-experiments.json',results);dump(OUT/'q-values.json',qrows);dump(OUT/'human-q-ranking.json',humans)
    dump(DATA/'compatible-stats.json',snapshots)
    dump(OUT/'calibration-summary.json',dict(selected=selection,calibration={p:macro('Calibration',p) for p in [str(k) for k in GRID]+['EB-count-adjusted']},
        development={p:macro('Development',p) for p in [str(k) for k in GRID]+['EB-count-adjusted']},seconds=time.perf_counter()-start,statsHash=sha(DATA/'compatible-stats.json'),
        effectiveSampling='Nested SHA-ordered whole card/game clusters until requested copy/draw count reached; overshoot and shortfalls reported, no game outcome split',
        eb='Moment variance subtracts cluster-weighted p(1-p)/n_eff, n_eff=(sum weights)^2/sum weights²; converts effective-count k using median count/n_eff, freezes calibration median'))

