"""Copy-aware final-pool reconstruction. Final pools never enter inference at a pick."""
from phase_e_models import *
FREE={'Plains':0,'Island':1,'Swamp':2,'Mountain':3,'Forest':4}
def main_from(deck,m):
 if deck is None:return {},(),17
 cards={e['CardIdentifier']['Value'] if isinstance(e['CardIdentifier'],dict) else e['CardIdentifier']:e['Count'] for e in deck['Nonlands']+deck['NonbasicLands']}
 for e in deck['GeneratedBasics']:cards['basic:'+'WUBRG'[('Plains','Island','Swamp','Mountain','Forest').index(e['Type'])]]=e['Count']
 return cards,tuple('WUBRG'.index(c) for c in deck['Plan']['Pair']['Code']),deck['LandCount']
def canonical(build,m):
 result={}
 for i,n in build.items():
  key='basic:'+'WUBRG'[FREE[m[i]['name']]] if m[i]['name'] in FREE else i;result[key]=result.get(key,0)+n
 return result
def island(i,m):return i.startswith('basic:') or m[i]['land']
def nonland(build,m):return {i:n for i,n in build.items() if not island(i,m)}
def allocate(spells,pool,p,land,m):
 result=dict(spells);nonbasic=[i for i in pool if m[i]['land'] and not m[i]['basic'] and any(k in p for k in m[i].get('produced',[]) if k<5)]
 sources={c:0 for c in p}
 for i in sorted(nonbasic,key=lambda i:(-len(set(m[i].get('produced',[]))&set(p)),i)):
  n=min(pool[i],max(0,min(4,land)-sum(v for j,v in result.items() if m[j]['land'])));result[i]=n
  for c in p:
   if c in m[i].get('produced',[]):sources[c]+=n
 remain=land-sum(n for i,n in result.items() if m[i]['land']);demand={c:sum(n*m[i]['pips'][c] for i,n in spells.items()) for c in p};total=sum(demand.values())
 quotas={c:remain*(demand[c]/total if total else 1/len(p)) for c in p};basics={c:int(quotas[c]) for c in p}
 for c in sorted(p,key=lambda c:(-(quotas[c]-basics[c]),c))[:remain-sum(basics.values())]:basics[c]+=1
 for c,n in basics.items():
  if n:result['basic:'+'WUBRG'[c]]=n
 return {i:n for i,n in result.items() if n}
def reconstruction(pred,truth,m):
 truth=canonical(truth,m);a=nonland(pred,m);b=nonland(truth,m);inter=sum(min(n,b.get(i,0)) for i,n in a.items());na=sum(a.values());nb=sum(b.values());precision=inter/max(1,na);recall=inter/max(1,nb)
 overlap=sum(min(n,truth.get(i,0)) for i,n in pred.items());union=sum(max(pred.get(i,0),truth.get(i,0)) for i in set(pred)|set(truth));cv=np.array(curve(a,m));tc=np.array(curve(b,m))
 colors=lambda d:tuple(np.argsort(-color_load(nonland(d,m),m),kind='stable')[:2])
 cl=lambda d:sum(n for i,n in nonland(d,m).items() if m[i]['creature'])
 land=lambda d:sum(n for i,n in d.items() if island(i,m));rem=lambda d:sum(n*(role(i,'RemovalWeight',m) or 0) for i,n in nonland(d,m).items())
 return dict(copyOverlap=overlap/40,exactCopyOverlap=overlap,nonlandPrecision=precision,nonlandRecall=recall,nonlandF1=2*inter/max(1,na+nb),Jaccard=overlap/max(1,union),Top23Precision=precision,Top23Recall=recall,landMAE=abs(land(pred)-land(truth)),mainColorAgreement=float(set(colors(pred))==set(colors(truth))),splashAgreement=float({c for c,v in enumerate(color_load(a,m)) if v>0 and c not in colors(pred)}=={c for c,v in enumerate(color_load(b,m)) if v>0 and c not in colors(truth)}),creatureMAE=abs(cl(pred)-cl(truth)),curveL1=float(abs(cv-tc).sum()),removalDifference=rem(pred)-rem(truth),totalCards=sum(pred.values()))
def meanrows(rs):return {k:float(np.mean([r[k] for r in rs])) if rs else None for k in ('copyOverlap','exactCopyOverlap','nonlandPrecision','nonlandRecall','nonlandF1','Jaccard','Top23Precision','Top23Recall','landMAE','mainColorAgreement','splashAgreement','creatureMAE','curveL1','removalDifference','totalCards')}|dict(n=len(rs))
def index_current():
 guard();m=meta();index={};times=[]
 with open(RUN/'current-builder.jsonl',encoding='utf8') as f:
  for line in f:
   r=json.loads(line);cards,p,land=main_from(r['Deck'],m);index[r['Set']+'/'+r['Draft']]=dict(main=cards,pair=p,lands=land,availability=r['Availability']);times.append(r['Milliseconds'])
 dump(DATA/'current-builder-index.json',index);dump(OUT/'current-builder-timing.json',dict(n=len(times),meanMs=float(np.mean(times)),p95Ms=float(np.quantile(times,.95))))
def builder(pool,s,m,q,name,models=None,scale=1):
 if name=='highest-Q':spells,p=selected(pool,s,m,q,structural=False);land=17
 else:
  spells,p=selected(pool,s,m,q);average=sum(n*(m[i]['manaValue'] or 0) for i,n in spells.items())/max(1,sum(spells.values()));land=int(np.clip(round(17+scale*(average-3)),15,19)) if name!='curve-17' else 17
  prob=None
  if name=='C':
   prob={}
   for i in pool:
    if m[i]['land']:continue
    before=dict(pool);before[i]-=1
    if not before[i]:del before[i]
    f,_=feature(before,i,s,m,q,44);prob[i]=predict(models['E-C'],f)
  spells,p=selected(pool,s,m,q,land=land,prob=prob)
 return allocate(spells,pool,p,land,m)
def evaluate_builders():
 guard();m=meta();q=qualities();models=calibrated_models();current=load(DATA/'current-builder-index.json')
 opts=[]
 for scale in (.5,1,1.5):
  scores=[]
  for s in CAL:
   rs=[r for r in rows(s) if r['partition']=='validation'];scores.append(float(np.mean([reconstruction(builder(r['pool'],s,m,q,'B',scale=scale),r['build'],m)['nonlandF1'] for r in rs])))
  opts.append((float(np.mean(scores)),scale))
 scale=max(opts,key=lambda v:(v[0],-v[1]))[1]
 dump(OUT/'builder-selection.json',dict(calibrationCandidates=opts,scale=scale,developmentNotRead=True,objective='Set-macro calibration-validation nonland copy F1',landRule='clip(round(17+scale*(current projected spell average MV-3)),15,19)'))
 result={};cut=collections.defaultdict(list);examplesOut=[];allIntegrity=True
 for s in SETS:
  result[s]={};buckets=collections.defaultdict(list)
  for r in rows(s):
   if r['partition']!='target':continue
   key=s+'/'+r['draft'];preds={'current':current[key]['main']}
   for name in ('highest-Q','curve-17','B','C'):preds[name]=builder(r['pool'],s,m,q,name,models,scale)
   for name,pred in preds.items():
    metric=reconstruction(pred,r['build'],m);buckets[name].append(metric)
    if sum(pred.values())!=40 or any(not i.startswith('basic:') and n>r['pool'].get(i,0) for i,n in pred.items()):allIntegrity=False
   # Current projected-rank calibration uses actual selected decisions/strength from builder output.
   main=preds['current'];spells=sorted([i for i in r['pool'] if not m[i]['land']],key=lambda i:(-int(main.get(i,0)>0),-q.get((s,i),.5),i));rank=0
   for i in spells:
    for ordinal in range(r['pool'][i]):
     rank+=1;label=r['build'].get(i,0)/r['pool'][i];bucket='1-10' if rank<=10 else '11-20' if rank<=20 else '21-23' if rank<=23 else '24-26' if rank<=26 else '27+'
     cut[bucket].append(label)
   if len(examplesOut)<6:examplesOut.append(dict(set=s,draft=r['draft'],pool=r['pool'],observed=r['build'],builders=preds))
  result[s]={k:meanrows(v) for k,v in buckets.items()}
 dump(OUT/'reconstruction.json',dict(bySet=result,allDeckIntegrity=allIntegrity,top23Caveat='Precision/recall are selected-spell copy metrics; flexible builders may select 21-25 spells, so no universal literal 23.',examples=examplesOut))
 dump(OUT/'cutline.json',dict(bins={k:dict(n=len(v),observedCopyInclusion=float(np.mean(v))) for k,v in cut.items()},rankDefinition='Actual production starter membership first, then frozen Q within membership group; ties are source-ID order. This is an audit ordering, not a claimed optimizer objective rank.'))
