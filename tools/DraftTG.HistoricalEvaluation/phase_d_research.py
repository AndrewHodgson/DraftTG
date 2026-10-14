"""Qualified builds, preregistered controls and disjoint completion evaluation."""
import statistics,subprocess
from phase_d_core import *
from phase_d_models import fit,Inference

def rows(s,partition='fit'):
    allowed(s)
    return [r for r in readrows(DATA/f'{s}.builds.jsonl.gz') if r['partition']==partition]
def structures(rs):return [dict(cards=r['cards'],key=r['draft']) for r in rs]
def qualify():
    guard();DATA.mkdir(parents=True,exist_ok=True);start=time.perf_counter();m=meta();audits=[];inputs={str(DATA/'metadata.json'):sha(DATA/'metadata.json')}
    registered=load('V2_PHASE_B1_SPLIT_MANIFEST.json')['Sets']
    for base in (OLD,B1):
        for path in sorted(base.glob('*.primary-builds.jsonl.gz')):
            s,f=path.name.split('.')[:2]
            if s not in registered or s in ('MH3','DFT'):continue
            allowed(s);counts=collections.Counter();lands=collections.Counter();colors=collections.Counter();games=collections.Counter();opening=collections.Counter();valid=[]
            inputs[str(path)]=sha(path);source=base/f'{s}.{f}.qualification.json';qualification=load(source)
            g=qualification.get('games',qualification)
            if isinstance(g,list):g=g[0]
            for r in readrows(path):
                counts['availablePrimaryBuilds']+=1;reason=build_eligibility(r,m);counts[reason or 'qualified']+=1
                cards={c['Card']['CanonicalId']:c['Count'] for c in r['MainDeck'] if c['Card']['CanonicalId'] in m}
                lands[sum(n for i,n in cards.items() if m[i]['land'])]+=1;colors[shell(cards,m,'broad')]+=1
                if r['Configurations']==1:games[r['Games']]+=1
                opening['agreement' if r.get('OpeningObservedAgreement') else 'not-proven-agree']+=1
                if reason:counts['LOW']+=1;continue
                confidence='HIGH' if r['Games']>=5 and r.get('OpeningObservedAgreement') else 'MEDIUM';counts[confidence]+=1
                if s in COMPATIBLE and f=='PremierDraft':
                    part=('audit' if audit_group(s,r['DraftId']) else 'fit') if evidence_group(s,f,r['DraftId']) else 'target'
                    valid.append(dict(set=s,format=f,draft=r['DraftId'],cards=cards,partition=part,games=r['Games'],wins=r['Wins'],confidence=confidence,openingAgreement=r.get('OpeningObservedAgreement')))
            if s in COMPATIBLE and f=='PremierDraft':write_rows(DATA/f'{s}.builds.jsonl.gz',valid)
            audits.append(dict(set=s,format=f,counts=dict(counts),landCount=dict(lands),mainColorDistribution=dict(colors),knownModeSupportHistogram=dict(games),openingAgreement=dict(opening),partitions=dict(collections.Counter(v['partition'] for v in valid)),
                sourceBuildAudit=g.get('builds',{}),availableGameDrafts=g.get('sampledDrafts'),observedGames=g.get('sampledGames'),
                incompleteHistories='Unavailable as a proven completeness label; bounded samples and absent opening keys are not complete events',
                modeSupport='Exactly Games only for Configurations==1; unknown otherwise, declined',sourceSha256=sha(path)))
            print('qualified',s,f,counts['qualified'],flush=True)
    dump(OUT/'build-qualification.json',dict(datasets=audits,qualifiedAll=sum(a['counts'].get('qualified',0) for a in audits),modelQualified=sum(sum(a['partitions'].values()) for a in audits),seconds=time.perf_counter()-start,inputHashes=inputs,
        noOutcomeSelection=True,notFinalSubmittedDecks=True,models=list(COMPATIBLE),formats=['PremierDraft']))

def tasks(s,rs,model,matched=True,limit=500):
    m=meta();q=qualities();index={v:i for i,v in enumerate(model['vocabulary'])};gp=np.array(model['globalp']);result=[]
    for r in sorted(rs,key=lambda r:digest(SEED+'/task/'+s+'/'+r['draft'])):
        truecards=sorted((i for i in r['cards'] if not m[i]['land'] and i in index),key=lambda i:digest(SEED+'/'+r['draft']+'/'+i))
        for true in truecards:
            pool={i:n for i,n in r['cards'].items() if i!=true};candidates=[]
            for other in index:
                if other in r['cards'] or m[other]['land'] or m[other]['colors']!=m[true]['colors']:continue
                if matched:
                    if m[other]['manaValue'] is None or m[true]['manaValue'] is None or abs(m[other]['manaValue']-m[true]['manaValue'])>1:continue
                    if (s,other) not in q or (s,true) not in q or abs(q[(s,other)]-q[(s,true)])>.1:continue
                    if not .5<=gp[index[other]]/gp[index[true]]<=2:continue
                candidates.append(other)
            if len(candidates)<4:continue
            distract=sorted(candidates,key=lambda i:digest(SEED+'/distract/'+r['draft']+'/'+true+'/'+i))[:4]
            result.append(dict(draft=r['draft'],true=true,pool=pool,candidates=[true]+distract,matched=matched));break
        if len(result)>=limit:break
    return result

def evaluate(s,model,ts,weight='uniform',cold=250):
    inf=Inference(model,meta(),qualities(),s);scores=collections.defaultdict(list);examples=[]
    for t in ts:
        state=inf.pool(t['pool'],weight);c=t['candidates'];freq=inf.frequency(state,c);q=[inf.q.get((s,i),.5) for i in c]
        values={'color-only':[1]*len(c),'frequency':freq,'Q':q,'frequency-plus-Q':[math.log(max(1e-6,x))+2*y for x,y in zip(freq,q)]}
        for family in ('A','B','C'):
            outputs=inf.score(state,c,family,cold);values[family]=[.5+confidence*(fit-.5) if applicable else .5 for fit,confidence,applicable in outputs]
        for name,value in values.items():scores[name].append(rank_metrics(value))
        examples.append(dict(draft=t['draft'],true=t['true'],candidates=c,pool=t['pool'],scores=values))
    return {k:mean_metrics(v) for k,v in scores.items()},examples

def representation():
    guard();start=time.perf_counter();m=meta();results=[]
    for rep in ('binary','counts','nonland','sqrt'):
        for context in ('pair','broad','global'):
            errors={}
            for s in CAL:
                model,_=fit(structures(rows(s)),m,rep,context,latent=False,diagnose=False);inf=Inference(model,m,qualities(),s);error=[]
                for r in rows(s,'audit'):
                    partial={i:n for i,n in r['cards'].items() if int(digest(SEED+'/mask/'+i+r['draft'])[:8],16)%2==0}
                    state=inf.pool(partial);target=np.array([int(i in r['cards']) for i in model['vocabulary']],dtype=float)
                    predicted=np.array(inf.frequency(state,model['vocabulary']));error.append(float(np.mean(abs(predicted-target))))
                errors[s]=float(np.mean(error))
            results.append(dict(representation=rep,context=context,perSetMAE=errors,macroMAE=float(np.mean(list(errors.values())))))
            print('representation',rep,context,flush=True)
    best=min(results,key=lambda r:(r['macroMAE'],r['representation'],r['context']));dump(OUT/'representation.json',dict(selected=best,results=results,seconds=time.perf_counter()-start,scale='Identical binary card-inclusion target, deterministic half-build pool; rank-one profiles; disjoint evidence audit'))

def train():
    guard();start=time.perf_counter();choice=load(OUT/'representation.json')['selected'];m=meta();diagnostics=[];models={};grid=[]
    for s in COMPATIBLE:
        model,diag=fit(structures(rows(s)),m,choice['representation'],choice['context']);model.update(set=s,format='PremierDraft',evidenceHash=sha(DATA/f'{s}.builds.jsonl.gz'))
        dump(DATA/f'{s}.model.json',model);models[s]=model;diagnostics.append(dict(set=s,diagnostics=diag))
        print('fit',s,model['n'],len(model['shells']),flush=True)
    for family in ('A','B','C'):
        for weight in ('uniform','quality'):
            for cold in (50,250,500):
                per={}
                for s in CAL:
                    ts=tasks(s,rows(s,'audit'),models[s]);metrics,_=evaluate(s,models[s],ts,weight,cold);per[s]=metrics[family]
                grid.append(dict(family=family,weight=weight,cold=cold,perSet=per,MRR=float(np.mean([v['MRR'] for v in per.values()]))))
    selected={f:max((r for r in grid if r['family']==f),key=lambda r:(r['MRR'],-r['cold'],r['weight']=='uniform')) for f in ('A','B','C')}
    dump(OUT/'model-fit.json',dict(selected=selected,grid=grid,diagnostics=diagnostics,seconds=time.perf_counter()-start,representation=choice,
        models={s:dict(sha256=sha(DATA/f'{s}.model.json'),bytes=(DATA/f'{s}.model.json').stat().st_size,n=models[s]['n']) for s in models},noOutcomesUsed=True))

def completion():
    guard();start=time.perf_counter();params=load(OUT/'model-fit.json')['selected'];results=[];examples=[]
    for s in COMPATIBLE:
        model=load(DATA/f'{s}.model.json');rs=rows(s,'target')
        for matched in (True,False):
            ts=tasks(s,rs,model,matched);combined={};first=None
            for family in ('A','B','C'):
                par=params[family];metrics,ex=evaluate(s,model,ts,par['weight'],par['cold']);combined[family]=metrics[family]
                if first is None:first=metrics;examples.extend(dict(set=s,matched=matched,**x) for x in ex)
            for name in ('color-only','frequency','Q','frequency-plus-Q'):combined[name]=first.get(name,mean_metrics([]))
            results.append(dict(set=s,partition='calibration' if s in CAL else 'development',matched=matched,tasks=len(ts),metrics=combined))
            print('completion',s,'matched' if matched else 'same-color',len(ts),flush=True)
    write_rows(DATA/'completion-cases.jsonl.gz',examples)
    dump(OUT/'build-completion.json',dict(results=results,seconds=time.perf_counter()-start,pairedIdenticalTasks=True,labelsNotInferenceFeatures=True))

def select():
    guard();results=[r for r in load(OUT/'build-completion.json')['results'] if r['matched'] and r['set'] in DEV];models=('A','B','C','color-only','frequency','Q','frequency-plus-Q')
    macro={f:float(np.mean([r['metrics'][f]['MRR'] for r in results])) for f in models};best=max(('A','B','C'),key=lambda f:(macro[f],f=='A'));ref=max(models[3:],key=lambda f:macro[f]);relative=macro[best]/macro[ref]-1
    wins=sum(r['metrics'][best]['MRR']>max(r['metrics'][f]['MRR'] for f in models[3:]) for r in results)
    loss=max(1-r['metrics'][best]['MRR']/max(r['metrics'][f]['MRR'] for f in models[3:]) for r in results)
    gates=dict(primary=relative>=.02,perSet=wins>=3 and loss<=.05,coverage=len(results)==5 and all(r['tasks']>=200 for r in results))
    inputs={str(p):sha(p) for p in (OUT/'representation.json',OUT/'model-fit.json',OUT/'build-completion.json')}
    inputs.update({str(DATA/f'{s}.model.json'):sha(DATA/f'{s}.model.json') for s in COMPATIBLE})
    path=OUT/'selection.json';value=dict(version=VERSION,best=best,reference=ref,developmentMRR=macro,relativeImprovement=relative,setsImproving=wins,worstRelativeLoss=loss,primaryGates=gates,
        parameters=load(OUT/'model-fit.json')['selected'],inputs=inputs,holdoutsConsumed=False,productionChanged=False)
    if path.exists():raise ValueError('Selection already frozen')
    dump(path,value);print('Frozen',best,'primary gates',gates,flush=True)
