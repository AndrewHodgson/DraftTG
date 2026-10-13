"""Reporting diagnostics from the released corpus; no fitting, model evaluation, or parameter changes.

Unique-game baselines must not be confused with card-game-weighted GIH. Percentile diagnostics
use calibration/development only and do not select a fourth candidate.
"""
import collections,json,pathlib,statistics,sys
from phase_b_core import *
from phase_b_source import ROOT,sha
sys.path.insert(0,str(ROOT/'cache/phase-b/python'))
import pyarrow.parquet as pq
DATA=ROOT/'normalized/phase-b-v1';OUT=pathlib.Path('docs/evidence/v2-phase-b')

def diagnostics():
    verify_registration();release=json.loads((OUT/'holdout-release.json').read_text())
    if 'completedAt' not in release:raise ValueError('Final release must complete before reporting diagnostics')
    target=OUT/'report-diagnostics.json'
    if target.exists():return json.loads(target.read_text())
    datasets=[];all_drafts=set();picked_drafts=set();mappings=json.loads((DATA/'identity-map.json').read_text())
    qualification=json.loads((OUT/'qualification.json').read_text())
    for dataset in qualification['datasets']:
        s=dataset['set'];f=dataset['format'];p=DATA/f'{s}.{f}.observations.parquet';games={};drafts=set()
        if p.exists():
            for batch in pq.ParquetFile(p).iter_batches(columns=['game','draft','won','rank','colors'],batch_size=131072):
                for r in batch.to_pylist():
                    metadata=(r['won'],r['rank'],r['colors'])
                    if r['game'] in games and games[r['game']]!=metadata:raise ValueError('Conflicting per-game reporting metadata')
                    games[r['game']]=metadata;drafts.add(r['draft']);all_drafts.add((s,f,r['draft']))
            strata=collections.defaultdict(lambda:[0,0]);colors=collections.defaultdict(lambda:[0,0])
            for win,rank,color in games.values():
                label=(rank or 'unknown').lower().split('-')[0]
                if label not in ('bronze','silver','gold','platinum','diamond','mythic'):label='unknown'
                strata[label][0]+=1;strata[label][1]+=win;colors[color or 'unknown'][0]+=1;colors[color or 'unknown'][1]+=win
            summary=lambda groups:{k:dict(games=v[0],wins=v[1],winRate=v[1]/v[0]) for k,v in sorted(groups.items())}
            datasets.append(dict(set=s,format=f,uniqueGames=len(games),uniqueGameWinRate=sum(v[0] for v in games.values())/len(games),
                gameDrafts=len(drafts),rankStrata=summary(strata),reportedMainColors=summary(colors),sourceHash=sha(p)))
        # Draft count only; do not recompute predictions or labels. Source decisions are canonical compact JSONL.
        decisions=DATA/f'{s}.{f}.decisions.jsonl.gz'
        if decisions.exists():
            import gzip,re
            with gzip.open(decisions,'rt',encoding='utf-8') as stream:
                for line in stream:
                    draft=re.search(r'"DraftId":"([^"]+)"',line).group(1)
                    picked_drafts.add((s,f,draft));all_drafts.add((s,f,draft))
    # Percentile range/ranking diagnostic on already exported training features only.
    features=json.loads((OUT/'training-card-features.json').read_text());baselines=json.loads((OUT/'training-baselines.json').read_text())
    selected=json.loads((OUT/'selection.json').read_text());k=selected['candidates'][0]['k'];percentiles=[]
    for dataset in baselines:
        rows=[r for r in features if r['set']==dataset['set'] and r['format']==dataset['format'] and r['targetN']>=100 and r['evidenceN']>=50]
        b=dataset['evidenceBaseline']['weighted'];tb=dataset['targetBaseline']['weighted']
        rates=[shrink(r['evidenceRate'],r['evidenceN'],b,k) for r in rows];cdf=[percentile(p,rates) for p in rates]
        targetQ=[quality(r['targetRate'],tb,dataset['targetBaseline']['spread']) for r in rows]
        percentiles.append(dict(set=dataset['set'],format=dataset['format'],cards=len(rows),QMAE=statistics.mean(abs(q-t) for q,t in zip(cdf,targetQ)),
            rankCorrelation=rank_correlation(cdf,[r['targetRate'] for r in rows]),
            signAccuracy=statistics.mean((q>.5)==(r['targetRate']>tb) for q,r in zip(cdf,rows))))
    result=dict(pickDrafts=len(picked_drafts),combinedDistinctDrafts=len(all_drafts),datasets=datasets,percentileDiagnostic=percentiles,
        percentileScope='Calibration/development only, post-selection reporting; no fourth candidate or parameter tuning; target reference is mature delta Q',
        baselineSemantics='Unique-game WR counts each game once. Card-game-weighted GIH weights each observed card/game presence, so the same game may contribute several cards. Copy-weighted GIH counts opening+drawn copies. These are three different denominators.',
        bias='Ranks and reported main_colors are descriptive post-draft strata only, never pick-time features; source lifetime buckets have uncertain temporal scope.')
    target.write_text(json.dumps(result,indent=2));print('Reporting diagnostics complete');return result

if __name__=='__main__':diagnostics()
