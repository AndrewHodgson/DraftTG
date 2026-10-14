"""Transparent behavioral probes, including failure cases; not promotion evidence."""
from phase_c_core import *

def run():
    guard();params=load(OUT/'temporal-calibration.json')['selected'];cases={}
    for family,p in params.items():
        cfg=p['config'];state=Accumulator(cfg['halfLife'],cfg['retention'],cfg['negative'])
        short=features(histogram({3:1000}),1000);late=features(histogram({12:1000}),1000)
        pos={'TEST/PremierDraft':dict(set={'all':short},cards={'blue':dict(seen={'all':short}),'early-blue':dict(seen={'all':late})})}
        meta={c:dict(colors=[1],land=False) for c in ('blue','early-blue')};quality={('TEST',c):.9 for c in meta};values=[]
        for pick in range(1,15):
            i=dict(SetCode='TEST',Format='PremierDraft',DraftId='probe',PackNumber=1,PickNumber=pick,GlobalPickIndex=pick-1,
                OfferedCards=[dict(Card=dict(CanonicalId='blue' if pick<=8 else 'early-blue'),Count=1)])
            profile=state.observe(i,observation(i,pos,meta,quality,family,cfg['lambdaSignal'],False));values.append(dict(pick=pick,**profile))
        cases[family]=dict(contestedBlue=values,blueAt8=values[7]['colors'][1],blueAt14=values[-1]['colors'][1],
            knownLimitation='Zero fitted explicit negative weight and coverage-style confidence can retain/increase support despite earlier-than-expected qualified sightings. This controlled counterexample blocks interpreting confidence as calibrated truth.')
    dump(OUT/'behavior-probes.json',dict(synthetic=True,usedForCalibration=False,candidates=cases,qualifiedByTheseProbes=False))
    print({f:(v['blueAt8'],v['blueAt14']) for f,v in cases.items()})

if __name__=='__main__':run()
