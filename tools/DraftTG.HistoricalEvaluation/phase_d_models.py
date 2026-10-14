"""Interpretable shell profiles, soft nonnegative factors and shrunk pair residuals."""
from phase_d_core import *

def fit(rows,m,representation='binary',context='pair',latent=True,diagnose=True):
    # Input contract deliberately excludes games/wins/labels and target identifiers.
    if any(set(r)!= {'cards','key'} for r in rows):raise ValueError('Fit accepts structure and evidence key only')
    vocab=sorted({i for r in rows for i in r['cards'] if not m[i]['basic']});n=len(rows)
    allbin=np.array([[int(r['cards'].get(i,0)>0) for i in vocab] for r in rows],dtype=float)
    globalp=(allbin.sum(axis=0)+1)/(n+2);groups=collections.defaultdict(list)
    for j,r in enumerate(rows):groups[shell(r['cards'],m,context)].append(j)
    model=dict(version=VERSION,vocabulary=vocab,n=n,representation=representation,context=context,globalp=globalp.tolist(),shells={});diags=[]
    for label,index in sorted(groups.items()):
        if len(index)<30:continue
        b=allbin[index];count=b.sum(axis=0);p=(count+50*globalp)/(len(index)+50)
        x=np.array([vector(rows[j]['cards'],vocab,m,representation) for j in index]);common=count>=20
        factors=np.ones((len(index),1));h=np.mean(x,axis=0,keepdims=True);chosen=1;details=[]
        if latent and common.sum()>3 and len(index)>=80:
            train=[j for j in range(len(index)) if int(digest(SEED+rows[index[j]]['key'])[:8],16)%5!=0];test=[j for j in range(len(index)) if j not in set(train)]
            seed=int(digest(SEED+label)[:8],16);base=None
            for k in (1,2,4):
                if len(train)<k*25 or not test:continue
                w0,h0=nmf(x[train][:,common],k,seed+k);wt=memberships(x[test][:,common],h0)
                # membership is normalized for reconstruction, with fitted scale restored by optimal row scalar.
                rec=wt@h0;scale=(x[test][:,common]*rec).sum(axis=1)/np.maximum((rec*rec).sum(axis=1),1e-10)
                error=float(np.mean(abs(x[test][:,common]-rec*scale[:,None])))
                if base is None:base=error
                sample=list(range(0,len(train),2));_,h1=nmf(x[[train[j] for j in sample]][:,common],k,seed+k)
                stable=stability(h0,h1);soft=w0/np.maximum(w0.sum(axis=1,keepdims=True),1e-10);support=soft.sum(axis=0)
                details.append(dict(k=k,auditMAE=error,stability=stable,minimumEffectiveSupport=float(support.min())))
                if chosen==1 and k>1 and error<=.99*base and stable>=.8 and support.min()>=25:chosen=k
            factors,hc=nmf(x[:,common],chosen,seed+chosen);factors/=np.maximum(factors.sum(axis=1,keepdims=True),1e-10)
            h=np.zeros((chosen,len(vocab)));h[:,common]=hc
        effective=factors.sum(axis=0);pk=(factors.T@b+50*p)/(effective[:,None]+50);pk=np.clip(pk,1e-5,1-1e-5)
        fittedprob=factors@pk;expected=fittedprob.T@fittedprob;observed=b.T@b;pairs=[]
        for a in range(len(vocab)) if latent else ():
            for c in range(a+1,len(vocab)):
                effect,conf=pair_effect(float(observed[a,c]),float(expected[a,c]),float(count[a]),float(count[c]))
                if effect:pairs.append([a,c,effect,conf,float(observed[a,c]),float(expected[a,c])])
        model['shells'][label]=dict(n=len(index),p=p.tolist(),count=count.tolist(),factors=pk.tolist(),prevalence=(effective/len(index)).tolist(),pairs=pairs)
        if diagnose:diags.append(dict(shell=label,n=len(index),factors=chosen,complexity=details,effectiveSupport=effective.tolist(),topCards=[[vocab[i] for i in np.argsort(-pk[j]/p)[:12]] for j in range(chosen)]))
    return model,diags

class Inference:
    def __init__(self,model,m,q,setcode):
        self.model=model;self.m=m;self.q=q;self.s=setcode;self.index={v:i for i,v in enumerate(model['vocabulary'])};self.globalp=np.array(model['globalp']);self.shells={}
        for label,d in model['shells'].items():
            self.shells[label]={**d,'p':np.array(d['p']),'factors':np.array(d['factors']),'prevalence':np.array(d['prevalence'])}
            pair=np.zeros((len(self.index),len(self.index)))
            for a,b,e,*_ in d['pairs']:pair[a,b]=pair[b,a]=e
            self.shells[label]['pairMatrix']=pair
    def pool(self,cards,weight='uniform'):
        usable={i:n for i,n in cards.items() if i in self.index and not self.m[i]['land'] and not self.m[i]['basic']}
        ids=[self.index[i] for i in usable]
        rep=self.model['representation'];w=np.array([1 if rep=='binary' else math.sqrt(min(n,3)) if rep=='sqrt' else n for n in usable.values()],dtype=float)
        if rep in ('counts','nonland') and len(w):w*=len(w)/w.sum()
        if weight=='quality':w*=np.array([max(.1,self.q.get((self.s,i),.5)) for i in usable])
        size=float(w.sum())
        if not ids or not self.shells:return None
        labels=list(self.shells);logs=[]
        for l in labels:
            d=self.shells[l];logs.append(math.log(d['n']/self.model['n'])+float(np.sum(w*np.log(d['p'][ids]/self.globalp[ids])))/4)
        shellweights=softmax(logs);belief={}
        for l in labels:
            d=self.shells[l];log=np.log(d['prevalence'])+(np.log(d['factors'][:,ids]/d['p'][ids])@w)/4
            belief[l]=softmax(log)
        return dict(ids=ids,weights=w,size=size,labels=labels,shellweights=shellweights,belief=belief)
    def score(self,state,candidates,family='B',cold=250):
        if state is None:return [(0.,0.,False) for _ in candidates]
        raw=np.zeros(len(candidates));confidence=0.
        for l,sw in zip(state['labels'],state['shellweights']):
            d=self.shells[l];confidence+=sw*d['n']/(d['n']+cold)
            for j,i in enumerate(candidates):
                if i not in self.index:continue
                c=self.index[i];value=math.log(d['p'][c]/self.globalp[c])
                if family in ('B','C'):value+=math.log(float(state['belief'][l]@d['factors'][:,c])/d['p'][c])
                if family=='C':value+=float(d['pairMatrix'][c,state['ids']]@state['weights'])/max(1,state['size'])
                raw[j]+=sw*value
        confidence*=state['size']/(state['size']+8)
        return [(float(.5+.25*np.tanh(x)),float(confidence),i in self.index and not self.m[i]['land']) for x,i in zip(raw,candidates)]
    def frequency(self,state,candidates):
        if state is None:return [float(self.globalp[self.index[i]]) if i in self.index else 0 for i in candidates]
        return [sum(sw*self.shells[l]['p'][self.index[i]] for l,sw in zip(state['labels'],state['shellweights'])) if i in self.index else 0 for i in candidates]
