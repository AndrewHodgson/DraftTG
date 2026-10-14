"""Phase F execution/publication orchestration, with label-free worker boundaries."""
from phase_f_research import *
import subprocess
DLL='data/historical/evaluation/phase-e/runner4/DraftTG.DeckFitResearch.dll'
def validation_baseline():
 guard();jobs=[r for s in CAL for r in readrows(DATA/f'{s}.pair-jobs.jsonl.gz') if r['partition']=='validation'];validationCount=len(jobs)
 with open(DATA/'validation-baseline-input.jsonl','w',encoding='utf8') as f:
  for r in jobs:f.write(json.dumps({k:r[k] for k in ('set','draft','pool','candidates')})+'\n')
 subprocess.run(['dotnet',DLL,'--mode','pairs','--input',str(DATA/'validation-baseline-input.jsonl'),'--output',str(RUN/'validation-baseline.jsonl')],check=True)
 lookup=load(EDATA/'current-pair-D.json')
 for line in open(RUN/'validation-baseline.jsonl',encoding='utf8'):
  r=json.loads(line);lookup[r['Key']]=r['D']
 fq=qualities();eq=equalities()
 for s in SETS:
  jobs=list(readrows(DATA/f'{s}.pair-jobs.jsonl.gz'))
  for r in jobs:
   r['productionD']={i:lookup[r['key']+'/'+i] for i in r['candidates']}
   for i in r['candidates']:
    if abs(fq.get((s,i),.5)-eq.get((s,i),.5))>1e-9:r['probabilities'][i]=None
  write_rows(DATA/f'{s}.pair-jobs.jsonl.gz',jobs)
 dump(OUT/'baseline-reuse.json',dict(targetBaseline='Unchanged Phase E per-identity actual late production D cache.',validationBaseline='Actual frozen C# adapter called only for previously unavailable calibration-validation counterfactuals; outputs written only Phase F.',noHistoricalReplayRepeated=True,validationPools=validationCount))
def build_job(job):
 model=_builder_worker;pool=job['pool'];s=job['set'];r=model.build(pool,s,44);main=dict(r['main']);main.update(r['mana']['nonbasic']);main.update({'basic:'+'WUBRG'[int(k)]:n for k,n in r['mana']['basics'].items() if n})
 return dict(key=job['key'],deck=main,projection=r,milliseconds=sum(model.builderTimes[-1:]))
_builder_worker=None
def init_builder(params,dataRoot=None):
 global _builder_worker
 if dataRoot is not None:
  import phase_f_core
  phase_f_core.DATA=pathlib.Path(dataRoot)
 _builder_worker=MarginalModel(meta(),qualities(),params)
def builders():
 guard();selection=load(OUT/'selection.json');params=selection['families'][selection['selectedFamily']]['params'];jobs=[dict(key=s+'/'+r['draft'],set=s,pool=r['pool']) for s in SETS for r in rows(s) if r['partition']=='target'];out={};t=time.perf_counter()
 with ProcessPoolExecutor(max_workers=WORKERS,initializer=init_builder,initargs=(params,str(DATA))) as executor:
  for j,r in enumerate(executor.map(build_job,jobs,chunksize=4)):
   out[r['key']]=r
   if (j+1)%200==0:print('builder pools',j+1,flush=True)
 write_rows(DATA/'builder-results.jsonl.gz',out.values())
 from phase_e_builders import reconstruction,meanrows
 m=meta();old=load(EOUT/'reconstruction.json')['bySet'];result={};deficit={};failed=[];times=[]
 for s in SETS:
  scores=[];ds=[];integrity=0
  for r in rows(s):
   if r['partition']!='target':continue
   p=out[s+'/'+r['draft']];times.append(p['milliseconds']);valid=p['projection']['complete'] and sum(p['deck'].values())==40 and all(i.startswith('basic:') or n<=r['pool'].get(i,0) for i,n in p['deck'].items())
   if not valid:failed.append(dict(set=s,draft=r['draft'],reason='No complete40-card owned spell/mana projection',ownedCards=sum(p['deck'].values())));pred={}
   else:integrity+=1;pred=p['deck']
   scores.append(reconstruction(pred,r['build'],m));ds.append(sum(n*(1-v['score']) for i,v in p['projection']['casts'].items() for n in [p['projection']['main'][i]]))
  result[s]=dict(F=meanrows(scores),current=old[s]['current'],E_C=old[s]['C'],validDecks=integrity,total=len(scores),integrityRate=integrity/max(1,len(scores)));deficit[s]=float(np.mean(ds))
 dump(OUT/'reconstruction.json',dict(bySet=result,manaSourceDeficit=deficit,declines=failed,seconds=time.perf_counter()-t,meanBuilderMs=float(np.mean(times)),p95BuilderMs=float(np.quantile(times,.95)),allCandidateIntegrity=not failed,partialDecksNeverCounted=True))
def predict():
 guard();selection=load(OUT/'selection.json');configs=selected_configs();fq=qualities();eq=equalities();changes=[];compatibility=[];t=time.perf_counter();modelKeys={f:params_key(selection['families'][f]['params']) for f in ('A','B','C')};n=0;candidateN=0;declines=collections.Counter()
 def jobs():
  # Phase E already computed E-C probabilities from legal pick-time state. No refit or feature replay.
  for line in open(ERUN/'compose-sidecar.jsonl',encoding='utf8'):
   r=json.loads(line);o=r['observation'];pool=counts(o['PoolBeforePick']);s=o['SetCode'];cards=o['Cards'];prob={}
   for c,p in zip(cards,r['probabilities'],strict=True):
    i=c['Id'];delta=abs(fq.get((s,i),.5)-(c['QualityComponent'] if c['QualityComponent'] is not None else .5));compatibility.append(delta)
    prob[i]=p['C'] if abs(fq.get((s,i),.5)-eq.get((s,i),.5))<1e-9 else None
   yield dict(key=s+'/'+o['DraftId']+'/'+str(o['PackNumber'])+'/'+str(o['PickNumber']),set=s,pool=pool,picks=o['GlobalPickIndex'],candidates=list(dict.fromkeys(c['Id'] for c in cards)),productionD={c['Id']:c['DeckNeedComponent'] for c in cards},probabilities=prob)
 with open(RUN/'compose-input.jsonl','w',encoding='utf8') as f, gzip.open(DATA/'contextual-predictions.jsonl.gz','wt',encoding='utf8',compresslevel=1) as trace,ProcessPoolExecutor(max_workers=WORKERS,initializer=init_worker,initargs=(configs,str(DATA))) as executor:
  # map preserves source order. Results and baseline rows must be aligned exactly.
  base=(json.loads(line) for line in open(ERUN/'compose-sidecar.jsonl',encoding='utf8'))
  for original,r in zip(base,executor.map(score_job,jobs(),chunksize=8,buffersize=32),strict=True):
   o=original['observation'];expected=o['SetCode']+'/'+o['DraftId']+'/'+str(o['PackNumber'])+'/'+str(o['PickNumber'])
   if r['key']!=expected:raise ValueError('Async/ordered research result alignment mismatch')
   cs=[];compact=[]
   for c in o['Cards']:
    i=c['Id'];ds=[c['DeckNeedComponent']];details={}
    for family in ('A','B','C'):
     p=r['scores'][modelKeys[family]][i][family];ds.append(p['DValue']);details[family]=p
     if not p['available']:declines[family+': '+str(p['reason'])]+=1
    cs.append(dict(Weights=c['Weights'],EffectiveQualityComponent=c['EffectiveQualityComponent'] if c['EffectiveQualityComponent'] is not None else .5,LaneComponent=c['LaneComponent'],ArchetypeComponent=c['ArchetypeComponent'],D=ds));compact.append(dict(id=i,Q=c['QualityComponent'],models=details))
   f.write(json.dumps(dict(cards=cs),allow_nan=False)+'\n');trace.write(json.dumps(dict(key=r['key'],set=o['SetCode'],pack=o['PackNumber'],pick=o['PickNumber'],cards=compact),allow_nan=False)+'\n');n+=1;candidateN+=len(cs)
   if n%1000==0:print('F predictions',n,'seconds',round(time.perf_counter()-t,1),flush=True)
 dump(OUT/'prediction-run.json',dict(comparableDecisions=n,candidateOccurrences=candidateN,seconds=time.perf_counter()-t,workers=WORKERS,maxProductionQDifference=max(compatibility),declines=dict(declines),productionReplayReused=True,probabilityModelNotRefit=True))
def probes():
 from phase_f_probes import probes as evaluate
 evaluate()
def contextual():
 guard();subprocess.run(['dotnet',DLL,'--mode','compose','--input',str(RUN/'compose-input.jsonl'),'--output',str(RUN/'compose-output.jsonl')],check=True)
 from phase_e_publish import mean_metrics_for
 by=collections.defaultdict(list);changes=collections.Counter();maxerror=0;correlations=collections.defaultdict(lambda:collections.defaultdict(list));n=0
 predictions=readrows(DATA/'contextual-predictions.jsonl.gz')
 with open(RUN/'compose-output.jsonl') as results,open(ERUN/'compose-sidecar.jsonl',encoding='utf8') as original:
  for line,oldline,pred in zip(results,original,predictions,strict=True):
   values=json.loads(line);o=json.loads(oldline)['observation'];cards=o['Cards'];ids=[c['Id'] for c in cards];key=o['SetCode']+'/'+o['DraftId']+'/'+str(o['PackNumber'])+'/'+str(o['PickNumber'])
   if pred['key']!=key:raise ValueError('Mismatched research result generation/order')
   for col,name in enumerate(('current','A','B','C')):
    order=sorted(range(len(cards)),key=lambda j:(-values[j][col],-(cards[j]['QualityComponent'] or 0),-(cards[j]['StatisticalDataWeight'] or 0),cards[j]['PackIndex']));rank=min(order.index(j)+1 for j,i in enumerate(ids) if i==o['ActualPicked'])
    if col==0:
     originalOrder=order;originalRank=rank
     if rank!=o['Rank']:raise ValueError('Baseline ranks drifted')
     maxerror=max(maxerror,max(abs(values[j][col]-cards[j]['ContextualValue']) for j in range(len(cards))))
    else:
     changes[name+'/changed']+=order!=originalOrder;changes[name+'/improved']+=rank<originalRank;changes[name+'/regressed']+=rank>originalRank
    value=dict(Top1=float(rank==1),Top3=float(rank<=3),MRR=1/rank,normalizedRank=(rank-1)/max(1,len(cards)-1))
    for scope in ('overall','set='+o['SetCode'],'pack='+str(o['PackNumber'])):by[name+'/'+scope].append(value)
   for c,p in zip(cards,pred['cards'],strict=True):
    for f,detail in p['models'].items():
     k=f+'/pack='+str(o['PackNumber']);correlations[k]['D'].append(detail['DValue']);correlations[k]['Q'].append(c['QualityComponent'] if c['QualityComponent'] is not None else .5);correlations[k]['playability'].append(detail['AbsoluteEligibility'] or 0);correlations[k]['probability'].append(detail['Final23Probability'] if detail['Final23Probability'] is not None else .5)
   n+=1
 if maxerror>1e-12:raise ValueError('Actual production score calibration drift')
 corr={}
 for k,cols in correlations.items():
  corr[k]={name:float(np.corrcoef(cols['D'],cols[name])[0,1]) if np.std(cols[name])>0 and np.std(cols['D'])>0 else None for name in ('Q','playability','probability')}
 dump(OUT/'contextual-replay.json',dict(version=VERSION,decisions=n,metrics={k:mean_metrics_for(v) for k,v in by.items()},changes=dict(changes),maxBaselineError=maxerror,actualProductionReplayReused=True,productionChanged=False))
 dump(OUT/'correlations.json',corr)
def cold():
 guard();selection=load(OUT/'selection.json');rs={r['key']:r['scores'] for r in readrows(DATA/'calibration-scores.jsonl.gz')};out={};family=selection['selectedFamily']
 for held in CAL:
  remaining=[s for s in CAL if s!=held];chosen=max(selection['results'][family],key=lambda r:(float(np.mean([r['bySet'][s]['accuracy'] for s in remaining])),-r['params']['structureWeight'],r['params']['scale'],-abs(r['params']['virtualBase']-.5)))
  # A/B have no learned support; C probability is frozen from all calibration sets.
  # Leave-out checks calibrate marginal parameters only; disclose the inherited support limitation.
  test=chosen['bySet'][held];reference=selection['families'][family]['bySet'][held];out[held]=dict(params=chosen['params'],heldAccuracy=test['accuracy'],fullCalibrationAccuracy=reference['accuracy'],gain=test['accuracy']-reference['accuracy'])
 dump(OUT/'cold-start.json',dict(selectedFamily=family,leaveCalibrationSetOut=out,macroLoss=-float(np.mean([r['gain'] for r in out.values()])),passesMarginalParameterGate=float(np.mean([r['gain'] for r in out.values()]))>=-.005,developmentSameSetBuildTrainingRows=0,setIdentityFeatures=False,probabilityCaveat='Frozen E-C was trained on the three calibration sets; leave-calibration-set-out is a marginal-parameter sensitivity check, not fully independent E-C probability training. Development sets have zero same-set training for both components.',zeroBuildDevelopmentTransferEvaluated=True))
def corrected_cold():
 import phase_f_core
 guard();original=load(phase_f_core.OUT/'cold-start.json');selection=load(OUT/'selection.json');family=selection['selectedFamily'];configs=list({params_key(p):p for p in [selection['families'][family]['params']]+[r['params'] for r in original['leaveCalibrationSetOut'].values()]}.values());jobs=[r for s in CAL for r in readrows(DATA/f'{s}.pair-jobs.jsonl.gz') if r['partition']=='validation'];scores={}
 with ProcessPoolExecutor(max_workers=WORKERS,initializer=init_worker,initargs=(configs,str(DATA))) as executor:
  for r in executor.map(score_job,jobs,chunksize=2):scores[r['key']]=r['scores']
 results={}
 for s in CAL:
  full=[];held=[];fullKey=params_key(selection['families'][family]['params']);heldKey=params_key(original['leaveCalibrationSetOut'][s]['params'])
  for r in readrows(EDATA/f'{s}.pairs.jsonl.gz'):
   if r['partition']!='validation':continue
   values=[]
   for key in (fullKey,heldKey):
    p=scores[s+'/'+r['draft']][key];a=p[r['positive']][family]['DValue'];b=p[r['negative']][family]['DValue'];values.append(float(a>b)+.5*float(abs(a-b)<1e-12))
   full.append(values[0]);held.append(values[1])
  results[s]=dict(params=original['leaveCalibrationSetOut'][s]['params'],heldAccuracy=float(np.mean(held)),fullCalibrationAccuracy=float(np.mean(full)),gain=float(np.mean(held)-np.mean(full)))
 loss=-float(np.mean([r['gain'] for r in results.values()]));dump(OUT/'cold-start.json',dict(selectedFamily=family,leaveCalibrationSetOut=results,macroLoss=loss,passesMarginalParameterGate=loss<=.005,developmentSameSetBuildTrainingRows=0,setIdentityFeatures=False,frozenFoldChoicesReevaluated=True,newCalibration=False,probabilityCaveat=original['probabilityCaveat'],zeroBuildDevelopmentTransferEvaluated=True))
def mana_audit():
 guard();m=meta();by=collections.defaultdict(list);qualified=collections.Counter();unknown=collections.Counter()
 for r in readrows(DATA/'builder-results.jsonl.gz'):
  s=r['key'].split('/')[0];p=r['projection'];sources=p['mana']['sources'];unmet=0.;denominator=0
  for i,cast in p['casts'].items():
   for color,need in enumerate(m[i]['pipNeed']):
    if not need:continue
    minimum=next((n for n in range(41) if hyper_tail(n,cast['drawsByGrace'],need)>=.8),40);qty=p['main'][i];unmet+=qty*max(0,minimum-sources[color])/max(1,minimum);denominator+=qty
  by[s].append(dict(unmetPipSourceAdequacy=unmet,maxRequiredChannelCount=denominator,sourceShortfallScore=unmet/max(1,denominator),effectiveFixing=p['mana']['effectiveFixing'],lands=p['land']))
 for s in SETS:
  for r in rows(s):
   for p in r['picks']:
    c=m[p['id']]
    if c.get('broadMainDeck',False) and c['roleProfile']['RemovalWeight']>0:qualified[s]+=1
    if c.get('unknownNarrow',False):unknown[s]+=1
 dump(OUT/'mana-source-audit.json',dict(bySet={s:{k:float(np.mean([r[k] for r in rs])) for k in rs[0]} for s,rs in by.items()},qualifiedPositiveRemovalOccurrences=dict(qualified),unknownNarrowOccurrences=dict(unknown),sourceInterpretation='Structured produced_mana counts are potential direct sources; conditional activation, tapped tempo and overlapping dual-source assignments remain unqualified. Exact joint casting probability is not claimed.'))
def benchmark():
 from phase_b import initialize_telemetry
 from phase_b_core import peak_memory_bytes
 initialize_telemetry();guard();m=meta();q=qualities();sel=load(OUT/'selection.json');family=sel['selectedFamily'];model=MarginalModel(m,q,sel['families'][family]['params']);times=[];noProb=[];effect=[];samples=[]
 for r in list(readrows(DATA/'DMU.pair-jobs.jsonl.gz'))[:30]:
  for i in r['candidates'][:4]:
   pool=dict(r['pool']);pool[i]-=1
   if not pool[i]:del pool[i]
   t=time.perf_counter();result=model.marginal(pool,i,'DMU',44,family,r['productionD'][i],r['probabilities'][i]);times.append((time.perf_counter()-t)*1000);p=result.get('Final23Probability');base=model.marginal(pool,i,'DMU',44,family,r['productionD'][i],None);effect.append(result['DValue']-base['DValue'])
   if len(samples)<8:samples.append(result)
 def summary(x):return dict(n=len(x),meanMs=float(np.mean(x)) if x else None,p95Ms=float(np.quantile(x,.95)) if x else None)
 dump(OUT/'performance.json',dict(candidate=summary(times),beforeAfterBuilder=summary(model.builderTimes),castability=summary(model.castTimes),mana=summary(model.manaTimes),peakBytes=peak_memory_bytes(),supportProbabilityMeanDChange=float(np.mean(effect)),supportProbabilityMaxAbsDChange=max(abs(v) for v in effect),frozenEModelBytes=(EDATA/'models-calibrated.json').stat().st_size,kernelSourceBytes=pathlib.Path('tools/DraftTG.HistoricalEvaluation/phase_f_model.py').stat().st_size,baselineProbeSamples=samples))
def validate():
 import xml.etree.ElementTree as ET,re
 guard();p=subprocess.run([sys.executable,'tools/DraftTG.HistoricalEvaluation/test_phase_f.py'],capture_output=True,text=True);(RUN/'python-tests.log').write_text(p.stdout+p.stderr,encoding='utf8')
 if p.returncode:raise ValueError(p.stdout+p.stderr)
 counters=collections.Counter()
 for f in (RUN/'tests').glob('*.trx'):
  c=next(x for x in ET.parse(f).iter() if x.tag.endswith('Counters'))
  for k in ('total','passed','failed','notExecuted'):counters[k]+=int(c.get(k,0))
 gold=load(RUN/'golden/golden.json')
 if counters['total']!=1298 or counters['failed'] or counters['notExecuted'] or not gold['Passed'] or gold['Tested']!=40:raise ValueError('Incomplete C#/golden validation')
 raw=(RUN/'build.log').read_bytes();txt=raw.decode('utf-16' if raw[:2] in (b'\xff\xfe',b'\xfe\xff') else 'utf-8-sig')
 if not all(v in txt for v in ('Build succeeded.','0 Warning(s)','0 Error(s)')):raise ValueError('Build not clean')
 sources={p.as_posix():sha(p) for p in pathlib.Path('tools/DraftTG.HistoricalEvaluation').glob('*phase_f*.py')};dump(OUT/'execution-sources.json',dict(files=sources));dump(OUT/'validation.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),dirtyTree=True,buildPassed=True,warnings=0,errors=0,CSharp=dict(counters),pythonTests=int(re.search(r'Ran (\d+) tests',p.stderr).group(1)),golden=40,goldenPassed=True,preservedFiles=len(load(OUT/'preservation.json')['files']),productionChanged=False,sourceFingerprint=digest(json.dumps(sources,sort_keys=True)),scoreAssemblySha256=sha('src/DraftTG.RecommendationEngine/bin/Debug/net10.0/DraftTG.RecommendationEngine.dll')))
def final():
 guard();root=OUT/'corrected-inputs';selection=load(OUT/'selection.json');family=selection['selectedFamily'];pairs=load(root/'pairwise.json')['bySet'];recon=load(root/'reconstruction.json');context=load(root/'contextual-replay.json');pred=load(root/'prediction-run.json');probe=load(OUT/'probes.json');validation=load(OUT/'validation.json');cold0=load(root/'cold-start.json')
 for p,h in load(OUT/'analytical-freeze.json')['files'].items():
  if sha(p)!=h:raise ValueError('Analytical source changed after calibration')
 corr=load(root/'correction-registration.json')
 if sha(OUT/'selection.json')!=corr['originalSelectionSha256'] or sha(root/'selection.json')!=corr['correctedSelectionSha256']:raise ValueError('Corrected run re-selected parameters')
 gains={s:pairs[s]['families'][family]['accuracy']-max(pairs[s]['controls']['current-D']['accuracy'],pairs[s]['controls']['generic']['accuracy']) for s in DEV};fg={s:recon['bySet'][s]['F']['nonlandF1']-max(recon['bySet'][s][k]['nonlandF1'] for k in ('current','E_C')) for s in DEV};og={s:recon['bySet'][s]['F']['copyOverlap']-max(recon['bySet'][s][k]['copyOverlap'] for k in ('current','E_C')) for s in DEV};metrics=context['metrics']
 tests=dict(pair=float(np.mean(list(gains.values())))>=.02 and sum(v>0 for v in gains.values())>=4 and min(gains.values())>=-.02,
 coverage=all(pairs[s]['families'][family]['fullyExperimentalPairs']>=500 and pairs[s]['drafts']>=100 for s in DEV),structuralProbes=probe['selectedPass'],castability=probe['families'][family]['castability']['passed'],
 reconstruction=float(np.mean(list(fg.values())))>=.01 and float(np.mean(list(og.values())))>=.005 and sum(v>0 for v in fg.values())>=4 and recon['allCandidateIntegrity'] and all(recon['bySet'][s]['F']['mainColorAgreement']>=recon['bySet'][s]['current']['mainColorAgreement']-.02 and recon['bySet'][s]['F']['landMAE']<=recon['bySet'][s]['current']['landMAE']+.25 for s in DEV),
 contextual=context['decisions']>=50000 and all(metrics[family+'/'+scope]['Top1']>=metrics['current/'+scope]['Top1']-.0025 and metrics[family+'/'+scope]['MRR']>=metrics['current/'+scope]['MRR']-.002 for scope in ['overall']+['set='+s for s in DEV]),
 cold=cold0['passesMarginalParameterGate'],goldens=validation['goldenPassed'],compatibility=pred['maxProductionQDifference']<1e-9 and context['maxBaselineError']<1e-12)
 dump(OUT/'development-gate.json',dict(gates=tests,passAll=all(tests.values()),selectedFamily=family,pairGainBySet=gains,reconstructionF1GainBySet=fg,overlapGainBySet=og,correctedInputRunUsed=True,originalRunPreserved=True,selectionNotRetuned=True))
 dump(OUT/'holdout-status.json',dict(consumed=False,MH3='untouched',DFT='untouched',releaseLockCreated=False,results=None,reason='All frozen gates required; failure prohibits performance access/acquisition or release. No production integration.'))
 if all(tests.values()):raise ValueError('All development gates passed; final release requires frozen source-qualified MH3/DFT release implementation. Production promotion remains prohibited.')
 summary=dict(conclusion='Retain production v1.6.2 D and builder; no Phase F integration.',selectedFamily=family,selection=selection,pairwise=load(root/'pairwise.json'),reconstruction=recon,contextual=context,prediction=pred,correlations=load(root/'correlations.json'),performance=load(root/'performance.json'),cold=cold0,probes=probe,gate=load(OUT/'development-gate.json'),holdout=load(OUT/'holdout-status.json'),validation=validation,originalQCompatibility=load(OUT/'Q-compatibility.json'),correction=corr)
 dump(OUT/'summary.json',summary)
 from phase_f_report import report
 report()
 files={p.as_posix():sha(p) for p in OUT.rglob('*.json') if p.name!='completed-evidence.json'};files.update({p.as_posix():sha(p) for p in DATA.rglob('*') if p.is_file()});files.update(load(OUT/'execution-sources.json')['files']);files['V2_PHASE_F_MARGINAL_DECK_VALUE.md']=sha('V2_PHASE_F_MARGINAL_DECK_VALUE.md');dump(OUT/'completed-evidence.json',dict(files=files))
def summary():
 z=load(OUT/'summary.json');print(json.dumps(dict(conclusion=z['conclusion'],selected=z['selectedFamily'],parameters={f:r['params'] for f,r in z['selection']['families'].items()},gates=z['gate'],pairAccuracy={s:{f:r['accuracy'] for f,r in row['families'].items()} for s,row in z['pairwise']['bySet'].items() if s in DEV},contextual={k:z['contextual']['metrics'][k+'/overall'] for k in ('current','A','B','C')},holdouts=z['holdout'],validation=z['validation']),indent=2))
def report():
 from phase_f_report import report as create
 create()
