"""Three interpretable families; calibration-only temporal fit; no outcome features."""
import collections,gzip,itertools,json,math,statistics,time
from phase_c_core import *

def resources():
    return load(DATA/'positions.json'),load(DATA/'metadata.json'),{(r['set'],r['id']):r['Q'] for r in load(DATA/'quality.json')},load(OUT/'position-policy.json')

def drafts(s):
    group=[];scope=None
    for d in decisions(s,bulk=True):
        new=d['Inputs']['DraftId']
        if new!=scope and group:yield group;group=[]
        scope=new;group.append(d)
    if group:yield group

def future_mass(rows,t,h,meta,q,picked=False):
    current=rows[t]['Inputs'];future=rows[t+1:t+1+h]
    if len(future)!=h or any(d['Inputs']['PackNumber']!=current['PackNumber'] for d in future):return None
    if any(d['Inputs']['GlobalPickIndex']!=current['GlobalPickIndex']+j or any(x['Card']['CanonicalId'] not in meta for x in d['Inputs']['OfferedCards']) for j,d in enumerate(future,1)):return None
    if picked and any(d['Label']['ActualPickedCard']['CanonicalId'] not in meta for d in future):return None
    total=[0.]*5
    for d in future:
        i=d['Inputs']
        if picked:
            i=dict(i,OfferedCards=[dict(Card=d['Label']['ActualPickedCard'],Count=1)])
        m=quality_mass(i,meta,q)
        for color in range(5):total[color]+=m[color]
    return normalized(total)

def calibrate(output=None):
    guard();start=time.perf_counter();positions,meta,q,policy=resources();scores=collections.defaultdict(lambda:collections.defaultdict(list));count=0
    configs={family:[dict(family=family,halfLife=h,retention=r,negative=n,lambdaSignal=l) for h,r,n,l in itertools.product((4,8,16),(0,.25,.5),(0,) if family=='A' else (0,.25),(20,100,400))] for family in ('A','B','C')}
    for s in ('DMU','BRO','WOE'):
        for rows in drafts(s):
            states={json.dumps(c,sort_keys=True):Accumulator(c['halfLife'],c['retention'],c['negative']) for cs in configs.values() for c in cs}
            for t,d in enumerate(rows):
                i=d['Inputs'];signals={(family,l):observation(i,positions,meta,q,family,l,policy['packSpecific'],policy['lambdaDrafts']) for family in configs for l in (20,100,400)}
                target=future_mass(rows,t,3,meta,q)
                for key,state in states.items():
                    cfg=json.loads(key);profile=state.observe(i,signals[(cfg['family'],cfg['lambdaSignal'])])
                    if target is not None and i['PoolIsComplete'] and i['PickNumber']<=14 and all(x['Card']['CanonicalId'] for x in i['OfferedCards']+i['PoolBeforePick']):
                        stage='early' if i['PickNumber']<=5 else 'mid' if i['PickNumber']<=9 else 'late'
                        scores[key][s+'/'+str(i['PackNumber'])+'/'+stage].append(brier(fit_color(profile['colors']),target))
                count+=1
        print('calibrated',s,count,flush=True)
    result=[]
    for key,groups in scores.items():result.append(dict(config=json.loads(key),Brier=statistics.mean(statistics.mean(v) for v in groups.values()),groups={g:dict(n=len(v),Brier=statistics.mean(v)) for g,v in groups.items()}))
    selected={family:min((r for r in result if r['config']['family']==family),key=lambda r:(r['Brier'],r['config']['halfLife'],r['config']['retention'],r['config']['negative'])) for family in configs}
    evidence=dict(version=VERSION,metric='Equal set/pack/pick-stage next-3 quality-card availability Brier',calibrationSets=['DMU','BRO','WOE'],positionPolicy=policy,
        selected=selected,allConfigurations=result,decisions=count,seconds=time.perf_counter()-start,inputHashes={str(p):sha(p) for p in (DATA/'positions.json',DATA/'quality.json',DATA/'metadata.json')})
    path=output or OUT/'temporal-calibration.json'
    if path.exists():
        old=load(path)
        if old['selected']!=evidence['selected'] or old['inputHashes']!=evidence['inputHashes']:raise ValueError('Calibration already fixed; cannot retune')
        return old
    dump(path,evidence);print({f:r['config'] for f,r in selected.items()},flush=True)

def predict():
    guard();positions,meta,q,policy=resources();params=load(OUT/'temporal-calibration.json')['selected'];path=DATA/'predictions.jsonl.gz';n=0
    with gzip.open(path,'wt',encoding='utf-8',compresslevel=1) as writer:
        for s in sorted(COMPATIBLE):
            for rows in drafts(s):
                states={f:Accumulator(p['config']['halfLife'],p['config']['retention'],p['config']['negative']) for f,p in params.items()}
                for d in rows:
                    i=d['Inputs'];models={f:state.observe(i,observation(i,positions,meta,q,f,params[f]['config']['lambdaSignal'],policy['packSpecific'],policy['lambdaDrafts'])) for f,state in states.items()}
                    if s+'/'+i['Format'] not in positions:
                        for state in models.values():state['applicable']=False
                    record=dict(set=s,format=i['Format'],draft=i['DraftId'],pack=i['PackNumber'],pick=i['PickNumber'],models=models)
                    writer.write(json.dumps(record,separators=(',',':'))+'\n');n+=1
    dump(OUT/'prediction-provenance.json',dict(version=VERSION,decisions=n,sha256=sha(path),calibrationHash=sha(OUT/'temporal-calibration.json'),
        evidenceOnlyPositions=True,scoringAPI='PickTimeState -> observation -> accumulator; no label/outcome/future arguments',
        qualityFrozen='Existing B.1 currentQ/k500 scalars only for bounded eligibility; never substituted'))
    print('predictions',n,flush=True)
