"""Verify pinned bytes and independent original-dump spot checks; no performance analysis."""
import collections,csv,json,pathlib,time
from phase_b_qualify import RAW,ROOT,OUT,EVIDENCE,filter_table,source_path,decision_key,dump,pa,pq
from phase_b_source import sha
from phase_b_core import verify_registration

def audit():
    verify_registration();sources=json.loads((EVIDENCE/'source-provenance.json').read_text());checked=[]
    for item in sources['files']:
        actual=sha(RAW/item['path'])
        if actual!=item['sha256']:raise ValueError('Pinned source content changed: '+item['path'])
        checked.append(dict(path=item['path'],sha256=actual,bytes=(RAW/item['path']).stat().st_size))
    for item in sources['wideFallbacks']:
        if sha(item['file'])!=item['sha256']:raise ValueError('Original wide sample changed')
    comparisons=[]
    for s in ('WOE','LCI','MKM','OTJ','VOW'):
        original=ROOT/'raw'/f'{s}.PremierDraft.draft_data.sample.csv'
        with original.open(encoding='utf-8',newline='') as stream:rows=list(csv.DictReader(stream))
        ids=sorted({r['draft_id'] for r in rows});picks={decision_key(r):r for r in filter_table(source_path('picks',s,'PremierDraft'),ids).to_pylist()}
        tables={}
        for table,column in [('pack_cards','pack_card'),('pool_cards','pool')]:
            indexed=collections.defaultdict(dict)
            for r in filter_table(source_path(table,s,'PremierDraft'),ids).to_pylist():
                key=decision_key(r)
                if r['card'] in indexed[key]:raise ValueError('Duplicate long-table occurrence')
                indexed[key][r['card']]=r[column]
            tables[table]=indexed
        count=missing=mismatches=0
        for r in rows:
            key=decision_key(r);p=picks.get(key)
            if not p:missing+=1;continue
            count+=1
            expected_pack={k[10:]:int(v) for k,v in r.items() if k.startswith('pack_card_') and int(v)>0}
            expected_pool={k[5:]:int(v) for k,v in r.items() if k.startswith('pool_') and int(v)>0}
            if p['pick']!=r['pick'] or expected_pack!=tables['pack_cards'][key] or expected_pool!=tables['pool_cards'][key]:mismatches+=1
        comparisons.append(dict(set=s,format='PremierDraft',originalSha256=sha(original),originalRows=len(rows),compared=count,missing=missing,mismatches=mismatches,
                                checkedFields=['draft/pack/pick keys','picked name','all offered occurrence counts','all pre-pick pool occurrence counts']))
        if mismatches:raise ValueError('Public transformation differs from original source')
    dump(EVIDENCE/'source-audit.json',dict(verifiedFiles=checked,originalSpotChecks=comparisons,
        limitation='Spot checks establish transformed draft semantics in five sets, not exhaustive ETL verification. Lost old-game keys explicitly declined; originals used for 2021 games. No provider-derived targets/embeddings/aggregate ratings used.'))
    positions=[]
    for path in sorted(OUT.glob('*.positions.json')):
        features=json.loads(path.read_text());seen=taken=0
        for r in features:
            for label in ('SeenPosition','TakenPosition'):
                p=r[label];n=sum(p['distribution'].values())
                if n!=p['count']:raise ValueError('Position distribution/count mismatch')
                if n and abs(sum(int(k)*v for k,v in p['distribution'].items())/n-p['mean'])>1e-12:raise ValueError('Position mean mismatch')
                if any(int(k)<1 for k in p['distribution']):raise ValueError('Positions must be 1-based')
            if sum(p['count'] for p in r['packSpecificSeen'].values())!=r['SeenPosition']['count']:raise ValueError('Pack-specific position mismatch')
            seen+=r['SeenPosition']['count'];taken+=r['TakenPosition']['count']
        positions.append(dict(set=features[0]['set'],format=features[0]['format'],cards=len(features),seenCount=seen,takenCount=taken,featuresSha256=sha(path)))
    # Full distributions stay in the ignored normalized corpus; small portable feature rows retain medians and counts.
    small=[]
    for path in sorted(OUT.glob('*.positions.json')):
        for r in json.loads(path.read_text()):
            small.append(dict(set=r['set'],format=r['format'],card=r['card'],
                SeenCount=r['SeenPosition']['count'],TakenCount=r['TakenPosition']['count'],
                DerivedAverageSeenAt=r['SeenPosition']['mean'],SeenPositionMedian=r['SeenPosition']['median'],
                DerivedAverageTakenAt=r['TakenPosition']['mean'],TakenPositionMedian=r['TakenPosition']['median'],
                SeenPositionPercentile=r['SeenPositionPercentile'],TakenPositionPercentile=r['TakenPositionPercentile'],
                SeenLaterThanSetExpectation=r['SeenLaterThanSetExpectation']))
    dump(EVIDENCE/'derived-positions.json',small)
    dump(EVIDENCE/'position-audit.json',dict(datasets=positions,indexing='Source 0-based pack/pick -> 1-based pack-local pick, never global index; duplicate offered copies each count once; pack-specific histograms retained',
        officialALSAComparison=None,officialATAComparison=None,
        reason='No licensed overlapping published ALSA/ATA aggregate supplied. Current API explicitly forbids outside use. Derived pack-local sample means must not be described as official ALSA/ATA.'))
    print('Source/position audits passed')

if __name__=='__main__':audit()
