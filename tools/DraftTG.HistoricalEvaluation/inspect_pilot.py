import csv,json,pathlib,sqlite3,collections
raw=pathlib.Path('data/historical/raw')
for f in sorted(raw.glob('*.draft_data.sample.csv')):
    with f.open(newline='',encoding='utf-8') as stream:
        rows=list(csv.DictReader(stream))
    first=rows[0]['draft_id'];r=[x for x in rows if x['draft_id']==first]
    print(f.name,'first-draft',[(x['pack_number'],x['pick_number'],sum(int(v) for k,v in x.items() if k.startswith('pool_')),sum(int(v) for k,v in x.items() if k.startswith('pack_card_'))) for x in r[:3]+r[13:17]+r[-3:]])
    print('total/max pick',len(rows),max(int(x['pick_number']) for x in rows))
summary=pathlib.Path('data/historical/evaluation/pilot/summary.json')
if summary.exists():
    j=json.loads(summary.read_text())
    for m in j['Datasets']:
        print(m['SetCode'],m['Format'],'identity issues',m['IdentityIssues'])
