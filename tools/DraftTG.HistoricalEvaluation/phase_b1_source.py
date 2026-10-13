"""New development acquisition only; holdouts receive metadata checks, never bytes."""
import concurrent.futures, json, pathlib, time, urllib.request
from phase_b_source import API,DOWNLOAD,REVISION,REPO,TABLES,get
from phase_b1_core import *

RAW=pathlib.Path('data/historical/raw/phase-b1')

def acquire():
    guard(); files=[]; metadata=[]
    for s in ('ONE','MOM','MH3','DFT'):
        for table in TABLES:
            events=get(API+table+'/expansion='+s)
            metadata.append(dict(set=s,table=table,events=[e['path'] for e in events]))
            if s in ('MH3','DFT'): continue
            for event in events:
                fmt=event['path'].split('=')[-1]
                if fmt not in ('PremierDraft','QuickDraft'): continue
                for item in get(API+event['path']):
                    if item['type']=='file': files.append(dict(item,set=s,format=fmt,table=table))
    dump(OUT/'source-availability.json',dict(revision=REVISION,metadataOnlyHoldouts=['MH3','DFT'],entries=metadata))
    if sum(i['size'] for i in files)>2*1024**3: raise ValueError('B.1 acquisition budget exceeded')
    def fetch(item):
        target=RAW/item['path'];target.parent.mkdir(parents=True,exist_ok=True)
        expected=item['lfs']['oid'];cached=target.exists() and sha(target)==expected;start=time.perf_counter()
        if not cached:
            partial=target.with_suffix('.download')
            urllib.request.urlretrieve(DOWNLOAD+item['path'],partial)
            if sha(partial)!=expected: raise ValueError('LFS SHA256 mismatch')
            partial.replace(target)
        return dict(item,url=DOWNLOAD+item['path'],sha256=expected,bytes=target.stat().st_size,cacheHit=cached,seconds=time.perf_counter()-start)
    complete=[]
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for item in pool.map(fetch,files):
            complete.append(item);print(item['path'],item['bytes'],flush=True)
    result=dict(repository=REPO,revision=REVISION,license='CC-BY-4.0 per pinned README',files=complete)
    dump(RAW/'sources.json',result);dump(OUT/'new-source-provenance.json',result)

def qualify():
    guard()
    import phase_b_qualify as old
    old.RAW=RAW;old.OUT=DATA;old.EVIDENCE=OUT;DATA.mkdir(parents=True,exist_ok=True)
    old.group_hash=group_hash;old.evidence_group=evidence_group
    identity=old.Identities(['ONE','MOM','MUL'])
    identity.policy=json.loads(json.dumps(identity.policy));identity.policy['BonusSets'].update(ONE=[],MOM=['MUL'])
    dump(OUT/'identity-policy.json',identity.policy);identity.provenance['policySha256']=sha(OUT/'identity-policy.json')
    sources=load(RAW/'sources.json')['files'];results=[]
    for s in ('ONE','MOM'):
        f='PremierDraft';subset=[i for i in sources if i['set']==s and i['format']==f]
        if len(subset)!=5: raise ValueError('Incomplete development source '+s)
        path=DATA/f'{s}.{f}.qualification.json'
        if path.exists():
            # Resume by loading the complete catalog/map, not silently losing identity metadata.
            cached=load(DATA/'new-identity-map.json');identity.map.update(cached)
            identity.catalog.update({c['id']:c for c in load(DATA/'new-catalog.json')})
            results.append(load(path));continue
        source_map={i['table']:i for i in subset}
        picks=old.qualify_picks(s,f,identity,source_map)
        games=old.qualify_games(s,f,identity,source_map)
        result=dict(set=s,format=f,picks=picks,games=games)
        dump(path,result);results.append(result)
        dump(DATA/'new-catalog.json',list(identity.catalog.values()));dump(DATA/'new-identity-map.json',identity.map)
        print('qualified',s,flush=True)
    dump(OUT/'new-qualification.json',results)
    olddata=pathlib.Path('data/historical/normalized/phase-b-v1')
    catalog={c['id']:c for c in load(olddata/'catalog.json')}
    for cid,c in identity.catalog.items():
        if cid in catalog and catalog[cid]!=c: raise ValueError('Catalog conflict '+cid)
        catalog[cid]=c
    dump(DATA/'catalog.json',list(catalog.values()))
    mapping=load(olddata/'identity-map.json');mapping.update(identity.map);dump(DATA/'identity-map.json',mapping)
    dump(DATA/'bonus.json',identity.policy['BonusSets'])

if __name__=='__main__':
    import sys
    acquire() if sys.argv[1]=='acquire' else qualify()
