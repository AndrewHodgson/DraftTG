"""Generic, interpretable fractional-copy logistic models; calibration only selection."""
from phase_e_core import *
GENERIC=['intercept','Q','playable','stage','colorless','multicolor']
STRUCT=['mana','manaUnknown','creature','land','pips','pipUnknown','offPips','copyCount','poolSize','projectedSpells','creatureCount','early','top','earlyHole','creatureHole','topSaturation','roleUnknown','removal','fixing','roleKnown']
ROLE_EXTRA=['removalUnknown','fixingUnknown','projectedRemoval','removalHole']+[k+n for n in ROLE_NAMES for k in ('tag','unknown')]
MARGINAL=['membership','cutDelta','creatureDelta','earlyDelta','topDelta','qualityDelta','colorChange']
def fit(rs,names,ridge):
 x=np.array([[r['x'][k] for k in names] for r in rs]);y=np.array([r['y'] for r in rs]);b=np.zeros(len(names));pen=np.eye(len(names))*ridge;pen[0,0]=.01
 for _ in range(35):
  p=sigmoid(x@b);w=np.maximum(p*(1-p),1e-5);g=x.T@(y-p)-pen@b;h=(x.T*w)@x+pen
  step=np.linalg.solve(h,g);b+=np.clip(step,-2,2)
  if max(abs(step))<1e-7:break
 return dict(features=names,coefficients=b.tolist(),ridge=ridge)
def predict(model,x):return float(sigmoid(sum(x[k]*v for k,v in zip(model['features'],model['coefficients']))))
def calibrated_models():return load(DATA/('models-calibrated.json' if (DATA/'models-calibrated.json').exists() else 'models.json'))
def probability_calibration():
 guard();models=load(DATA/'models.json');val=[r for s in CAL for r in examples(s) if r['partition']=='validation'];result={}
 for name,model in models.items():
  p=np.array([predict(model,r['x']) for r in val]);y=np.array([r['y'] for r in val]);x=np.column_stack([np.ones(len(p)),logit(p)]);b=np.array([0.,1.]);prior=b.copy();pen=np.diag([1.,10.])
  for _ in range(30):
   pp=sigmoid(x@b);h=(x.T*(pp*(1-pp)))@x+pen;step=np.linalg.solve(h,x.T@(y-pp)-pen@(b-prior));b+=step
   if max(abs(step))<1e-8:break
  # Affine logit calibration can be absorbed into the coefficients exactly.
  old=list(model['coefficients']);model['coefficients']=[float(v*b[1]+(b[0] if k=='intercept' else 0)) for k,v in zip(model['features'],old)]
  result[name]=dict(intercept=float(b[0]),slope=float(b[1]),before=metrics(p,y),after=metrics([predict(model,r['x']) for r in val],y))
 dump(DATA/'models-calibrated.json',models);dump(OUT/'probability-calibration.json',dict(validationOnly=True,calibrationSets=list(CAL),developmentNotRead=True,selectionUnchanged=True,models=result))
def metrics(p,y):
 if not len(y):return dict(n=0,Brier=None,LogLoss=None,ECE=None,AUROC=None,AveragePrecision=None)
 p=np.asarray(p);y=np.asarray(y);bins=[];ece=0
 for low in np.arange(0,1,.1):
  ix=(p>=low)&(p<(low+.1) if low<.9 else p<=1);n=int(ix.sum())
  if n:ece+=n*abs(float(p[ix].mean()-y[ix].mean()));bins.append(dict(low=float(low),n=n,p=float(p[ix].mean()),label=float(y[ix].mean())))
 binary=(y==0)|(y==1);pp=p[binary];yy=y[binary];pos=pp[yy==1];neg=pp[yy==0];auc=None;ap=None
 if len(pos) and len(neg):
  order=np.argsort(pp,kind='stable');sortedp=pp[order];rank=np.empty(len(pp));start=0
  while start<len(pp):
   end=int(np.searchsorted(sortedp,sortedp[start],side='right'));rank[order[start:end]]=(start+1+end)/2;start=end
  auc=float((rank[yy==1].sum()-len(pos)*(len(pos)+1)/2)/(len(pos)*len(neg)))
  order=np.argsort(-pp,kind='stable');hit=np.cumsum(yy[order]);start=0;area=0.
  while start<len(pp):
   end=start+1
   while end<len(pp) and pp[order[end]]==pp[order[start]]:end+=1
   positives=float(yy[order[start:end]].sum());area+=positives*hit[end-1]/end;start=end
  ap=float(area/len(pos))
 return dict(n=len(y),Brier=float(np.mean(y*(1-p)**2+(1-y)*p*p)),LogLoss=float(np.mean(logloss(p,y))),ECE=float(ece/len(y)),AUROC=auc,AveragePrecision=ap,binaryMetricN=int(binary.sum()),bins=bins)
def examples(s):
 m=meta();rs=list(readrows(DATA/f'{s}.features.jsonl.gz'))
 for r in rs:decorate(r['x'],r['id'],r['audit'],m)
 return rs
def macro(rs,model):
 by={s:metrics([predict(model,r['x']) for r in rs if r['set']==s],[r['y'] for r in rs if r['set']==s]) for s in sorted({r['set'] for r in rs})}
 return dict(bySet=by,LogLoss=float(np.mean([v['LogLoss'] for v in by.values()])),Brier=float(np.mean([v['Brier'] for v in by.values()])))
def fit_models():
 guard();train=[r for s in CAL for r in examples(s) if r['partition']=='train'];val=[r for s in CAL for r in examples(s) if r['partition']=='validation']
 variants={'generic':GENERIC,'NO-Q':list(dict.fromkeys(['intercept','playable','stage']+STRUCT+MARGINAL[:1]+MARGINAL[2:])),'Q-CONTEXT':list(dict.fromkeys(['intercept','playable','stage']+STRUCT+MARGINAL)),
 'FULL':GENERIC+STRUCT+MARGINAL+ROLE_EXTRA,'no-roles':GENERIC+[k for k in STRUCT if k not in ('roleUnknown','removal','fixing','roleKnown')]+MARGINAL,
 'no-curve':GENERIC+[k for k in STRUCT if k not in ('early','top','earlyHole','topSaturation')]+[k for k in MARGINAL if k not in ('earlyDelta','topDelta')],
 'detailed-curve':GENERIC+STRUCT+MARGINAL+[f'curve{k}' for k in range(8)],'no-mana':GENERIC+[k for k in STRUCT if k not in ('mana','manaUnknown','pips','pipUnknown','offPips')]+MARGINAL,
 'E-B':GENERIC+STRUCT,'basic-roles':GENERIC+[k for k in STRUCT if k not in ('removal','fixing','roleUnknown','roleKnown')]+MARGINAL}
 models={};scores={}
 for name,keys in variants.items():
  opts=[]
  for ridge in (1,10,100):
   model=fit(train,keys,ridge);result=macro(val,model);opts.append((result['LogLoss'],-ridge,model,result))
  _,_,model,result=min(opts,key=lambda v:v[:2]);models[name]=model;scores[name]=dict(validation=result,ridge=model['ridge'])
  print('fitted',name,flush=True)
 # Hybrid family selection exclusively by calibration-validation log loss.
 selectedName=min(('NO-Q','Q-CONTEXT','FULL'),key=lambda k:(scores[k]['validation']['LogLoss'],len(models[k]['features'])))
 models['E-C']=models[selectedName]
 familyLoss={'A':float(np.mean([np.mean(logloss([heuristic(r['x']) for r in val if r['set']==s],[r['y'] for r in val if r['set']==s])) for s in CAL])),
 'B':scores['E-B']['validation']['LogLoss'],'C':scores[selectedName]['validation']['LogLoss']}
 selectedFamily=min(familyLoss,key=familyLoss.get)
 dump(DATA/'models.json',models)
 dump(OUT/'selection.json',dict(version='research-d-phase-e-v1',selectedHybrid=selectedName,selectedFamily=selectedFamily,familyCalibrationLogLoss=familyLoss,models=models,calibration=scores,trainingOccurrences=len(train),validationOccurrences=len(val),calibrationSets=list(CAL),developmentNotRead=True,identityAndSetFeatures=False,probabilityCalibration='Regularized logistic intercept/slope and features learned on calibration training; validation selects ridge. No post-development recalibration.'))
def dscores(f,models):
 g=predict(models['generic'],f);b=predict(models['E-B'],f);c=predict(models['E-C'],f)
 # Residual relative to generic inclusion controls Q/color/stage double counting.
 return dict(A=heuristic(f),B=float(np.clip(.5+.5*(b-g),0,1)),C=float(np.clip(.5+.5*(c-g),0,1))),dict(generic=g,A=heuristic(f),B=b,C=c)
def compare(inclusion_only=False):
 guard();models=calibrated_models();e={};pairResult={};stages={};redundancy={};m=meta()
 for s in SETS:
  rs=[r for r in examples(s) if r['partition']=='target'];e[s]={}
  for name,model in models.items():e[s][name]=metrics([predict(model,r['x']) for r in rs],[r['y'] for r in rs])
  e[s]['E-A']=metrics([heuristic(r['x']) for r in rs],[r['y'] for r in rs])
  for p in (1,2,3):
   bucket=[r for r in rs if r['pack']==p];stages[s+'/'+str(p)]={name:metrics([predict(model,r['x']) for r in bucket],[r['y'] for r in bucket]) for name,model in models.items() if name in ('generic','E-B','E-C')}
  if inclusion_only:continue
  tasks=tasks_for(s,'target');pairResult[s]={}
  current=load(DATA/'current-builder-index.json');prod=load(DATA/'current-pair-D.json') if (DATA/'current-pair-D.json').exists() else {}
  vals=collections.defaultdict(list);margins=collections.defaultdict(list);pll=collections.defaultdict(list)
  for r in tasks:
   f0,f1=r['x'];ss=[dscores(f,models)[0] for f in r['x']];main=current[s+'/'+r['draft']]['main'];positive,negative=r['positive'],r['negative']
   pairs={k:[v[k] for v in ss] for k in ('A','B','C')};pairs['generic']=[predict(models['generic'],f) for f in r['x']];pairs['current-optimizer']=[float(main.get(k,0)>0) for k in (positive,negative)]
   if prod:pairs['current-D']=[prod[s+'/'+r['draft']+'/'+k] for k in (positive,negative)]
   for k,(a,b) in pairs.items():
    vals[k].append(float(a>b)+.5*float(abs(a-b)<1e-12));margins[k].append(a-b);pll[k].append(float(logloss(sigmoid(4*(a-b)),1)))
  for k,v in vals.items():pairResult[s][k]=dict(n=len(v),accuracy=float(np.mean(v)),margin=float(np.mean(margins[k])),pairLogLoss=float(np.mean(pll[k])))
  yy=[r['x']['Q'] for r in rs]
  redundancy[s]={k:float(np.corrcoef(yy,[dscores(r['x'],models)[0][k] for r in rs])[0,1]) if rs else None for k in ('A','B','C')}
 dump(OUT/'inclusion.json',e);dump(OUT/'stage.json',stages)
 if not inclusion_only:dump(OUT/'pairwise.json',pairResult);dump(OUT/'Q-redundancy.json',redundancy)
def cold():
 guard();models=load(DATA/'models.json');out={}
 for held in CAL:
  train=[r for s in CAL if s!=held for r in examples(s) if r['partition']=='train'];test=[r for r in examples(held) if r['partition']=='target'];names=models['E-C']['features'];candidate=fit(train,names,models['E-C']['ridge']);generic=fit(train,GENERIC,models['generic']['ridge']);out[held]=dict(candidate=macro(test,candidate),generic=macro(test,generic))
 dump(OUT/'cold-start.json',dict(leaveCalibrationSetOut=out,zeroNewSetBuilds=True,developmentTransfer='Models trained only DMU/BRO/WOE; development target results in inclusion.json. No set adaptation.',passGate=float(np.mean([v['candidate']['LogLoss'] for v in out.values()]))<=float(np.mean([v['generic']['LogLoss'] for v in out.values()])) and float(np.mean([v['candidate']['Brier'] for v in out.values()]))<=float(np.mean([v['generic']['Brier'] for v in out.values()]))))
def tasks_for(s,part='target'):
 tasks=[r for r in readrows(DATA/f'{s}.pairs.jsonl.gz') if r['partition']==part];m=meta();drafts={r['draft']:r for r in rows(s)};q=qualities()
 for r in tasks:
  pool=drafts[r['draft']]['pool']
  for x,i in zip(r['x'],(r['positive'],r['negative'])):
   before=dict(pool);before[i]-=1
   if not before[i]:del before[i]
   selectedBefore,p=selected(before,s,m,q);decorate(x,i,dict(selected=selectedBefore),m)
 return tasks
def strict_noq_audit():
 # Correct a diagnostic feature-list oversight without altering selected E-B/E-C.
 guard();original=load(DATA/'models.json');models=calibrated_models();keys=[k for k in original['NO-Q']['features'] if k!='qualityDelta']
 train=[r for s in CAL for r in examples(s) if r['partition']=='train'];val=[r for s in CAL for r in examples(s) if r['partition']=='validation'];options=[]
 for ridge in (1,10,100):
  model=fit(train,keys,ridge);options.append((macro(val,model)['LogLoss'],-ridge,model))
 model=min(options,key=lambda v:v[:2])[2];models['NO-Q-strict']=model
 dump(DATA/'models-calibrated.json',models);dump(OUT/'no-q-audit.json',dict(originalSelectionSha256=sha(OUT/'selection.json'),selectedCandidateUnchanged=True,reason='The original NO-Q diagnostic included numeric qualityDelta; retain it as a Q-conditioned diagnostic, add strict coefficient-level NO-Q with qualityDelta removed. Structural projections themselves remain frozen-Q-conditioned.',calibrationOnly=True,model=model,validation=macro(val,model),developmentNotRead=True))
