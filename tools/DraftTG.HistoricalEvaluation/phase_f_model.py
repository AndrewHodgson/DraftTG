"""Deterministic marginal deck utility; no labels, learned set/card IDs or app changes."""
from phase_f_core import *
from functools import lru_cache
@lru_cache(maxsize=None)
def hyper_tail(sources,draws,need,deck=40):
 if need<=0:return 1.
 if sources<need or draws<need:return 0.
 denominator=math.comb(deck,draws)
 return sum(math.comb(sources,k)*math.comb(deck-sources,draws-k) for k in range(need,min(sources,draws)+1) if 0<=draws-k<=deck-sources)/denominator
def castability(card,sources,lands):
 if card['land']:return dict(supported=True,score=1.,onCurve=None,byGrace=None,requirements=[0]*6)
 if not card.get('costSupported',False) or card['manaValue'] is None:return dict(supported=False,score=0.,reason='Unsupported/unknown mana cost; no silent hybrid/Phyrexian/X/alternate-cost equivalence.')
 mv=max(0,int(math.ceil(card['manaValue'])));need=card['pipNeed'];turn=max(2,min(8,mv));onDraw=min(40,7+turn-1);graceDraw=min(40,onDraw+2)
 def score(draws):return min([hyper_tail(lands,draws,mv)]+[hyper_tail(int(sources[k]),draws,n) for k,n in enumerate(need) if n])
 grace=score(graceDraw);return dict(supported=True,score=min(1,grace/.8),onCurve=score(onDraw),byGrace=grace,requirements=need,drawsByGrace=graceDraw,interpretation='Minimum marginal source tails, NOT exact joint cast-on-curve probability.')
def structure(cards,m):
 v=dict(creatures=0,early=0,mv3=0,mv4=0,top=0,removal=0.,ramp=0.,cardAdvantage=0.,unknownNarrow=0,spells=0)
 for i,n in cards.items():
  c=m[i]
  if c['land']:continue
  v['spells']+=n;mv=c['manaValue'] or 0;v['creatures']+=n*c['creature'];v['early']+=n*(mv<=2 and (c['creature'] or c.get('broadMainDeck',False)));v['mv3']+=n*(mv==3);v['mv4']+=n*(mv==4);v['top']+=n*(mv>=5);v['unknownNarrow']+=n*c.get('unknownNarrow',True)
  if c.get('broadMainDeck',False):
   v['removal']+=n*c['roleProfile']['RemovalWeight'];v['ramp']+=n*('Ramp' in c['roleProfile']['Roles']);v['cardAdvantage']+=n*('CardAdvantage' in c['roleProfile']['Roles'])
 return v
def potential_parts(v):
 c=v['creatures'];e=v['early'];h=v['top'];r=v['removal']
 return dict(creature=-(max(0,14-c)**2/14+max(0,c-18)**2/6),early=-max(0,4-e)**2/4,top=-max(0,h-3)**2/4,removal=-max(0,3-r)**2/3,
  curve=-.2*(max(0,6-v['mv3'])**2/6+max(0,4-v['mv4'])**2/4))
def potential(v,weight):return weight*sum(potential_parts(v).values())
def virtual_level(picks,openSlots,base):
 remaining=max(0,42-picks-1)
 return .35 if remaining==0 else float(np.clip(base+.05*math.tanh((remaining*.8/max(1,openSlots)-1)/3),.35,.65))
class MarginalModel:
 def __init__(self,m,q,params,probability=None):self.m=m;self.q=q;self.params=params;self.probability=probability;self.cache={};self.builderTimes=[];self.castTimes=[];self.manaTimes=[]
 def quality(self,s,i):return self.q.get((s,i),.5)
 def land_sources(self,pool,pair,splash=None):
  ids=[]
  for i,n in pool.items():
   c=self.m[i]
   if not c['land'] or c['basic'] or not c.get('sourceKnown',False):continue
   colorlessNeed=any(self.m[j].get('pipNeed',[0]*6)[5]>0 for j in pool if not self.m[j]['land'])
   if set(c['produced'])&set(pair) or colorlessNeed and 5 in c['produced']:ids.extend([i]*n)
  ids.sort(key=lambda i:(-len(set(self.m[i]['produced'])&set(pair)),-int(splash is not None and splash in self.m[i]['produced']),i))
  return collections.Counter(ids[:4])
 def allocate(self,cards,pool,pair,land,splash=None):
  t=time.perf_counter();nonbasic=self.land_sources(pool,pair,splash);source=[0]*6
  for i,n in nonbasic.items():
   for k in self.m[i]['produced']:
    if k<6:source[k]+=n
  available=land-sum(nonbasic.values());offBasic=int(splash is not None);available-=offBasic
  demand={k:sum(n*self.m[i]['pipNeed'][k] for i,n in cards.items()) for k in pair};used=[k for k in pair if demand[k]>0]
  if not used:used=[pair[0]]
  floor=min(6,available//len(used));left=available-floor*len(used);total=sum(demand[k] for k in used);quotas={k:left*(demand[k]/total if total else 1/len(used)) for k in used};basics={k:floor+int(quotas[k]) for k in used}
  for k in sorted(used,key=lambda k:(-(quotas[k]-int(quotas[k])),k))[:available-sum(basics.values())]:basics[k]+=1
  if splash is not None:basics[splash]=1
  for k,n in basics.items():source[k]+=n
  self.manaTimes.append((time.perf_counter()-t)*1000)
  return dict(lands=land,nonbasic=dict(nonbasic),basics=basics,sources=source,effectiveFixing=sum(n for i,n in nonbasic.items() if len([k for k in self.m[i]['produced'] if k<5])>=2),splash=splash)
 def casts(self,cards,mana):
  start=time.perf_counter();result={i:castability(self.m[i],mana['sources'],mana['lands']) for i in cards};self.castTimes.append((time.perf_counter()-start)*1000);return result
 def _projection(self,pool,s,pair,land,splash):
  target=40-land;spells=[]
  for i,n in sorted(pool.items()):
   c=self.m[i]
   if c['land'] or not c.get('costSupported',False) or c['manaValue'] is None:continue
   outside=[k for k in c['colors'] if k not in pair]
   if outside:
    if splash is None or outside!=[splash] or c['pipNeed'][splash]>1 or self.quality(s,i)<.65:continue
   spells.extend([i]*n)
  rough=collections.Counter(spells);mana=self.allocate(rough,pool,pair,land,splash)
  if splash is not None and mana['sources'][splash]<3:return None
  cast=self.casts(rough,mana);ids=sorted(rough);capacity=np.array([rough[i] for i in ids]);chosen=np.zeros(len(ids),dtype=int)
  utilities=np.array([self.quality(s,i)*cast[i]['score'] for i in ids]);flags=np.array([[self.m[i]['creature'],int((self.m[i]['manaValue'] or 0)<=2 and (self.m[i]['creature'] or self.m[i].get('broadMainDeck',False))),int(self.m[i]['manaValue']==3),int(self.m[i]['manaValue']==4),int((self.m[i]['manaValue'] or 0)>=5),self.m[i]['roleProfile']['RemovalWeight'] if self.m[i].get('broadMainDeck',False) else 0] for i in ids],dtype=float)
  if not ids:return dict(main={},pair=pair,land=land,mana=mana,casts={},structure=structure({},self.m),utility=0.,objective=0.,targetSpells=target,complete=False,splash=splash)
  state=dict(creatures=0.,early=0.,mv3=0.,mv4=0.,top=0.,removal=0.)
  # Exact separable discrete marginal potentials; no linear role bonus.
  gridCounts=np.arange(121)/2
  tables=np.array([-(np.maximum(0,14-gridCounts)**2/14+np.maximum(0,gridCounts-18)**2/6),-np.maximum(0,4-gridCounts)**2/4,-.2*np.maximum(0,6-gridCounts)**2/6,-.2*np.maximum(0,4-gridCounts)**2/4,-np.maximum(0,gridCounts-3)**2/4,-np.maximum(0,3-gridCounts)**2/3])
  tableIndices=np.arange(6)[None,:];flagIndices=(flags*2).astype(int)
  stateIndices=np.zeros(6,dtype=int)
  def scores():
   gains=self.params['structureWeight']*np.sum(tables[tableIndices,stateIndices[None,:]+flagIndices]-tables[np.arange(6),stateIndices][None,:],axis=1)
   gains=np.where(np.array([self.m[i].get('unknownNarrow',True) for i in ids]),np.minimum(0,gains),gains)
   return utilities+gains
  for _ in range(min(target,int(capacity.sum()))):
   z=scores();z[chosen>=capacity]=-np.inf;j=int(np.argmax(z));chosen[j]+=1
   stateIndices+=flagIndices[j]
   for ix,key in enumerate(state):state[key]+=flags[j,ix]
  main={i:int(n) for i,n in zip(ids,chosen) if n};mana=self.allocate(main,pool,pair,land,splash);cast=self.casts(main,mana)
  # One deterministic local spell exchange, based on full selected-deck castability/structure.
  # Selection marginal already enforces diminishing need; retain legal quantities throughout.
  value=sum(n*self.quality(s,i)*cast[i]['score'] for i,n in main.items());v=structure(main,self.m);objective=value+potential(v,self.params['structureWeight'])
  bench=[i for i in ids if main.get(i,0)<rough[i]]
  if main and bench:
   options=[]
   for add in sorted(bench,key=lambda i:(-self.quality(s,i)*castability(self.m[i],mana['sources'],land)['score'],i))[:4]:
    for remove in sorted(main,key=lambda i:(self.quality(s,i)*cast[i]['score'],i))[:4]:
     if add==remove:continue
     candidate=dict(main);candidate[remove]-=1
     if not candidate[remove]:del candidate[remove]
     candidate[add]=candidate.get(add,0)+1;newV=structure(candidate,self.m)
     approx=value+self.quality(s,add)*castability(self.m[add],mana['sources'],land)['score']-self.quality(s,remove)*cast[remove]['score']+potential(newV,self.params['structureWeight']);options.append((approx,add,remove,candidate))
   if options:
    approx,add,remove,candidate=max(options,key=lambda z:(z[0],z[1],z[2]))
    if approx>objective+1e-9:
     newMana=self.allocate(candidate,pool,pair,land,splash);newCast=self.casts(candidate,newMana);newValue=sum(n*self.quality(s,k)*newCast[k]['score'] for k,n in candidate.items());newStructure=structure(candidate,self.m);newObjective=newValue+potential(newStructure,self.params['structureWeight'])
     if newObjective>objective+1e-9:main,mana,cast,value,v,objective=candidate,newMana,newCast,newValue,newStructure,newObjective
  if splash is not None:
   baseCards={i:n for i,n in main.items() if set(self.m[i]['colors'])<=set(pair)};baseMana=self.allocate(baseCards,pool,pair,land);baseCast=self.casts(baseCards,baseMana)
   if any(baseCast[i]['score']-cast[i]['score']>.04 for i in baseCards):return None
   if sum(n for i,n in main.items() if splash in self.m[i]['colors'])>2:return None
  return dict(main=main,pair=pair,land=land,mana=mana,casts=cast,structure=v,utility=value,objective=objective,targetSpells=target,complete=sum(main.values())==target,splash=splash)
 def build(self,pool,s,picks,preferred=None):
  key=(s,tuple(sorted((i,n) for i,n in pool.items() if n)),picks,preferred)
  if key in self.cache:return self.cache[key]
  start=time.perf_counter();loads=np.zeros(5)
  for i,n in pool.items():
   c=self.m[i]
   if not c['land']:
    for k in c['colors']:loads[k]+=n*self.quality(s,i)/max(1,len(c['colors']))
  pairs=sorted([(a,b) for a in range(5) for b in range(a+1,5)],key=lambda p:(-sum(loads[k] for k in p),p))[:3]
  if preferred is not None and preferred not in pairs:pairs.append(preferred)
  projections=[]
  for pair in pairs:
   eligible=sum(n for i,n in pool.items() if not self.m[i]['land'] and set(self.m[i]['colors'])<=set(pair) and self.m[i].get('costSupported',False))
   lands=range(15,20) if eligible>=21 else (17,)
   splashes=[None]
   for color in range(5):
    if color in pair:continue
    sources=sum(n for i,n in self.land_sources(pool,pair,color).items() if color in self.m[i]['produced'])
    if sources>=2 and any(color in self.m[i]['colors'] and self.m[i]['pipNeed'][color]<=1 and self.quality(s,i)>=.65 for i in pool if not self.m[i]['land']):splashes.append(color)
   for land in lands:
    for splash in splashes:
     p=self._projection(pool,s,pair,land,splash)
     if p is None:continue
     openSlots=p['targetSpells']-sum(p['main'].values());p['openSlots']=openSlots;p['objective']+=openSlots*virtual_level(picks,openSlots,self.params['virtualBase'])
     projections.append(p)
  best=max(projections,key=lambda p:(p['objective'],p['complete'],-abs(p['land']-17),tuple(-k for k in p['pair'])))
  if preferred is not None:
   alternatives=[p for p in projections if p['pair']==preferred and best['objective']-p['objective']<=.02]
   if alternatives:best=max(alternatives,key=lambda p:p['objective'])
  top={p['pair']:max(x['objective'] for x in projections if x['pair']==p['pair']) for p in projections};values=np.array(list(top.values()));weights=np.exp((values-values.max())/.25);weights/=weights.sum();best=dict(best,pairPlausibility=float(weights[list(top).index(best['pair'])]),pairObjectives={''.join('WUBRG'[k] for k in p):v for p,v in top.items()},unmodeledPool=sum(n for i,n in pool.items() if not self.m[i]['land'] and not self.m[i].get('costSupported',False)))
  self.builderTimes.append((time.perf_counter()-start)*1000)
  if len(self.cache)>3000:self.cache.clear()
  self.cache[key]=best;return best
 def marginal(self,pool,i,s,picks,family='C',productionD=.5,supportProbability=None,before=None):
  c=self.m[i]
  if not c['land'] and not c.get('costSupported',False) or not c['known'] or c['land'] and not c.get('sourceKnown',False):return dict(available=False,DValue=productionD,reason='Unsupported castability or direct-source metadata; production D retained.',candidate=i)
  if c['land'] and family!='C':return dict(available=False,DValue=productionD,reason='Replacement-only family has no land benefit estimator; production D retained.',candidate=i)
  before=before or self.build(pool,s,picks);afterPool=dict(pool);afterPool[i]=afterPool.get(i,0)+1;after=self.build(afterPool,s,picks,before['pair'])
  selected=after['main'].get(i,0)>before['main'].get(i,0) if not c['land'] else after['mana']['nonbasic'].get(i,0)>before['mana']['nonbasic'].get(i,0)
  removed={k:n-after['main'].get(k,0) for k,n in before['main'].items() if n>after['main'].get(k,0)};added={k:n-before['main'].get(k,0) for k,n in after['main'].items() if n>before['main'].get(k,0)}
  replacement=sum(n*self.quality(s,k)*after['casts'][k]['score'] for k,n in added.items())-sum(n*self.quality(s,k)*before['casts'][k]['score'] for k,n in removed.items())
  virtual=virtual_level(picks,before['openSlots'],self.params['virtualBase']);replacement-=virtual*(sum(added.values())-sum(removed.values()))
  retained=sum(min(n,after['main'].get(k,0))*self.quality(s,k)*(after['casts'][k]['score']-before['casts'][k]['score']) for k,n in before['main'].items() if k in after['main'])
  sb=potential_parts(before['structure']);sa=potential_parts(after['structure']);deltas={k:self.params['structureWeight']*(sa[k]-sb[k]) for k in sb};structural=sum(deltas.values())
  casts=after['casts'].get(i,castability(c,after['mana']['sources'],after['land']));gate=casts['score'] if selected else 0.
  if c['land']:
   gate=1. if selected and retained>1e-9 else 0.;replacement=0.;structural=0.
  completeness=sum(before['main'].values())/max(1,before['targetSpells']);confidence=min(1,completeness)*picks/(picks+8)*(.5+.5*before['pairPlausibility']) if picks else 0.
  if family=='C':confidence*=.85+.15*(supportProbability if supportProbability is not None else .5)
  if c.get('unknownNarrow',True):structural=min(0,structural)
  raw=replacement+(structural if family!='A' else 0)+(retained if family=='C' else 0)
  value=float(np.clip(.1+gate*(.4+.4*confidence*math.tanh(raw/self.params['scale'])),0,1))
  return dict(available=True,candidate=i,CandidateSelected=selected,DisplacedCards=removed,AddedCards=added,ReplacementValue=replacement,VirtualReplacement=virtual,VirtualSlotsBefore=before['openSlots'],Castability=casts,AbsoluteEligibility=gate,CastabilityDelta=retained,ManaConsistencyDelta=retained,CurveDelta=deltas['curve']+deltas['top'],CreatureDelta=deltas['creature'],EarlyPlayDelta=deltas['early'],RemovalDelta=deltas['removal'],FixingDelta=retained if c['land'] else 0,RoleDelta=deltas['removal'],StructuralDelta=structural,Final23Probability=supportProbability,Confidence=confidence,RawMarginalValue=raw,DValue=value,BeforePair=before['pair'],AfterPair=after['pair'],BeforeMana=before['mana'],AfterMana=after['mana'],BeforeMain=before['main'],AfterMain=after['main'],ColorPairChanged=before['pair']!=after['pair'],LandCountChanged=before['land']!=after['land'],UnknownNarrow=c.get('unknownNarrow',True),OptionalPositiveRoleBonusAllowed=c.get('broadMainDeck',False),provenance=VERSION+'/'+family)
