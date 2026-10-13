"""Build explicit local metadata/stat snapshots from public CSV samples. No scoring or weights."""
import collections,csv,gzip,hashlib,json,os,pathlib

raw=pathlib.Path('data/historical/raw'); cache=pathlib.Path('data/historical/cache'); cache.mkdir(parents=True,exist_ok=True)
def file_hash(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()
sets={'WOE','LCI','MKM','OTJ','VOW'}
bonus={'WOE':['WOT'],'LCI':[],'MKM':['SPG'],'OTJ':['OTP','BIG','SPG'],'VOW':[]}
allowed=sets|{s for values in bonus.values() for s in values}
with (raw/'cards.csv').open(encoding='utf-8',newline='') as f:
    helper=list(csv.DictReader(f))
arena_ids={int(r['id']) for r in helper if r['expansion'].upper() in allowed}
catalog=[]
bulk=pathlib.Path(os.environ['LOCALAPPDATA'])/'DraftTG/card-data/scryfall-default-cards.jsonl.gz'
with gzip.open(bulk,'rt',encoding='utf-8') as f:
    for line in f:
        c=json.loads(line)
        if c.get('arena_id') in arena_ids and c.get('set','').upper() in allowed:
            catalog.append(c)
catalog.sort(key=lambda c:c['id'])
(cache/'catalog.json').write_text(json.dumps(catalog),encoding='utf-8')
snapshots=[]; observed=[]
for file in sorted(raw.glob('*.draft_data.sample.csv')):
    set_code,format_name=file.name.split('.')[:2]
    features=collections.defaultdict(lambda:collections.Counter())
    with file.open(encoding='utf-8',newline='') as f:
        reader=csv.DictReader(f)
        columns=reader.fieldnames
        packs=[(h,h[len('pack_card_'):]) for h in columns if h.startswith('pack_card_')]
        rows=0
        for row in reader:
            rows+=1; p=int(row['pick_number'])+1
            for column,name in packs:
                n=int(row[column])
                if n: features[name]['seen']+=n;features[name]['seen_position']+=p*n
            features[row['pick']]['taken']+=1;features[row['pick']]['taken_position']+=p
    game_file=raw/f'{set_code}.{format_name}.game_data.sample.csv'
    game_rows=0; total_wins=0
    if game_file.exists():
        with game_file.open(encoding='utf-8',newline='') as f:
            reader=csv.DictReader(f)
            game_columns=reader.fieldnames
            observed.append({'file':str(game_file),'columnCount':len(game_columns),'metadataColumns':[c for c in game_columns if not c.startswith(('deck_','opening_hand_','drawn_','tutored_','sideboard_'))]})
            names=[h[len('deck_'):] for h in game_columns if h.startswith('deck_')]
            for row in reader:
                game_rows+=1; won=int(row['won'].lower() in ['true','1']); total_wins+=won
                for name in names:
                    c=features[name]
                    if int(float(row.get('deck_'+name,'0')))>0: c['gp']+=1;c['gp_wins']+=won
                    opening=int(float(row.get('opening_hand_'+name,'0')))>0
                    drawn=int(float(row.get('drawn_'+name,'0')))>0
                    if opening: c['oh']+=1;c['oh_wins']+=won
                    if drawn: c['drawn']+=1;c['drawn_wins']+=won
                    if opening or drawn: c['gih']+=1;c['gih_wins']+=won
    cards=[]
    for name,c in sorted(features.items()):
        def rate(w,n): return c[w]/c[n] if c[n] else None
        cards.append({'SourceIdentity':name,'SetCode':set_code,'PrimaryFormat':format_name,
          'GIHWinRate':rate('gih_wins','gih'),'GIHGames':c['gih'] if c['gih'] else None,
          'GamesPlayedWinRate':rate('gp_wins','gp'),'GamesPlayed':c['gp'] if c['gp'] else None,
          'OpeningHandWinRate':rate('oh_wins','oh'),'DrawnWinRate':rate('drawn_wins','drawn'),
          'ALSA':None,'ATA':None,'DraftedCount':c['taken'],
          'SampleAverageSeenPosition':rate('seen_position','seen'),'SampleAverageTakenPosition':rate('taken_position','taken'),
          'SetBaseline':total_wins/game_rows if game_rows else None,'AvailableAt':None,
          'Period':'bounded historical prefix samples; not full-format all-time',
          'Provenance':f'Draft sha256={file_hash(file)}; game sha256={file_hash(game_file) if game_file.exists() else "unavailable"}; sample-derived union-of-opening/drawn GIH, average seen/taken positions.'})
    snapshots.append({'SetCode':set_code,'Format':format_name,'Regime':'MatureFormatReplay','AvailableAt':None,
       'Provenance':'Public sample-derived aggregate; overlaps replay drafts; chronological future information permitted only in explicitly labelled mature-format replay. Low samples; not full-format statistics.',
       'Cards':cards})
    observed.append({'file':str(file),'columnCount':len(columns),'metadataColumns':[c for c in columns if not c.startswith(('pack_card_','pool_'))],'rows':rows,'gameRows':game_rows})
(cache/'stats.json').write_text(json.dumps(snapshots),encoding='utf-8')
(cache/'observed-schemas.json').write_text(json.dumps(observed,indent=2),encoding='utf-8')
(cache/'identity-metadata.json').write_text(json.dumps({'publicHelperUri':'https://17lands-public.s3.amazonaws.com/analysis_data/cards/cards.csv',
  'helperSha256':file_hash(raw/'cards.csv'),'scryfallCacheSha256':file_hash(bulk),
  'filter':'Only exact Arena ids from public helper in declared sets/sheets; all matching printings retained, ambiguity not first-matched.',
  'catalogCards':len(catalog),'bonusSets':bonus},indent=2),encoding='utf-8')
print(json.dumps({'catalogCards':len(catalog),'snapshots':len(snapshots),'observed':observed},indent=2))
