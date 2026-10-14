"""Labels joined AFTER predictions; current lane calls the real Phase9B implementation."""
import collections,gzip,json,math,pathlib,statistics
from phase_c_core import *
from phase_c_research import drafts,future_mass

def labels(s,rows,meta):
    base=B1 if s in ('ONE','MOM') else OLD;builds={}
    with gzip.open(base/f'{s}.PremierDraft.primary-builds.jsonl.gz','rt',encoding='utf-8') as stream:
        for line in stream:
            d=json.loads(line)
            if d['Games']<3 or sum(x['Count'] for x in d['MainDeck'])!=40:continue
            mass=[0.]*5;valid=True
            for item in d['MainDeck']:
                m=meta.get(item['Card']['CanonicalId'])
                if not m:valid=False;break
                if not m['land'] and m['colors']:
                    for c in m['colors']:mass[c]+=item['Count']/len(m['colors'])
            if valid and normalized(mass):builds[d['DraftId']]=normalized(mass)
    return builds

class Acc:
    def __init__(self):self.n=0;self.loss=0.;self.hit=0.;self.mass=0.;self.cal=collections.defaultdict(lambda:[0,0.,0.])
    def add(self,p,y):
        self.n+=1;self.loss+=brier(p,y);top=[i for i,v in enumerate(p) if abs(v-max(p))<1e-12]
        ordered=sorted(range(5),key=lambda c:-y[c]);target=[c for c in ordered[:2] if y[c]>0]
        self.hit+=sum(c in target for c in top)/len(top);self.mass+=sum(p[c] for c in target)
        for a,b in zip(p,y):
            bucket=self.cal[min(9,int(a*10))];bucket[0]+=1;bucket[1]+=a;bucket[2]+=b
    def result(self):return dict(n=self.n,Brier=self.loss/self.n if self.n else None,top1InTop2=self.hit/self.n if self.n else None,top2ProbabilityMass=self.mass/self.n if self.n else None,
        calibration=[dict(bin=k,count=v[0],prediction=v[1]/v[0],observed=v[2]/v[0]) for k,v in sorted(self.cal.items())])

def metrics():
    guard();meta=load(DATA/'metadata.json');q={(r['set'],r['id']):r['Q'] for r in load(DATA/'quality.json')}
    path=pathlib.Path('data/historical/evaluation/phase-c/contextual.json.lanes.jsonl');source=iter(path.open(encoding='utf-8'));allmetrics=collections.defaultdict(Acc);early=collections.defaultdict(collections.Counter);recovery=collections.defaultdict(collections.Counter);stability=collections.defaultdict(collections.Counter);build_context=collections.defaultdict(lambda:[0,0,0,0.,0.]);coverage=collections.Counter()
    for s in sorted(COMPATIBLE):
        builds=labels(s,None,meta)
        for rows in drafts(s):
            states=[]
            for d in rows:
                trace=json.loads(next(source));i=d['Inputs']
                if (trace['SetCode'],trace['DraftId'],trace['PackNumber'],trace['PickNumber'])!=(s,i['DraftId'],i['PackNumber'],i['PickNumber']):raise ValueError('Prediction/label stream mismatch')
                models={f:dict(colors=v['Colors'],confidence=v['Confidence']) for f,v in trace['Models'].items()};models['current-derived']=dict(colors=trace['Current']['Colors'],confidence=trace['Current']['Confidence'])
                models['uniform']=dict(colors=[0.]*5,confidence=0.)
                mass=quality_mass(i,meta,q);density=normalized(mass) or [.2]*5
                models['current-pack-quality-mass']=dict(probability=density,colors=[0.]*5,confidence=0.)
                states.append((models,trace))
            build=builds.get(rows[0]['Inputs']['DraftId']);last=rows[-1];li=last['Inputs'];max_pick=load((B1 if s in ('ONE','MOM') else OLD)/f'{s}.PremierDraft.qualification.json')['picks']['picksPerPack']
            pool=None
            if li['PackNumber']==3 and li['PickNumber']==max_pick and li['PoolIsComplete']:
                items=li['PoolBeforePick']+[dict(Card=last['Label']['ActualPickedCard'],Count=1)];mass=[0.]*5
                if all(x['Card']['CanonicalId'] in meta for x in items):
                    for item in items:
                        m=meta[item['Card']['CanonicalId']]
                        if not m['land'] and m['colors']:
                            for c in m['colors']:mass[c]+=item['Count']/len(m['colors'])
                    pool=normalized(mass)
            coverage['drafts']+=1;coverage['buildLabels']+=build is not None;coverage['completedPoolLabels']+=pool is not None
            for t,d in enumerate(rows):
                i=d['Inputs'];models,trace=states[t]
                if not trace['Complete']:continue
                coverage['completeDecisions']+=1
                targets={f'availability-{h}':future_mass(rows,t,h,meta,q) for h in (1,3,5)}
                targets.update({f'picked-quality-{h}':future_mass(rows,t,h,meta,q,True) for h in (1,3,5)})
                targets.update(build=build,pool=pool)
                stage='early' if i['PickNumber']<=5 else 'mid' if i['PickNumber']<=9 else 'late'
                win=d['Outcome'].get('FinalWins');outcome='unknown' if win is None else '0-2' if win<=2 else '3-4' if win<=4 else str(win)
                for family,state in models.items():
                    p=state.get('probability') or fit_color(state['colors']);prefix=s+'/'+family
                    for lens,target in targets.items():
                        if target is None:continue
                        for suffix in ('overall','pack='+str(i['PackNumber']),'pick='+str(i['PickNumber']),'stage='+str(i['PackNumber'])+'/'+stage,'record='+outcome):
                            allmetrics[prefix+'/'+lens+'/'+suffix].add(p,target)
                    if family in ('A','B','C','current-derived'):
                        if t>0 and rows[t-1]['Inputs']['PackNumber']==i['PackNumber']:
                            old=states[t-1][0][family]['colors'];a=stability[prefix];a['n']+=1;a['L1']+=sum(abs(x-y) for x,y in zip(old,state['colors']))/5
                            if leader(old) is not None and leader(state['colors']) is not None:a['nonTiedTransitions']+=1;a['switches']+=leader(old)!=leader(state['colors'])
                        if i['PackNumber']==1 and i['PickNumber']<=3:
                            top=leader(state['colors']);gap=sorted(state['colors'],reverse=True)[0]-sorted(state['colors'],reverse=True)[1]
                            for threshold in (.25,.5,.75):
                                count=early[prefix+'/'+str(threshold)];count['earlyComplete']+=1
                                lock=top is not None and gap>=threshold and state['confidence']>=threshold
                                count['lock']+=lock
                                if lock and build:
                                    main=sorted(range(5),key=lambda c:-build[c])[:2];count['labeledLocks']+=1;count['false']+=top not in main
                        if build:
                            code=''.join(COLORS[c] for c in sorted(sorted(range(5),key=lambda c:-build[c])[:2]));agreement=trace['Agreements'].get(family)
                            if agreement and agreement['Rank'] is not None:
                                a=build_context[prefix+'/'+code];a[0]+=1;a[1]+=agreement['Rank']==1;a[2]+=agreement['Rank']<=3;a[3]+=1/agreement['Rank'];a[4]+=(agreement['Rank']-1)/(agreement['Occurrences']-1)
            # Descriptive within-pack reversal, labels constructed after every prediction is finished.
            for pack in (1,2,3):
                def window(lo,hi):
                    vectors=[quality_mass(r['Inputs'],meta,q) for r in rows if r['Inputs']['PackNumber']==pack and lo<=r['Inputs']['PickNumber']<=hi]
                    return normalized([sum(v[c] for v in vectors) for c in range(5)]) if len(vectors)==hi-lo+1 else None
                old,new=window(4,6),window(9,11)
                if old is None or new is None or leader(old)==leader(new):continue
                if min(sorted(old,reverse=True)[0]-sorted(old,reverse=True)[1],sorted(new,reverse=True)[0]-sorted(new,reverse=True)[1])<.05:continue
                index6=next((t for t,r in enumerate(rows) if r['Inputs']['PackNumber']==pack and r['Inputs']['PickNumber']==6),None)
                index11=next((t for t,r in enumerate(rows) if r['Inputs']['PackNumber']==pack and r['Inputs']['PickNumber']==11),None)
                if index6 is None or index11 is None:continue
                for family in ('A','B','C','current-derived'):
                    count=recovery[s+'/'+family];count['episodes']+=1;initial=leader(states[index6][0][family]['colors'])==leader(old);count['initiallyRecognized']+=initial
                    count['newLeaderBy11']+=leader(states[index11][0][family]['colors'])==leader(new)
                    if initial:
                        when=next((r['Inputs']['PickNumber'] for t,r in enumerate(rows) if r['Inputs']['PackNumber']==pack and r['Inputs']['PickNumber']>=9 and leader(states[t][0][family]['colors'])==leader(new)),None)
                        count['recoveredBy11']+=when is not None and when<=11;count['everRecovered']+=when is not None
                        if when is not None:count['latencySum']+=when-9
        print('metrics',s,flush=True)
    if next(source,None) is not None:raise ValueError('Extra observer records')
    results={k:v.result() for k,v in allmetrics.items()};dump(OUT/'lane-metrics.json',dict(version=VERSION,metrics=results,coverage=dict(coverage),
        interpretation='Future/build/pool/records are post-scoring descriptive labels; same-pack horizons, shared packs/cards, no causal or chronological claim',
        early={k:dict(v,lockRate=v['lock']/v['earlyComplete'] if v['earlyComplete'] else None,falseEarlyLockRate=v['false']/v['earlyComplete'] if v['earlyComplete'] else None) for k,v in early.items()},
        recovery={k:dict(v,recoveryRate=v['recoveredBy11']/v['initiallyRecognized'] if v['initiallyRecognized'] else None,meanLatency=v['latencySum']/v['everRecovered'] if v['everRecovered'] else None) for k,v in recovery.items()},
        stability={k:dict(v,meanL1=v['L1']/v['n'] if v['n'] else None,switchRate=v['switches']/v['nonTiedTransitions'] if v['nonTiedTransitions'] else None) for k,v in stability.items()},
        buildContext={k:dict(n=v[0],Top1=v[1]/v[0],Top3=v[2]/v[0],MRR=v[3]/v[0],NormalizedRank=v[4]/v[0]) for k,v in build_context.items()}))

def primary(result,s,f):
    return statistics.mean(v['Brier'] for k,v in result['metrics'].items() if k.startswith(s+'/'+f+'/availability-3/stage=') and v['n'])

def compare():
    guard();lane=load(OUT/'lane-metrics.json');replay=load(OUT/'contextual-replay.json');dev=('LCI','MKM','OTJ','ONE','MOM');cal=('DMU','BRO','WOE');families=('A','B','C','current-derived','uniform','current-pack-quality-mass')
    tables={s:{f:primary(lane,s,f) for f in families} for s in COMPATIBLE}
    macro={part:{f:statistics.mean(tables[s][f] for s in sets) for f in families} for part,sets in (('calibration',cal),('development',dev))}
    chosen=min(('A','B','C'),key=lambda f:(macro['development'][f],f));reference=min(('current-derived','uniform','current-pack-quality-mass'),key=lambda f:macro['development'][f]);improvement=1-macro['development'][chosen]/macro['development'][reference]
    metrics=replay['Metrics'];better=sum(tables[s][chosen]<min(tables[s][f] for f in ('current-derived','uniform','current-pack-quality-mass')) for s in dev)
    def context(f,field):return sum(metrics['lane-'+f+' | set='+s][field]*metrics['lane-'+f+' | set='+s]['Eligible'] for s in dev)/sum(metrics['lane-'+f+' | set='+s]['Eligible'] for s in dev)
    def recovery(f):
        counts=[lane['recovery'].get(s+'/'+f,{}) for s in dev];n=sum(v.get('initiallyRecognized',0) for v in counts)
        return sum(v.get('recoveredBy11',0) for v in counts)/n if n else None
    candidate_recovery=recovery(chosen);current_recovery=recovery('current-derived')
    contextual=context(chosen,'Top1')>=context('current-derived','Top1') and context(chosen,'MeanReciprocalRank')>=context('current-derived','MeanReciprocalRank')
    early_ok=all(lane['early'][s+'/'+chosen+'/0.5']['falseEarlyLockRate']<=lane['early'][s+'/current-derived/0.5']['falseEarlyLockRate'] for s in dev)
    recovery_ok=candidate_recovery is not None and current_recovery is not None and candidate_recovery>=current_recovery
    convincing=improvement>=.01 and better>=4 and contextual and early_ok and recovery_ok
    result=dict(version=VERSION,best=chosen,reference=reference,macro=macro,perSet=tables,relativeDevelopmentImprovement=improvement,setsBeatingAllReferences=better,
        contextualDevelopment={f:{field:context(f,field) for field in ('Top1','Top3','MeanReciprocalRank','MeanNormalizedRank')} for f in ('A','B','C','current-derived')},
        developmentGate=dict(predictive=improvement>=.01,perSet=better>=4,contextual=contextual,earlyLock=early_ok,recovery=recovery_ok),convincing=convincing,
        qualifiedForIntegration=False,holdoutsConsumed=False,recoveryDevelopment=dict(candidate=candidate_recovery,current=current_recovery),
        recommendation='No final holdout release without the complete preregistered development gate. Preserve production; Phase D not started.')
    dump(OUT/'comparison.json',result);print(json.dumps(result,indent=2));return result

def freeze():
    result=load(OUT/'comparison.json');path=OUT/'selection.json'
    inputs={str(p):sha(p) for p in (OUT/'position-policy.json',OUT/'temporal-calibration.json',OUT/'contextual-replay.json',OUT/'lane-metrics.json',OUT/'comparison.json',DATA/'predictions.jsonl.gz')}
    value=dict(version=VERSION,best=result['best'],convincing=result['convincing'],parameters=load(OUT/'temporal-calibration.json')['selected'][result['best']]['config'],inputs=inputs,
        splitSha256=sha('V2_PHASE_B1_SPLIT_MANIFEST.json'),holdoutsUntouched=['MH3','DFT'],productionChanged=False)
    if path.exists():
        if load(path)!=value:raise ValueError('Selection immutable; no retuning')
    else:dump(path,value)
    print('Frozen',value['best'],'convincing=',value['convincing'])

def final():
    guard();selection=load(OUT/'selection.json')
    for p,h in selection['inputs'].items():
        if sha(p)!=h:raise ValueError('Research changed after selection')
    if not selection['convincing']:
        dump(OUT/'holdout-status.json',dict(consumed=False,sets=['MH3','DFT'],reason='Preregistered development gate fails; no files downloaded or performance read; no release lock created'))
        print('MH3/DFT remain untouched: development gate fails.');return
    raise ValueError('This published study has no approved convincing final release; do not reuse its holdouts through this command.')

def golden():
    guard();result=load('docs/evidence/v2-phase-b1/golden-portability.json')
    from phase_b_core import soft_limit
    fixture=load('tests/Fixtures/phase9e2b-p1p8-v1.6.2.json');checks=[]
    for card in fixture['Cards']+fixture['UldarosNonRegression']:
        before=card['After'];w=before['Weights'];weighted=w['Quality']*before['EffectiveQualityComponent']+w['Lane']*before['LaneComponent']+w['Archetype']*before['ArchetypeComponent']+w['DeckNeed']*before['DeckNeedComponent']
        exact=50*soft_limit(.5+1.4*(weighted-.5),.45)
        if abs(exact-50*before['ContextualValue'])>1e-10:raise ValueError('Missing-position fallback recomposition differs')
        checks.append(dict(name=card['Name'],rawQ=before['QualityComponent'],R=before['QualityRelevance'],C=before['ColorCommitment']['Strength'] if before['ColorCommitment'] else None,
            exact=exact,QADCommitmentUnchanged=True,missingLDeclined=True,LRetained=before['LaneComponent']))
    dump(OUT/'golden-reference.json',dict(productionQCommitmentUnchanged=True,reference=result,usedForTuning=False,
        checks=checks,missingPositionPolicy='FRA has no qualified positional dataset: decline experimental substitution and retain existing L, with explicit provenance; missing individual-card expectation contributes no evidence'))
