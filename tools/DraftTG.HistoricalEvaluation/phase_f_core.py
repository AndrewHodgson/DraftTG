"""Independent Phase F namespace; reused Phase E evidence is strictly read-only."""
from phase_e_core import np,collections,gzip,json,math,pathlib,sys,time,load,dump,sha,digest,CAL,DEV,readrows,write_rows,counts
from phase_e_core import OUT as EOUT,DATA as EDATA,RUN as ERUN,rows as erows,qualities as equalities
OUT=pathlib.Path('docs/evidence/v2-phase-f');DATA=pathlib.Path('data/historical/normalized/phase-f');RUN=pathlib.Path('data/historical/evaluation/phase-f')
SETS=CAL+DEV;SEED='DraftTG-phase-f-v1';VERSION='research-d-phase-f-v1'
def guard():
 r=load(OUT/'registration.json')
 for p,k in ((OUT/'preregistration.json','planSha256'),(OUT/'preservation.json','preservationSha256'),(pathlib.Path('V2_PHASE_B1_SPLIT_MANIFEST.json'),'splitSha256')):
  if sha(p)!=r[k]:raise ValueError('F registration changed')
 for p,h in load(OUT/'preservation.json')['files'].items():
  if sha(p)!=h:raise ValueError('Frozen prior/production file changed: '+p)
def allowed(s):
 if s not in SETS:raise ValueError('Protected/unregistered set '+s)
def rows(s):allowed(s);return erows(s)
def meta():return load(DATA/'metadata.json')
def qualities():return {(r['set'],r['id']):r['Q'] for r in load(DATA/'quality.json')}
def prepare():
 guard();DATA.mkdir(parents=True,exist_ok=True);RUN.mkdir(parents=True,exist_ok=True)
 m=load(EDATA/'metadata.json');catalog=load('data/historical/normalized/phase-b1-v1/catalog.json');costs={}
 for c in catalog:
  front=c.get('card_faces',[c])[0] if c.get('card_faces') and c.get('layout')!='split' else c
  cost=front.get('mana_cost');tokens=[]
  if cost is not None:
   import re
   tokens=re.findall(r'\{([^{}]+)\}',cost)
  supported=cost is not None and ''.join('{'+t+'}' for t in tokens)==cost and all(t.isdigit() or t in 'WUBRGC' and len(t)==1 for t in tokens)
  d=m[c['id']];d.update(cost=cost,costSupported=supported,pipNeed=[tokens.count(k) for k in 'WUBRGC'],primaryFaceOnly=bool(c.get('card_faces')),unknownNarrow=not d['creature'] and not d['land'],broadMainDeck=d['creature'],sourceKnown=d['land'] and bool(d.get('produced')) and not bool(c.get('card_faces')))
  costs[c['id']]=cost
 q=equalities();stats=load('data/historical/normalized/phase-b1-v1/compatible-stats.json');changes=[]
 for ds in stats:
  s=ds['SetCode'];names={r['SourceIdentity'] for r in ds['Cards']}
  for (ss,i),v in list(q.items()):
   if ss==s and m[i]['name'] not in names:
    if v!=.5:changes.append(dict(set=s,id=i,name=m[i]['name'],oldQ=v,actualQ=.5))
    q[s,i]=.5
 dump(DATA/'metadata.json',m);dump(DATA/'quality.json',[dict(set=s,id=i,Q=v) for (s,i),v in sorted(q.items())]);dump(OUT/'input-contract.json',dict(correctionScope='F research lookup only: preserve production exact-name missing-row neutral policy; frozen E-C model and all E outputs untouched.',changes=changes,metadataSourceSha256=sha(EDATA/'metadata.json'),frozenEProbabilitySha256=sha(EDATA/'models-calibrated.json'),unsupportedCosts=sum(not d['costSupported'] and not d['land'] for d in m.values()),inheritedQualificationSha256=sha(EOUT/'qualification.json'),inheritedPairsSha256=sha(EOUT/'pair-task-freeze.json')))
def grid():
 import itertools
 return [dict(scale=s,structureWeight=w,virtualBase=v) for s,w,v in itertools.product((.05,.10),(.02,.04),(.45,.50,.55))]
def params_key(p):return f"s{p['scale']}/w{p['structureWeight']}/v{p['virtualBase']}"
