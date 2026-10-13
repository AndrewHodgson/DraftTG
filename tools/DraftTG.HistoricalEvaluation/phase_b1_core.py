"""Phase B.1 research only. No application/provider requests or holdout release."""
import dataclasses, hashlib, json, math, pathlib, statistics
from phase_b_core import soft_limit, valid_rate, correlation, rank_correlation, quantile

ROOT = pathlib.Path('.')
OUT = ROOT/'docs/evidence/v2-phase-b1'
DATA = ROOT/'data/historical/normalized/phase-b1-v1'
SEED = 'DraftTG-phase-b1-v1'
VERSION = 'research-q-phase-b1-v1'
SEMANTIC = 'opening-plus-draw-instances'
COMPATIBLE = ('DMU','BRO','WOE','LCI','MKM','OTJ','ONE','MOM')
GRID = (0,50,100,250,500,1000,2000)
LEVELS = (50,100,250,500,1000,'full')

def sha(path):
    with pathlib.Path(path).open('rb') as f: return hashlib.file_digest(f,'sha256').hexdigest()

def dump(path,value):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(value,indent=2,default=str,allow_nan=False),encoding='utf-8')

def load(path): return json.loads(pathlib.Path(path).read_text(encoding='utf-8-sig'))
def digest(value): return hashlib.sha256(value.encode()).hexdigest()
def group_hash(s,f,d): return digest(f'{SEED}/17LandsPublic/{s}/{f}/{d}')
def evidence_group(s,f,d): return int(group_hash(s,f,d)[:8],16)%2==0

def guard():
    preservation=load(OUT/'preservation.json')
    if sha('V2_PHASE_B1_SPLIT_MANIFEST.json') != preservation['SplitSha256']: raise ValueError('B.1 split changed')
    for item in preservation['Files']:
        if sha(item['Path']).lower()!=item['Sha256'].lower(): raise ValueError('Protected Phase B/production bytes changed: '+item['Path'])
    split=load('V2_PHASE_B1_SPLIT_MANIFEST.json')
    if set(split['UntouchedHoldout'])!={'MH3','DFT'} or set(split['SpentReference'])!={'BLB','DSK'}: raise ValueError('Holdout protection changed')
    return split

def allow(s):
    split=guard()
    if s in split['UntouchedHoldout'] or s in split['SpentReference']: raise ValueError('Protected holdout/reference: '+s)

def count_weights(row):
    # Draw counts can exceed deck copies through redraw; do not cap them.
    for name in ('opening','drawn','tutored','deck','sideboard'):
        if not isinstance(row[name],int) or row[name]<0: raise ValueError('Invalid raw count '+name)
    return dict(presence=int(row['deck']>0 and row['opening']+row['drawn']>0),
                copies=(row['opening']+row['drawn']) if row['deck']>0 else 0,
                gp=row['deck'],oh=row['opening'],drawn=row['drawn'],
                drawnWithoutOh=int(row['deck']>0 and row['opening']==0 and row['drawn']>0),
                neverSeen=int(row['deck']>0 and row['opening']==0 and row['drawn']==0))

def q(rate,baseline):
    return .5 if not valid_rate(rate) or not valid_rate(baseline) else soft_limit(.5+(rate-baseline)/.16)

@dataclasses.dataclass(frozen=True)
class QualityEvidence:
    set: str
    format: str
    card: str
    rate: float|None
    sample_count: int
    count_semantics: str
    source: str
    time_period: str
    is_primary: bool
    provenance: str

@dataclasses.dataclass(frozen=True)
class QualityPriorResult:
    effective_rate: float|None
    q: float
    primary_source: str|None
    alternate_source: str|None
    reliability: float
    shrinkage: float
    mode: str
    reason: str
    provenance: tuple

def prior(primary,alternate,primary_baseline,alternate_baseline,k=500,reliability=.9,
          allowed_direction=None,bound=None):
    if k<0 or not 0<=reliability<=1: raise ValueError('Invalid prior parameters')
    provenance=(primary.provenance,) if primary else ()
    def neutral(reason): return QualityPriorResult(primary_baseline,.5,primary.source if primary else None,None,0,0,'neutral',reason,provenance)
    if not primary or not primary.is_primary or not valid_rate(primary_baseline): return neutral('Missing primary identity/baseline')
    if primary.count_semantics != SEMANTIC or not primary.time_period or not primary.source or not primary.provenance:
        return neutral('Unqualified primary evidence units/provenance')
    if valid_rate(primary.rate) and primary.sample_count>0:
        rate=(primary.sample_count*primary.rate+k*primary_baseline)/(primary.sample_count+k)
        return QualityPriorResult(rate,q(rate,primary_baseline),primary.source,None,1,primary.sample_count/(primary.sample_count+k),'primary','Usable primary wins',provenance)
    if not alternate: return neutral('No alternate')
    if alternate.is_primary or primary.set!=alternate.set or primary.card!=alternate.card: return neutral('Set/identity conflict')
    if primary.format==alternate.format or (alternate.format,primary.format)!=allowed_direction: return neutral('Unqualified format direction')
    if primary.count_semantics!=alternate.count_semantics or primary.time_period!=alternate.time_period: return neutral('Count/time-period mismatch')
    if not valid_rate(alternate.rate) or alternate.sample_count<=0 or not valid_rate(alternate_baseline) or not alternate.provenance or not alternate.source: return neutral('Unusable alternate provenance/rate')
    strength=alternate.sample_count/(alternate.sample_count+500)
    delta=reliability*strength*(alternate.rate-alternate_baseline)
    if bound is not None:
        if bound<0: raise ValueError('Invalid bound')
        delta=max(-bound,min(bound,delta))
    rate=max(0,min(1,primary_baseline+delta))
    return QualityPriorResult(rate,q(rate,primary_baseline),primary.source,alternate.source,reliability,strength,'alternate-prior','Explicit same-set alternate prior',provenance+(alternate.provenance,))

def sample_clusters(observations,level,key):
    ordered=sorted(observations,key=lambda x:digest(f'{SEED}/sample/{key}/{x[0]}'))
    if level=='full': return ordered
    sample=[]; units=0
    for x in ordered:
        sample.append(x);units+=x[2]
        if units>=level: break
    return sample

def rate_units(observations):
    n=sum(x[2] for x in observations)
    return (sum(x[1]*x[2] for x in observations)/n if n else None),n

def metrics(predicted,actual):
    if not actual: return dict(n=0,mae=None)
    errors=[p-t for p,t in zip(predicted,actual)]
    return dict(n=len(actual),mae=statistics.mean(abs(e) for e in errors),rmse=math.sqrt(statistics.mean(e*e for e in errors)),
                bias=statistics.mean(errors),over2pp=statistics.mean(e>=.02 for e in errors),over5pp=statistics.mean(e>=.05 for e in errors),
                correlation=correlation(predicted,actual),rankCorrelation=rank_correlation(predicted,actual))
