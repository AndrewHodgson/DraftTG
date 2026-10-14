"""Frozen Phase F calibration/target tasks. Workers accept legal state only."""
from phase_f_model import *
from concurrent.futures import ProcessPoolExecutor
from phase_e_models import calibrated_models as emodels,predict as epredict,tasks_for as etasks
WORKERS=4
_MODELS=None;_CONFIGS=None
def init_worker(configs,dataRoot=None):
 global _MODELS,_CONFIGS
 if dataRoot is not None:
  import phase_f_core
  phase_f_core.DATA=pathlib.Path(dataRoot)
 _CONFIGS=configs;m=meta();q=qualities();_MODELS={params_key(p):MarginalModel(m,q,p) for p in configs}
def score_job(job):
 # No observed build, outcome, actual-pick label or future history in the worker input.
 if {'build','outcome','actualPicked','futurePicks','completedPool'}&set(job):raise ValueError('Labels/future state forbidden at marginal worker boundary')
 result={};pool=job['pool'];s=job['set'];picks=job['picks']
 for key,model in _MODELS.items():
  candidates={}
  for i in job['candidates']:
   actualPool=dict(pool)
   if job.get('removeOne',False):
    actualPool[i]-=1
    if not actualPool[i]:del actualPool[i]
   before=model.build(actualPool,s,picks);results={}
   for family in ('A','B','C'):
    r=model.marginal(actualPool,i,s,picks,family,job.get('productionD',{}).get(i,.5),job.get('probabilities',{}).get(i),before)
    results[family]=r if job.get('detailed',False) else {k:r.get(k) for k in ('available','DValue','AbsoluteEligibility','Confidence','ReplacementValue','StructuralDelta','ManaConsistencyDelta','Final23Probability','reason')}
   candidates[i]=results
  result[key]=candidates
 return dict(key=job['key'],scores=result)
def export_pair_inputs():
 guard();models=emodels();eMeta=load(EDATA/'metadata.json');eQ=equalities();fQ=qualities();production=load(EDATA/'current-pair-D.json');out=[]
 for s in SETS:
  drafts={r['draft']:r for r in rows(s)};tasks=list(readrows(EDATA/f'{s}.pairs.jsonl.gz'));needed=collections.defaultdict(set)
  # Recompute only the frozen E-C inference support, never fit its model or select it again.
  support={};perpart={}
  for part in ('validation','target'):
   if part=='validation' and s not in CAL:continue
   augmented=etasks(s,part)
   for r in augmented:
    for i,x in zip((r['positive'],r['negative']),r['x']):support[(r['draft'],i)]=epredict(models['E-C'],x)
  for r in tasks:
   if r['partition']=='target' or s in CAL and r['partition']=='validation':needed[r['draft']].update((r['positive'],r['negative']))
  jobs=[]
  for d,ids in sorted(needed.items()):
   row=drafts[d];jobs.append(dict(key=s+'/'+d,set=s,draft=d,partition=row['partition'],pool=row['pool'],candidates=sorted(ids),picks=44,removeOne=True,productionD={i:production[s+'/'+d+'/'+i] for i in ids if s+'/'+d+'/'+i in production},probabilities={i:support[d,i] if abs(eQ.get((s,i),.5)-fQ.get((s,i),.5))<1e-9 else None for i in ids}))
  write_rows(DATA/f'{s}.pair-jobs.jsonl.gz',jobs);out.append(dict(set=s,jobs=len(jobs),originalPairsSha256=sha(EDATA/f'{s}.pairs.jsonl.gz')))
 dump(OUT/'pair-inputs.json',dict(datasets=out,frozenEModelSha256=sha(EDATA/'models-calibrated.json'),noNewMatching=True,labelsOutsideWorkers=True))
def calibrate():
 guard();configs=grid();jobs=[r for s in CAL for r in readrows(DATA/f'{s}.pair-jobs.jsonl.gz') if r['partition']=='validation'];t=time.perf_counter();scores={}
 with ProcessPoolExecutor(max_workers=WORKERS,initializer=init_worker,initargs=(configs,str(DATA))) as executor:
  for j,r in enumerate(executor.map(score_job,jobs,chunksize=2)):
   scores[r['key']]=r['scores']
   if (j+1)%25==0:print('calibration pools',j+1,flush=True)
 write_rows(DATA/'calibration-scores.jsonl.gz',(dict(key=k,scores=v) for k,v in scores.items()))
 results={};chosen={}
 for family in ('A','B','C'):
  results[family]=[]
  for config in configs:
   key=params_key(config);sets={}
   for s in CAL:
    vals=[]
    for r in readrows(EDATA/f'{s}.pairs.jsonl.gz'):
     if r['partition']!='validation':continue
     pair=scores[s+'/'+r['draft']][key];a=pair[r['positive']][family]['DValue'];b=pair[r['negative']][family]['DValue'];vals.append(float(a>b)+.5*float(abs(a-b)<1e-12))
    sets[s]=dict(n=len(vals),accuracy=float(np.mean(vals)))
   results[family].append(dict(params=config,bySet=sets,macro=float(np.mean([v['accuracy'] for v in sets.values()]))))
  best=max(results[family],key=lambda r:(r['macro'],-r['params']['structureWeight'],r['params']['scale'],-abs(r['params']['virtualBase']-.5)));chosen[family]=best
 family=max(('A','B','C'),key=lambda f:(chosen[f]['macro'],-('A','B','C').index(f)))
 dump(OUT/'selection.json',dict(version=VERSION,results=results,families=chosen,selectedFamily=family,seconds=time.perf_counter()-t,calibrationOnly=True,developmentNotRead=True,frozenEProbability=True))
 analytical={p.as_posix():sha(p) for p in (pathlib.Path('tools/DraftTG.HistoricalEvaluation/phase_f_model.py'),pathlib.Path('tools/DraftTG.HistoricalEvaluation/phase_f_core.py'))};dump(OUT/'analytical-freeze.json',dict(files=analytical,selectionSha256=sha(OUT/'selection.json')))
def selected_configs():
 selection=load(OUT/'selection.json');ps=[r['params'] for r in selection['families'].values()];return list({params_key(p):p for p in ps}.values())
def pair_evaluate():
 guard();configs=selected_configs();jobs=[r for s in SETS for r in readrows(DATA/f'{s}.pair-jobs.jsonl.gz') if r['partition']=='target'];t=time.perf_counter();scores={}
 with ProcessPoolExecutor(max_workers=WORKERS,initializer=init_worker,initargs=(configs,str(DATA))) as executor:
  for j,r in enumerate(executor.map(score_job,jobs,chunksize=4)):
   scores[r['key']]=r['scores']
   if (j+1)%100==0:print('target pair pools',j+1,flush=True)
 write_rows(DATA/'target-pair-scores.jsonl.gz',(dict(key=k,scores=v) for k,v in scores.items()));selection=load(OUT/'selection.json');old=load(EOUT/'pairwise.json');byset={};clusters={}
 # Frozen E generic/current/E-A/E-C aggregates are reused exactly, never re-selected.
 for s in SETS:
  bucket={f:[] for f in ('A','B','C')};declined=collections.Counter();bydraft=collections.defaultdict(lambda:collections.defaultdict(list));tasks=[r for r in readrows(EDATA/f'{s}.pairs.jsonl.gz') if r['partition']=='target'];controlD=load(EDATA/'current-pair-D.json');generic=emodels()['generic']
  for r in tasks:
   key=s+'/'+r['draft']
   g=[epredict(generic,x) for x in r['x']];cd=[controlD[key+'/'+i] for i in (r['positive'],r['negative'])]
   bydraft[r['draft']]['current-D'].append(float(cd[0]>cd[1])+.5*float(abs(cd[0]-cd[1])<1e-12));bydraft[r['draft']]['generic'].append(float(g[0]>g[1])+.5*float(abs(g[0]-g[1])<1e-12))
   for family in bucket:
    ps=scores[key][params_key(selection['families'][family]['params'])];a=ps[r['positive']][family];b=ps[r['negative']][family];v=float(a['DValue']>b['DValue'])+.5*float(abs(a['DValue']-b['DValue'])<1e-12);bucket[family].append(v);bydraft[r['draft']][family].append(v);declined[family]+=not(a['available'] and b['available'])
  byset[s]=dict(controls=old[s],families={f:dict(n=len(v),accuracy=float(np.mean(v)),declinedPairs=declined[f],fullyExperimentalPairs=len(v)-declined[f]) for f,v in bucket.items()},drafts=len(bydraft))
  # Whole-draft resampling: candidate uncertainty reflects shared occurrences/pairs.
  rng=np.random.default_rng(int(digest(SEED+'/bootstrap/'+s)[:8],16));ds=sorted(bydraft);sample={};indices=rng.integers(0,len(ds),(500,len(ds)))
  for family in bucket:
   means=np.array([np.mean(bydraft[d][family]) for d in ds]);boot=means[indices].mean(axis=1);paired={}
   for control in ('current-D','generic'):
    delta=means-np.array([np.mean(bydraft[d][control]) for d in ds]);paired[control]=dict(draftMacroGain=float(delta.mean()),gainInterval=np.quantile(delta[indices].mean(axis=1),[.025,.975]).tolist())
   sample[family]=dict(draftMacro=float(means.mean()),accuracyInterval=np.quantile(boot,[.025,.975]).tolist(),pairedControls=paired,interpretation='500 paired whole-draft resamples; equal draft weighting within each set, distinct from primary pair-weighted set mean. Shared cards/pairs stay in the draft cluster; no independent-pair significance claim.')
  clusters[s]=sample
 dump(OUT/'pairwise.json',dict(bySet=byset,clustered=clusters,seconds=time.perf_counter()-t,coverageFrozen=True))
