import csv,json,pathlib
p=pathlib.Path('data/historical/raw')
for f in p.glob('*.csv'):
    with f.open(encoding='utf-8',newline='') as stream:
        reader=csv.DictReader(stream); row=next(reader)
    print(f.name,len(row),{k:v for k,v in row.items() if not k.startswith(('pack_card_','pool_'))})
data=json.loads((p/'public-index.json').read_text(encoding='utf-8'))
print('HELPERS',json.dumps(data['helper_files'])[:3000])
print('CARD LISTS',json.dumps(data['card_lists'])[:5000])
