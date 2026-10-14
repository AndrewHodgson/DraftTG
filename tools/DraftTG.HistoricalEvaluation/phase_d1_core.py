"""Separate D.1 namespace, inherited evidence and strict immutable boundaries."""
import itertools,sqlite3,subprocess
from phase_d_core import (np,collections,gzip,json,math,pathlib,sys,time,load,dump,sha,digest,evidence_group,COMPATIBLE,CAL,DEV,readrows,write_rows,shell,softmax,rank_metrics,mean_metrics,nmf)
from phase_d_core import DATA as DDATA,OUT as DOUT,meta as dmeta,qualities
from phase_d_research import rows as drows,structures
OUT=pathlib.Path('docs/evidence/v2-phase-d1');DATA=pathlib.Path('data/historical/normalized/phase-d1')
SEED='DraftTG-phase-d1-v1';VERSION='research-a-phase-d1-v1';B1=pathlib.Path('data/historical/normalized/phase-b1-v1')
def guard():
    reg=load(OUT/'registration.json')
    for p,key in ((OUT/'preregistration.json','planSha256'),(OUT/'preservation.json','preservationSha256'),(pathlib.Path('V2_PHASE_B1_SPLIT_MANIFEST.json'),'splitSha256')):
        if sha(p)!=reg[key]:raise ValueError('D1 registration changed')
    for p,h in load(OUT/'preservation.json')['files'].items():
        if sha(p)!=h:raise ValueError('Frozen production/Phase D changed: '+p)
def allowed(s):
    if s not in COMPATIBLE:raise ValueError('Unqualified/spent/held-out D1 set '+s)
def audit_part(s,key):return 'utility' if int(digest(SEED+'/audit/'+s+'/'+key)[:8],16)%2==0 else 'confidence'
def rows(s,part='fit'):
    allowed(s)
    if part in ('utility','confidence'):return [r for r in drows(s,'audit') if audit_part(s,r['draft'])==part]
    return drows(s,part)
def meta():return load(DATA/'metadata.json')
def sigmoid(x):return 1/(1+np.exp(-np.clip(x,-30,30)))
def logit(x):
    x=np.clip(x,1e-5,1-1e-5);return np.log(x/(1-x))
def logloss(p,y):
    p=np.clip(p,1e-7,1-1e-7);y=np.asarray(y,dtype=float);return -(y*np.log(p)+(1-y)*np.log(1-p))
def cold_envelope(x,safe):
    # Lower monotone envelope never exceeds any calibrated safety weight.
    a=np.minimum.accumulate(np.array(safe,dtype=float)[::-1])[::-1]
    return dict(x=list(x),weights=a.tolist())
def cold_weight(n,mapping):return float(np.interp(math.log(max(1,n)),np.log(mapping['x']),mapping['weights'],left=0,right=mapping['weights'][-1]))
def pav(values,weights):
    blocks=[]
    for v,w in zip(values,weights):
        blocks.append([float(v),float(w),1])
        while len(blocks)>1 and blocks[-2][0]>blocks[-1][0]:
            b=blocks.pop();a=blocks.pop();weight=a[1]+b[1];blocks.append([(a[0]*a[1]+b[0]*b[1])/max(1,weight),weight,a[2]+b[2]])
    return [max(0,b[0]) for b in blocks for _ in range(b[2])]
def bin_conf(c):return min(4,int(np.searchsorted([.1,.25,.5,.75],c,side='right')))
def paired_utility(gains):
    if len(gains)<10:return dict(n=len(gains),mean=float(np.mean(gains)) if gains else None,se=None,lower=None,weight=0.)
    mean=float(np.mean(gains));se=float(np.std(gains,ddof=1)/math.sqrt(len(gains)));lower=mean-.84*se
    return dict(n=len(gains),mean=mean,se=se,lower=lower,weight=float(np.clip(lower/.02,0,1)))
def ensure_fit(s,rs):
    keys={r['draft'] for r in rows(s)}
    if any(r['key'] not in keys or not evidence_group(s,'PremierDraft',r['key']) for r in rs):raise ValueError('Target/audit build leaked into model fit')
def qualification():
    guard();DATA.mkdir(parents=True,exist_ok=True);m=dmeta();catalog=load(B1/'catalog.json')
    for r in catalog:
        if r['id'] not in m:continue
        front=r.get('card_faces',[{}])[0] if r.get('card_faces') and r.get('layout')!='split' else r
        typ=front.get('type_line',r.get('type_line'));m[r['id']].update(instantSorcery=bool(typ and ('Instant' in typ or 'Sorcery' in typ)),artifactEnchantment=bool(typ and ('Artifact' in typ or 'Enchantment' in typ)),typeKnown=typ is not None,printingSet=r.get('set','').upper())
    dump(DATA/'metadata.json',m);datasets=[]
    for s in COMPATIBLE:
        fit=rows(s);util=rows(s,'utility');conf=rows(s,'confidence');target=rows(s,'target')
        if set(r['draft'] for r in fit)&set(r['draft'] for r in util+conf+target):raise ValueError('Non-disjoint builds')
        datasets.append(dict(set=s,fit=len(fit),utility=len(util),confidence=len(conf),target=len(target),sourceSha256=sha(DDATA/f'{s}.builds.jsonl.gz')))
    dump(OUT/'qualification.json',dict(inheritedQualificationSha256=sha(DOUT/'build-qualification.json'),datasets=datasets,metadataSha256=sha(DATA/'metadata.json'),noAcquisition=True,noOutcomeSelection=True))
