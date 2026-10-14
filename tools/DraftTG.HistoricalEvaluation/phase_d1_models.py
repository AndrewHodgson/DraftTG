"""Deconfounded shell, logistic/residual-NMF factors and uncertainty-gated pairs."""
from phase_d1_baseline import *

def card_residual(count,n,g):
    adjusted=(count+50*g)/(n+50);delta=np.clip(logit(adjusted)-logit(g),-2,2)
    se=np.sqrt(1/np.maximum(count+50*g,1)+1/np.maximum(n-count+50*(1-g),1))
    uncertainty=count/(count+50)*abs(delta)/(abs(delta)+1.64*se+1e-12)
    return delta,uncertainty,se
def align_loadings(a,b):
    if len(a)==0:return b,0.
    aa=a/np.maximum(np.linalg.norm(a,axis=1,keepdims=True),1e-12);bb=b/np.maximum(np.linalg.norm(b,axis=1,keepdims=True),1e-12);similarity=aa@bb.T
    perm=max(itertools.permutations(range(len(a))),key=lambda p:sum(abs(similarity[i,j]) for i,j in enumerate(p)))
    aligned=np.array([b[j]*(1 if similarity[i,j]>=0 else -1) for i,j in enumerate(perm)])
    return aligned,float(np.mean([abs(similarity[i,j]) for i,j in enumerate(perm)]))
def latent_fit(b,bias,k,method):
    p=sigmoid(bias);r=(b-p)/np.sqrt(np.maximum(p*(1-p),.02));r-=r.mean(axis=0)
    if method=='logistic':
        u,s,vt=np.linalg.svd(r,full_matrices=False);z=u[:,:k]*np.sqrt(len(b))*.5;v=vt[:k]*.5
        for _ in range(100):
            err=b-sigmoid(bias+z@v);v+=.05*(z.T@err/max(1,len(b))-.2*v);z+=.05*(err@v.T/max(1,b.shape[1])-.2*z);z-=z.mean(axis=0)
        for a in range(k):
            scale=max(float(np.std(z[:,a])),1e-6);z[:,a]/=scale;v[a]*=scale
    else:
        # Positive and negative residual channels; biases are never factors.
        split=np.concatenate([np.maximum(r,0),np.maximum(-r,0)],axis=1);z,h=nmf(split,k,12345+k)
        v=h[:,:b.shape[1]]-h[:,b.shape[1]:];z-=z.mean(axis=0);v*=.25
        for a in range(k):
            scale=max(float(np.std(z[:,a])),1e-6);z[:,a]/=scale;v[a]*=scale
    return np.clip(z,-4,4),np.clip(v,-2,2)
def latent_pool(ids,w,bias,v):
    if not len(ids) or not len(v):return np.zeros(len(v))
    z=np.zeros(len(v));vi=v[:,ids]
    for _ in range(20):
        gradient=vi@(w*(1-sigmoid(bias[ids]+z@vi)))-4*z
        z+=.05*gradient/max(1,float(w.sum()));z=np.clip(z,-3,3)
    return z
def pair_uncertainty(observed,expected,left,right,halfEffects,variance):
    if expected<5 or min(left,right)<20 or (observed<5 and (expected<20 or min(left,right)<30)):return 0.,0.
    raw=float(np.clip(math.log((observed+5)/(expected+5)),-1,1));se=math.sqrt(max(variance,1))/(expected+5)
    consistent=all(e*raw>0 for e in halfEffects)
    reliability=float(consistent)*expected/(expected+50)*max(0,1-1.64*se/max(abs(raw),1e-12))
    return raw*reliability,reliability

def fit_model(s,rs,data,coeff,m,q,method='logistic',rank=2):
    ensure_fit(s,rs);n=len(rs);vocab=data['vocabulary'];gen=Generic(data,coeff,m,q,s);_,b,groups=population(s,rs,m,vocab)
    model=dict(version=VERSION,set=s,format='PremierDraft',n=n,generic=data,coefficients=coeff,method=method,rank=rank,shells={},evidenceKeysSha256=digest('|'.join(sorted(r['key'] for r in rs))))
    for label,idx in sorted(groups.items()):
        count=b[idx].sum(axis=0);g=gen.generic[label];delta,unc,se=card_residual(count,len(idx),g)
        halves=[[j for j in idx if int(digest(SEED+'/half/'+rs[j]['key'])[:8],16)%2==part] for part in (0,1)]
        halfD=[card_residual(b[h].sum(axis=0),len(h),g)[0] if h else np.zeros(len(vocab)) for h in halves]
        sign=(halfD[0]*halfD[1]>0)&(halfD[0]*delta>0);unc*=sign
        bias=logit(g)+delta*unc;common=(count>=20)&np.array([not m[i]['land'] for i in vocab]);v=np.zeros((rank,len(vocab)));z=np.zeros((len(idx),rank));stable=0.
        if len(idx)>=rank*25 and common.sum()>rank and all(len(h)>=rank*5 for h in halves):
            z,vc=latent_fit(b[idx][:,common],bias[common],rank,method);v[:,common]=vc
            halfV=[latent_fit(b[h][:,common],bias[common],rank,method)[1] for h in halves];_,stable=align_loadings(halfV[0],halfV[1])
        factorConfidence=(stable if stable>=.8 else 0)*len(idx)/(len(idx)+100)
        v*=count/(count+50);prob=sigmoid(bias+z@v);expected=prob.T@prob;genericExpected=np.outer(g,g)*len(idx);observed=b[idx].T@b[idx]
        pairs=[];valid=np.where((count>=20)&np.array([not m[i]['land'] for i in vocab]))[0]
        localHalf=[[k for k,j in enumerate(idx) if j in set(h)] for h in halves]
        halfObs=[b[h].T@b[h] for h in halves];halfExp=[prob[h].T@prob[h] for h in localHalf]
        for a,j in enumerate(valid):
            for c in valid[a+1:]:
                exp=float(expected[j,c]);obs=float(observed[j,c]);he=[math.log((o[j,c]+5)/(e[j,c]+5)) for o,e in zip(halfObs,halfExp)];pj=prob[:,j]*prob[:,c]
                effect,reliability=pair_uncertainty(obs,exp,count[j],count[c],he,float(np.sum(pj*(1-pj))))
                if effect:pairs.append([int(j),int(c),effect,reliability,obs,exp,float(genericExpected[j,c])])
        model['shells'][label]=dict(n=len(idx),generic=g.tolist(),delta=delta.tolist(),cardConfidence=unc.tolist(),se=se.tolist(),count=count.tolist(),bias=bias.tolist(),loadings=v.tolist(),stability=stable,factorConfidence=factorConfidence,pairs=pairs,utility=0.)
    return model

class Residual:
    def __init__(self,model,m,q,control='uniform-frequency',mapping=None,calibration=None):
        self.model=model;self.m=m;self.s=model['set'];self.generic=Generic(model['generic'],model['coefficients'],m,q,self.s);self.index=self.generic.index;self.control=control;self.mapping=mapping;self.calibration=calibration;self.shells={}
        for label,d in model['shells'].items():
            a={k:np.array(d[k]) for k in ('generic','delta','cardConfidence','se','count','bias','loadings')};a.update({k:d[k] for k in ('n','stability','factorConfidence','utility')});a['pair']=np.zeros((len(self.index),len(self.index)))
            for j,c,e,*_ in d['pairs']:a['pair'][j,c]=a['pair'][c,j]=e
            self.shells[label]=a
    def pool(self,cards,weight='uniform'):
        # Frozen belief uses current pool only; inference sees neither candidate nor labels.
        mode='sqrt-capped3' if weight=='sqrt-capped3' else 'modest-Q' if weight=='modest-Q' else 'uniform';state=self.generic.pool(cards,mode)
        if state is None:return None
        state['latent']={l:latent_pool(state['ids'],state['w'],d['bias'],d['loadings']) for l,d in self.shells.items()}
        return state
    def components(self,state,candidates):
        out=[]
        if state is None:return [dict(shell=0.,factor=0.,pair=0.,confidence=0.,utility=0.,applicable=False) for _ in candidates]
        for i in candidates:
            if i not in self.index or self.m[i]['land']:out.append(dict(shell=0.,factor=0.,pair=0.,confidence=0.,utility=0.,applicable=False));continue
            j=self.index[i];a=f=p=c=u=0.
            for label,sw in zip(state['labels'],state['shellWeights']):
                d=self.shells[label];a+=sw*d['delta'][j]*d['cardConfidence'][j];f+=sw*float(state['latent'][label]@d['loadings'][:,j])*d['factorConfidence']
                p+=sw*float(d['pair'][j,state['ids']]@state['w'])/max(1,state['n']);c+=sw*d['count'][j]/(d['count'][j]+50)*d['n']/(d['n']+100);u+=sw*d['utility']
            c*=state['n']/(state['n']+8)
            out.append(dict(shell=float(a),factor=float(f),pair=float(p),confidence=float(c),utility=float(u),applicable=True))
        return out
    def predict(self,state,candidates,family='C',alpha=None):
        known=[i for i in candidates if i in self.index];baseKnown=self.generic.probability(state,known,self.control);base={i:p for i,p in zip(known,baseKnown)};components=self.components(state,candidates);values=[]
        cold=1. if self.mapping is None else cold_weight(self.model['n'],self.mapping)
        for i,c in zip(candidates,components):
            raw=c['shell']+(c['factor'] if family in ('B','C') else 0)+(.25*c['pair'] if family=='C' else 0)
            confidence=c['confidence']*((c['utility']*cold) if family=='C' else 1.)
            if alpha is not None:confidence=c['confidence']*c['utility']*alpha
            values.append(dict(probability=float(sigmoid(logit(base.get(i,.5))+confidence*raw)),fit=float(.5+.25*np.tanh(raw)),confidence=float(confidence),utility=c['utility'],
                raw=float(raw),applicable=bool(c['applicable'] and state is not None),components=c))
        return values
