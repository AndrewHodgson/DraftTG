"""Pinned public Parquet acquisition; no provider targets, private endpoints or scoring changes.

Run from the repository root. PyArrow is an acquisition/research-only optional dependency.
"""
import concurrent.futures, hashlib, json, pathlib, time, urllib.error, urllib.request

ROOT = pathlib.Path('data/historical')
RAW = ROOT / 'raw/phase-b'
REVISION = '766a56bbdb5ebe1ed4c0d3441df694c81ec9f774'
REPO = 'TrevorJS/17lands-corpus'
API = f'https://huggingface.co/api/datasets/{REPO}/tree/{REVISION}/'
DOWNLOAD = f'https://huggingface.co/datasets/{REPO}/resolve/{REVISION}/'
TABLES = ('picks', 'pack_cards', 'pool_cards', 'games', 'game_cards')

def sha(path):
    with pathlib.Path(path).open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()

def get(url):
    for attempt in range(4):
        try:
            with urllib.request.urlopen(url, timeout=90) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code == 404: return []
            if attempt == 3: raise
        except (OSError, TimeoutError):
            if attempt == 3: raise
        time.sleep(2 ** attempt)

def inventory():
    sets = json.loads(pathlib.Path('docs/historical/splits/phase-b-v1.json').read_text(encoding='utf-8-sig'))['Sets']
    tasks = [(table, s) for s in sets for table in TABLES]
    def entries(task):
        table, s = task
        out = []
        for event in get(API + table + '/expansion=' + s):
            if event['path'].split('=')[-1] not in ('PremierDraft', 'QuickDraft'): continue
            for item in get(API + event['path']):
                if item['type'] == 'file':
                    out.append(dict(item, table=table, set=s, format=event['path'].split('=')[-1]))
        return out
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        files = [e for result in pool.map(entries, tasks) for e in result]
    files += [e for e in get(API+'ref') if e['path'] in ('ref/cards.parquet', 'ref/card_names.parquet')]
    # card_attrs is deliberately not used: its undocumented representative-printing choices are not proof.
    return sorted(files, key=lambda f: f['path'])

def acquire():
    RAW.mkdir(parents=True, exist_ok=True)
    manifest = RAW / 'sources.json'
    files = inventory() if not manifest.exists() else json.loads(manifest.read_text())['files']
    total = sum(f['size'] for f in files)
    if total > 6 * 1024**3: raise RuntimeError(f'Pinned corpus exceeds 6 GiB acquisition budget: {total}')
    print(f'{len(files)} public files; {total/1024**3:.3f} GiB', flush=True)
    def fetch(item):
        target = RAW / item['path']; target.parent.mkdir(parents=True, exist_ok=True)
        expected = item['lfs']['oid']
        cached = target.exists() and sha(target) == expected
        start = time.perf_counter()
        if not cached:
            partial = target.with_suffix('.download')
            for attempt in range(4):
                try:
                    urllib.request.urlretrieve(DOWNLOAD + item['path'], partial)
                    if sha(partial) != expected: raise ValueError('Public LFS content hash mismatch')
                    partial.replace(target); break
                except (OSError, ValueError):
                    if attempt == 3: raise
                    time.sleep(2 ** attempt)
        return dict(item, url=DOWNLOAD+item['path'], sha256=expected,
                    bytes=target.stat().st_size, cacheHit=cached, seconds=time.perf_counter()-start)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        complete = []
        for item in pool.map(fetch, files):
            complete.append(item); print(item['path'], item['bytes'], 'cached' if item['cacheHit'] else 'downloaded', flush=True)
    result = dict(repository=REPO, revision=REVISION, license='CC-BY-4.0 per pinned README',
                  origin='Public 17Lands bulk dumps; third-party long-table Parquet transformation, independently spot-checked',
                  excludedTables=['targets', 'embeddings', 'replay_*', 'bench', 'reviews'], files=complete)
    manifest.write_text(json.dumps(result, indent=2))
    urllib.request.urlretrieve(f'https://huggingface.co/datasets/{REPO}/raw/{REVISION}/README.md', RAW/'SOURCE_README.md')
    print(manifest)

if __name__ == '__main__': acquire()
