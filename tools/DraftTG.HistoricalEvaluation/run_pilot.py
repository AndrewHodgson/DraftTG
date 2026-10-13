"""Reproducible bounded pilot runner; inputs prepared separately, raw/normalized files stay ignored."""
import json,pathlib,subprocess,time
root=pathlib.Path.cwd(); raw=root/'data/historical/raw';cache=root/'data/historical/cache'
normalized=root/'data/historical/normalized';normalized.mkdir(parents=True,exist_ok=True)
db=normalized/'pilot-v1.sqlite'; output=root/'data/historical/evaluation/phase-a'
project=root/'tools/DraftTG.HistoricalEvaluation/DraftTG.HistoricalEvaluation.csproj'
cli=root/'tools/DraftTG.HistoricalEvaluation/bin/Debug/net10.0/DraftTG.HistoricalEvaluation.Cli.dll'
def run(args, log=None):
    started=time.perf_counter()
    if log:
        with log.open('w',encoding='utf-8') as f: subprocess.run(['dotnet',str(cli),*args],stdout=f,check=True)
    else: subprocess.run(['dotnet',str(cli),*args],check=True)
    return time.perf_counter()-started
splits=root/'docs/historical/splits/pilot-v1.json';bonuses=root/'docs/historical/bonus-sheets.json'
split=json.loads(splits.read_text());bonus=json.loads(bonuses.read_text())
times=[]
for file in sorted(raw.glob('*.draft_data.sample.csv')):
    set_code,format_name=file.name.split('.')[:2]
    options={'DatasetId':f'public-prefix-v1-{set_code}-{format_name}','Source':'17LandsPublic',
        'SourceUri':f'https://17lands-public.s3.amazonaws.com/analysis_data/draft_data/draft_data_public.{set_code}.{format_name}.csv.gz',
        'SetCode':set_code,'Format':format_name,'Partition':split['Sets'][set_code],'BonusSets':bonus[set_code],
        'PicksPerPack':{'LCI':15,'MKM':13,'OTJ':14,'VOW':14,'WOE':14}[set_code],
        'SourceSchema':'17lands-wide-draft-v1','IsPrefixSample':True}
    op=root/f'docs/historical/datasets/{set_code}-{format_name}-prefix-v1.json';op.parent.mkdir(parents=True,exist_ok=True)
    op.write_text(json.dumps(options,indent=2),encoding='utf-8')
    elapsed=run(['import','--source',str(file),'--options',str(op),'--catalog',str(cache/'catalog.json'),
        '--splits',str(splits),'--db',str(db)],normalized/f'{set_code}-{format_name}-manifest.json')
    print(f'Imported {set_code}/{format_name} in {elapsed:.3f}s',flush=True)
    game=raw/f'{set_code}.{format_name}.game_data.sample.csv'
    if game.exists():
        run(['import-decks','--source',str(game),'--options',str(op),'--catalog',str(cache/'catalog.json'),'--db',str(db)],
            normalized/f'{set_code}-{format_name}-deck-manifest.json')
    times.append({'set':set_code,'format':format_name,'importProcessSeconds':elapsed})
run(['validate','--db',str(db),'--splits',str(splits),'--output',str(normalized/'validation.json')])
run(['decks','--db',str(db),'--catalog',str(cache/'catalog.json'),'--output',str(normalized)])
run(['stats','--catalog',str(cache/'catalog.json'),'--stats',str(cache/'stats.json'),'--bonus',str(bonuses),'--output',str(normalized)])
elapsed=run(['evaluate','--db',str(db),'--catalog',str(cache/'catalog.json'),'--stats',str(cache/'stats.json'),
    '--splits',str(splits),'--bonus',str(bonuses),'--models','contextual-pick-score-v1.6,contextual-pick-score-v1.6.1,contextual-pick-score-v1.6.2',
    '--output',str(output)])
(normalized/'pilot-timings.json').write_text(json.dumps({'imports':times,'evaluationProcessSeconds':elapsed},indent=2))
