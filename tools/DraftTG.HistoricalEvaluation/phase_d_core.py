"""Offline archetype research; outcome-free fit/inference and immutable boundaries."""
import collections,gzip,json,math,os,pathlib,sys,time
os.environ.setdefault('OPENBLAS_NUM_THREADS','1')
sys.path.insert(0,str(pathlib.Path('data/historical/cache/phase-d/python').resolve()))
import numpy as np
from phase_b1_core import load,dump,sha,digest,evidence_group,COMPATIBLE
OUT=pathlib.Path('docs/evidence/v2-phase-d');DATA=pathlib.Path('data/historical/normalized/phase-d-v1')
B1=pathlib.Path('data/historical/normalized/phase-b1-v1');OLD=pathlib.Path('data/historical/normalized/phase-b-v1')
SEED='DraftTG-phase-d-v1';VERSION='research-a-phase-d-v1';CAL=('DMU','BRO','WOE');DEV=('LCI','MKM','OTJ','ONE','MOM')
def guard():
    reg=load(OUT/'registration.json')
    if sha(OUT/'preregistration.json')!=reg['planSha256'] or sha(OUT/'preservation.json')!=reg['preservationSha256'] or sha('V2_PHASE_B1_SPLIT_MANIFEST.json')!=reg['splitSha256']:raise ValueError('Registration changed')
    for p,h in load(OUT/'preservation.json')['files'].items():
        if sha(p)!=h:raise ValueError('Frozen prior file changed: '+p)
def allowed(s):
    if s not in load('V2_PHASE_B1_SPLIT_MANIFEST.json')['Sets'] or s in ('MH3','DFT','BLB','DSK'):raise ValueError('Protected/unregistered set '+s)
def readrows(path):
    with gzip.open(path,'rt',encoding='utf-8') as f:
        for line in f:yield json.loads(line)
def write_rows(path,rows):
    path.parent.mkdir(parents=True,exist_ok=True)
    with gzip.open(path,'wt',encoding='utf-8',compresslevel=1) as f:
        for row in rows:f.write(json.dumps(row,separators=(',',':'),allow_nan=False)+'\n')
def audit_group(s,d):return int(digest(f'{SEED}/audit/{s}/{d}')[:8],16)%5==0
def meta():return load(DATA/'metadata.json')
def qualities():return {(r['set'],r['id']):r['Q'] for r in load('data/historical/normalized/phase-c-v1/quality.json')}
def build_eligibility(row,m):
    cards=row['MainDeck'];counts=[c['Count'] for c in cards];ids=[c['Card']['CanonicalId'] for c in cards]
    if any(type(n)!=int or n<=0 for n in counts) or sum(counts)!=40:return 'invalid-quantities-or-size'
    if len(set(ids))!=len(ids):return 'duplicate-identity'
    if any(i not in m or not m[i]['known'] for i in ids):return 'unknown-identity-or-types'
    if row['Games']<3:return 'under-three-observed-games'
    if row['Configurations']!=1:return 'mode-support-unavailable-multiple-configurations'
    lands=sum(c['Count'] for c in cards if m[c['Card']['CanonicalId']]['land'])
    if not 12<=lands<=20:return 'suspicious-land-composition'
    return None
def spell_load(cards,m):
    v=np.zeros(5)
    for i,n in cards.items():
        if not m[i]['land'] and m[i]['colors']:
            for c in m[i]['colors']:v[c]+=n/len(m[i]['colors'])
    return v
def shell(cards,m,context='pair'):
    v=spell_load(cards,m);order=np.argsort(-v,kind='stable')
    if context=='global':return 'all'
    if not v.sum():return 'colorless'
    colors=[int(c) for c in order[:2] if v[c]>0] if context=='pair' else [i for i,x in enumerate(v) if x/v.sum()>=.15]
    return ''.join('WUBRG'[i] for i in sorted(colors))
def vector(cards,vocab,m,representation):
    values=np.array([cards.get(i,0) for i in vocab],dtype=float)
    if representation=='binary':return (values>0).astype(float)
    if representation=='sqrt':return np.sqrt(np.minimum(values,3))
    if representation=='nonland':values*=np.array([not m[i]['land'] for i in vocab])
    return values/max(1,values.sum())
def softmax(x):
    x=np.array(x,dtype=float);e=np.exp(x-x.max());return e/e.sum()
def nmf(x,k,seed,steps=100):
    rng=np.random.default_rng(seed);w=rng.random((len(x),k))+.1;h=rng.random((k,x.shape[1]))+.1
    for _ in range(steps):
        h*= (w.T@x+1e-10)/(w.T@w@h+1e-10)
        w*= (x@h.T+1e-10)/(w@h@h.T+1e-10)
    return w,h
def memberships(x,h):
    w=np.full((len(x),len(h)),1/len(h))
    for _ in range(50):w*= (x@h.T+1e-10)/(w@h@h.T+1e-10)
    return w/np.maximum(w.sum(axis=1,keepdims=True),1e-10)
def stability(a,b):
    import itertools
    aa=a/np.maximum(np.linalg.norm(a,axis=1,keepdims=True),1e-10);bb=b/np.maximum(np.linalg.norm(b,axis=1,keepdims=True),1e-10)
    sim=aa@bb.T
    return max(float(np.mean([sim[i,j] for i,j in enumerate(p)])) for p in itertools.permutations(range(len(a))))
def pair_effect(observed,expected,left,right):
    if expected<5 or min(left,right)<20 or (observed<5 and (expected<20 or min(left,right)<30)):return 0.,0.
    confidence=expected/(expected+50)
    return float(np.clip(math.log((observed+5)/(expected+5)),-1,1))*confidence,confidence
def rank_metrics(scores,true=0):
    values=np.array(scores);better=int(np.sum(values>values[true]+1e-12));ties=int(np.sum(abs(values-values[true])<=1e-12));ranks=np.arange(better+1,better+ties+1,dtype=float)
    return dict(Top1=float(np.mean(ranks<=1)),Top3=float(np.mean(ranks<=3)),MRR=float(np.mean(1/ranks)),normalizedRank=float(np.mean((ranks-1)/max(1,len(scores)-1))))
def mean_metrics(rows):
    return dict(n=len(rows),**{k:float(np.mean([r[k] for r in rows])) if rows else None for k in ('Top1','Top3','MRR','normalizedRank')})
