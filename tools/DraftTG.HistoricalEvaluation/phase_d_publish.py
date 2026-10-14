"""Post-fit diagnostics, pick-time replay, immutable gate and publication."""
import statistics,subprocess,sqlite3,xml.etree.ElementTree as ET
from phase_d_core import *
from phase_d_models import fit,Inference
from phase_d_research import rows,structures,tasks,evaluate

def diagnostics():
    guard();m=meta();result=[];features=[]
    for s in COMPATIBLE:
        model=load(DATA/f'{s}.model.json');inf=Inference(model,m,qualities(),s);desc=[]
        decks=rows(s);summaries=[]
        for r in decks:
            cards=r['cards'];lands=sum(n for i,n in cards.items() if m[i]['land']);spells=40-lands
            curve=collections.Counter();unknownMana=0;removal=0;untagged=0;fixing=0
            for i,n in cards.items():
                md=m[i]
                if not md['land']:
                    if md['manaValue'] is None:unknownMana+=n
                    else:curve[str(min(6,int(md['manaValue'])))]+=n
                    removal+=n*md['roleProfile']['RemovalWeight'];untagged+=n*(md['roleProfile']['RemovalWeight']==0)
                fixing+=n*('Fixing' in md['roleProfile']['ReliableRoles'])
            load0=spell_load(cards,m);total=float(load0.sum());base=shell(cards,m,'pair');splash=['WUBRG'[c] for c,x in enumerate(load0) if x>0 and 'WUBRG'[c] not in base]
            summaries.append(dict(lands=lands,creatures=sum(n for i,n in cards.items() if m[i]['creature']),noncreatures=spells-sum(n for i,n in cards.items() if m[i]['creature']),earlyPlays=curve['0']+curve['1']+curve['2'],curve=dict(curve),unknownMana=unknownMana,assignedRemovalLowerBound=removal,untaggedRemovalUnknown=untagged,assignedFixingLowerBound=fixing,splashColors=splash))
        features.append(dict(set=s,n=len(summaries),mean={k:float(np.mean([r[k] for r in summaries])) for k in ('lands','creatures','noncreatures','earlyPlays','unknownMana','assignedRemovalLowerBound','untaggedRemovalUnknown','assignedFixingLowerBound')},
            meanCurve={str(k):float(np.mean([r['curve'].get(str(k),0) for r in summaries])) for k in range(7)},splashBuilds=sum(bool(r['splashColors']) for r in summaries)))
        for label,d in inf.shells.items():
            for k,p in enumerate(d['factors']):
                tops=np.argsort(-p/d['p'])[:12];cards=[model['vocabulary'][i] for i in tops];traits=[]
                for r in decks:
                    if shell(r['cards'],m,model['context'])!=label:continue
                    state=inf.pool(r['cards']);belief=state['belief'][label][k] if state else 0;cards0=r['cards'];spell=sum(n for i,n in cards0.items() if not m[i]['land']);creatures=sum(n for i,n in cards0.items() if m[i]['creature']);mv=[(m[i]['manaValue'],n) for i,n in cards0.items() if not m[i]['land'] and m[i]['manaValue'] is not None]
                    traits.append((belief,r['games'],r['wins'],creatures/max(1,spell),sum(v*n for v,n in mv)/max(1,sum(n for _,n in mv))))
                total=sum(t[0] for t in traits)
                desc.append(dict(shell=label,factor=k,prevalence=float(d['prevalence'][k]),effectiveBuilds=float(d['prevalence'][k]*d['n']),
                    topCards=[dict(id=i,name=m[i]['name'],roles=m[i]['roleProfile']['Roles'],roleCoverage='Unassigned roles are unknown, not absence') for i in cards],
                    creatureRatio=sum(t[0]*t[3] for t in traits)/max(1,total),averageManaValue=sum(t[0]*t[4] for t in traits)/max(1,total),
                    observedGameWR=sum(t[0]*t[2] for t in traits)/max(1,sum(t[0]*t[1] for t in traits)),
                    description='Automatically learned factor; no authored archetype label enters calculations'))
        result.append(dict(set=s,factors=desc))
    dump(OUT/'archetype-diagnostics.json',dict(sets=result,roleInterpretation='Current precision-first classifier; untagged synergy/ramp roles unknown. Structured creature/land/mana/curve coverage reported.',outcomesDescriptiveOnly=True))
    dump(OUT/'build-features.json',dict(sets=features,roleMissingIsUnknown=True,removalFixingAreAssignedLowerBounds=True,featuresUsedForDescriptionsOnly=True))

def pairs():
    guard();m=meta();allpairs=[];uplift=[];start=time.perf_counter()
    for s in COMPATIBLE:
        model=load(DATA/f'{s}.model.json');rs=rows(s);inference=Inference(model,m,qualities(),s)
        for label,d in model['shells'].items():
            for a,b,e,c,observed,expected in d['pairs']:
                allpairs.append(dict(set=s,format='PremierDraft',shell=label,left=model['vocabulary'][a],right=model['vocabulary'][b],effect=e,confidence=c,observed=observed,expected=expected,sourceModelSha256=sha(DATA/f'{s}.model.json')))
            comparable=[r for r in rs if shell(r['cards'],m,model['context'])==label];strong=[r for r in comparable if r['games']>=5 and r['wins']/r['games']>=.6]
            counts=collections.Counter(i for r in strong for i in r['cards']);allcounts=collections.Counter(i for r in comparable for i in r['cards'])
            for i,n in counts.items():
                if n<10 or allcounts[i]<20:continue
                # Descriptive uplift is deliberately never imported into inference models.
                inside=[r for r in comparable if i in r['cards']];outside=[r for r in comparable if i not in r['cards']]
                wr=lambda rr:sum(r['wins'] for r in rr)/sum(r['games'] for r in rr) if rr else None
                uplift.append(dict(set=s,shell=label,id=i,name=m[i]['name'],strongerPresence=n,strongerBuilds=len(strong),allPresence=allcounts[i],allBuilds=len(comparable),strongerInclusion=n/max(1,len(strong)),allInclusion=allcounts[i]/max(1,len(comparable)),withObservedWR=wr(inside),withoutObservedWR=wr(outside)))
    write_rows(DATA/'pairs.jsonl.gz',allpairs);write_rows(DATA/'outcome-uplift.jsonl.gz',uplift)
    dump(OUT/'pair-affinity.json',dict(pairs=len(allpairs),positive=sum(p['effect']>0 for p in allpairs),negative=sum(p['effect']<0 for p in allpairs),
        maximumAbsoluteEffect=max((abs(p['effect']) for p in allpairs),default=0),topPositive=sorted(allpairs,key=lambda p:-p['effect'])[:40],topNegative=sorted(allpairs,key=lambda p:p['effect'])[:40],
        completeArtifactSha256=sha(DATA/'pairs.jsonl.gz'),seconds=time.perf_counter()-start,baseline='Latent within-shell expected co-occurrence; shared power/frequency not causal synergy',
        descriptiveUpliftRows=len(uplift),upliftExamples=sorted(uplift,key=lambda p:-(p['strongerInclusion']-p['allInclusion']))[:30],outcomesUsedForInference=False))

def predict():
    guard();selection=load(OUT/'selection.json');m=meta();q=qualities();models={s:Inference(load(DATA/f'{s}.model.json'),m,q,s) for s in COMPATIBLE};timings=[];count=0
    con=sqlite3.connect(B1/'bulk-replay.sqlite');sql='SELECT inputs FROM decisions ORDER BY set_code,format,source,draft,ordinal'
    def iterator():
        nonlocal count
        for payload, in con.execute(sql):
            i=json.loads(payload);s=i['SetCode'];allowed(s);start=time.perf_counter();pool={c['Card']['CanonicalId']:c['Count'] for c in i['PoolBeforePick'] if c['Card']['CanonicalId']};ids=sorted({c['Card']['CanonicalId'] for c in i['OfferedCards'] if c['Card']['CanonicalId']});out={}
            for family in ('A','B','C'):
                par=selection['parameters'][family];state=models[s].pool(pool,par['weight']);scores=models[s].score(state,ids,family,par['cold'])
                out[family]={cid:dict(fit=fit0,confidence=conf,applicable=bool(applicable and i['PoolIsComplete'] and all(c['Card']['CanonicalId'] for c in i['PoolBeforePick']))) for cid,(fit0,conf,applicable) in zip(ids,scores)}
            timings.append((time.perf_counter()-start)*1000);count+=1
            if count%10000==0:print('predicted',count,flush=True)
            yield dict(set=s,format=i['Format'],draft=i['DraftId'],pack=i['PackNumber'],pick=i['PickNumber'],models=out)
    write_rows(DATA/'predictions.jsonl.gz',iterator());con.close()
    dump(OUT/'prediction-provenance.json',dict(version=VERSION,decisions=count,sha256=sha(DATA/'predictions.jsonl.gz'),selectionSha256=sha(OUT/'selection.json'),
        pickTimeFeatures=['PoolBeforePick','offered identity','frozen Q metadata weighting only','disjoint model'],noLabelsRead=True,
        meanThreeModelMilliseconds=float(np.mean(timings)),p95ThreeModelMilliseconds=float(np.quantile(timings,.95)),estimatedPerCandidateMilliseconds=float(np.mean(timings))/30))

def replay():
    guard();from phase_c_replay import common,run
    path=pathlib.Path('data/historical/evaluation/phase-d/contextual.json');elapsed=run(common(path)+['--affinity',DATA/'predictions.jsonl.gz'],path.with_suffix('.log'))
    result=load(path);expected=load('docs/evidence/v2-phase-c/contextual-replay.json')['Metrics']['current | overall']
    if result['Metrics']['current | overall']!=expected:raise ValueError('Frozen baseline metrics changed')
    result.update(ProcessSeconds=elapsed,AOnly=True,baselineUnchanged=True,AssemblySha256=sha('tools/DraftTG.QualityEvidenceResearch/bin/Debug/net10.0/DraftTG.QualityEvidenceResearch.dll'))
    dump(OUT/'contextual-replay.json',result);association(path.with_name(path.name+'.affinity.jsonl'))

def association(path):
    known={(s,r['draft']):set(r['cards']) for s in COMPATIBLE for r in rows(s,'target')};groups=collections.defaultdict(lambda:collections.defaultdict(list));comparables=0;human=collections.defaultdict(list)
    predictions=iter(readrows(DATA/'predictions.jsonl.gz'));m=meta();con=sqlite3.connect(B1/'bulk-replay.sqlite');inputs=iter(con.execute('SELECT inputs FROM decisions ORDER BY set_code,format,source,draft,ordinal'))
    with path.open(encoding='utf-8-sig') as f:
        for line in f:
            r=json.loads(line)
            pred=next(predictions);inp=json.loads(next(inputs)[0]);coordinate=(r['SetCode'],r['DraftId'],r['PackNumber'],r['PickNumber'])
            if coordinate!=(pred['set'],pred['draft'],pred['pack'],pred['pick']) or coordinate!=(inp['SetCode'],inp['DraftId'],inp['PackNumber'],inp['PickNumber']):raise ValueError('Post-scoring join coordinate mismatch')
            pool={c['Card']['CanonicalId']:c['Count'] for c in inp['PoolBeforePick'] if c['Card']['CanonicalId'] in m};base=shell(pool,m)
            if not r['Complete'] or (r['SetCode'],r['DraftId']) not in known:continue
            comparables+=1;deck=known[(r['SetCode'],r['DraftId'])]
            stage='early' if r['PickNumber']<=5 else 'mid' if r['PickNumber']<=9 else 'late'
            for model,cards in r['Models'].items():
                actual=[c for c in cards if c['Id']==r['ActualPicked'] and c['CurrentPackRank']]
                if actual:
                    picked=min(actual,key=lambda c:c['CurrentPackRank']);value=picked['QualityComponent'];qb='unknown' if value is None else 'low' if value<.5 else 'medium' if value<.7 else 'high';include=picked['Id'] in deck
                    rank=picked['CurrentPackRank'];agreement=dict(Top1=float(rank==1),Top3=float(rank<=3),MRR=1/rank,normalizedRank=(rank-1)/max(1,len(cards)-1))
                    conf=0 if model=='current' else pred['models'][model].get(picked['Id'],{}).get('confidence',0);bucket='0-.25' if conf<.25 else '.25-.5' if conf<.5 else '.5-.75' if conf<.75 else '.75-1'
                    for key in ('Q='+qb,'eventual-inclusion='+str(include),'affinity-confidence='+bucket):human[model+' | '+key].append(agreement)
                for c in cards:
                    included=c['Id'] in deck;q=c['QualityComponent'];qb='unknown' if q is None else 'low' if q<.5 else 'medium' if q<.7 else 'high'
                    group='included' if included else 'not-included';nr=(c['CurrentPackRank']-1)/max(1,len(cards)-1) if c['CurrentPackRank'] else None
                    if nr is None:continue
                    conf=0 if model=='current' else pred['models'][model].get(c['Id'],{}).get('confidence',0);bucket='0-.25' if conf<.25 else '.25-.5' if conf<.5 else '.5-.75' if conf<.75 else '.75-1';colors=m.get(c['Id'],{}).get('colors',[]);colorfit='colorless' if not colors else 'pool-empty' if base=='colorless' else 'within-pool-shell' if all('WUBRG'[k] in base for k in colors) else 'off-pool-shell'
                    for key in ('overall','set='+r['SetCode'],'stage='+stage,'pick='+str(r['PickNumber']),'pack='+str(r['PackNumber']),'Q='+qb,'color-fit='+colorfit,'affinity-confidence='+bucket,'commitment='+str(int(r['Commitment']*4)/4),'actual-picked' if c['Id']==r['ActualPicked'] else 'offered-other'):
                        groups[model+' | '+key][group].append(nr)
    con.close();dump(OUT/'maindeck-association.json',dict(comparableDecisionsWithQualifiedBuild=comparables,metrics={k:{g:dict(n=len(v),normalizedRank=float(np.mean(v))) for g,v in gs.items()} for k,gs in groups.items()},humanBuildQualifiedStrata={k:mean_metrics(v) for k,v in human.items()},interpretation='Post-hoc modal-build association, not drafting correctness or final-deck truth; actual identity uses best occurrence when pack contains duplicates'))

def associations():association(pathlib.Path('data/historical/evaluation/phase-d/contextual.json.affinity.jsonl'))

def cold():
    guard();start=time.perf_counter();selection=load(OUT/'selection.json');best=selection['best'];params=selection['parameters'][best];choice=load(OUT/'representation.json')['selected'];m=meta();results=[]
    for s in COMPATIBLE:
        fullmodel=load(DATA/f'{s}.model.json');rs=sorted(rows(s),key=lambda r:digest(SEED+'/learning/'+r['draft']));ts=tasks(s,rows(s,'target'),fullmodel)
        for level in (50,100,250,500,1000,2500,'full'):
            used=rs if level=='full' else rs[:level]
            model,diag=(fullmodel,[]) if len(used)==len(rs) else fit(structures(used),m,choice['representation'],choice['context'])
            metrics,_=evaluate(s,model,ts,params['weight'],params['cold']);comparison={}
            for label,d in model['shells'].items():
                old=fullmodel['shells'].get(label)
                if old is None or len(old['factors'])!=len(d['factors']):comparison[label]=None;continue
                common=[i for i in model['vocabulary'] if i in fullmodel['vocabulary']];a=np.array(d['factors'])[:,[model['vocabulary'].index(i) for i in common]];b=np.array(old['factors'])[:,[fullmodel['vocabulary'].index(i) for i in common]];comparison[label]=stability(a,b)
            results.append(dict(set=s,requested=level,achieved=len(used),metrics=metrics,factors={k:len(v['factors']) for k,v in model['shells'].items()},stabilityToFull=comparison,artifactBytes=len(json.dumps(model)),
                confidenceBudget=len(used)/(len(used)+params['cold']),unknownCardPolicy='Retain current A'))
            print('cold',s,level,len(used),flush=True)
    dump(OUT/'cold-start.json',dict(results=results,seconds=time.perf_counter()-start,frozenParameters=True,qualification='Warm/full evidence budgets are continuous; no calibrated maturity threshold inferred from build count alone'))

def probes():
    guard();m=meta();q=qualities();best=load(OUT/'selection.json')['best'];model=load(DATA/'DMU.model.json');inf=Inference(model,m,q,'DMU');r=rows('DMU')[0];i=next(i for i in r['cards'] if not m[i]['land']);empty=inf.score(inf.pool({}),[i],best)[0]
    scores=inf.score(inf.pool(r['cards']),model['vocabulary'],best);lowpair=pair_effect(2,2,2,2);roleMissing=sum(not d['roleProfile']['HasStructuredMetadata'] for d in m.values())
    details=dict(emptyPoolRetainsA=empty[1]==0,missingCardDeclines=inf.score(inf.pool(r['cards']),['not-in-model'],best)[0][2] is False,
        lowSupportPairZero=lowpair==(0.,0.),boundedFitConfidence=all(.25<=fit0<=.75 and 0<=c<=1 for fit0,c,_ in scores),
        basicsExcluded=not any(m[i]['basic'] for i in model['vocabulary']),rareCannotCreateFactor='Factor vocabulary requires20 builds; complexity requires25 effective support and .8 stability',
        hybridSoftMembership=all(abs(v.sum()-1)<1e-10 for v in inf.pool(r['cards'])['belief'].values()),
        targetLeakageRejected=not any(evidence_group('DMU','PremierDraft',r['draft']) is False for r in rows('DMU')),
        sameColorPowerConfound='Measured by matched completion controls, not certified by synthetic scores',
        unstructuredRoles=roleMissing,splashPolicy='Spell loads select base shells; land/fixing features descriptive only',
        noOutcomeFit=True,noProductionChanges=True)
    dump(OUT/'failure-probes.json',details)
    cases=[r for r in readrows(DATA/'completion-cases.jsonl.gz') if r['matched'] and r['set'] in DEV]
    params=load(OUT/'selection.json')['parameters'];inference={s:Inference(load(DATA/f'{s}.model.json'),m,q,s) for s in DEV}
    for case in cases:
        for family in ('A','B','C'):
            par=params[family];inf0=inference[case['set']];state=inf0.pool(case['pool'],par['weight']);values=inf0.score(state,case['candidates'],family,par['cold'])
            case['scores'][family]=[.5+confidence*(fit0-.5) if applicable else .5 for fit0,confidence,applicable in values]
    # Freeze actual case coordinates/scores only after selection; never choose parameters from these cases.
    reps=sorted(cases,key=lambda r:-(rank_metrics(r['scores'][best])['MRR']-rank_metrics(r['scores']['frequency'])['MRR']))[:4]
    reps+=sorted(cases,key=lambda r:rank_metrics(r['scores'][best])['MRR']-rank_metrics(r['scores']['frequency'])['MRR'])[:4]
    dump(OUT/'archetype-regression-cases.json',dict(selectionSha256=sha(OUT/'selection.json'),usedForTuning=False,cases=reps,
        labels='Actual matched relational-fit/generic/off-fit cases; unassigned payoff/enabler roles remain unknown',
        caseExportCorrection='Portable cases use each family own frozen pool-weight parameters; initial ignored task trace used the first family weights. Primary aggregate metrics and selection are unaffected.'))

def final():
    guard();selection=load(OUT/'selection.json')
    for p,h in load(OUT/'analytical-source-freeze.json')['files'].items():
        if sha(p)!=h:raise ValueError('Analytical source changed after selection')
    for p,h in selection['inputs'].items():
        if sha(p)!=h:raise ValueError('Selection input changed')
    cold0=[r for r in load(OUT/'cold-start.json')['results'] if r['set'] in DEV and r['requested']==50];best=selection['best']
    coldpass=all(r['metrics'][best]['MRR']>=.95*max(v['MRR'] for k,v in r['metrics'].items() if k in ('color-only','frequency','Q','frequency-plus-Q')) for r in cold0)
    probe=load(OUT/'failure-probes.json');tests=load(OUT/'validation.json');gates={**selection['primaryGates'],'confounds':probe['lowSupportPairZero'] and probe['boundedFitConfidence'],
        'coldStart':coldpass and probe['emptyPoolRetainsA'] and probe['missingCardDeclines'],'goldens':tests['golden']==40 and tests['CSharp']['failed']==0}
    passed=all(gates.values())
    dump(OUT/'promotion-gate.json',dict(gates=gates,developmentPass=passed,qualifiedForFutureIntegration=False,
        reason='No production adoption in Phase D; holdout and compatibility review required' if passed else 'Development gate failed; retain production A',productionChanged=False))
    dump(OUT/'holdout-status.json',dict(sets=['MH3','DFT'],consumed=False,performance=None,
        reason='Development gate fails; no holdout acquisition or performance read' if not passed else 'Development gate passes; separately identified permitted D holdout release inputs unavailable. Holdouts retained; not an integration qualification.'))
    print('Development gate',gates,'MH3/DFT untouched',flush=True)

def benchmark():
    # Exact frozen-fit reproducibility, not hyperparameter analysis or artifact overwrite.
    guard();expected=load(DATA/'DMU.model.json');start=time.perf_counter();actual,_=fit(structures(rows('DMU')),meta(),expected['representation'],expected['context'])
    actual.update(set='DMU',format='PremierDraft',evidenceHash=sha(DATA/'DMU.builds.jsonl.gz'))
    if actual!=expected:raise ValueError('Frozen model fit does not reproduce')
    from phase_b_core import peak_memory_bytes
    dump(OUT/'fit-reproducibility.json',dict(set='DMU',evidenceBuilds=actual['n'],seconds=time.perf_counter()-start,peakWorkingSetBytes=peak_memory_bytes(),
        exactModelReproduced=True,modelSha256=sha(DATA/'DMU.model.json'),scope='Single-set frozen-fit validation; original whole-study Python fit peak unavailable; no models or parameters overwritten'))

def inference_benchmark():
    guard();selection=load(OUT/'selection.json');m=meta();inf=Inference(load(DATA/'DMU.model.json'),m,qualities(),'DMU');par=selection['parameters'][selection['best']]
    con=sqlite3.connect(B1/'bulk-replay.sqlite');poolms=[];scorems=[];candidateCount=0
    for payload, in con.execute("SELECT inputs FROM decisions WHERE set_code='DMU' ORDER BY format,source,draft,ordinal LIMIT 500"):
        i=json.loads(payload);pool={c['Card']['CanonicalId']:c['Count'] for c in i['PoolBeforePick'] if c['Card']['CanonicalId']};ids=sorted({c['Card']['CanonicalId'] for c in i['OfferedCards'] if c['Card']['CanonicalId']})
        t=time.perf_counter();state=inf.pool(pool,par['weight']);poolms.append((time.perf_counter()-t)*1000);t=time.perf_counter();inf.score(state,ids,selection['best'],par['cold']);scorems.append((time.perf_counter()-t)*1000);candidateCount+=len(ids)
    con.close();dump(OUT/'inference-benchmark.json',dict(set='DMU',family=selection['best'],picks=len(poolms),candidates=candidateCount,
        poolMeanMilliseconds=float(np.mean(poolms)),candidateAmortizedMilliseconds=sum(scorems)/candidateCount,pickMeanMilliseconds=float(np.mean(np.array(poolms)+scorems)),
        pickP95Milliseconds=float(np.quantile(np.array(poolms)+scorems,.95)),scope='Measured frozen-model inference; candidate cost amortized over actual offered identities; no target labels or outcomes read'))

def summary():
    guard();replay0=load(OUT/'contextual-replay.json');completion0=load(OUT/'build-completion.json');fit0=load(OUT/'model-fit.json');cold0=load(OUT/'cold-start.json');selection0=load(OUT/'selection.json');best=selection0['best']
    result=dict(version=VERSION,qualification=load(OUT/'build-qualification.json'),representation=load(OUT/'representation.json')['selected'],selection=selection0,gate=load(OUT/'promotion-gate.json'),
        primary=dict(metric='Power-matched masked-build completion MRR',calibration=[r for r in completion0['results'] if r['matched'] and r['set'] in CAL],development=[r for r in completion0['results'] if r['matched'] and r['set'] in DEV],
            uniformFrequencyDevelopmentMRR=float(np.mean([r['metrics']['frequency']['MRR'] for r in cold0['results'] if r['set'] in DEV and r['requested']=='full'])),
            controlsCaveat='Original primary controls use A-family calibration-selected quality pool weights; frozen full learning-curve controls also expose a stronger uniform-pool frequency comparator. No parameter retuning.'),
        secondary=dict(replayDecisions=replay0['Decisions'],completeComparable=replay0['Metrics']['complete-baseline | overall']['Eligible'],drafts=replay0['Drafts'],
            contextual={f:replay0['Metrics'][k+' | overall'] for f,k in (('current-A','complete-baseline'),('A','affinity-A'),('B','affinity-B'),('C','affinity-C'))},changes=replay0['Changes'][best+' | overall'],
            qualifiedModalAssociationDecisions=load(OUT/'maindeck-association.json')['comparableDecisionsWithQualifiedBuild']),
        factorCounts={s['set']:{d['shell']:d['factors'] for d in s['diagnostics']} for s in fit0['diagnostics']},
        coldStart=dict(policy='No model/card, unsupported shell or empty pool retains current A; confidence budget is not calibrated correctness',frozenParameters=True,learningCurve=[dict(set=r['set'],requested=r['requested'],achieved=r['achieved'],MRR=r['metrics'][best]['MRR']) for r in cold0['results']]),
        performance=dict(fitSeconds=fit0['seconds'],modelBytes=sum(r['bytes'] for r in fit0['models'].values()),replaySeconds=replay0['Seconds'],replayPeakBytes=replay0['PeakWorkingSetBytes'],
            inference=load(OUT/'inference-benchmark.json'),frozenFitReproduction=load(OUT/'fit-reproducibility.json')),
        holdout=load(OUT/'holdout-status.json'),tests=load(OUT/'validation.json'),productionChanged=False,
        recommendation='Retain production current A and all v1.6.2 components. No integration or next phase started. Any affinity follow-up must be separately preregistered and resolve frequency controls, cold coverage and card-level uncertainty before new holdout release.')
    dump(OUT/'summary.json',result);print(json.dumps({k:v for k,v in result.items() if k!='qualification'},indent=2))

def report():
    from phase_d_report import report as publish
    publish()

def validate():
    guard();log=pathlib.Path('data/historical/evaluation/phase-d/python-tests.log');p=subprocess.run([sys.executable,'tools/DraftTG.HistoricalEvaluation/test_phase_d.py'],capture_output=True,text=True);log.parent.mkdir(parents=True,exist_ok=True);log.write_text(p.stdout+p.stderr,encoding='utf-8')
    if p.returncode:raise ValueError(p.stdout+p.stderr)
    counters=collections.Counter()
    for path in pathlib.Path('data/historical/evaluation/phase-d/tests').glob('*.trx'):
        c=next(x for x in ET.parse(path).iter() if x.tag.endswith('Counters'))
        for k in ('total','passed','failed','notExecuted'):counters[k]+=int(c.get(k,0))
    if counters['total']!=1297 or counters['failed'] or counters['notExecuted']:raise ValueError('Incomplete solution validation')
    gold=load('data/historical/evaluation/phase-d/golden/golden.json')
    if not gold['Passed'] or gold['Tested']!=40:raise ValueError('Golden failure')
    buildpath=pathlib.Path('data/historical/phase-d-build-final.log');raw=buildpath.read_bytes();build=raw.decode('utf-16' if raw[:2] in (b'\xff\xfe',b'\xfe\xff') else 'utf-8-sig')
    if 'Build succeeded.' not in build or '0 Warning(s)' not in build or '0 Error(s)' not in build:raise ValueError('Build not clean')
    sources={str(p):sha(p) for p in sorted(pathlib.Path('tools/DraftTG.HistoricalEvaluation').glob('*phase_d*.py'))}
    for p in ('src/DraftTG.HistoricalEvaluation/ExperimentalAffinitySubstitution.cs','tools/DraftTG.QualityEvidenceResearch/Program.cs','tests/DraftTG.HistoricalEvaluation.Tests/ExperimentalAffinityTests.cs'):sources[p]=sha(p)
    dump(OUT/'execution-sources.json',dict(files=sources,fingerprint=digest(json.dumps(sources,sort_keys=True))))
    dump(OUT/'validation.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),dirtyTree=True,CSharp=dict(counters),pythonTests=8,golden=gold['Tested'],productionChanged=False,
        sourceFingerprint=digest(json.dumps(sources,sort_keys=True)),scoreAssemblySha256=sha('src/DraftTG.RecommendationEngine/bin/Debug/net10.0/DraftTG.RecommendationEngine.dll'),
        preservedFiles=len(load(OUT/'preservation.json')['files']),buildPassed=True,warnings=0,errors=0,baselineUnchanged=load(OUT/'contextual-replay.json')['baselineUnchanged']))
