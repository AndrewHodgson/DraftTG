"""D1 diagnostics and reuse of frozen D C# A-only replay. No production writes."""
import xml.etree.ElementTree as ET
from phase_d1_research import *

def metadata_feature(md):
    mv=md.get('manaValue');return np.array([md['creature'],md.get('instantSorcery',False),md.get('artifactEnchantment',False),mv is not None and mv<=2,mv is not None and mv>=5,
        md['roleProfile']['RemovalWeight'],md['roleProfile']['RemovalWeight']==0,not md['colors'],len(md['colors'])>1],dtype=float)
def metadata_prior():
    guard();m=meta();features={i:metadata_feature(md) for i,md in m.items()};groups=collections.defaultdict(list)
    for s in CAL:
        for r in rows(s):
            cards=[features[i] for i in r['cards'] if not m[i]['land']];groups[s+'/'+shell(r['cards'],m)].append(np.mean(cards,axis=0))
    residual=[]
    for vs in groups.values():
        x=np.array(vs);residual.extend(x-x.mean(axis=0))
    covariance=np.array(residual).T@np.array(residual)/max(1,len(residual));covariance/=max(float(np.linalg.norm(covariance)),1e-12)
    # Independent prospective optional prior, never mixed into the selected model.
    results=[]
    for s in COMPATIBLE:
        gen=generic(s);ts=task_rows(s,'confidence' if s in CAL else 'target');control=load(OUT/'baseline-freeze.json')['strongest'];scores={alpha:[] for alpha in (0,.05,.1)}
        for t in ts:
            values=[features[i] for i in t['pool'] if i in features and not m[i]['land']];pool=np.mean(values,axis=0) if values else np.zeros(9);raw=np.array([float(pool@covariance@features[i]) for i in t['candidates']]);base=baseline_values(gen,t)[control]
            for alpha in scores:scores[alpha].append(rank_metrics(sigmoid(logit(base)+alpha*np.clip(raw,-1,1))))
        results.append(dict(set=s,n=len(ts),metrics={str(alpha):mean_metrics(v) for alpha,v in scores.items()}))
    qualifying=all(r['n']>0 for r in results if r['set'] in CAL)
    dump(OUT/'metadata-prior.json',dict(calibrationOnlyCovariance=covariance.tolist(),calibrationStructureRows=len(residual),results=results,adopted=False,
        reason='Calibration primary confidence coverage insufficient; optional prior remains0 and is not an approved cold fallback' if not qualifying else 'No automatic adoption: separate incremental/cold qualification required',
        missingRoles='Unassigned removal indicator explicit; never interpreted as proven absence',noQorOutcomeFit=True))

def predict():
    guard();selection=load(OUT/'selection.json');control=load(OUT/'baseline-freeze.json')['strongest'];mapping=load(OUT/'confidence-calibration.json')['mapping'];weights=load(OUT/'model-fit.json')['poolWeights'];m=meta();q=qualities()
    models={s:Residual(load(DATA/f'{s}.utility-model.json'),m,q,control,mapping) for s in COMPATIBLE};con=sqlite3.connect(B1/'bulk-replay.sqlite');count=0;times=[];candidateCount=0;utilityCounts=collections.Counter()
    def iterator():
        nonlocal count,candidateCount
        for payload, in con.execute('SELECT inputs FROM decisions ORDER BY set_code,format,source,draft,ordinal'):
            i=json.loads(payload);s=i['SetCode'];allowed(s);t=time.perf_counter();pool={c['Card']['CanonicalId']:c['Count'] for c in i['PoolBeforePick'] if c['Card']['CanonicalId']};ids=sorted({c['Card']['CanonicalId'] for c in i['OfferedCards'] if c['Card']['CanonicalId']});out={}
            for family in ('A','B','C'):
                state=models[s].pool(pool,weights[family]);values=models[s].predict(state,ids,family);out[family]={cid:dict(fit=v['fit'],confidence=v['confidence'],applicable=bool(v['applicable'] and i['PoolIsComplete']),utility=v['utility']) for cid,v in zip(ids,values)}
            times.append((time.perf_counter()-t)*1000);candidateCount+=len(ids)*3;count+=1
            utilityCounts[s+'/active']+=sum(v['confidence']>0 for v in out['C'].values());utilityCounts[s+'/proposed']+=len(ids)
            if count%10000==0:print('predictions',count,flush=True)
            yield dict(version=VERSION,set=s,format=i['Format'],draft=i['DraftId'],pack=i['PackNumber'],pick=i['PickNumber'],models=out)
    write_rows(DATA/'predictions.jsonl.gz',iterator());con.close();dump(OUT/'prediction-provenance.json',dict(version=VERSION,decisions=count,sha256=sha(DATA/'predictions.jsonl.gz'),selectionSha256=sha(OUT/'selection.json'),
        features='Current PoolBeforePick, offered identity, disjoint model, frozen metadata/Q only; SELECT inputs column, no labels/outcomes',
        meanThreeFamilyMilliseconds=float(np.mean(times)),p95ThreeFamilyMilliseconds=float(np.quantile(times,.95)),amortizedActualCandidateMilliseconds=sum(times)/max(1,candidateCount),activity=dict(utilityCounts)))

def replay():
    guard();from phase_c_replay import common,run
    path=pathlib.Path('data/historical/evaluation/phase-d1/contextual.json');elapsed=run(common(path)+['--affinity',DATA/'predictions.jsonl.gz'],path.with_suffix('.log'));r=load(path)
    if r['Metrics']['current | overall']!=load(DOUT/'contextual-replay.json')['Metrics']['current | overall']:raise ValueError('Baseline replay changed')
    rawVersion=r['Version'];r.update(Version=VERSION,RawFrozenHelperVersion=rawVersion,processSeconds=elapsed,AOnly=True,baselineUnchanged=True,predictionsSha256=sha(DATA/'predictions.jsonl.gz'))
    dump(OUT/'contextual-replay.json',r);dump(OUT/'execution-scope.json',dict(version=VERSION,rawHelperVersion=rawVersion,
        runner='Unmodified DraftTG.QualityEvidenceResearch and ExperimentalAffinitySubstitution from Phase D',families={'A':'D1-A','B':'D1-B','C':'D1-C'},
        sourceSha256=sha('tools/DraftTG.QualityEvidenceResearch/Program.cs'),predictionSpecSha256=sha(OUT/'prediction-provenance.json'),sourceAndProductionUnchanged=True))

def associate():
    guard();known={(s,r['draft']):set(r['cards']) for s in COMPATIBLE for r in rows(s,'target')};groups=collections.defaultdict(lambda:collections.defaultdict(list));confidenceStrata=collections.defaultdict(list);count=0
    path=pathlib.Path('data/historical/evaluation/phase-d1/contextual.json.affinity.jsonl');preds=iter(readrows(DATA/'predictions.jsonl.gz'));con=sqlite3.connect(B1/'bulk-replay.sqlite');ins=iter(con.execute('SELECT inputs FROM decisions ORDER BY set_code,format,source,draft,ordinal'))
    gen={s:generic(s) for s in COMPATIBLE}
    # Join the preserved D-C comparison by the same coordinates; no D rerun.
    dfile=pathlib.Path('data/historical/evaluation/phase-d/contextual.json.affinity.jsonl')
    with path.open(encoding='utf-8-sig') as f,dfile.open(encoding='utf-8-sig') as old:
        for line,dline in zip(f,old,strict=True):
            r=json.loads(line);d=json.loads(dline);p=next(preds);i=json.loads(next(ins)[0]);key=(r['SetCode'],r['DraftId'],r['PackNumber'],r['PickNumber'])
            if key!=(d['SetCode'],d['DraftId'],d['PackNumber'],d['PickNumber']) or key!=(p['set'],p['draft'],p['pack'],p['pick']):raise ValueError('Stale/mismatched replay join')
            if not r['Complete'] or (r['SetCode'],r['DraftId']) not in known:continue
            count+=1;s=r['SetCode'];deck=known[(s,r['DraftId'])];pool={c['Card']['CanonicalId']:c['Count'] for c in i['PoolBeforePick'] if c['Card']['CanonicalId']};state=gen[s].pool(pool);models={**r['Models'],'Phase-D-C':d['Models']['C']};label=shell(pool,gen[s].m)
            knownids=sorted({c['Id'] for c in r['Models']['current'] if c['Id'] in gen[s].index});genericProbability=dict(zip(knownids,gen[s].probability(state,knownids,'generic-logistic')))
            for model,cards in models.items():
                for c in cards:
                    if c['CurrentPackRank'] is None:continue
                    cid=c['Id'];included=cid in deck;rank=(c['CurrentPackRank']-1)/max(1,len(cards)-1);gp=float(genericProbability[cid]) if cid in genericProbability else None
                    pb='unknown' if gp is None else 'low' if gp<.05 else 'medium' if gp<.2 else 'high';q=c['QualityComponent'];qb='unknown' if q is None else 'low' if q<.5 else 'medium' if q<.7 else 'high';prediction=p['models'].get(model,{}).get(cid,{});conf=prediction.get('confidence',0);utility=prediction.get('utility',0)
                    keys=('overall','set='+s,'pack='+str(r['PackNumber']),'pick='+str(r['PickNumber']),'stage='+('early' if r['PickNumber']<=5 else 'mid' if r['PickNumber']<=9 else 'late'),'shell='+label,'generic='+pb,'Q='+qb,'confidence='+str(bin_conf(conf)),'utility='+('zero' if utility==0 else 'positive'),'commitment='+str(int(r['Commitment']*4)/4))
                    for k in keys:groups[model+' | '+k]['included' if included else 'not-included'].append(rank)
                    if model in ('A','B','C'):confidenceStrata[model+'/'+str(bin_conf(conf))].append((included,gp))
    con.close();dump(OUT/'build-association.json',dict(decisions=count,metrics={k:{g:dict(n=len(v),normalizedRank=float(np.mean(v))) for g,v in gs.items()} for k,gs in groups.items()},
        confidenceInclusion={k:dict(n=len(v),observedInclusion=float(np.mean([x[0] for x in v])),meanGenericProbability=float(np.mean([x[1] for x in v if x[1] is not None]))) for k,v in confidenceStrata.items()},
        interpretation='Post-scoring modal-build labels, never pick-time features; strata use known generic probability and utility'))

def otj():
    guard();from phase_b_core import correlation
    m=meta();q=qualities();result=[]
    for s in COMPATIBLE:
        model=load(DATA/f'{s}.utility-model.json');data=model['generic'];known=rows(s);pairs=[p for d in model['shells'].values() for p in d['pairs']];fq=[(data['counts'][j]/data['n'],q.get((s,i),.5)) for j,i in enumerate(data['vocabulary']) if not m[i]['land']]
        splash=multicolor=fixing=bonus=total=removal=untagged=0;diversity=set();factorEntropy=[]
        for d in model['shells'].values():
            for v in d['loadings']:
                x=np.abs(v);p0=x/max(float(x.sum()),1e-12);factorEntropy.append(float(-sum(a*math.log(a) for a in p0 if a>0)))
        for r in known:
            cards=r['cards'];base=shell(cards,m);splash+=any(any('WUBRG'[c] not in base for c in m[i]['colors']) for i in cards if not m[i]['land']);diversity.add(tuple(sorted((i,n) for i,n in cards.items() if not m[i]['land'])))
            for i,n in cards.items():
                if m[i]['land']:fixing+=n*('Fixing' in m[i]['roleProfile']['ReliableRoles']);continue
                total+=n;multicolor+=n*(len(m[i]['colors'])>1);bonus+=n*(m[i].get('printingSet') not in ('',s));removal+=n*m[i]['roleProfile']['RemovalWeight'];untagged+=n*(m[i]['roleProfile']['RemovalWeight']==0)
        ts=task_rows(s);g=generic(s);gaps=[]
        for task in ts:
            candidates=task['candidates'];state=g.pool(task['pool']);p0=g.probability(state,candidates,'generic-logistic');true=candidates[0]
            gaps.append(dict(Q=float(np.mean([abs(q.get((s,true),.5)-q.get((s,c),.5)) for c in candidates[1:]])),mana=float(np.mean([abs(m[true]['manaValue']-m[c]['manaValue']) for c in candidates[1:]])),generic=float(np.mean(abs(p0[1:]-p0[0])))))
        result.append(dict(set=s,fitBuilds=len(known),shellCounts={l:d['n'] for l,d in model['shells'].items()},rank=model['rank'],method=model['method'],
            stability={l:d['stability'] for l,d in model['shells'].items()},utility={l:d['utility'] for l,d in model['shells'].items()},shellEntropy=float(-sum((d['n']/len(known))*math.log(d['n']/len(known)) for d in model['shells'].values())),
            frequencyQCorrelation=correlation([x[0] for x in fq],[x[1] for x in fq]),meanInclusion=float(np.mean(data['gp'])),pairs=len(pairs),meanPairAbsEffect=float(np.mean([abs(p[2]) for p in pairs])) if pairs else 0,
            splashRate=splash/max(1,len(known)),multicolorSpellRate=multicolor/max(1,total),assignedFixingPerBuild=fixing/max(1,len(known)),bonusPrintingFraction=bonus/max(1,total),distinctSpellBuilds=len(diversity),
            factorLoadingEntropy=float(np.mean(factorEntropy)),factorPrevalence='Signed residual axes have no cluster-probability prevalence; shell prevalence and loading entropy are proxies',
            assignedRemovalPerSpell=removal/max(1,total),untaggedRemovalUnknownFraction=untagged/max(1,total),distractorGaps={k:float(np.mean([r[k] for r in gaps])) if gaps else None for k in ('Q','mana','generic')},
            roleCoverage='Current structured/assigned-role provenance only; untagged roles unknown',primaryTasks=load(OUT/'baseline-freeze.json')['coverage'].get(s),
            noHandPatch=True))
    dump(OUT/'otj-diagnostics.json',dict(sets=result,interpretation='Descriptive cross-set comparison; correlations do not identify a causal OTJ root cause. Primary coverage and utility/calibration shortages are generic, not an OTJ branch.'))

def probes():
    guard();m=meta();q=qualities();model=load(DATA/'DMU.utility-model.json');inf=Residual(model,m,q,load(OUT/'baseline-freeze.json')['strongest'],load(OUT/'confidence-calibration.json')['mapping']);sample=rows('DMU')[0];ids=[i for i in sample['cards'] if i in inf.index and not m[i]['land']];state=inf.pool(sample['cards']);before=json.dumps({k:v.tolist() if isinstance(v,np.ndarray) else v for k,v in state.items() if k!='latent'},default=lambda x:x.tolist())
    values=inf.predict(state,ids,'C');inf.predict(state,list(reversed(ids)),'C');after=json.dumps({k:v.tolist() if isinstance(v,np.ndarray) else v for k,v in state.items() if k!='latent'},default=lambda x:x.tolist())
    d,c,se=card_residual(np.array([20.,0]),200,np.array([.1,.1]));empty=inf.predict(None,ids,'C');pair=pair_uncertainty(2,2,2,2,[1,1],1)
    result=dict(emptyPoolExactFallback=all(v['confidence']==0 for v in empty),missingCardDeclined=not inf.predict(state,['absent'],'C')[0]['applicable'],candidateNeverChangesPool=before==after,
        twentyObservationCardConfidenceAtMost20Over70=float(c[0])<=20/70,zeroObservedCardConfidence=float(c[1])==0,twoOfTwoPairZero=pair==(0.,0.),boundedFitConfidence=all(.25<=v['fit']<=.75 and 0<=v['confidence']<=1 for v in values),
        falseFactorNoUtilitySuppressed=paired_utility([-0.01]*20)['weight']==0,unstableFactorSuppressed=all(d['factorConfidence']==0 for s in COMPATIBLE for d in load(DATA/f'{s}.utility-model.json')['shells'].values() if d['stability']<.8),
        hybridBeliefsSoft=all(x>0 for x in state['shellWeights']) and abs(sum(state['shellWeights'])-1)<1e-10,splashDoesNotChangeBaseShell=shell({'r':12,'g':10,'u':1},{'r':dict(colors=[3],land=False),'g':dict(colors=[4],land=False),'u':dict(colors=[1],land=False)})=='RG',
        baselineUtilityNotGuaranteedByStableFactors=True,goodStuffRequiresUtility=True,noSetNameBranch=True,
        activeMultiArchetypeBenefitDemonstrated=False,offArchetypeSameColorSeparationQualified=False)
    dump(OUT/'failure-probes.json',result)

def benchmark():
    guard();m=meta();q=qualities();inf=Residual(load(DATA/'DMU.utility-model.json'),m,q,load(OUT/'baseline-freeze.json')['strongest'],load(OUT/'confidence-calibration.json')['mapping']);weight=load(OUT/'model-fit.json')['poolWeights']['C'];con=sqlite3.connect(B1/'bulk-replay.sqlite');pool=[];pick=[];score=[];n=0
    for payload, in con.execute("SELECT inputs FROM decisions WHERE set_code='DMU' ORDER BY format,source,draft,ordinal LIMIT 500"):
        i=json.loads(payload);cards={c['Card']['CanonicalId']:c['Count'] for c in i['PoolBeforePick'] if c['Card']['CanonicalId']};ids=[c['Card']['CanonicalId'] for c in i['OfferedCards'] if c['Card']['CanonicalId']];t=time.perf_counter();state=inf.pool(cards,weight);pool.append((time.perf_counter()-t)*1000);t=time.perf_counter();inf.predict(state,ids,'C');score.append((time.perf_counter()-t)*1000);n+=len(ids)
    con.close();pick=np.array(pool)+score;models={s:(DATA/f'{s}.utility-model.json').stat().st_size for s in COMPATIBLE};dump(OUT/'performance.json',dict(picks=len(pool),candidates=n,inferenceMeanMs=float(np.mean(pick)),inferenceP95Ms=float(np.quantile(pick,.95)),candidateAmortizedMs=sum(score)/n,perSetModelBytes=models,totalModelBytes=sum(models.values()),noRawRowsInArtifacts=True))

def validate():
    guard();path=pathlib.Path('data/historical/evaluation/phase-d1');path.mkdir(parents=True,exist_ok=True);p=subprocess.run([sys.executable,'tools/DraftTG.HistoricalEvaluation/test_phase_d1.py'],capture_output=True,text=True);(path/'python-tests.log').write_text(p.stdout+p.stderr,encoding='utf-8')
    if p.returncode:raise ValueError(p.stdout+p.stderr)
    counts=collections.Counter()
    for f in (path/'tests').glob('*.trx'):
        c=next(x for x in ET.parse(f).iter() if x.tag.endswith('Counters'))
        for k in ('total','passed','failed','notExecuted'):counts[k]+=int(c.get(k,0))
    if counts['total']!=1297 or counts['failed'] or counts['notExecuted']:raise ValueError('Incomplete C# validation')
    gold=load(path/'golden/golden.json')
    if not gold['Passed'] or gold['Tested']!=40:raise ValueError('Golden failure')
    build=pathlib.Path('data/historical/phase-d1-build-final.log');raw=build.read_bytes();txt=raw.decode('utf-16' if raw[:2] in (b'\xff\xfe',b'\xfe\xff') else 'utf-8-sig')
    if 'Build succeeded.' not in txt or '0 Warning(s)' not in txt or '0 Error(s)' not in txt:raise ValueError('Build not clean')
    files={str(p):sha(p) for p in sorted(pathlib.Path('tools/DraftTG.HistoricalEvaluation').glob('*phase_d1*.py'))};dump(OUT/'execution-sources.json',dict(files=files,fingerprint=digest(json.dumps(files,sort_keys=True))))
    import re
    dump(OUT/'validation.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),dirtyTree=True,CSharp=dict(counts),pythonTests=int(re.search(r'Ran (\d+) tests',p.stderr).group(1)),golden=40,buildPassed=True,warnings=0,errors=0,
        preservedFiles=len(load(OUT/'preservation.json')['files']),sourceFingerprint=digest(json.dumps(files,sort_keys=True)),scoreAssemblySha256=sha('src/DraftTG.RecommendationEngine/bin/Debug/net10.0/DraftTG.RecommendationEngine.dll'),productionChanged=False))

def final():
    guard();sel=load(OUT/'selection.json')
    for p,h in load(OUT/'analytical-freeze.json')['files'].items():
        if sha(p)!=h:raise ValueError('Analytical source changed after selection')
    for p,h in sel['inputs'].items():
        if sha(p)!=h:raise ValueError('D1 selection evidence changed')
    cold0=load(OUT/'cold.json')['results'];best=sel['best'];nonnull=[r for r in cold0 if r['set'] in DEV and r['best']['MRR'] is not None];coverage=all(r['best']['n']>=300 for r in cold0 if r['set'] in DEV)
    coldSafe=all(r['best']['MRR']>=.99*r['control']['MRR'] for r in nonnull if r['requested'] in (50,100,250));positive=all(float(np.mean([r['best']['MRR']-r['control']['MRR'] for r in nonnull if r['requested']==n]))>0 for n in (1000,'full'))
    probes0=load(OUT/'failure-probes.json');validation=load(OUT/'validation.json');gates={**sel['primaryGates'],
        'uncertainty':load(OUT/'completion.json')['uncertainty']['qualified'],'cold':coldSafe and positive and coverage,
        'probes':all(v for v in probes0.values() if isinstance(v,bool)),'goldens':validation['golden']==40,'contextualIntegrity':load(OUT/'contextual-replay.json')['baselineUnchanged']}
    passed=all(gates.values());dump(OUT/'development-gate.json',dict(gates=gates,passAll=passed,qualifiedForFutureIntegration=False,productionChanged=False))
    dump(OUT/'holdout-status.json',dict(sets=['MH3','DFT'],consumed=False,performance=None,reason='Development gates fail; no holdout download/read or release lock' if not passed else 'Complete development gate passes; separately qualified permitted D1 release inputs required, no protected runner bypass'))
    print('D1 development gates',gates,'MH3/DFT untouched',flush=True)

def summary():
    guard();sel=load(OUT/'selection.json');r=load(OUT/'contextual-replay.json');best=sel['best'];result=dict(version=VERSION,baseline=load(OUT/'baseline-freeze.json'),selection=sel,gate=load(OUT/'development-gate.json'),
        completion=load(OUT/'completion.json'),uncertainty=load(OUT/'confidence-calibration.json'),cold=load(OUT/'cold.json'),metadataPrior=load(OUT/'metadata-prior.json'),
        replay=dict(decisions=r['Decisions'],drafts=r['Drafts'],complete=r['Metrics']['complete-baseline | overall']['Eligible'],metrics={f:r['Metrics'][k+' | overall'] for f,k in (('current','complete-baseline'),('A','affinity-A'),('B','affinity-B'),('C','affinity-C'))},changes=r['Changes'][best+' | overall']),
        holdout=load(OUT/'holdout-status.json'),tests=load(OUT/'validation.json'),performance=load(OUT/'performance.json'),productionChanged=False,
        recommendation='Retain production A/v1.6.2. Do not qualify undercovered tasks, uncalibrated confidence or zero-weight fallback as strategic benefit. Next work requires separately preregistered evaluation design with adequate matched coverage; no automatic next phase.')
    dump(OUT/'summary.json',result);print('Summary',OUT/'summary.json','qualified=',False)
def report():
    from phase_d1_report import report as publish
    publish()
