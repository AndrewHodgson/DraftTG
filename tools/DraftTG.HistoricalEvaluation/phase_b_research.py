"""Offline Q research: train (calibration/development) -> frozen selection -> final once.

Uses public game observations; never reads provider targets/aggregate ratings. Whole-set
isolation is checked before files are read. See preregistration for non-chronological semantics.
"""
import argparse, collections, gzip, hashlib, json, pathlib, statistics, sys, time
from phase_b_core import *
from phase_b_source import ROOT, sha
sys.path.insert(0,str(ROOT/'cache/phase-b/python'))
import pyarrow.parquet as pq

DATA=ROOT/'normalized/phase-b-v1'; OUT=pathlib.Path('docs/evidence/v2-phase-b')

def dump(path,value):path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(value,indent=2),encoding='utf-8')
def fingerprint():
    paths=['phase_b_core.py','phase_b_qualify.py','phase_b_research.py','phase_b_replay.py']
    hashes={p:sha(pathlib.Path(__file__).parent/p) for p in paths}
    hashes.update({p:sha(p) for p in ['docs/historical/splits/phase-b-v1.json','docs/historical/phase-b-identity-policy.json','docs/evidence/v2-phase-b/source-provenance.json']})
    for p in ['src/DraftTG.HistoricalEvaluation/ExperimentalQualitySubstitution.cs','src/DraftTG.HistoricalEvaluation/HistoricalEvaluator.cs','tools/DraftTG.HistoricalEvaluation/Program.cs']:
        hashes[p]=sha(p)
    for p in sorted(DATA.glob('*')):
        if p.suffix=='.parquet' or p.name in ('catalog.json','identity-map.json') or p.name.endswith('.decisions.jsonl.gz') or p.name.endswith('.primary-builds.jsonl.gz'):
            hashes[str(p)]=sha(p)
    return digest(json.dumps(hashes,sort_keys=True))

def load_dataset(s,f,mappings,final=False):
    splits=verify_registration()
    if splits[s]=='Holdout':
        if not final or not (OUT/'holdout-release.json').exists():raise ValueError('Holdout quality remains locked')
        release=json.loads((OUT/'holdout-release.json').read_text())
        if release['releaseProcessId']!=__import__('os').getpid():raise ValueError('Holdout release belongs to a different process')
    path=DATA/f'{s}.{f}.observations.parquet'
    if not path.exists():return None
    streams=collections.defaultdict(lambda:dict(evidence=[],target=[]))
    counters=collections.defaultdict(collections.Counter);strata=collections.defaultdict(lambda:collections.defaultdict(collections.Counter))
    excluded=collections.Counter();uniquegames=set();evidencegames=set();targetgames=set();draftgroups={}
    # Each source game stays entirely in one evidence/target group, independent of outcome.
    for batch in pq.ParquetFile(path).iter_batches(batch_size=65536):
        for r in batch.to_pylist():
            maprow=mappings[s+'|'+r['card']]
            if not r['resolved']:excluded['unresolvedCardGameRows']+=1;continue
            if maprow['category']=='basic lands':excluded['basicLandCardGameRows']+=1;continue
            if r['deck']<=0:excluded['sideboardOnlyCardGameRows']+=1;continue
            c=counters[r['card']];won=r['won'];oh=r['opening']>0;draw=r['drawn']>0;seen=oh or draw
            uniquegames.add(r['game']);draftgroups[r['draft']]=r['evidence']
            (evidencegames if r['evidence'] else targetgames).add(r['game'])
            for tag,present in [('gp',True),('oh',oh),('drawn',draw),('gih',seen),('never',not seen),('drawnWithoutOpening',draw and not oh)]:
                if present:c[tag]+=1;c[tag+'Wins']+=won
            c['copyGih']+=r['opening']+r['drawn'];c['copyGihWins']+=(r['opening']+r['drawn'])*won
            c['copyGp']+=r['deck'];c['copyGpWins']+=r['deck']*won
            if r['evidence']:
                c['evidenceGp']+=1;c['evidenceGpWins']+=won
                if seen:c['evidenceGih']+=1;c['evidenceGihWins']+=won
                if oh:c['evidenceOh']+=1;c['evidenceOhWins']+=won
                if draw:c['evidenceDrawn']+=1;c['evidenceDrawnWins']+=won
            else:
                c['targetGp']+=1;c['targetGpWins']+=won
                if seen:c['targetGih']+=1;c['targetGihWins']+=won
            if seen:streams[r['card']]['evidence' if r['evidence'] else 'target'].append((r['game'],won,oh))
            for label in ['rank','colors']:
                bucket=strata[r['card']][label+'='+str(r[label])]
                bucket['gp']+=1;bucket['wins']+=won
    if evidencegames & targetgames:raise ValueError('Evidence/target games overlap')
    rows=[]
    for card,c in sorted(counters.items()):
        def rate(tag):return c[tag+'Wins']/c[tag] if c[tag] else None
        row=dict(set=s,format=f,card=card,canonicalId=mappings[s+'|'+card]['identity']['CanonicalId'],rarity=mappings[s+'|'+card]['rarity'],
            counts=dict(c),rate=rate('gih'),n=c['gih'],gamePresenceGIH=rate('gih'),copyWeightedGIH=rate('copyGih'),
            GP=rate('gp'),OH=rate('oh'),Drawn=rate('drawn'),NeverDrawn=rate('never'),
            DrawnWithoutOpening=rate('drawnWithoutOpening'),IWD=(rate('gih')-rate('never')) if c['gih'] and c['never'] else None,
            evidenceRate=rate('evidenceGih'),evidenceN=c['evidenceGih'],targetRate=rate('targetGih'),targetN=c['targetGih'],
            selectionStrata={k:dict(v) for k,v in strata[card].items()})
        rows.append(row)
        streams[card]['evidence']=sample_observations(streams[card]['evidence'],'full',s+'/'+f+'/'+card)
    training_baseline=baseline([dict(rate=r['evidenceRate'],n=r['evidenceN']) for r in rows])
    target_baseline=baseline([dict(rate=r['targetRate'],n=r['targetN']) for r in rows])
    return dict(set=s,format=f,rows=rows,streams=streams,baseline=baseline(rows),evidenceBaseline=training_baseline,targetBaseline=target_baseline,
        exclusions=dict(excluded),games=len(uniquegames),evidenceGames=len(evidencegames),targetGames=len(targetgames),drafts=len(draftgroups),
        sourceHash=sha(path))

def metrics(predictions,truth,counts):
    if not predictions:return dict(cards=0,MAE=None)
    n=len(predictions);total=sum(counts);errors=[p-t for p,t in zip(predictions,truth)]
    bins=collections.defaultdict(lambda:[0,0.,0.])
    for p,t,c in zip(predictions,truth,counts):
        bucket=bins[min(9,int(p*10))];bucket[0]+=c;bucket[1]+=p*c;bucket[2]+=t*c
    calibration=[dict(lower=k/10,upper=(k+1)/10,observations=v[0],prediction=v[1]/v[0],observed=v[2]/v[0]) for k,v in sorted(bins.items())]
    return dict(cards=n,targetObservations=total,MAE=statistics.mean(abs(e) for e in errors),
        observationWeightedMAE=sum(abs(e)*c for e,c in zip(errors,counts))/total,
        RMSE=math.sqrt(statistics.mean(e*e for e in errors)),bias=statistics.mean(errors),
        Brier=sum(c*(p*p-2*p*t+t) for p,t,c in zip(predictions,truth,counts))/total,
        rankCorrelation=rank_correlation(predictions,truth),overestimate2pp=statistics.mean(e>=.02 for e in errors),
        overestimate5pp=statistics.mean(e>=.05 for e in errors),
        calibrationECE=sum(abs(v['prediction']-v['observed'])*v['observations'] for v in calibration)/total,
        calibrationBins=calibration)

def experiment(dataset,policies):
    b=dataset['evidenceBaseline']['weighted'];results=[]
    # Target >=100 game-presence observations, evidence >=50; sparse samples may cap below requested n, reported.
    cards=[r for r in dataset['rows'] if r['targetN']>=100 and r['evidenceN']>=50]
    for level in LEVELS:
        for name,family,k in policies:
            predictions=[];truth=[];counts=[];actual=[]
            for r in cards:
                observations=dataset['streams'][r['card']]['evidence'];p=predict(observations,level,b,k,family)
                predictions.append(p);truth.append(r['targetRate']);counts.append(r['targetN'])
                actual.append(len(observations) if level=='full' else min(level,len(observations)))
            m=metrics(predictions,truth,counts)
            if predictions:
                m['signAccuracy']=statistics.mean((p>b)==(t>dataset['targetBaseline']['weighted']) for p,t in zip(predictions,truth))
                predictedQ=[quality(p,b,dataset['evidenceBaseline']['spread'],'standardized' if name=='B' else 'delta') for p in predictions]
                targetQ=[quality(t,dataset['targetBaseline']['weighted'],dataset['targetBaseline']['spread']) for t in truth]
                m['QMAE']=statistics.mean(abs(p-t) for p,t in zip(predictedQ,targetQ))
            results.append(dict(set=dataset['set'],format=dataset['format'],level=str(level),policy=name,family=family,k=k,
                actualSampleMinimum=min(actual) if actual else 0,actualSampleMedian=statistics.median(actual) if actual else 0,
                fullySupportedCards=sum(n>=level for n in actual) if level!='full' else len(actual),**m))
    return results

def specs(k,policy,c_k,eb_k):
    common=dict(version=VERSION,missing='No valid same-format evidence -> Q=.5, explicit neutral provenance; no generalized alternate prior',
        format='Exact set + format only; same-set alternate experiments blocked without legitimate paired games',
        range='(0,1), soft knee .35 around .5; no rarity input',provenance='Public game-presence opening-or-drawn outcomes; whole-draft disjoint samples')
    return [dict(**common,id='A',family=policy,k=k,ebCalibrationK=eb_k,
                 formula='Q=SoftLimit(.5+(shrunk_GIH-weighted_set_format_baseline)/.16,.35)',
                 shrinkage='(n*p+k*b)/(n+k); adaptive uses k*sqrt(500/n); EB uses frozen calibration median k',
                 early='Strong shrinkage toward neutral; evidence weight grows continuously'),
            dict(**common,id='B',family=policy,k=k,ebCalibrationK=eb_k,
                 formula='Q=SoftLimit(.5+(shrunk_GIH-b)/(4*max(.005,set_format_card_rate_SD)),.35)',
                 shrinkage='Same as A; SD from evidence group only',early='May amplify noisy early between-card spread; .005 floor'),
            dict(**common,id='C',family='decomposition',k=c_k,
                 formula='Disjoint OH-presence vs drawn-without-OH strata; shrink each to b with k/2; mix by observed stratum frequency; delta/.16 soft limit',
                 shrinkage='No double weighting of GIH/OH/drawn/GP/IWD; missing stratum has zero mixture mass',
                 early='Separate strata noisier at low n; shrink each separately')]

def candidate_rows(dataset,candidates):
    b=dataset['evidenceBaseline']['weighted'];sd=dataset['evidenceBaseline']['spread'];out=[]
    for r in dataset['rows']:
        obs=dataset['streams'][r['card']]['evidence'];row=dict(set=dataset['set'],format=dataset['format'],card=r['card'],canonicalId=r['canonicalId'],
            evidenceN=len(obs),targetN=r['targetN'],baseline=b,spread=sd,qualities={})
        row['qualities']['currentQ']=quality(predict(obs,'full',b,500),b,sd)
        for c in candidates:
            p=predict(obs,'full',b,c['k'],c['family']);row['qualities'][c['id']]=quality(p,b,sd,'standardized' if c['id']=='B' else 'delta')
        out.append(row)
    return out

def human_quality(dataset,rows):
    lookup={r['card']:r['qualities'] for r in rows};acc=collections.defaultdict(collections.Counter);missing=collections.Counter()
    path=DATA/f"{dataset['set']}.{dataset['format']}.decisions.jsonl.gz"
    if not path.exists():return None
    with gzip.open(path,'rt',encoding='utf-8') as f:
        for line in f:
            d=json.loads(line);i=d['Inputs'];n=sum(x['Count'] for x in i['OfferedCards'])
            if n<2 or i['PickNumber']>14 or not i['PoolIsComplete'] or any(x['Card']['CanonicalId'] is None for x in i['OfferedCards']+i['PoolBeforePick']):continue
            names=[x['Card']['SourceIdentity'] for x in i['OfferedCards'] for _ in range(x['Count'])];actual=d['Label']['ActualPickedCard']['SourceIdentity']
            for model in ['currentQ','A','B','C']:
                qs=[lookup.get(name,{}).get(model,.5) for name in names]
                missing[model]+=sum(name not in lookup or lookup[name].get(model) is None for name in names)
                order=sorted(range(n),key=lambda index:(-qs[index],names[index],index));rank=min(j+1 for j,index in enumerate(order) if names[index]==actual)
                a=acc[model];a['decisions']+=1;a['top1']+=rank==1;a['top3']+=rank<=3;a['rr']+=1/rank;a['rank']+=(rank-1)/(n-1)
    return dict(set=dataset['set'],format=dataset['format'],interpretation='Q-only ranking, not contextual score; human picks are secondary revealed preferences',
        models={m:dict(eligible=a['decisions'],top1=a['top1']/a['decisions'],top3=a['top3']/a['decisions'],MRR=a['rr']/a['decisions'],
            normalizedRank=a['rank']/a['decisions'],neutralOccurrences=missing[m]) for m,a in acc.items()})

def export_features(datasets,candidates,prefix):
    feature_rows=[];baselines=[];qrows=[];humans=[];associations=[];snapshots=[]
    for d in datasets:
        print('features',d['set'],flush=True)
        q=candidate_rows(d,candidates);qrows.extend(q);h=human_quality(d,q)
        if h:humans.append(h)
        # All performance-bearing exports are restricted to the currently authorized whole sets.
        for r in d['rows']:
            feature_rows.append({k:v for k,v in r.items() if k!='selectionStrata'})
        rarity={r:baseline([x for x in d['rows'] if x['rarity']==r]) for r in sorted({x['rarity'] for x in d['rows'] if x['rarity'] is not None})}
        baselines.append({k:v for k,v in d.items() if k not in ('rows','streams')}|dict(rarityDiagnostics=rarity,
            copyWeightedBaseline=baseline([dict(rate=r['copyWeightedGIH'],n=r['counts'].get('copyGih',0)) for r in d['rows']])))
        primary=DATA/f"{d['set']}.{d['format']}.primary-builds.jsonl.gz"
        if primary.exists():
            card_presence=collections.defaultdict(lambda:dict(low=0,high=0));groupcounts=collections.Counter()
            with gzip.open(primary,'rt',encoding='utf-8') as f:
                for line in f:
                    deck=json.loads(line)
                    if deck['Games']<3:continue
                    group='high' if deck['Wins']/deck['Games']>=.6 else 'low';groupcounts[group]+=1
                    for card in deck['MainDeck']:card_presence[card['Card']['SourceIdentity']][group]+=1
            differences=[v['high']/groupcounts['high']-v['low']/groupcounts['low'] for card,v in card_presence.items() if card in {r['card'] for r in q}] if groupcounts['high'] and groupcounts['low'] else []
            paired=[(next(r for r in q if r['card']==card)['qualities']['A'],v['high']/groupcounts['high']-v['low']/groupcounts['low']) for card,v in card_presence.items() if card in {r['card'] for r in q}] if differences else []
            associations.append(dict(set=d['set'],format=d['format'],groupCounts=dict(groupcounts),cards=len(paired),
                associationCorrelation=correlation([p[0] for p in paired],[p[1] for p in paired]),
                interpretation='Unique modal observed 40 builds >=3 observed games, high observed game WR>=.6; build presence once, not copies; noncausal descriptive label'))
        snapshotcards=[]
        for r in d['rows']:
            c=r['counts'];n=c.get('evidenceGih',0)
            snapshotcards.append(dict(SourceIdentity=r['card'],SetCode=d['set'],PrimaryFormat=d['format'],GIHWinRate=r['evidenceRate'],GIHGames=n or None,
                OpeningHandWinRate=c.get('evidenceOhWins',0)/c['evidenceOh'] if c.get('evidenceOh',0) else None,
                DrawnWinRate=c.get('evidenceDrawnWins',0)/c['evidenceDrawn'] if c.get('evidenceDrawn',0) else None,
                ALSA=None,ATA=None,Period='Phase B deterministic evidence draft group; not chronological',
                Provenance=d['sourceHash']))
        snapshots.append(dict(SetCode=d['set'],Format=d['format'],Regime='MatureFormatReplay',AvailableAt=None,Provenance=d['sourceHash'],Cards=snapshotcards))
    dump(OUT/f'{prefix}-card-features.json',feature_rows);dump(OUT/f'{prefix}-baselines.json',baselines)
    dump(OUT/f'{prefix}-q-values.json',qrows);dump(OUT/f'{prefix}-human-q-ranking.json',humans);dump(OUT/f'{prefix}-build-association.json',associations)
    dump(DATA/f'{prefix}-stats.json',snapshots)
    return baselines

def macro(rows,partition,splits):
    selected=[r for r in rows if splits[r['set']]==partition and r.get('MAE') is not None]
    by=collections.defaultdict(list)
    for r in selected:by[r['set']].append(r['MAE'])
    return statistics.mean(statistics.mean(v) for v in by.values()) if by else None

def run(final=False):
    started=time.perf_counter();splits=verify_registration();selection_path=OUT/'selection.json'
    if final:
        selection=json.loads(selection_path.read_text());final_lock(OUT/'holdout-release.json',selection,fingerprint())
    else:
        if selection_path.exists():raise ValueError('Selection already frozen. Training/retuning refused.')
    sets=allowed_sets(splits,final);mappings=json.loads((DATA/'identity-map.json').read_text())
    datasets=[]
    for s in sorted(sets):
        for f in ('PremierDraft','QuickDraft'):
            print('loading',s,f,flush=True);d=load_dataset(s,f,mappings,final)
            if d and d['evidenceBaseline'] and d['targetBaseline']:datasets.append(d)
    if not final:
        eb=statistics.median(d['evidenceBaseline']['ebK'] for d in datasets if splits[d['set']]=='Calibration')
        policies=[('fixed-'+str(k),'delta',k) for k in GRID]+[('adaptive-'+str(k),'adaptive',k) for k in (250,500,1000)]+[('EB-calibration-median','delta',eb)]
        policies += [('decomposition-'+str(k),'decomposition',k) for k in GRID]
        experiments=[r for d in datasets for r in experiment(d,policies)]
        dev={name:macro([r for r in experiments if r['policy']==name],'Development',splits) for name,_,_ in policies}
        calibration={name:macro([r for r in experiments if r['policy']==name],'Calibration',splits) for name,_,_ in policies}
        fitted_fixed=min([p for p in policies if p[0].startswith('fixed-')],key=lambda p:(round(calibration[p[0]],12),p[2]))
        fitted_adaptive=min([p for p in policies if p[1]=='adaptive'],key=lambda p:(round(calibration[p[0]],12),p[2]))
        fitted_eb=next(p for p in policies if p[0]=='EB-calibration-median')
        winner=min([fitted_fixed,fitted_adaptive,fitted_eb],key=lambda p:(round(dev[p[0]],12),p[2],p[0]))
        multi=min([p for p in policies if p[1]=='decomposition'],key=lambda p:(round(calibration[p[0]],12),p[2]))
        candidates=specs(winner[2],winner[1],multi[2],eb)
        selected='C' if dev[multi[0]]<dev[winner[0]] else 'A'
        selection=dict(version=VERSION,selectedCandidate=selected,selectedPolicy=multi[0] if selected=='C' else winner[0],
            candidates=candidates,EBcalibrationMedian=eb,developmentMacroMAE=dev,calibrationSets=sorted(s for s in splits if splits[s]=='Calibration'),
            developmentSets=sorted(s for s in splits if splits[s]=='Development'),untouchedHoldouts=sorted(s for s in splits if splits[s]=='Holdout'),
            calibrationMacroMAE=calibration,fittedFamilies=dict(fixed=fitted_fixed[0],adaptive=fitted_adaptive[0],empiricalBayes=fitted_eb[0],multi=multi[0]),
            researchFingerprint=fingerprint(),selectionRule='Fit k within fixed/adaptive/decomposition families using calibration set-macro MAE; choose fitted family on development set-macro MAE, averaged equally over six levels. Tie lower k then lexical; B shares prediction, A wins complexity tie.',
            frozenAt=__import__('datetime').datetime.now(__import__('datetime').timezone.utc).isoformat(),productionPromotion=False)
        candidate_policies=[('current-k500','delta',500)]+[(c['id'],c['family'],c['k']) for c in candidates]
        dump(OUT/'training-candidate-experiments.json',[r for d in datasets for r in experiment(d,candidate_policies)])
        dump(OUT/'training-experiments.json',experiments);dump(selection_path,selection)
        dump(OUT/'cross-format-experiments.json',dict(pairs=0,evaluatedCards=0,metrics=None,
            methods=['neutral Q=.5','v1.6.1-style same-set reliability .9/k500','generalized direction/sample reliability'],
            reason='Only VOW QuickDraft draft rows are available in permitted public dump; no QuickDraft game table. Current aggregate API forbids outside use. No legitimate card-quality pairs. General reliability cannot be fitted or validated.',
            productionFRAOnlyPolicyUnchanged=True))
    else:
        candidates=selection['candidates'];policies=[('current-k500','delta',500)]
        policies += [(c['id'],c['family'],c['k']) for c in candidates]
        experiments=[r for d in datasets for r in experiment(d,policies)]
        dump(OUT/'holdout-experiments.json',experiments)
    prefix='holdout' if final else 'training'
    baselines=export_features(datasets,selection['candidates'],prefix)
    summary=dict(version=VERSION,sets=sorted(sets),datasets=len(datasets),seconds=time.perf_counter()-started,
        cardCount=sum(len(d['rows']) for d in datasets),games=sum(d['games'] for d in datasets),
        evidenceGames=sum(d['evidenceGames'] for d in datasets),targetGames=sum(d['targetGames'] for d in datasets),peakWorkingSetBytes=peak_memory_bytes(),
        interpretation='Sample-size simulation, not chronological backtest; card-game presence, disjoint whole-draft evidence/target; target rates are noisy and selected-deck empirical associations')
    if final:
        summary['selectedCandidate']=selection['selectedCandidate'];summary['macroMAE']={name:macro([r for r in experiments if r['policy']==name],'Holdout',splits) for name,_,_ in policies}
        dump(OUT/'holdout-result.json',summary)
        from phase_b_replay import replay
        replay(True)
        release=json.loads((OUT/'holdout-release.json').read_text());release['completedAt']=__import__('datetime').datetime.now(__import__('datetime').timezone.utc).isoformat()
        release['qualityResultSha256']=sha(OUT/'holdout-result.json');release['contextualResultSha256']=sha(OUT/'holdout-production-replay.json');dump(OUT/'holdout-release.json',release)
    else:dump(OUT/'training-result.json',summary)
    print(json.dumps(summary,indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('command',choices=['train','final']);args=p.parse_args();run(args.command=='final')
