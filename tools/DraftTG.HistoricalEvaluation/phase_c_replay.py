import pathlib,subprocess,time
from phase_c_core import *
CLI=pathlib.Path('tools/DraftTG.QualityEvidenceResearch/bin/Debug/net10.0/DraftTG.QualityEvidenceResearch.dll')

def run(args,log):
    log.parent.mkdir(parents=True,exist_ok=True);start=time.perf_counter()
    with log.open('w',encoding='utf-8') as f:subprocess.run(['dotnet',str(CLI),*map(str,args)],stdout=f,stderr=subprocess.STDOUT,check=True)
    return time.perf_counter()-start

def common(output):
    return ['--db',B1/'bulk-replay.sqlite','--catalog',B1/'catalog.json','--stats',B1/'compatible-stats.json',
        '--splits','V2_PHASE_B1_SPLIT_MANIFEST.json','--bonus',B1/'bonus.json','--sets',','.join(COMPATIBLE),'--cache','true','--detailed','false','--output',output]

def metadata():
    guard();DATA.mkdir(parents=True,exist_ok=True)
    run(common(DATA/'metadata-unused.json')+['--export-card-metadata',DATA/'metadata.json'],DATA/'metadata.log')

def replay():
    guard();path=pathlib.Path('data/historical/evaluation/phase-c/contextual.json')
    if (OUT/'selection.json').exists():raise ValueError('Selection frozen; no additional development rerun')
    seconds=run(common(path)+['--lane',DATA/'predictions.jsonl.gz','--lane-alsa',DATA/'current-alsa.json'],path.with_suffix('.log'))
    result=load(path);result['ProcessSeconds']=seconds;result['AssemblySha256']=sha(CLI);result['PredictionsSha256']=sha(DATA/'predictions.jsonl.gz')
    result['PositionHash']=sha(DATA/'positions.json');result['QualitySnapshotHash']=sha(B1/'compatible-stats.json');result['LOnly']=True
    expected=load('docs/evidence/v2-phase-b1/contextual-replay.json')
    if result['Metrics']['current | overall']!=expected['Metrics']['current | overall']:raise ValueError('Frozen B.1 baseline changed')
    dump(OUT/'contextual-replay.json',result);print(result['Decisions'],result['Metrics']['complete-baseline | overall']['Eligible'],flush=True)
