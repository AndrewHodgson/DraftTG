"""Offline FRA snapshot calibration. No network/cache changes; deterministic card-held-out analysis."""
import argparse
import hashlib
import json
import math
import statistics as st
from pathlib import Path

def valid(r):
    g, n = r.get('ever_drawn_win_rate'), r.get('ever_drawn_game_count')
    return g is not None and math.isfinite(g) and 0 <= g <= 1 and n is not None and n > 0

def baseline(rows):
    rows = [r for r in rows if valid(r)]
    return sum(r['ever_drawn_win_rate'] * r['ever_drawn_game_count'] for r in rows) / sum(r['ever_drawn_game_count'] for r in rows)

def ranks(values):
    return [1 + sum(v < x for v in values) + (sum(v == x for v in values) - 1) / 2 for x in values]

def corr(x, y):
    return st.correlation(x, y) if len(x) > 1 and len(set(x)) > 1 and len(set(y)) > 1 else None

def q(value, base):
    value = .5 + (value-base)/.16
    distance = abs(value-.5)
    return value if distance <= .35 else .5 + math.copysign(.35 + .15*(1-math.exp(-(distance-.35)/.15)), value-.5)

def predict(r, policy, primary, alternate):
    rho, k = policy
    primary = r.get('MaskedPrimaryBaseline', primary)
    return primary + rho*r['AlternateN']/(r['AlternateN']+k)*(r['AlternateGIH']-alternate)

def metrics(rows, policy, primary, alternate):
    if not rows: return {'Cards': 0}
    predictions = [predict(r, policy, primary, alternate) for r in rows]
    actual = [r['PrimaryGIH'] for r in rows]
    neutral = [r.get('MaskedPrimaryBaseline', primary) for r in rows]
    target_q = [q(b+r['PrimaryN']/(r['PrimaryN']+500)*(r['PrimaryGIH']-b), b) for r,b in zip(rows,neutral)]
    prior_q = [q(p, b) for p,b in zip(predictions,neutral)]
    errors = [abs(p-g) for p,g in zip(predictions,actual)]
    neutral_errors = [abs(p-g) for p,g in zip(neutral,actual)]
    return {
        'Cards': len(rows), 'PriorMAE': st.mean(errors), 'NeutralMAE': st.mean(neutral_errors),
        'PriorRMSE': math.sqrt(st.mean((p-g)**2 for p,g in zip(predictions,actual))),
        'NeutralRMSE': math.sqrt(st.mean((b-g)**2 for b,g in zip(neutral,actual))),
        'PriorQMAE': st.mean(abs(p-g) for p,g in zip(prior_q,target_q)), 'NeutralQMAE': st.mean(abs(.5-g) for g in target_q),
        'RankCorrelation': corr(ranks(predictions), ranks(actual)), 'NeutralRankCorrelation': None,
        'DirectionAccuracy': st.mean((p>b)==(g>b) for p,g,b in zip(predictions,actual,neutral)),
        'NeutralDirectionAccuracy': None,
        'CatastrophicOverestimates5pp': sum(p-g>=.05 for p,g in zip(predictions,actual)),
        'NeutralCatastrophicOverestimates5pp': sum(b-g>=.05 for b,g in zip(neutral,actual)),
        'MateriallyWorseThanNeutral2pp': [dict(r, Prediction=p, PriorError=abs(p-g), NeutralError=abs(b-g))
            for r,p,g,b in zip(rows,predictions,actual,neutral) if abs(p-g)-abs(b-g)>=.02],
        'Rows': [dict(r, Prediction=p, Error=p-g, PrimaryQ=tq, PriorQ=pq)
            for r,p,g,tq,pq in zip(rows,predictions,actual,target_q,prior_q)]
    }

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('quick'); parser.add_argument('premier'); parser.add_argument('output')
    args = parser.parse_args()
    quick, premier = [json.loads(Path(p).read_text(encoding='utf-8-sig')) for p in (args.quick,args.premier)]
    for cache, fmt in ((quick,2),(premier,0)):
        assert cache['Expansion']=='FRA' and cache['RequestedFormat']==fmt and cache['SourceFormat']==fmt
        assert cache['SchemaVersion']==3 and cache['TimePeriod']=='ALL_TIME' and not cache.get('DeckColors')
    qr, pr = [c['Payload']['data'] for c in (quick,premier)]
    qb, pb = baseline(qr), baseline(pr)
    qnames, pnames = [{r['name']:r for r in rows if valid(r)} for rows in (qr,pr)]
    pairs = [dict(Name=name, PrimaryGIH=qnames[name]['ever_drawn_win_rate'], PrimaryN=qnames[name]['ever_drawn_game_count'],
        AlternateGIH=pnames[name]['ever_drawn_win_rate'], AlternateN=pnames[name]['ever_drawn_game_count'])
        for name in sorted(qnames.keys() & pnames.keys())]
    quick_games = sum(r['ever_drawn_game_count'] for r in qnames.values())
    premier_games = sum(r['ever_drawn_game_count'] for r in pnames.values())
    for r in pairs:
        # Artificially hide this exact card's rate, including its contribution to the primary baseline.
        r['MaskedPrimaryBaseline'] = (qb*quick_games-r['PrimaryGIH']*r['PrimaryN'])/(quick_games-r['PrimaryN'])
    def h(name): return int(hashlib.sha256(name.encode()).hexdigest()[:16],16)
    dev, test = [r for r in pairs if h(r['Name'])%5], [r for r in pairs if h(r['Name'])%5==0]
    assert len(dev)>=100 and len(test)>=25, 'Insufficient independent card coverage to fit policy'
    grid = []
    for rho in (.1,.2,.3,.4,.5,.6,.7,.8,.9,1.0):
        for k in (500,1000,2000,4000,8000,16000):
            losses = [st.mean(abs(predict(r,(rho,k),qb,pb)-r['PrimaryGIH']) for r in dev if (h(r['Name'])//5)%5==fold)
                for fold in range(5)]
            grid.append(dict(Reliability=rho, ShrinkageGames=k, FoldMAE=losses, MeanMAE=st.mean(losses), SE=st.stdev(losses)/math.sqrt(5),
                MeanEffectiveReliability=st.mean(rho*r['AlternateN']/(r['AlternateN']+k) for r in dev)))
    best = min(grid, key=lambda r:r['MeanMAE'])
    acceptable = [r for r in grid if r['MeanMAE'] <= best['MeanMAE']+best['SE']]
    chosen = min(acceptable, key=lambda r:(r['MeanEffectiveReliability'],r['MeanMAE'],r['Reliability'], -r['ShrinkageGames']))
    assert chosen['Reliability'] < 1, 'No conservative cross-format attenuation identified'
    policy = chosen['Reliability'], chosen['ShrinkageGames']
    held = metrics(test,policy,qb,pb)
    assert held['PriorMAE'] < .8*held['NeutralMAE'], 'Held-out improvement is insufficient'
    assert held['CatastrophicOverestimates5pp'] <= held['NeutralCatastrophicOverestimates5pp'], 'Unclean held-out overestimation'
    ordered_deltas = sorted(r['PrimaryGIH']-r['AlternateGIH'] for r in pairs)
    def percentile(p): return ordered_deltas[round(p*(len(ordered_deltas)-1))]
    result = {
        'Scope': 'FRA only, two asynchronous cached snapshots, card-held-out not temporal/event-held-out',
        'Sources': [dict(File=Path(p).name,SHA256=hashlib.sha256(Path(p).read_bytes()).hexdigest(),FetchedAt=c['FetchedAt'])
            for p,c in ((args.quick,quick),(args.premier,premier))],
        'QuickBaseline': qb,'PremierBaseline':pb,'MatchedCards':len(pairs),'DevelopmentCards':len(dev),'HeldOutCards':len(test),
        'Correlation':corr([r['PrimaryGIH'] for r in pairs],[r['AlternateGIH'] for r in pairs]),
        'MeanQuickMinusPremier':st.mean(ordered_deltas),'DeltaSD':st.stdev(ordered_deltas),
        'DeltaQuantiles':{str(p):percentile(p) for p in (.05,.25,.5,.75,.95)},
        'Selection': 'Development-fold MAE minimum + one SE, then minimum mean effective reliability. Test cards never used for selection. Each masked primary card is also removed from its primary environment baseline.',
        'BestDevelopment':best,'Chosen':chosen,'Grid':grid,'HeldOut':held,'AllPairs':metrics(pairs,policy,qb,pb),
        'SampleBuckets': {f'{low}-{high}':metrics([r for r in pairs if low<=r['AlternateN']<high],policy,qb,pb)
            for low,high in ((0,500),(500,2850),(2850,5000),(5000,10000),(10000,20000),(20000,1000000))},
        'PrimarySampleBuckets': {f'{low}-{high}':metrics([r for r in pairs if low<=r['PrimaryN']<high],policy,qb,pb)
            for low,high in ((500,1000),(1000,2000),(2000,5000),(5000,1000000))},
        'ApproachAUncenteredSameParameters':dict(MAE=st.mean(abs(r['MaskedPrimaryBaseline']+policy[0]*r['AlternateN']/(r['AlternateN']+policy[1])*(r['AlternateGIH']-r['MaskedPrimaryBaseline'])-r['PrimaryGIH']) for r in test)),
        'ReverseDirectionNotCalibrated':metrics([dict(r,PrimaryGIH=r['AlternateGIH'],PrimaryN=r['AlternateN'],AlternateGIH=r['PrimaryGIH'],AlternateN=r['PrimaryN'],
            MaskedPrimaryBaseline=(pb*premier_games-r['AlternateGIH']*r['AlternateN'])/(premier_games-r['AlternateN'])) for r in test],policy,pb,qb),
        'RawPairs':pairs,
        'Limitations': 'Only FRA; no Traditional or bonus-sheet study. Alternate overlap samples >=2850. Samples below that are mathematical shrinkage tests, not empirically validated. Observed primary is noisy; shared players are unknown; no independent deployment campaign. Cards are masked individually, not all test cards simultaneously. Holdout cannot eliminate same-set selection bias.'
    }
    path=Path(args.output);path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps({k:result[k] for k in ('MatchedCards','DevelopmentCards','HeldOutCards','Correlation','QuickBaseline','PremierBaseline','Chosen')},indent=2))
    print(json.dumps({k:v for k,v in held.items() if k not in ('Rows','MateriallyWorseThanNeutral2pp')},indent=2))

if __name__=='__main__': main()
