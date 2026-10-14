"""Calibrated residual models; primary matching never relaxed after coverage results."""
from phase_d1_models import *
from phase_b_core import peak_memory_bytes

def generic(s,coeff=None,data=None):return Generic(data or load(DATA/f'{s}.generic.json'),coeff or load(OUT/'generic-fit.json')['selected']['coefficients'],meta(),qualities(),s)
def task_rows(s,part='target',secondary=False):
    path=DATA/f'{s}.{part}.tasks.jsonl.gz'
    if not secondary:return list(readrows(path))
    return [r for r in readrows(DDATA/'completion-cases.jsonl.gz') if r['set']==s and r['matched']]
def score_tasks(inference,ts,family,poolWeight='uniform',alpha=None):
    result=[]
    for t in ts:
        state=inference.pool(t['pool'],poolWeight);values=inference.predict(state,t['candidates'],family,alpha);base=baseline_values(inference.generic,t)[inference.control]
        scores=[v['probability'] for v in values];metrics=rank_metrics(scores);baseline=rank_metrics(base);conf=float(np.mean([v['confidence'] for v in values]));true=values[0]['probability']
        result.append(dict(draft=t['draft'],MRR=metrics['MRR'],metrics=metrics,baseline=baseline,gain=metrics['MRR']-baseline['MRR'],confidence=conf,
            utility=float(np.mean([v['utility'] for v in values])),residualBrier=float(np.mean((np.array(scores)-np.array([1,0,0,0,0]))**2)),
            logLoss=float(np.mean(logloss(scores,[1,0,0,0,0]))),trueInclusionProbability=true,baseTrueProbability=float(base[0]),shell=t.get('shell',shell(t['pool'],inference.m))))
    return result
def task_metrics(records):return mean_metrics([r['metrics'] for r in records])

def secondary_controls():
    guard()
    if (OUT/'secondary-baseline-freeze.json').exists():return
    result=[]
    for s in COMPATIBLE:
        gen=generic(s);ts=task_rows(s,secondary=True);b=collections.defaultdict(list)
        for t in ts:
            for c,v in baseline_values(gen,t).items():b[c].append(rank_metrics(v))
        result.append(dict(set=s,tasks=len(ts),metrics={c:mean_metrics(b[c]) for c in CONTROLS}))
    macro={c:float(np.mean([r['metrics'][c]['MRR'] for r in result if r['set'] in DEV])) for c in CONTROLS}
    dump(OUT/'secondary-baseline-freeze.json',dict(strongest=max(CONTROLS,key=lambda c:macro[c]),developmentMRR=macro,results=result,scope='Original D matched target tasks, nongating; controls frozen before candidate fitting'))
def reconstruction(inference,rs,family,weight):
    # Rank controls such as tied color-only are not calibrated binary probabilities.
    # Formulation selection uses the modeled generic inclusion offset, as preregistered.
    inference=Residual(inference.model,inference.m,inference.generic.q,'generic-logistic');errors=[]
    for r in rs:
        pool={i:n for i,n in r['cards'].items() if int(digest(SEED+'/mask/'+r['draft']+'/'+i)[:8],16)%2==0};state=inference.pool(pool,weight)
        candidates=[i for i in inference.index if not inference.m[i]['land'] and i not in pool]
        y=[i in r['cards'] for i in candidates];pred=inference.predict(state,candidates,family);errors.append(float(np.mean(logloss([v['probability'] for v in pred],y))))
    return float(np.mean(errors)) if errors else None

def fit():
    guard()
    if not (OUT/'baseline-freeze.json').exists():raise ValueError('Strong baseline must freeze before candidate fitting')
    secondary_controls();start=time.perf_counter();m=meta();q=qualities();coeff=load(OUT/'generic-fit.json')['selected']['coefficients'];control=load(OUT/'baseline-freeze.json')['strongest'];grid=[]
    # Fixed grid; only calibration evidence and utility audit choose formulation.
    for method in ('logistic','residual-nmf'):
        for rank in (1,2,4):
            per={}
            for s in CAL:
                rs=structures(rows(s));data=load(DATA/f'{s}.generic.json');cache=DATA/f'{s}.{method}.{rank}.cal-model.json'
                model=load(cache) if cache.exists() else fit_model(s,rs,data,coeff,m,q,method,rank)
                dump(cache,model)
                inf=Residual(model,m,q,control);per[s]=reconstruction(inf,rows(s,'utility'),'B','uniform')
            grid.append(dict(method=method,rank=rank,perSetLogLoss=per,macroLogLoss=float(np.mean(list(per.values())))));print('factor fit',method,rank,flush=True)
    best=min(grid,key=lambda r:(r['macroLogLoss'],r['rank'],r['method']!='logistic'));models={}
    for s in COMPATIBLE:
        path=DATA/f'{s}.{best["method"]}.{best["rank"]}.cal-model.json'
        model=load(path) if path.exists() else fit_model(s,structures(rows(s)),load(DATA/f'{s}.generic.json'),coeff,m,q,best['method'],best['rank'])
        models[s]=model;dump(DATA/f'{s}.model.json',model)
    weights={};weightGrid=[]
    for family in ('A','B','C'):
        for w in ('uniform','sqrt-capped3','modest-Q'):
            per={s:reconstruction(Residual(models[s],m,q,control),rows(s,'utility'),'B' if family=='C' else family,w) for s in CAL}
            weightGrid.append(dict(family=family,weight=w,perSetLogLoss=per,macroLogLoss=float(np.mean(list(per.values())))))
        weights[family]=min((r for r in weightGrid if r['family']==family),key=lambda r:(r['macroLogLoss'],r['weight']!='uniform'))['weight']
    dump(OUT/'model-fit.json',dict(version=VERSION,selected=best,grid=grid,poolWeights=weights,poolWeightGrid=weightGrid,seconds=time.perf_counter()-start,peakBytes=peak_memory_bytes(),
        baselineSha256=sha(OUT/'baseline-freeze.json'),noOutcomesOrTargetModels=True,modelHashes={s:sha(DATA/f'{s}.model.json') for s in COMPATIBLE}))

def utility():
    guard();control=load(OUT/'baseline-freeze.json')['strongest'];w=load(OUT/'model-fit.json')['poolWeights']['C'];results=[]
    for s in COMPATIBLE:
        model=load(DATA/f'{s}.model.json');inf=Residual(model,meta(),qualities(),control);records=score_tasks(inf,task_rows(s,'utility'),'B',w);group=collections.defaultdict(list)
        for r in records:group[r['shell']].append(r['gain'])
        measured={}
        for label in model['shells']:
            evidence=paired_utility(group[label]);model['shells'][label]['utility']=evidence['weight'];measured[label]=evidence
        dump(DATA/f'{s}.utility-model.json',model);results.append(dict(set=s,utility=measured,unmatchedAuditLogLoss=reconstruction(inf,rows(s,'utility'),'B',w)))
    dump(OUT/'shell-utility.json',dict(results=results,formula='clip((pairedMeanMRR-.84*SE)/.02,0,1), n>=10 else0',auditDisjoint=True,targetNotUsed=True,
        modelHashes={s:sha(DATA/f'{s}.utility-model.json') for s in COMPATIBLE}))

def cold_model(s,level):
    full=load(DATA/f'{s}.utility-model.json');rs=sorted(rows(s),key=lambda r:digest(SEED+'/cold/'+s+'/'+r['draft']));subset=rs if level=='full' else rs[:level]
    if len(subset)==len(rs):return full
    m=meta();q=qualities();coeff=full['coefficients'];data,_,_=population(s,structures(subset),m,full['generic']['vocabulary'])
    model=fit_model(s,structures(subset),data,coeff,m,q,full['method'],full['rank']);inf=Residual(model,m,q,load(OUT/'baseline-freeze.json')['strongest']);w=load(OUT/'model-fit.json')['poolWeights']['C'];records=score_tasks(inf,task_rows(s,'utility'),'B',w);group=collections.defaultdict(list)
    for r in records:group[r['shell']].append(r['gain'])
    for l in model['shells']:model['shells'][l]['utility']=paired_utility(group[l])['weight']
    return model

def calibrate():
    guard();start=time.perf_counter();control=load(OUT/'baseline-freeze.json')['strongest'];weights=load(OUT/'model-fit.json')['poolWeights'];safe=[];curves=[];xs=[]
    for level in (50,100,250,500,1000,'full'):
        models={s:cold_model(s,level) for s in CAL};xs.append(float(np.mean([m['n'] for m in models.values()])));levels=[]
        for alpha in (0,.25,.5,.75,1):
            per={}
            for s in CAL:
                records=score_tasks(Residual(models[s],meta(),qualities(),control),task_rows(s,'confidence'),'C',weights['C'],alpha);per[s]=dict(n=len(records),gain=float(np.mean([r['gain'] for r in records])) if records else None)
            valid=all(v['n']>0 and v['gain']>=0 for v in per.values());levels.append(dict(alpha=alpha,perSet=per,safe=valid))
        safe.append(max((r['alpha'] for r in levels if r['safe']),default=0));curves.append(dict(requested=level,achieved=xs[-1],grid=levels));print('calibrated cold',level,'safe alpha',safe[-1],flush=True)
    mapping=cold_envelope(xs,safe);calRecords=[]
    for s in CAL:
        inf=Residual(load(DATA/f'{s}.utility-model.json'),meta(),qualities(),control,mapping);calRecords+=score_tasks(inf,task_rows(s,'confidence'),'C',weights['C'])
    bins=[]
    for j in range(5):
        records=[r for r in calRecords if bin_conf(r['confidence'])==j];bins.append(dict(bin=j,n=len(records),meanConfidence=float(np.mean([r['confidence'] for r in records])) if records else None,
            lift=float(np.mean([r['gain'] for r in records])) if records else None,residualError=float(np.mean([r['residualBrier'] for r in records])) if records else None))
    isotonic=pav([b['lift'] or 0 for b in bins],[max(1,b['n']) for b in bins]);dump(OUT/'confidence-calibration.json',dict(mapping=mapping,curves=curves,bins=bins,predictedLift=isotonic,
        seconds=time.perf_counter()-start,calibrationSetsOnly=True,probabilityClaim=False,qualifiedCalibrationCoverage=sum(r['confidence']>0 for r in calRecords)>=100))

def completion():
    guard();start=time.perf_counter();control=load(OUT/'baseline-freeze.json')['strongest'];cal=load(OUT/'confidence-calibration.json');weights=load(OUT/'model-fit.json')['poolWeights'];result=[];allRecords=[]
    for s in COMPATIBLE:
        model=load(DATA/f'{s}.utility-model.json');inf=Residual(model,meta(),qualities(),control,cal['mapping'])
        for secondary in (False,True):
            ts=task_rows(s,secondary=secondary);metrics={}
            for f in ('A','B','C'):
                rr=score_tasks(inf,ts,f,weights[f]);metrics[f]=task_metrics(rr);allRecords.extend(dict(set=s,family=f,secondary=secondary,**r) for r in rr)
            baseline={c:mean_metrics([rank_metrics(baseline_values(inf.generic,t)[c]) for t in ts]) for c in CONTROLS}
            result.append(dict(set=s,secondary=secondary,tasks=len(ts),metrics=metrics,baselines=baseline));print('completion',s,'secondary' if secondary else 'primary',len(ts),flush=True)
    write_rows(DATA/'completion-records.jsonl.gz',allRecords);devRecords=[r for r in allRecords if not r['secondary'] and r['set'] in DEV and r['family']=='C'];bins=[]
    for j in range(5):
        rs=[r for r in devRecords if bin_conf(r['confidence'])==j];bins.append(dict(bin=j,n=len(rs),lift=float(np.mean([r['gain'] for r in rs])) if rs else None,
            meanConfidence=float(np.mean([r['confidence'] for r in rs])) if rs else None,residualError=float(np.mean([r['residualBrier'] for r in rs])) if rs else None,buildInclusionAssociation=float(np.mean([r['trueInclusionProbability'] for r in rs])) if rs else None))
    meaningful=[b for b in bins if b['n']>=30];violations=sum(a['lift']>b['lift']+.01 for a,b in zip(meaningful,meaningful[1:]));total=sum(b['n'] for b in bins)
    error=sum(b['n']*abs((b['lift'] or 0)-cal['predictedLift'][b['bin']]) for b in bins)/max(1,total);positive=sum(r['confidence']>0 for r in devRecords)
    dump(OUT/'completion.json',dict(results=result,seconds=time.perf_counter()-start,uncertainty=dict(bins=bins,weightedLiftError=error,monotonicViolations=violations,positiveConfidenceTasks=positive,
        qualified=error<=.02 and violations==0 and positive>=100),primaryNotRelaxed=True))

def select():
    guard();results=[r for r in load(OUT/'completion.json')['results'] if not r['secondary'] and r['set'] in DEV];control=load(OUT/'baseline-freeze.json')['strongest'];macro={f:float(np.mean([r['metrics'][f]['MRR'] for r in results if r['metrics'][f]['MRR'] is not None])) for f in ('A','B','C')};best=max(macro,key=lambda f:(macro[f],f=='A'));base=float(np.mean([r['baselines'][control]['MRR'] for r in results if r['baselines'][control]['MRR'] is not None]));gain=macro[best]/base-1
    improved=sum(r['metrics'][best]['MRR']>r['baselines'][control]['MRR'] for r in results if r['tasks']);loss=max((1-r['metrics'][best]['MRR']/r['baselines'][control]['MRR'] for r in results if r['tasks']),default=1)
    inputs={str(p):sha(p) for p in (OUT/'baseline-freeze.json',OUT/'secondary-baseline-freeze.json',OUT/'model-fit.json',OUT/'shell-utility.json',OUT/'confidence-calibration.json',OUT/'completion.json')}
    inputs.update({str(DATA/f'{s}.utility-model.json'):sha(DATA/f'{s}.utility-model.json') for s in COMPATIBLE})
    dump(OUT/'selection.json',dict(version=VERSION,best=best,macroAvailableSets=macro,baselineAvailableMRR=base,relativeAvailableGain=gain,improvedSets=improved,worstLoss=loss,
        coverage={r['set']:r['tasks'] for r in results},primaryGates=dict(primary=gain>=.02,perSet=improved>=4 and loss<=.02,coverage=all(r['tasks']>=300 for r in results)),
        parameters=dict(factor=load(OUT/'model-fit.json')['selected'],poolWeights=load(OUT/'model-fit.json')['poolWeights'],coldMapping=load(OUT/'confidence-calibration.json')['mapping']),inputs=inputs,
        noCompleteFiveSetClaim=True,holdoutsConsumed=False,productionChanged=False))
    print('Selected',best,'available-set gain',gain,'coverage FAIL',flush=True)

def cold():
    guard();start=time.perf_counter();control=load(OUT/'baseline-freeze.json')['strongest'];mapping=load(OUT/'confidence-calibration.json')['mapping'];weights=load(OUT/'model-fit.json')['poolWeights'];best=load(OUT/'selection.json')['best'];result=[]
    for s in COMPATIBLE:
        for level in (50,100,250,500,1000,'full'):
            model=cold_model(s,level);inf=Residual(model,meta(),qualities(),control,mapping);ts=task_rows(s);rr=score_tasks(inf,ts,best,weights[best]);cRecords=score_tasks(inf,ts,'C',weights['C'])
            support=collections.defaultdict(list)
            for r in cRecords:
                n=model['shells'].get(r['shell'],{}).get('n',0);support['<30' if n<30 else '30-99' if n<100 else '100+'].append(r)
            result.append(dict(set=s,requested=level,achieved=model['n'],best=task_metrics(rr),safeC=task_metrics(cRecords),control=mean_metrics([r['baseline'] for r in rr]),
                confidence=float(np.mean([r['confidence'] for r in cRecords])) if cRecords else None,byShellSupport={k:dict(n=len(v),lift=float(np.mean([r['gain'] for r in v])),confidence=float(np.mean([r['confidence'] for r in v]))) for k,v in support.items()}))
            print('cold',s,level,model['n'],flush=True)
    dump(OUT/'cold.json',dict(results=result,seconds=time.perf_counter()-start,parametersFrozen=True,undercoverageNotPassed=True))

def ablate():
    guard();control=load(OUT/'baseline-freeze.json')['strongest'];results=[]
    for s in DEV:
        inf=Residual(load(DATA/f'{s}.utility-model.json'),meta(),qualities(),control,load(OUT/'confidence-calibration.json')['mapping']);ts=task_rows(s,secondary=True);ranks=collections.defaultdict(list)
        for t in ts:
            state=inf.pool(t['pool']);components=inf.components(state,t['candidates']);base=baseline_values(inf.generic,t)[control]
            for name in ('generic','shell','factor','pair','factor-pair','gated-full'):
                if name=='gated-full':v=[a['probability'] for a in inf.predict(state,t['candidates'],'C')]
                else:
                    offset=np.array([c['shell'] if name=='shell' else c['factor'] if name=='factor' else .25*c['pair'] if name=='pair' else c['factor']+.25*c['pair'] if name=='factor-pair' else 0 for c in components]);v=sigmoid(logit(base)+offset)
                ranks[name].append(rank_metrics(v))
        results.append(dict(set=s,metrics={k:mean_metrics(v) for k,v in ranks.items()},scope='Legacy D matched tasks, descriptive nongating ablation'))
    dump(OUT/'ablations.json',dict(results=results,noSetSpecificRules=True))
