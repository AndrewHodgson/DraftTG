import collections,json,pathlib
p=pathlib.Path('data/historical/evaluation/phase-a/decisions.jsonl')
reasons=collections.Counter();examples=[]
with p.open(encoding='utf-8') as stream:
    for line in stream:
        row=json.loads(line);model=row['Models'][-1]
        if model['Agreement']['Rank'] is None:
            if model['UnscorableCards']:
                why=model['MissingInputs'][-1] if model['Cards'] else model['MissingInputs'][0]
            else:
                why='Unranked actual pick / quantity mismatch'
            reasons[why]+=1
            if row['SetCode']=='WOE' and len(examples)<2:
                examples.append({'SetCode':row['SetCode'],'Pack':row['PackNumber'],'Pick':row['PickNumber'],'Label':row['AnalysisOnly']['Label'],'Cards':[{k:c[k] for k in ['CardIdentifier','ContextualValue','CurrentPackRank']} for c in model['Cards']],'Agreement':model['Agreement']})
print(json.dumps({'reasons':dict(reasons),'examples':examples},indent=2)[:9000])
