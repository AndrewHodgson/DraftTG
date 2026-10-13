"""Provider-neutral, offline Phase B features and immutable research boundaries.

This module has no application/scoring dependency. Q is explicitly experimental.
"""
import collections, hashlib, json, math, pathlib, statistics, unicodedata

SEED = 'DraftTG-phase-b-v1'
VERSION = 'research-q-phase-b-v1'
LEVELS = (50, 100, 250, 500, 1000, 'full')
GRID = (0, 50, 100, 250, 500, 1000, 2000)

def digest(value): return hashlib.sha256(value.encode()).hexdigest()
def group_hash(set_code, format_name, draft): return digest(f'{SEED}/17LandsPublic/{set_code}/{format_name}/{draft}')
def evidence_group(set_code, format_name, draft): return int(group_hash(set_code, format_name, draft)[:8], 16) % 2 == 0
def normalized_name(name):
    import re
    return re.sub(r'\s*//\s*', ' // ', unicodedata.normalize('NFKC', name).replace('\u2019', "'").strip()).upper()

def verify_registration(root=pathlib.Path('.')):
    record = json.loads((root/'docs/evidence/v2-phase-b/preregistration-hashes.json').read_text(encoding='utf-8-sig'))
    entries=[dict(Path='docs/historical/splits/phase-b-v1.json',Sha256=record['SplitSha256']),
             dict(Path='docs/evidence/v2-phase-b/preregistration.json',Sha256=record['PlanSha256']),
             dict(Path='docs/evidence/v2-phase-a/baseline-freeze.json',Sha256=record['BaselineFreezeSha256'])]
    entries += json.loads((root/'docs/evidence/v2-phase-a/baseline-freeze.json').read_text(encoding='utf-8-sig'))['Files']
    for item in entries:
        path = root/item['Path']
        with path.open('rb') as f: actual = hashlib.file_digest(f, 'sha256').hexdigest()
        if actual.lower() != item['Sha256'].lower(): raise ValueError('Frozen registration changed: '+str(path))
    splits = json.loads((root/'docs/historical/splits/phase-b-v1.json').read_text(encoding='utf-8-sig'))
    if len([s for s,p in splits['Sets'].items() if p == 'Holdout']) < 2: raise ValueError('Two untouched whole-set holdouts required')
    return splits['Sets']

def allowed_sets(splits, final=False):
    return {s for s,p in splits.items() if (p == 'Holdout') == final}

def valid_rate(p): return p is not None and math.isfinite(p) and 0 <= p <= 1
def shrink(p, n, baseline, k):
    if not valid_rate(baseline) or k < 0: raise ValueError('Invalid prior')
    return baseline if not valid_rate(p) or n <= 0 else (n*p+k*baseline)/(n+k)

def quantile(values, p):
    a=sorted(values)
    if not a: return None
    at=(len(a)-1)*p; lo=int(at); hi=math.ceil(at)
    return a[lo]*(hi-at)+a[hi]*(at-lo) if hi != lo else a[lo]

def baseline(rows):
    rows=[r for r in rows if r['n'] > 0 and valid_rate(r['rate'])]
    if not rows: return None
    rates=[r['rate'] for r in rows]; total=sum(r['n'] for r in rows)
    b=sum(r['rate']*r['n'] for r in rows)/total
    spread=statistics.pstdev(rates)
    # Simple moment EB; subtract mean binomial sampling variance, never negative.
    tau=max(0, statistics.pvariance(rates)-statistics.mean(r['rate']*(1-r['rate'])/r['n'] for r in rows))
    k=min(2000, max(0, b*(1-b)/tau-1)) if tau > 0 else 2000
    return dict(weighted=b, unweighted=statistics.mean(rates), spread=spread, cards=len(rows),
                observations=total, quantiles={str(p):quantile(rates,p) for p in (.05,.25,.5,.75,.95)}, ebK=k)

def position_features(histogram):
    # Source pack/pick are zero-based. Histograms here are explicitly 1-based pack-local picks.
    n=sum(histogram.values())
    if not n: return dict(count=0, mean=None, median=None, distribution={})
    values=sorted((int(k),v) for k,v in histogram.items()); acc=0; middle=[]
    for k,v in values:
        old=acc; acc+=v
        for target in ((n-1)//2, n//2):
            if old <= target < acc: middle.append(k)
    return dict(count=n, mean=sum(k*v for k,v in values)/n, median=sum(middle)/2,
                distribution={str(k):v for k,v in values})

def percentile(value, values):
    return None if value is None or not values else (sum(x<value for x in values)+.5*sum(x==value for x in values))/len(values)

def sample_observations(observations, n, key):
    # One observation per card/game presence. Repeated copies cannot amplify this sample.
    ordered=sorted(observations, key=lambda x: digest(f'{SEED}/sample/{key}/{x[0]}'))
    return ordered if n == 'full' else ordered[:n]

def soft_limit(value, knee=.35):
    distance=abs(value-.5); headroom=.5-knee
    return value if distance <= knee else .5+math.copysign(knee+headroom*(1-math.exp(-(distance-knee)/headroom)), value-.5)

def quality(p, b, spread, family='delta'):
    if not valid_rate(p) or not valid_rate(b): return .5
    scale=.16 if family != 'standardized' else max(.005, spread)*4
    return soft_limit(.5+(p-b)/scale)

def predict(observations, n, baseline_value, k, family='delta'):
    if not observations: return baseline_value
    sample=observations if n == 'full' else observations[:n]
    count=len(sample); p=statistics.mean(x[1] for x in sample)
    if family == 'adaptive': k=k*math.sqrt(500/max(1,count))
    if family == 'decomposition':
        # Disjoint OH-presence and drawn-without-OH strata; each game is in exactly one.
        oh=[x[1] for x in sample if x[2]]; draw=[x[1] for x in sample if not x[2]]
        def part(a): return shrink(statistics.mean(a) if a else None,len(a),baseline_value,k/2)
        return (len(oh)*part(oh)+len(draw)*part(draw))/count
    return shrink(p,count,baseline_value,k)

def pair_formats(rows, set_code, primary, alternate):
    if primary == alternate: raise ValueError('Different formats required')
    left={r['card']:r for r in rows if r['set']==set_code and r['format']==primary}
    right={r['card']:r for r in rows if r['set']==set_code and r['format']==alternate}
    return [(left[c],right[c]) for c in sorted(left.keys() & right.keys())]

def correlation(a,b):
    if len(a)<2: return None
    ma=statistics.mean(a); mb=statistics.mean(b)
    den=math.sqrt(sum((x-ma)**2 for x in a)*sum((y-mb)**2 for y in b))
    return sum((x-ma)*(y-mb) for x,y in zip(a,b))/den if den else None

def rank_correlation(a,b):
    def ranks(values):
        sorted_v=sorted(values)
        return [sum(i+1 for i,x in enumerate(sorted_v) if x==v)/sum(x==v for x in sorted_v) for v in values]
    return correlation(ranks(a),ranks(b))

def final_lock(path, selection, fingerprint):
    if selection['researchFingerprint'] != fingerprint: raise ValueError('Research changed after candidate selection')
    path=pathlib.Path(path); path.parent.mkdir(parents=True,exist_ok=True)
    # Exclusive create before touching holdout outcomes; failed releases remain consumed.
    with path.open('x',encoding='utf-8') as f:
        json.dump(dict(state='consumed',selectionHash=digest(json.dumps(selection,sort_keys=True)),releaseProcessId=__import__('os').getpid()),f,indent=2)

def peak_memory_bytes():
    if __import__('os').name != 'nt': return None
    import ctypes
    from ctypes import wintypes
    class Counters(ctypes.Structure):
        _fields_=[('cb',wintypes.DWORD),('PageFaultCount',wintypes.DWORD)]+[(name,ctypes.c_size_t) for name in
            ('PeakWorkingSetSize','WorkingSetSize','QuotaPeakPagedPoolUsage','QuotaPagedPoolUsage','QuotaPeakNonPagedPoolUsage','QuotaNonPagedPoolUsage','PagefileUsage','PeakPagefileUsage')]
    counters=Counters();counters.cb=ctypes.sizeof(counters)
    ctypes.windll.kernel32.GetCurrentProcess.restype=wintypes.HANDLE
    if not ctypes.windll.psapi.GetProcessMemoryInfo(ctypes.windll.kernel32.GetCurrentProcess(),ctypes.byref(counters),counters.cb):return None
    return counters.PeakWorkingSetSize
