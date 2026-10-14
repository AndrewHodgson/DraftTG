"""Offline lane research: disjoint position evidence and strictly causal observation state."""
import collections,gzip,hashlib,json,math,pathlib,statistics
from phase_b1_core import load,dump,sha,group_hash,evidence_group,COMPATIBLE
from phase_b_core import correlation,quantile
OUT=pathlib.Path('docs/evidence/v2-phase-c')
DATA=pathlib.Path('data/historical/normalized/phase-c-v1')
OLD=pathlib.Path('data/historical/normalized/phase-b-v1')
B1=pathlib.Path('data/historical/normalized/phase-b1-v1')
SEED='DraftTG-phase-c-v1';VERSION='research-l-phase-c-v1'
COLORS='WUBRG'

def digest(s):return hashlib.sha256(s.encode()).hexdigest()
def guard():
    reg=load(OUT/'registration.json');pres=load(OUT/'preservation.json')
    if sha(OUT/'preregistration.json')!=reg['planSha256'] or sha(OUT/'preservation.json')!=reg['preservationSha256']:raise ValueError('Phase C registration changed')
    for item in pres['files']:
        if sha(item['path'])!=item['sha256']:raise ValueError('Protected production/research changed: '+item['path'])
    return load('V2_PHASE_B1_SPLIT_MANIFEST.json')

def allowed(s):
    split=load('V2_PHASE_B1_SPLIT_MANIFEST.json')
    if s not in split['Sets'] or split['Sets'][s]=='Holdout' or s in split['SpentReference']:raise ValueError('Protected/unregistered set '+s)

def decisions(s,f='PremierDraft',bulk=False):
    allowed(s);base=B1 if s in ('ONE','MOM') else OLD
    path=B1/f'{s}.{f}.bulk.jsonl.gz' if bulk else base/f'{s}.{f}.decisions.jsonl.gz'
    with gzip.open(path,'rt',encoding='utf-8') as stream:
        for line in stream:yield json.loads(line)

def histogram(values):
    h=[0]*16
    for p,n in values.items():
        p=int(p)
        if not 1<=p<=15 or n<0:raise ValueError('Pack-local 1-based position required')
        h[p]+=n
    return h

def features(h,drafts):
    n=sum(h);mean=sum(i*v for i,v in enumerate(h))/n if n else None
    if not n:return dict(n=0,drafts=drafts,mean=None,median=None,trimmed=None,sd=None,hist=h,cdf=[0.]*16)
    values=[i for i,v in enumerate(h) for _ in range(v)]
    trim=int(n*.1);trimmed=statistics.mean(values[trim:n-trim]) if trim else mean
    return dict(n=n,drafts=drafts,mean=mean,median=statistics.median(values),trimmed=trimmed,
                sd=math.sqrt(sum(v*(i-mean)**2 for i,v in enumerate(h))/n),hist=h,cdf=[sum(h[:i+1])/n for i in range(16)])

def confidence(n,lam):return n/(n+lam) if n>0 else 0
def gate(q):return max(0,min(1,2*(q-.5)))
def fit_color(support):
    # Probability for color-composition labels; no fit to production score scale.
    weights=[math.exp(2*x) for x in support];total=sum(weights);return [x/total for x in weights]
def brier(p,y):return sum((a-b)**2 for a,b in zip(p,y))/5
def normalized(v):
    total=sum(v);return [x/total for x in v] if total>0 else None
def leader(v):return max(range(5),key=lambda c:v[c]) if max(v)-min(v)>1e-12 else None

def quality_mass(input,metadata,qualities):
    mass=[0.]*5
    for item in input['OfferedCards']:
        cid=item['Card']['CanonicalId'];m=metadata.get(cid)
        if not m or m['land'] or not m['colors']:continue
        weight=gate(qualities.get((input['SetCode'],cid),.5))*item['Count']*(.5 if len(m['colors'])>1 else 1)/len(m['colors'])
        for c in m['colors']:mass[c]+=weight
    return mass

def card_signal(p,feature,family,lam,fallback,cdf_lam=0):
    if not 1<=p<=15:raise ValueError('Position must be 1-based and pack-local')
    if not feature or not feature['n']:return 0,0
    c=confidence(feature['drafts'],lam)
    if family=='A':signal=max(0,min(1,(p-feature['mean'])/3))
    elif family=='B':signal=max(-1,min(1,(p-feature['mean'])/max(1,feature['sd'])))
    elif family=='C':
        mid=feature['cdf'][p]-.5*feature['hist'][p]/feature['n']
        base=fallback['cdf'][p]-.5*fallback['hist'][p]/fallback['n']
        pooling=confidence(feature['drafts'],cdf_lam)
        signal=2*(pooling*mid+(1-pooling)*base)-1
    else:raise ValueError('Unknown lane family')
    return c*signal,c

def observation(input,positions,metadata,qualities,family,lam,pack_specific,cdf_lam=0):
    p=input['PickNumber'];pack=str(input['PackNumber']);s=input['SetCode'];f=input['Format'];positive=[0.]*5;negative=[0.]*5
    if s+'/'+f not in positions:return positive,negative,0.
    ramp=max(0,min(1,(p-2)/6));support=0.;den=0.
    fallback=positions[s+'/'+f]['set'][pack if pack_specific else 'all']
    for item in input['OfferedCards']:
        cid=item['Card']['CanonicalId'];meta=metadata.get(cid);row=positions[s+'/'+f]['cards'].get(cid)
        if not row or not meta or meta['land'] or not meta['colors']:continue
        feat=row['seen'].get(pack if pack_specific else 'all');elig=gate(qualities.get((s,cid),.5))
        signal,c=card_signal(p,feat,family,lam,fallback,cdf_lam)
        w=item['Count']*elig*(.5 if len(meta['colors'])>1 else 1)/len(meta['colors'])
        for color in meta['colors']:
            positive[color]+=max(signal,0)*w*ramp;negative[color]+=min(signal,0)*w*ramp
        support+=c*elig*item['Count'];den+=elig*item['Count']
    # One observation's mass is bounded; repeated cards/physical pack IDs are not independent samples.
    maximum=max(1,sum(positive)+sum(-x for x in negative))
    return ([v/maximum for v in positive],[v/maximum for v in negative],ramp*support/den if den else 0)

class Accumulator:
    def __init__(self,half_life,retention,negative):
        if half_life<=0 or not 0<=retention<=1 or not 0<=negative<=1:raise ValueError('Invalid temporal policy')
        self.half_life=half_life;self.retention=retention;self.negative=negative;self.last=None;self.banks={}
    def observe(self,input,signal):
        coordinate=(input['PackNumber'],input['PickNumber']);index=input['GlobalPickIndex']
        if self.last is not None and (index<=self.last[0] or coordinate<=self.last[1]):raise ValueError('Duplicate/out-of-order/future observation')
        decay=2**(-(index-self.last[0])/self.half_life) if self.last else 1
        for bank in self.banks.values():
            bank[0]=[x*decay for x in bank[0]];bank[1]*=decay
        pack=input['PackNumber'];bank=self.banks.setdefault(pack,[[0.]*5,0.]);pos,neg,c=signal
        bank[0]=[x+a+self.negative*b for x,a,b in zip(bank[0],pos,neg)];bank[1]+=c
        evidence=[0.]*5;support=0
        for old,values in self.banks.items():
            weight=1 if old==pack else .5 if (pack-old)%2==0 else self.retention
            for i in range(5):evidence[i]+=weight*values[0][i]
            support+=weight*values[1]
        conf=support/(support+4);mean=statistics.mean(evidence)
        result=[math.tanh((x-mean)/1.5)*conf for x in evidence]
        self.last=(index,coordinate)
        return dict(colors=result,confidence=conf)
