"""Small inspectable generic inclusion controls, frozen before residual modeling."""
from phase_d1_core import *
CONTROLS=('color-only','uniform-frequency','quality-frequency','frequency-Q','frequency-Q-mana','generic-logistic')
FEATURES=('intercept','globalLogit','shellLogit','Qcentered','manaCentered','manaUnknown','colorCompatible','colorless','multicolor','creature','instantSorcery','artifactEnchantment','land','typeUnknown','assignedRemoval','removalUnknown','assignedFixing','fixingUnknown')

def population(s,rs,m,vocab=None):
    if any(set(r)!= {'cards','key'} for r in rs):raise ValueError('Only structure/evidence keys accepted')
    vocab=vocab or sorted({i for r in rs for i in r['cards'] if not m[i]['basic']});b=np.array([[int(i in r['cards']) for i in vocab] for r in rs],dtype=float);n=len(rs)
    gp=(b.sum(axis=0)+1)/(n+2);groups=collections.defaultdict(list)
    for j,r in enumerate(rs):groups[shell(r['cards'],m)].append(j)
    data=dict(set=s,n=n,vocabulary=vocab,gp=gp.tolist(),counts=b.sum(axis=0).tolist(),shells={})
    for label,idx in sorted(groups.items()):
        count=b[idx].sum(axis=0);p=(count+50*gp)/(len(idx)+50)
        data['shells'][label]=dict(n=len(idx),counts=count.tolist(),p=p.tolist())
    return data,b,groups
def feature_matrix(s,data,label,m,q):
    d=data['shells'][label];values=[]
    for j,i in enumerate(data['vocabulary']):
        md=m[i];roles=md['roleProfile'];mv=md.get('manaValue');colors=md['colors'];fix='Fixing' in roles['ReliableRoles'];rem=roles['RemovalWeight'];known=md.get('typeKnown',md['known'])
        values.append([1,float(logit(data['gp'][j]))/5,float(logit(d['p'][j]))/5,q.get((s,i),.5)-.5,(mv-3)/3 if mv is not None else 0,mv is None,
            all('WUBRG'[c] in label for c in colors),not colors,len(colors)>1,md['creature'],md.get('instantSorcery',False),md.get('artifactEnchantment',False),md['land'],not known,rem,rem==0,fix,not fix])
    return np.array(values,dtype=float)
def logistic_fit(x,y,opportunities,ridge):
    beta=np.zeros(x.shape[1]);beta[0]=-3
    penalty=np.ones(len(beta))*ridge;penalty[0]=ridge*.01
    def objective(b):return float(np.sum(opportunities*logloss(sigmoid(x@b),y))+.5*np.sum(penalty*b*b))
    for _ in range(120):
        p=sigmoid(x@beta);w=opportunities*p*(1-p);gradient=x.T@(opportunities*(p-y))+penalty*beta
        h=(x.T*w)@x+np.diag(penalty)+np.eye(len(beta))*1e-8;step=np.linalg.solve(h,gradient);a=1.;before=objective(beta)
        while a>1e-6 and objective(beta-a*step)>before:a*=.5
        beta-=a*step
        if np.max(abs(a*step))<1e-7:break
    return beta
class Generic:
    def __init__(self,data,coeff,m,q,s):
        self.data=data;self.m=m;self.q=q;self.s=s;self.index={i:j for j,i in enumerate(data['vocabulary'])};self.labels=list(data['shells']);self.gp=np.array(data['gp']);self.p={l:np.array(data['shells'][l]['p']) for l in self.labels}
        self.generic={l:np.clip(sigmoid(feature_matrix(s,data,l,m,q)@np.array(coeff)),1e-5,1-1e-5) for l in self.labels}
    def pool(self,cards,weight='uniform'):
        ids=[self.index[i] for i in cards if i in self.index and not self.m[i]['land']];keys=[i for i in cards if i in self.index and not self.m[i]['land']]
        w=np.array([1 if weight=='uniform' else math.sqrt(min(3,cards[i])) for i in keys],dtype=float)
        if weight in ('quality','modest-Q'):w*=np.array([.5+self.q.get((self.s,i),.5) for i in keys])
        if not len(ids) or not self.labels:return None
        logs=[math.log(self.data['shells'][l]['n']/self.data['n'])+float(w@np.log(self.p[l][ids]/self.gp[ids]))/4 for l in self.labels]
        return dict(ids=ids,w=w,n=float(w.sum()),shellWeights=softmax(logs),labels=self.labels)
    def probability(self,state,ids,control):
        # Candidate contributes nothing to the prior state.
        j=[self.index[i] for i in ids];sw=state['shellWeights'] if state else np.array([self.data['shells'][l]['n']/max(1,self.data['n']) for l in self.labels]);sw=sw/max(1e-12,sw.sum())
        if control=='color-only':return np.ones(len(j))*.5
        matrices=self.generic if control=='generic-logistic' else self.p
        base=sum(w*matrices[l][j] for l,w in zip(self.labels,sw)) if self.labels else self.gp[j]
        if control in ('frequency-Q','frequency-Q-mana'):
            q=np.array([self.q.get((self.s,i),.5) for i in ids]);extra=2*(q-.5)
            if control=='frequency-Q-mana':extra-=.1*np.array([abs((self.m[i].get('manaValue') or 3)-3) for i in ids])
            return sigmoid(logit(base)+extra)
        return np.clip(base,1e-5,1-1e-5)

def task_set(s,rs,gen,limit=500):
    m=gen.m;q=gen.q;count=np.array(gen.data['counts']);result=[]
    for r in sorted(rs,key=lambda r:digest(SEED+'/task/'+s+'/'+r['draft'])):
        for true in sorted((i for i in r['cards'] if i in gen.index and not m[i]['land']),key=lambda i:digest(SEED+'/'+r['draft']+'/'+i)):
            ti=gen.index[true]
            if count[ti]<20 or m[true]['manaValue'] is None or (s,true) not in q:continue
            pool={i:n for i,n in r['cards'].items() if i!=true};state=gen.pool(pool);allp=gen.probability(state,gen.data['vocabulary'],'generic-logistic');tp=allp[ti];distr=[]
            for other,j in gen.index.items():
                md=m[other]
                if other in r['cards'] or md['land'] or md['colors']!=m[true]['colors'] or md['creature']!=m[true]['creature']:continue
                if md['manaValue'] is None or abs(md['manaValue']-m[true]['manaValue'])>1 or (s,other) not in q or abs(q[(s,other)]-q[(s,true)])>.05:continue
                if not .5<=count[j]/count[ti]<=2 or not .75<=allp[j]/tp<=4/3:continue
                distr.append(other)
            if len(distr)<4:continue
            ds=sorted(distr,key=lambda i:digest(SEED+'/distractor/'+r['draft']+'/'+true+'/'+i))[:4]
            result.append(dict(set=s,draft=r['draft'],pool=pool,candidates=[true]+ds,matching=dict(colors=m[true]['colors'],creature=m[true]['creature'],mana=m[true]['manaValue'],Q=q[(s,true)],genericProbability=tp,support=int(count[ti]))));break
        if len(result)>=limit:break
    return result
def baseline_values(gen,task):
    uni=gen.pool(task['pool']);quality=gen.pool(task['pool'],'quality')
    return {c:gen.probability(quality if c=='quality-frequency' else uni,task['candidates'],c) for c in CONTROLS}

def fit_baselines():
    if (OUT/'baseline-freeze.json').exists():raise ValueError('Generic controls already frozen')
    if (OUT/'generic-fit.json').exists():return recover_baseline()
    guard();m=meta();q=qualities();start=time.perf_counter();pops={};features=[];labels=[];op=[]
    for s in COMPATIBLE:
        rs=structures(rows(s));ensure_fit(s,rs);data,_,_=population(s,rs,m);dump(DATA/f'{s}.generic.json',data);pops[s]=data
        if s in CAL:
            for l,d in data['shells'].items():features.append(feature_matrix(s,data,l,m,q));labels.extend(np.array(d['counts'])/d['n']);op.extend([d['n']]*len(data['vocabulary']))
    x=np.vstack(features);y=np.array(labels);o=np.array(op);grid=[]
    for ridge in (1,10,100):
        beta=logistic_fit(x,y,o,ridge);errors={}
        for s in CAL:
            gen=Generic(pops[s],beta,m,q,s);rr=[]
            for r in rows(s,'audit'):
                label=shell(r['cards'],m);p=gen.generic.get(label,gen.gp);b=np.array([i in r['cards'] for i in gen.index],dtype=float);rr.append(float(np.mean(logloss(p,b))))
            errors[s]=float(np.mean(rr))
        grid.append(dict(ridge=ridge,coefficients=beta.tolist(),perSetLogLoss=errors,macroLogLoss=float(np.mean(list(errors.values())))))
    best=min(grid,key=lambda g:(g['macroLogLoss'],-g['ridge']));dump(OUT/'generic-fit.json',dict(version=VERSION,features=FEATURES,selected=best,grid=grid,seconds=time.perf_counter()-start,coefficientsFitCalibrationOnly=True,outcomesUnused=True))
    result=[]
    for s in COMPATIBLE:
        gen=Generic(pops[s],best['coefficients'],m,q,s)
        for part in ('utility','confidence','target'):
            ts=task_set(s,rows(s,part),gen);write_rows(DATA/f'{s}.{part}.tasks.jsonl.gz',ts)
            metrics={c:mean_metrics([rank_metrics(baseline_values(gen,t)[c]) for t in ts]) for c in CONTROLS}
            result.append(dict(set=s,partition=part,tasks=len(ts),metrics=metrics));print('baseline',s,part,len(ts),flush=True)
    freeze_baseline(result)

def freeze_baseline(result):
    macro={c:float(np.mean([r['metrics'][c]['MRR'] for r in result if r['partition']=='target' and r['set'] in DEV and r['metrics'][c]['MRR'] is not None])) for c in CONTROLS}
    strongest=max(CONTROLS,key=lambda c:macro[c]);inputs={str(OUT/'generic-fit.json'):sha(OUT/'generic-fit.json')}
    inputs.update({str(DATA/f'{s}.generic.json'):sha(DATA/f'{s}.generic.json') for s in COMPATIBLE})
    coverage={r['set']:r['tasks'] for r in result if r['partition']=='target' and r['set'] in DEV}
    dump(OUT/'baseline-freeze.json',dict(version=VERSION,strongest=strongest,developmentMRR=macro,results=result,inputs=inputs,coverage=coverage,
        fullPrimaryCoverage=all(n>=300 for n in coverage.values()),aggregateScope='Available development sets only; MKM unavailable, complete primary gate FAIL',stage='Frozen before residual candidates; generic controls only',noCandidateAnalysis=True))
    print('Frozen strongest baseline',strongest,macro[strongest],flush=True)

def recover_baseline():
    guard();m=meta();q=qualities();coeff=load(OUT/'generic-fit.json')['selected']['coefficients'];result=[]
    for s in COMPATIBLE:
        gen=Generic(load(DATA/f'{s}.generic.json'),coeff,m,q,s)
        for part in ('utility','confidence','target'):
            path=DATA/f'{s}.{part}.tasks.jsonl.gz'
            if not path.exists():raise ValueError('Missing original baseline task cache: '+str(path))
            ts=list(readrows(path));scores=collections.defaultdict(list)
            for t in ts:
                for c,values in baseline_values(gen,t).items():scores[c].append(rank_metrics(values))
            result.append(dict(set=s,partition=part,tasks=len(ts),metrics={c:mean_metrics(scores[c]) for c in CONTROLS}))
    freeze_baseline(result);dump(OUT/'baseline-recovery.json',dict(reason='Null MRR aggregation on zero-task MKM',noRefit=True,noTaskRegeneration=True,
        genericFitSha256=sha(OUT/'generic-fit.json'),missingMetricsRemainUnavailable=True))
