"""Publish small aggregate evidence, not raw drafts or row-level scored data."""
import collections,csv,hashlib,json,pathlib,sqlite3,xml.etree.ElementTree as ET
root=pathlib.Path.cwd();base=root/'data/historical';normalized=base/'normalized'
out=root/'docs/evidence/v2-phase-a';out.mkdir(parents=True,exist_ok=True)
summary=json.loads((base/'evaluation/phase-a/summary.json').read_text())
db=sqlite3.connect(normalized/'pilot-v1.sqlite')
datasets=[]
for m in summary['Datasets']:
    selected={k:v for k,v in m.items() if k not in ['ObservedColumns','IdentityIssues']}
    selected['ObservedColumnsSha256']=hashlib.sha256(json.dumps(m['ObservedColumns'],separators=(',',':')).encode()).hexdigest()
    selected['ColumnCount']=len(m['ObservedColumns'])
    selected['IdentityCoverage']=m['IdentityResolvedCount']/(m['IdentityResolvedCount']+m['IdentityUnresolvedCount'])
    selected['IdentityIssues']=m['IdentityIssues']
    datasets.append(selected)
coverage=collections.Counter()
for (text,) in db.execute('select inputs from decisions'):
    row=json.loads(text)
    coverage['identityIncompleteOfferedDecisions']+=any(q['Card']['CanonicalId'] is None for q in row['OfferedCards'])
    coverage['unsupportedPick15Decisions']+=row['PickNumber']>14
    coverage['sourcePartialPoolDecisions']+=not row['PoolIsComplete']
exclusions=collections.Counter()
with (base/'evaluation/phase-a/decisions.jsonl').open(encoding='utf-8') as replay:
    for line in replay:
        row=json.loads(line); result=row['Models'][-1]
        if result['Agreement']['Rank'] is not None: continue
        if not result['Cards']: exclusions[result['MissingInputs'][0]]+=1
        elif result['UnscorableCards']>0: exclusions['No usable environment baseline / unavailable scores']+=1
        elif result['Agreement']['Occurrences']==1: exclusions['Singleton pack: frozen engine intentionally emits no choice rank']+=1
        else: exclusions['Actual pick unranked']+=1
decks=collections.Counter()
for (text,) in db.execute('select payload from decks'):
    d=json.loads(text);decks['observedBuilds']+=1;decks['buildsWithJoinedFinalRecord']+=d['Wins'] is not None and d['Losses'] is not None
    decks['totalMainDeckOccurrences']+=d['Summary']['TotalCards'];decks['unresolvedMainDeckOccurrences']+=d['Summary']['UnresolvedCards']
    decks['structuredMetadataOccurrences']+=d['Summary']['StructuredMetadataCards'];decks['oracleCoveredOccurrences']+=d['Summary']['OracleTextCoveredCards']
    decks['buildsWithFullPool']+=d['FullPool'] is not None
sizes={}
for section in ['raw','cache','normalized','evaluation']:
    sizes[section]=sum(f.stat().st_size for f in (base/section).rglob('*') if f.is_file())
tests=[]
for file in (root/'artifacts/v2-phase-a/release-tests').glob('*.trx'):
    c=next(e for e in ET.parse(file).getroot().iter() if e.tag.endswith('Counters'));tests.append(c.attrib)
golden_path=root/'artifacts/v2-phase-a/golden/golden.json'
golden=json.loads(golden_path.read_text()) if golden_path.exists() else None
comparisons=[{k:v for k,v in c.items() if not k.startswith('Largest')} for c in summary['Comparisons']]
active={
 'raw':sum(f.stat().st_size for f in (base/'raw').iterdir() if f.is_file() and ('.draft_data.sample.csv' in f.name or '.game_data.sample.csv' in f.name or f.name=='cards.csv' or f.name=='public-index.json' or f.name.startswith('acquisition-'))),
 'cache':sizes['cache'],
 'normalized':sum(f.stat().st_size for f in normalized.iterdir() if f.is_file() and (not f.name.startswith('pilot') or f.name in ['pilot-v1.sqlite','pilot-timings.json'])),
 'evaluation':sum(f.stat().st_size for f in (base/'evaluation/phase-a').rglob('*') if f.is_file())}
pilot={'schema':'v2-phase-a-pilot-summary-v1','baseline':'contextual-pick-score-v1.6.2','build':summary['Build'],
       'regime':summary['Regime'],'interpretation':summary['Interpretation'],'sets':sorted({m['SetCode'] for m in datasets}),
       'formats':sorted({m['Format'] for m in datasets}),'drafts':summary['Drafts'],'decisions':summary['Decisions'],
       'overallMetrics':{model:summary['Metrics'][model+' | overall'] for model in summary['Models']},
       'setFormatMetrics':{k:v for k,v in summary['Metrics'].items() if 'set-format=' in k},
       'outcomeMetrics':{k:v for k,v in summary['Metrics'].items() if ' | outcome=' in k},
       'comparisons':comparisons,'coverage':dict(coverage),'exclusionCounts':dict(exclusions),'datasets':datasets,'decks':dict(decks),
       'featureCoverage':json.loads((normalized/'feature-coverage.json').read_text()),
       'performance':{k:summary[k] for k in ['Seconds','DecisionsPerSecond','PeakWorkingSetBytes','OptimizerCalls','EngineMilliseconds']},
       'diskBytes':sizes,'activePilotDiskBytes':active,'tests':{'total':sum(int(t['total']) for t in tests),'passed':sum(int(t['passed']) for t in tests),
       'failed':sum(int(t['failed']) for t in tests),'notExecuted':sum(int(t['notExecuted']) for t in tests)},
       'golden':golden,'splitManifest':'docs/historical/splits/pilot-v1.json','catalogHash':summary['CatalogHash'],'statsHash':summary['StatsHash'],
       'chronologicalBacktest':'Not available: no timestamped earlier snapshots. Mature sample statistics overlap replay drafts.',
       'limitations':summary['Limitations'],'phaseBRecommendation':'Feature coverage and larger representative data qualification, then preregister set-level evaluations. No V2 weight selection on this pilot.'}
(out/'pilot-summary.json').write_text(json.dumps(pilot,indent=2),encoding='utf-8')
(out/'model-comparisons.json').write_text(json.dumps(summary['Comparisons'],indent=2),encoding='utf-8')
(out/'identity-metadata.json').write_text((base/'cache/identity-metadata.json').read_text(encoding='utf-8'),encoding='utf-8')
(out/'observed-schemas.json').write_text((base/'cache/observed-schemas.json').read_text(encoding='utf-8'),encoding='utf-8')
(out/'metrics.csv').write_text((base/'evaluation/phase-a/metrics.csv').read_text(encoding='utf-8'),encoding='utf-8')
with db:
    manifest=[json.loads(p[0]) for p in db.execute('select payload from deck_manifests order by id')]
    for m in manifest:
        m['ColumnCount']=len(m.pop('ObservedColumns'))
    (out/'deck-manifests.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in pilot.items() if k in ['sets','formats','drafts','decisions','coverage','decks','performance','diskBytes','tests']},indent=2))
