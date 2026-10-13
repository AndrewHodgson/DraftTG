"""Reuses the existing canonical importer + actual frozen engine, on eight preregistered hash-selected drafts/dataset.

Holdout invocation is internal to the one final release, after selection; not a free-standing retuning command.
"""
import argparse,json,pathlib,subprocess,time
from phase_b_core import verify_registration
from phase_b_source import ROOT,sha

DATA=ROOT/'normalized/phase-b-v1';OUT=pathlib.Path('docs/evidence/v2-phase-b')
CLI=pathlib.Path('tools/DraftTG.HistoricalEvaluation/bin/Debug/net10.0/DraftTG.HistoricalEvaluation.Cli.dll')

def replay(final=False):
    splits=verify_registration();prefix='holdout' if final else 'training'
    if final and not (OUT/'holdout-release.json').exists():raise ValueError('Final release lock required')
    if final:
        release=json.loads((OUT/'holdout-release.json').read_text())
        if release['releaseProcessId']!=__import__('os').getpid() or 'completedAt' in release:raise ValueError('Final contextual evaluation is locked')
    if not (OUT/'selection.json').exists():raise ValueError('Frozen selection required')
    output=ROOT/'evaluation/phase-b'/prefix;output.mkdir(parents=True,exist_ok=True)
    db=DATA/f'{prefix}-replay.sqlite';catalog=DATA/'catalog.json';stats=DATA/f'{prefix}-stats.json'
    policy=json.loads(pathlib.Path('docs/historical/phase-b-identity-policy.json').read_text());bonus=DATA/'bonus.json'
    bonus.write_text(json.dumps(policy['BonusSets']))
    def run(args,log):
        start=time.perf_counter()
        with log.open('w',encoding='utf-8') as f:subprocess.run(['dotnet',str(CLI),*args],stdout=f,stderr=subprocess.STDOUT,check=True)
        return time.perf_counter()-start
    imports=[]
    for file in sorted(DATA.glob('*.replay.jsonl.gz')):
        s,fmt=file.name.split('.')[:2]
        if (splits[s]=='Holdout')!=final:continue
        q=json.loads((DATA/f'{s}.{fmt}.qualification.json').read_text())['picks']
        options=dict(DatasetId='phase-b-replay-'+s+'-'+fmt,Source='17LandsPublic',SourceUri=q['sourceFiles']['picks']['url'],
            SetCode=s,Format=fmt,Partition=splits[s],BonusSets=policy['BonusSets'].get(s,[]),PicksPerPack=q['picksPerPack'],
            SourceSchema='historical-decision-v1',IsPrefixSample=False)
        op=DATA/f'{s}.{fmt}.import-options.json';op.write_text(json.dumps(options));log=output/f'{s}.{fmt}.import.json'
        existed=db.exists()
        elapsed=run(['import','--source',str(file),'--options',str(op),'--catalog',str(catalog),'--splits','docs/historical/splits/phase-b-v1.json','--db',str(db)],log)
        log_text=log.read_text();manifest=json.loads(log_text[log_text.index('{'):]);imports.append(dict(set=s,format=fmt,processSeconds=elapsed,manifest=manifest))
        print('production import',s,fmt,manifest['PickCount'],flush=True)
    elapsed=run(['evaluate','--db',str(db),'--catalog',str(catalog),'--stats',str(stats),'--splits','docs/historical/splits/phase-b-v1.json',
        '--bonus',str(bonus),'--model','contextual-pick-score-v1.6.2','--research-q',str(OUT/f'{prefix}-q-values.json'),'--output',str(output)],output/'evaluate.log')
    summary=json.loads((output/'summary.json').read_text());summary['Imports']=imports
    summary['ProcessSeconds']=elapsed;summary['DatabaseBytes']=db.stat().st_size;summary['ExperimentalQHash']=sha(OUT/f'{prefix}-q-values.json')
    summary['Limitations']=[s for s in summary['Limitations'] if 'Prefix pilot' not in s]
    summary['Limitations'].append('Phase B actual contextual replay uses 8 whole drafts/dataset chosen by preregistered outcome-independent SHA256, rather than every qualified decision. Older game sources are 20k-row prefixes; modern game sources use 8k hash-selected drafts.')
    (OUT/f'{prefix}-production-replay.json').write_text(json.dumps(summary,indent=2))
    print('production replay complete',prefix,summary['Decisions'],summary['Seconds'],flush=True)
    return summary

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('command',choices=['training']);args=parser.parse_args();replay(False)
