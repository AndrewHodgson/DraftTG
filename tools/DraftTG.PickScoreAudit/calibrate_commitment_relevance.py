"""Product-calibration grid, not a fitted draft-outcome model. No Arena, HTTP or proprietary inputs."""
import argparse
import json
import math
from pathlib import Path

def smooth(x):
    t=max(0,min(1,x));return t*t*(3-2*t)

def old_r(p, picks):return 1-.8*smooth((picks-10)/25)*(1-p)

def tail(q, floor, premium=.2):return floor+(premium-floor)*smooth((q-.65)/.30)

def relevance(q,c,p,picks,floor=.05,premium=.2):
    return min(old_r(p,picks),1-c*(1-p)*(1-tail(q,floor,premium)))

def effective(q,r):return q if q<=.5 else .5+r*(q-.5)

def main():
    a=argparse.ArgumentParser();a.add_argument('output');args=a.parse_args()
    candidates=[]
    for floor in (.025,.05,.075,.10,.15,.20):
        for premium in (.10,.15,.20,.30,.40):
            ordinary=effective(.60,relevance(.60,.95,0,7,floor,premium))
            bomb=effective(.95,relevance(.95,1,0,7,floor,premium))
            candidates.append(dict(OrdinaryFloor=floor,PremiumFloor=premium,OrdinaryAtStrongCommitment=ordinary,
                PremiumAtFullCommitment=bomb,OrdinaryLosesToPlayableQ051=ordinary<.51,
                PremiumCanBeatPlayableQ055=bomb>.55,PreservesFrozenLatePremiumFloor=premium>=.2))
    admissible=[r for r in candidates if r['OrdinaryLosesToPlayableQ051'] and r['PremiumCanBeatPlayableQ055']
        and r['PremiumFloor']==.2]
    chosen=max(admissible,key=lambda r:r['OrdinaryFloor'])
    grid=[dict(Commitment=c,Playability=p,Quality=q,StagePicks=7,BaseR=old_r(p,7),
        NewR=relevance(q,c,p,7),EffectiveQ=effective(q,relevance(q,c,p,7)))
        for c in (0,.25,.5,.75,1) for p in (0,.25,.5,.75,1) for q in (.3,.5,.6,.8,.95)]
    audit={};step=.0001
    axes={'Commitment':lambda x:(.6,x,0,7),'Playability':lambda x:(.6,1,x,7),
        'Quality':lambda x:(x,1,0,7),'Stage':lambda x:(.6,.75,0,x*42)}
    for name,make in axes.items():
        values=[effective(q,relevance(q,c,p,n)) for q,c,p,n in (make(i*step) for i in range(10001))]
        sign=1 if name in ('Playability','Quality') else -1
        audit[name]=dict(Samples=len(values),Step=step,MaximumEffectiveQStep=max(abs(b-a) for a,b in zip(values,values[1:])),
            Monotone=all(sign*(b-a)>=-1e-12 for a,b in zip(values,values[1:])))
    path=Path(args.output);path.parent.mkdir(parents=True,exist_ok=True)
    result=dict(Scope='Structured behavioral calibration; not observational outcome calibration',
        Criterion='At C=.95,P=0,Q=.60 an ordinary off-plan advantage loses to playable Q=.51 with identical other components. At C=1 an extreme Q=.95 keeps a tail that can beat playable Q=.55.',
        Selection='Largest ordinary floor satisfying the general criterion, with the frozen .20 late relevance floor retained as the premium endpoint. Strong-quality .65 and near-extreme .95 are semantic knots, not rarity thresholds.',
        Chosen=chosen,Candidates=candidates,Grid=grid,Continuity=audit,
        Limit='Synthetic grid demonstrates behavior/smoothness, not draft win-rate improvements. Real P1P8 is a separate regression; no single-card target was fit.')
    path.write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(dict(Chosen=chosen,Continuity=audit),indent=2))

if __name__=='__main__':main()
