"""Phase E: exact joins, legal generic state, quantity-aware labels. No app imports."""
from phase_d_core import np,collections,gzip,json,math,pathlib,sys,time,load,dump,sha,digest,evidence_group,CAL,DEV,readrows,write_rows,qualities
from phase_d1_core import sigmoid,logit,logloss
OUT=pathlib.Path('docs/evidence/v2-phase-e'); DATA=pathlib.Path('data/historical/normalized/phase-e'); RUN=pathlib.Path('data/historical/evaluation/phase-e')
SETS=CAL+DEV; SEED='DraftTG-phase-e-v1'
def guard():
 r=load(OUT/'registration.json')
 for p,k in ((OUT/'preregistration.json','planSha256'),(OUT/'preservation.json','preservationSha256'),(pathlib.Path('V2_PHASE_B1_SPLIT_MANIFEST.json'),'splitSha256')):
  if sha(p)!=r[k]:raise ValueError('Registration changed')
 for p,h in load(OUT/'preservation.json')['files'].items():
  if sha(p)!=h:raise ValueError('Frozen file changed '+p)
 # Inherit earlier input freezes as well as preserving their ledgers themselves.
 for namespace in ('v2-phase-c','v2-phase-d','v2-phase-d1'):
  previous=load(pathlib.Path('docs/evidence')/namespace/'preservation.json')['files']
  entries=previous.items() if isinstance(previous,dict) else ((r['path'],r['sha256']) for r in previous)
  for p,h in entries:
   if sha(p)!=h:raise ValueError('Inherited frozen input changed '+p)
 if (OUT/'input-hashes.json').exists():
  for p,h in load(OUT/'input-hashes.json')['files'].items():
   if sha(p)!=h:raise ValueError('Phase E input changed '+p)
def allowed(s):
 if s not in SETS:raise ValueError('Protected/unregistered set '+s)
def meta():return load(DATA/'metadata.json')
def counts(qs):
 ids=[c['Card']['CanonicalId'] for c in qs]
 if any(i is None for i in ids) or len(ids)!=len(set(ids)) or any(type(c['Count'])!=int or c['Count']<=0 for c in qs):raise ValueError('unresolved-or-invalid-quantities')
 return dict(zip(ids,[c['Count'] for c in qs]))
def subset(pool,build,m):return all(m[i]['name'] in ('Plains','Island','Swamp','Mountain','Forest') or n<=pool.get(i,0) for i,n in build.items())
def qualify_draft(ds,b,m):
 if b is None:return 'no-observed-build',None
 try:
  build=counts(b['MainDeck']);pool={};ordered=sorted(ds,key=lambda d:d['Inputs']['GlobalPickIndex'])
  if len(ordered) not in (39,42,45):return 'incomplete-sequence',None
  per=len(ordered)//3
  for k,d in enumerate(ordered):
   x=d['Inputs'];i=d['Label']['ActualPickedCard']['CanonicalId'];p=x['PackNumber'];n=x['PickNumber']
   if (x['GlobalPickIndex'],p,n)!=(k,k//per+1,k%per+1):return 'noncontiguous-coordinates',None
   if n==1 and sum(counts(x['OfferedCards']).values())!=per:return 'opening-pack-length-conflict',None
   if not x['PoolIsComplete'] or counts(x['PoolBeforePick'])!=pool:return 'pool-history-conflict',None
   if i not in m or i not in counts(x['OfferedCards']):return 'picked-identity-or-pack-invalid',None
   pool[i]=pool.get(i,0)+1
  if any(i not in m or not m[i]['known'] for i in build):return 'build-metadata-missing',None
  if sum(build.values())!=40:return 'not-40-cards',None
  if b['Games']<3:return 'insufficient-observed-support',None
  if b['Configurations']!=1:return 'modal-support-unavailable',None
  if not 12<=sum(n for i,n in build.items() if m[i]['land'])<=20:return 'land-count-suspicious',None
  if not subset(pool,build,m):return 'build-not-subset-of-pool',None
  return None,(pool,build,ordered)
 except (KeyError,ValueError):return 'invalid-identity-or-quantities',None
def qualify():
 guard();DATA.mkdir(parents=True,exist_ok=True);RUN.mkdir(parents=True,exist_ok=True);m=meta();evidence=[]
 for s in SETS:
  root=pathlib.Path('data/historical/normalized/phase-b1-v1' if s in ('ONE','MOM') else 'data/historical/normalized/phase-b-v1')
  pp=root/f'{s}.PremierDraft.decisions.jsonl.gz';bp=root/f'{s}.PremierDraft.primary-builds.jsonl.gz'
  sources=[pp,pathlib.Path(f'data/historical/normalized/phase-b1-v1/{s}.PremierDraft.bulk.jsonl.gz')];drafts={};conflict=set();builds={};ambiguous=set()
  for p in sources:
   for d in readrows(p):
    x=d['Inputs'];key=x['DraftId'];k=x['GlobalPickIndex']
    if x['SetCode']!=s or x['Format']!='PremierDraft' or d['Provenance']['Provider']!='17LandsPublic':raise ValueError('source-scope-conflict')
    bucket=drafts.setdefault(key,{})
    if k in bucket and (bucket[k]['Inputs']!=x or bucket[k]['Label']!=d['Label']):conflict.add(key)
    bucket[k]=d
  for b in readrows(bp):
   key=b['DraftId']
   if key in builds:ambiguous.add(key)
   builds[key]=b
  excluded=collections.Counter();accepted=[];complete=joined=decisions=ident=total=0
  sourceHashes=dict(picksSha256=sha(pp),buildsSha256=sha(bp),bulkSha256=sha(sources[1]))
  for key,ds in sorted(drafts.items()):
   rows=list(ds.values());complete+=len(ds) in (39,42,45);joined+=key in builds
   total+=sum(sum(c['Count'] for c in d['Inputs']['OfferedCards']+d['Inputs']['PoolBeforePick'])+1 for d in rows)
   ident+=sum(sum(c['Count'] for c in d['Inputs']['OfferedCards']+d['Inputs']['PoolBeforePick'] if c['Card']['CanonicalId'] in m)+int(d['Label']['ActualPickedCard']['CanonicalId'] in m) for d in rows)
   reason,result=qualify_draft(rows,builds.get(key),m)
   if key in conflict:reason='conflicting-canonical-sources'
   if key in ambiguous:reason='ambiguous-build-join'
   if reason:excluded[reason]+=1;continue
   pool,build,rows=result;b=builds[key];part='target' if not evidence_group(s,'PremierDraft',key) else ('validation' if int(digest(SEED+'/validation/'+key)[:8],16)%5==0 else 'train')
   picks=[dict(id=d['Label']['ActualPickedCard']['CanonicalId'],pack=d['Inputs']['PackNumber'],pick=d['Inputs']['PickNumber'],index=d['Inputs']['GlobalPickIndex']) for d in rows]
   decisions+=sum(p['pick']<=14 for p in picks)
   accepted.append(dict(set=s,format='PremierDraft',provider='17LandsPublic',draft=key,partition=part,pool=pool,build=build,picks=picks,support={k:b[k] for k in ('Games','Configurations','OpeningObservedAgreement')},outcome=rows[-1]['Outcome'],provenance=dict(**sourceHashes,sourceUris=sorted({d['Provenance']['Uri'] for d in rows}))))
  write_rows(DATA/f'{s}.drafts.jsonl.gz',accepted)
  evidence.append(dict(set=s,pickDrafts=len(drafts),completeSequenceCount=complete,observedBuilds=len(builds),joined=len(set(drafts)&set(builds)),qualified=len(accepted),legalDecisions=decisions,pickedLabels=decisions,identityOccurrences=ident,totalIdentityOccurrences=total,exclusions=dict(excluded),partitions=dict(collections.Counter(r['partition'] for r in accepted)),fileSha256=sha(DATA/f'{s}.drafts.jsonl.gz')))
 dump(OUT/'qualification.json',dict(datasets=evidence,basicSupplyException='Ordinary basic lands are supplied by Arena; they alone may exceed drafted quantities.',copyLabels='m/n fractional; exact kept pick occurrence unknowable',qualified=sum(r['qualified'] for r in evidence),decisions=sum(r['legalDecisions'] for r in evidence),noAcquisition=True))
def rows(s):allowed(s);return list(readrows(DATA/f'{s}.drafts.jsonl.gz'))
def color_load(pool,m):
 v=np.zeros(5)
 for i,n in pool.items():
  if not m[i]['land']:
   for c in m[i]['colors']:v[c]+=n/max(1,len(m[i]['colors']))
 return v
def pair(pool,m):
 v=color_load(pool,m);return tuple(int(c) for c in np.argsort(-v,kind='stable')[:2])
def playable(i,p,m):return all(c in p for c in m[i]['colors'])
def curve(cards,m):return [sum(n for i,n in cards.items() if not m[i]['land'] and min(7,int(m[i]['manaValue'] or 0))==k) for k in range(8)]
def role(i,name,m):
 r=m[i]['roleProfile'];return r.get(name)
ROLE_NAMES=('CombatTrick','CardAdvantage','Ramp','Finisher','Defensive','Aggressive','Fixing')
def decorate(f,i,audit,m):
 roles=m[i]['roleProfile']['Roles']
 f['removalUnknown']=float(not f['removal']);f['fixingUnknown']=float(not f['fixing'])
 for name in ROLE_NAMES:
  known=float(name in roles);f['tag'+name]=known;f['unknown'+name]=1-known
 f['projectedRemoval']=sum(n*(role(j,'RemovalWeight',m) or 0) for j,n in audit['selected'].items())/3
 f['removalHole']=max(0,1-f['projectedRemoval'])
 return f
_projection_cache={}
def selected(pool,s,m,q,land=17,structural=True,prob=None):
 cachekey=(s,tuple(sorted(pool.items())),land,structural)
 if prob is None and cachekey in _projection_cache:return _projection_cache[cachekey]
 best=None
 for a in range(5):
  for b in range(a+1,5):
   p=(a,b);ids=[i for i in pool if not m[i]['land'] and playable(i,p,m)];chosen={}
   for k in range(40-land):
    eligible=[i for i in ids if chosen.get(i,0)<pool[i]]
    if not eligible:break
    cv=curve(chosen,m);cr=sum(n for i,n in chosen.items() if m[i]['creature']);early=sum(cv[:3]);high=sum(cv[5:]);rem=sum(n*(role(i,'RemovalWeight',m) or 0) for i,n in chosen.items())
    def value(i):
     c=m[i];mv=c['manaValue'] or 0;z=q.get((s,i),.5)
     if structural:z+=.12*c['creature']*max(0,14-cr)/14+.10*(mv<=2)*max(0,4-early)/4-.08*(mv>=5)*max(0,high-4)/3+.04*(role(i,'RemovalWeight',m) or 0)*max(0,3-rem)/3
     if prob is not None:z=.5*z+.5*prob.get(i,.5)
     return z
    i=min(eligible,key=lambda i:(-value(i),i));chosen[i]=chosen.get(i,0)+1
   objective=sum(n*q.get((s,i),.5) for i,n in chosen.items())/max(1,40-land)+min(len([i for i in chosen]),30)*.00001-abs(len(chosen)==0)*10
   if sum(chosen.values())<40-land:objective-=2*(40-land-sum(chosen.values()))
   if best is None or objective>best[0]:best=(objective,chosen,p)
 result=best[1],best[2]
 if prob is None:
  if len(_projection_cache)>2000:_projection_cache.clear()
  _projection_cache[cachekey]=result
 return result
def feature(pool,i,s,m,q,index):
 # Only current pool-before and candidate are accepted; no build/outcome/future argument.
 before,p=selected(pool,s,m,q);afterpool=dict(pool);afterpool[i]=afterpool.get(i,0)+1;after,ap=selected(afterpool,s,m,q)
 c=m[i];cv=curve(before,m);aftercv=curve(after,m);cr=sum(n for j,n in before.items() if m[j]['creature']);acr=sum(n for j,n in after.items() if m[j]['creature']);mv=c['manaValue'] or 0;raw=q.get((s,i),.5)
 displaced=sorted(j for j,n in before.items() if n>after.get(j,0));cut=min((q.get((s,j),.5) for j in before),default=.5);pip=c.get('pips',[0]*5)
 known=any(t in c['roleProfile'].get('Roles','') for t in ('Removal','Fixing','CardAdvantage','CombatTrick','Finisher','Defensive','Aggressive'));rem=role(i,'RemovalWeight',m) or 0;fix=float('Fixing' in c['roleProfile'].get('Roles',''))
 f=dict(intercept=1,Q=raw-.5,playable=float(playable(i,p,m)),stage=min(index,44)/44,mana=min(mv,10)/10,manaUnknown=float(c['manaValue'] is None),creature=float(c['creature']),land=float(c['land']),colorless=float(not c['colors']),multicolor=float(len(c['colors'])>1),pips=sum(pip)/4,pipUnknown=float(not c.get('pipsKnown',False)),offPips=sum(v for k,v in enumerate(pip) if k not in p)/4,copyCount=min(pool.get(i,0),5)/5,poolSize=sum(pool.values())/45,projectedSpells=sum(before.values())/23,creatureCount=cr/16,early=sum(cv[:3])/4,top=sum(cv[5:])/6,earlyHole=max(0,4-sum(cv[:3]))/4,creatureHole=max(0,14-cr)/14,topSaturation=max(0,sum(cv[5:])-4)/3,roleUnknown=float(not known),removal=rem,fixing=fix,roleKnown=float(known),membership=float(after.get(i,0)>before.get(i,0)),cutDelta=raw-cut,creatureDelta=(acr-cr)/2,earlyDelta=(sum(aftercv[:3])-sum(cv[:3]))/2,topDelta=(sum(aftercv[5:])-sum(cv[5:]))/2,qualityDelta=sum(n*q.get((s,j),.5) for j,n in after.items())-sum(n*q.get((s,j),.5) for j,n in before.items()),colorChange=float(p!=ap))
 f.update({f'curve{k}':cv[k]/6 for k in range(8)})
 audit=dict(pair=p,afterPair=ap,displaced=displaced,cutQ=cut,selected=before)
 return decorate(f,i,audit,m),audit
def heuristic(f):return float(np.clip(.10+.45*f['playable']*f['membership']+.20*math.tanh(4*f['cutDelta'])+.15*f['playable']*(f['creatureHole']*f['creature']+f['earlyHole']*(f['mana']<=.2)-f['topSaturation']*(f['mana']>=.5)),0,1))
def features():
 guard();m=meta();q=qualities();countsOut=[]
 for s in SETS:
  examples=[]
  for r in rows(s):
   pool={}
   for p in r['picks']:
    i=p['id']
    if p['pick']<=14 and not m[i]['basic']:
     f,a=feature(pool,i,s,m,q,p['index']);examples.append(dict(set=s,draft=r['draft'],partition=r['partition'],pack=p['pack'],pick=p['pick'],id=i,x=f,y=r['build'].get(i,0)/r['pool'][i],identityIncluded=i in r['build'],draftedCopies=r['pool'][i],buildCopies=r['build'].get(i,0),copyOrdinal=pool.get(i,0)+1,audit=a))
    pool[i]=pool.get(i,0)+1
  write_rows(DATA/f'{s}.features.jsonl.gz',examples);countsOut.append(dict(set=s,n=len(examples),partitions=dict(collections.Counter(r['partition'] for r in examples))))
 dump(OUT/'features.json',dict(datasets=countsOut,legalBoundary='feature(poolBefore,candidate,metadata,frozenQ,index); labels attached afterward',unknownRoles='HasAssignedRoles false remains unknown'))
def pair_tasks():
 guard();m=meta();q=qualities();e=[]
 for s in SETS:
  tasks=[]
  for r in rows(s):
   p=pair(r['pool'],m);included=[i for i in r['build'] if i in r['pool'] and not m[i]['land']];excluded=[i for i in r['pool'] if i not in r['build'] and not m[i]['land']]
   for i in sorted(included):
    def distance(j):return abs(q.get((s,i),.5)-q.get((s,j),.5))/.1+abs((m[i]['manaValue'] or 0)-(m[j]['manaValue'] or 0))/2+abs(r['pool'][i]-r['pool'][j])/3
    candidates=[j for j in excluded if playable(j,p,m)==playable(i,p,m) and m[i]['creature']==m[j]['creature'] and abs(q.get((s,i),.5)-q.get((s,j),.5))<=.1 and abs((m[i]['manaValue'] or 0)-(m[j]['manaValue'] or 0))<=2]
    if not candidates:continue
    j=min(candidates,key=lambda j:(distance(j),digest(SEED+'/'+r['draft']+'/'+i+'/'+j)))
    # Leave one copy out of current completed pool for both counterfactual evaluations.
    xs=[]
    for k in (i,j):
     pool=dict(r['pool']);pool[k]-=1
     if pool[k]==0:del pool[k]
     xs.append(feature(pool,k,s,m,q,44)[0])
    tasks.append(dict(set=s,draft=r['draft'],partition=r['partition'],positive=i,negative=j,distance=distance(j),x=xs))
  write_rows(DATA/f'{s}.pairs.jsonl.gz',tasks);target=[r for r in tasks if r['partition']=='target'];e.append(dict(set=s,pairs=len(target),drafts=len({r['draft'] for r in target}),allPairs=len(tasks),meanDistance=float(np.mean([r['distance'] for r in target])) if target else None))
 dump(OUT/'pair-task-freeze.json',dict(datasets=e,healthyCoverage=all(r['pairs']>=500 and r['drafts']>=100 for r in e if r['set'] in DEV),matchingSha256={s:sha(DATA/f'{s}.pairs.jsonl.gz') for s in SETS},candidatePerformanceNotRead=True))
