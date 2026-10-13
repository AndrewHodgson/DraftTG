"""Bounded public-data acquisition. No account endpoints; truncated gzip prefixes are explicit samples."""
import argparse, csv, gzip, hashlib, io, json, pathlib, urllib.parse, urllib.request, time

def get_json(url):
    with urllib.request.urlopen(url, timeout=40) as response:
        return json.load(response)

def index():
    root = 'https://17lands.cdn.prismic.io/api/v2'
    api = get_json(root)
    ref = next(r['ref'] for r in api['refs'] if r['isMasterRef'])
    url = root + '/documents/search?' + urllib.parse.urlencode({'ref': ref, 'q': '[[at(document.type, "public-data")]]'})
    return get_json(url)['results'][0]['data']

def links(data):
    for row in data['datasets']:
        def text(field):
            return ''.join(x.get('text', '') for x in row[field]) if isinstance(row[field], list) else row[field]
        yield text('expansion'), text('format'), row

def sample(url, target, maximum_rows, compressed_limit):
    started = time.perf_counter()
    request = urllib.request.Request(url, headers={'User-Agent': 'DraftTG-PhaseA-public-sample/1.0', 'Range': f'bytes=0-{compressed_limit-1}'})
    with urllib.request.urlopen(request, timeout=60) as response:
        compressed = response.read(compressed_limit)
        status, headers = response.status, dict(response.headers)
    # zlib accepts a deliberately truncated gzip stream; retain only complete CSV records.
    import zlib
    raw = zlib.decompressobj(16 + zlib.MAX_WBITS).decompress(compressed,128*1024*1024)
    container = 'gzip CSV'
    if raw[257:262] == b'ustar':
        raw = raw[512:]
        container = 'gzip tar; first CSV member'
    decoded = raw.decode('utf-8', errors='strict')
    lines = decoded.splitlines(keepends=True)
    if lines and not lines[-1].endswith('\n'):
        lines.pop()
    rows = list(csv.reader(lines[:maximum_rows + 1]))
    with target.open('w', newline='', encoding='utf-8') as output:
        csv.writer(output).writerows(rows)
    return {'url': url, 'httpStatus': status, 'responseHeaders': headers, 'sourcePrefixBytes': len(compressed),
            'sourcePrefixSha256': hashlib.sha256(compressed).hexdigest(), 'decompressedPrefixLimit':128*1024*1024,
            'rows': len(rows)-1, 'columns': rows[0], 'sha256': hashlib.sha256(target.read_bytes()).hexdigest(), 'container': container,
            'bytes': target.stat().st_size, 'seconds': time.perf_counter()-started,
            'sampling': 'First complete CSV rows from bounded compressed prefix; not random or representative; final draft may be truncated.'}

if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--output', default='data/historical/raw')
    p.add_argument('--rows', type=int, default=1500)
    p.add_argument('--prefix-bytes', type=int, default=4*1024*1024)
    p.add_argument('--sets', nargs='+', default=['WOE', 'LCI', 'MKM', 'OTJ'])
    p.add_argument('--list', action='store_true')
    p.add_argument('--kind', choices=['draft_data','game_data'], default='draft_data')
    args = p.parse_args()
    data = index()
    out = pathlib.Path(args.output); out.mkdir(parents=True, exist_ok=True)
    (out/'public-index.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
    entries = list(links(data))
    if args.list:
        print(json.dumps([{'set': s, 'format': f, 'links': row} for s,f,row in entries if s in args.sets], indent=2))
    else:
        manifest = []
        for set_code, format_name, row in entries:
            if set_code not in args.sets or format_name not in ['PremierDraft', 'QuickDraft']:
                continue
            field = row.get(args.kind, {})
            url = field.get('url') if isinstance(field, dict) else next((span['data']['url'] for item in field for span in item.get('spans', []) if span['type'] == 'hyperlink'), None)
            if not url:
                continue
            path = out/f'{set_code}.{format_name}.{args.kind}.sample.csv'
            result = sample(url, path, args.rows, args.prefix_bytes)
            result.update({'set': set_code, 'format': format_name, 'file': str(path)})
            manifest.append(result)
            print(json.dumps({k:v for k,v in result.items() if k not in ['columns','responseHeaders']}), flush=True)
        (out/f'acquisition-{args.kind}-{"-".join(args.sets)}.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
