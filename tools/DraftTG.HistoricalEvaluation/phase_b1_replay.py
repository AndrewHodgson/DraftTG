"""Existing canonical importer and production scorer; bounded B.1 bulk evidence."""
import collections,gzip,pathlib,subprocess,time
from phase_b1_core import *

CLI=pathlib.Path('tools/DraftTG.QualityEvidenceResearch/bin/Debug/net10.0/DraftTG.QualityEvidenceResearch.dll')
IMPORTER=pathlib.Path('tools/DraftTG.HistoricalEvaluation/bin/Debug/net10.0/DraftTG.HistoricalEvaluation.Cli.dll')
OLD=pathlib.Path('data/historical/normalized/phase-b-v1')

def run(args,log):
    start=time.perf_counter();log.parent.mkdir(parents=True,exist_ok=True)
    with log.open('w',encoding='utf-8') as f: subprocess.run(['dotnet',*map(str,args)],stdout=f,stderr=subprocess.STDOUT,check=True)
    return time.perf_counter()-start

def profile(name='before',cache=False,detailed=True):
    guard();DATA.mkdir(parents=True,exist_ok=True)
    sets=('WOE','LCI','MKM','OTJ') if detailed else tuple(s for s in load('V2_PHASE_B1_SPLIT_MANIFEST.json')['Sets'] if s not in ('MH3','DFT','ONE','MOM','KHM'))
    stats=[s for s in load(OLD/'training-stats.json') if s['SetCode'] in sets]
    path=DATA/'profile-stats.json';dump(path,stats)
    output=pathlib.Path('data/historical/evaluation/phase-b1')/('profile-'+name+'.json')
    seconds=run([CLI,'--db',OLD/'training-replay.sqlite','--catalog',OLD/'catalog.json','--stats',path,'--splits','V2_PHASE_B1_SPLIT_MANIFEST.json',
        '--bonus',OLD/'bonus.json','--sets',','.join(sets),'--draft-limit','2' if detailed else '8','--cache',str(cache).lower(),'--detailed',str(detailed).lower(),'--output',output],output.with_suffix('.log'))
    result=load(output);result['ProcessSeconds']=seconds
    dump(OUT/('profile-'+name+'.json'),result)
    if name!='before' and detailed:
        before=load(OUT/'profile-before.json')
        if before['OutputHash']!=result['OutputHash'] or before['Decisions']!=result['Decisions']: raise ValueError('Replay equivalence failed')
        dump(OUT/'replay-equivalence.json',dict(passed=True,decisions=result['Decisions'],drafts=result['Drafts'],hash=result['OutputHash'],
            before=before['OutputHash'],after=result['OutputHash'],scope='All serialized score fields, selected deck projections, commitment, ranks, explanations, provenance, observation history, missing reasons; only timings excluded',
            beforeSeconds=before['Seconds'],afterSeconds=result['Seconds']))
    print('profile',name,result['Decisions'],result['Seconds'],flush=True)

def prepare():
    split=guard();mapping=load(DATA/'identity-map.json');selection=[]
    # Qualification counts determine extension; no scoring/record outcomes influence draft selection.
    datasets=[]
    for s in COMPATIBLE:
        f='PremierDraft';base=DATA if s in ('ONE','MOM') else OLD
        path=base/f'{s}.{f}.decisions.jsonl.gz';ids=set();eligible=collections.Counter()
        with gzip.open(path,'rt',encoding='utf-8') as stream:
            for line in stream:
                d=__import__('json').loads(line);i=d['Inputs'];draft=i['DraftId'];ids.add(draft)
                valid=i['PickNumber']<=14 and i['PoolIsComplete'] and all(x['Card']['CanonicalId'] for x in i['OfferedCards']+i['PoolBeforePick']) and sum(x['Count'] for x in i['OfferedCards'])>=2
                if valid:eligible[draft]+=1
        # Use target draft group only: own games cannot enter pick-time quality aggregates.
        ordered=sorted((d for d in ids if not evidence_group(s,f,d)),key=lambda d:group_hash(s,f,d))
        datasets.append((s,f,base,path,ordered,eligible))
    level=next((n for n in (128,160,192) if sum(sum(count[d] for d in ids[:n]) for s,f,base,path,ids,count in datasets)>=50000),192)
    db=DATA/'bulk-replay.sqlite';imports=[]
    for s,f,base,path,ordered,eligible in datasets:
        wanted=set(ordered[:level]);target=DATA/f'{s}.{f}.bulk.jsonl.gz'
        with gzip.open(path,'rt',encoding='utf-8') as stream,gzip.open(target,'wt',encoding='utf-8',compresslevel=1) as out:
            for line in stream:
                d=__import__('json').loads(line)
                if d['Inputs']['DraftId'] in wanted:out.write(line)
        ql=load(base/f'{s}.{f}.qualification.json')['picks']
        options=dict(DatasetId='phase-b1-replay-'+s+'-'+f,Source='17LandsPublic',SourceUri=ql['sourceFiles']['picks']['url'],SetCode=s,Format=f,
            Partition=split['Sets'][s],BonusSets=load(DATA/'bonus.json').get(s,[]),PicksPerPack=ql['picksPerPack'],SourceSchema='historical-decision-v1',IsPrefixSample=False)
        op=DATA/f'{s}.{f}.import-options.json';dump(op,options)
        log=DATA/f'{s}.{f}.import.log'
        elapsed=run([IMPORTER,'import','--source',target,'--options',op,'--catalog',DATA/'catalog.json','--splits','V2_PHASE_B1_SPLIT_MANIFEST.json','--db',db],log)
        txt=log.read_text(encoding='utf-8-sig');manifest=__import__('json').loads(txt[txt.index('{'):]);imports.append(dict(set=s,processSeconds=elapsed,manifest=manifest))
        selection.append(dict(set=s,format=f,wholeDrafts=len(wanted),selectedHashes=[group_hash(s,f,d) for d in ordered[:level]],
            sourceHash=sha(path),selectedFileHash=sha(target),qualifiedComparativeDecisions=sum(eligible[d] for d in wanted)))
        print('import',s,manifest['PickCount'],flush=True)
    dump(OUT/'replay-selection.json',dict(seed=SEED,draftsPerDataset=level,selection='Lowest outcome-independent hashes within target draft group; count-based preregistered extension',datasets=selection,imports=imports))

def bulk():
    guard()
    if not (OUT/'replay-equivalence.json').exists() or not load(OUT/'replay-equivalence.json')['passed']: raise ValueError('Equivalence proof required')
    output=pathlib.Path('data/historical/evaluation/phase-b1/bulk.json')
    elapsed=run([CLI,'--db',DATA/'bulk-replay.sqlite','--catalog',DATA/'catalog.json','--stats',DATA/'compatible-stats.json','--splits','V2_PHASE_B1_SPLIT_MANIFEST.json',
        '--bonus',DATA/'bonus.json','--sets',','.join(COMPATIBLE),'--cache','true','--detailed','false','--q',OUT/'q-values.json','--output',output],output.with_suffix('.log'))
    result=load(output);result['ProcessSeconds']=elapsed;result['SelectionHash']=sha(OUT/'replay-selection.json');result['AssemblySha256']=sha(CLI)
    dump(OUT/'contextual-replay.json',result);print('bulk complete',result['Decisions'],result['Metrics']['current | overall']['Eligible'],flush=True)
