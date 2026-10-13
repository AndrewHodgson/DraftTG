"""Owned-cache diagnostics; no new aggregate downloads and no reliability fitting."""
import dataclasses,gzip,json,os,pathlib,statistics
from phase_b1_core import *

def owned_identities():
    from phase_b_qualify import Identities,normalized_name
    identity=Identities(['FRA'])
    # The historical helper predates FRA. Current owned Arena metadata is the
    # appropriate scope for a current owned cache, not that old helper's ID list.
    path=pathlib.Path(os.environ['LOCALAPPDATA'])/'DraftTG/card-data/scryfall-default-cards.jsonl.gz'
    seen={c['id'] for c in identity.cards};added=0
    with gzip.open(path,'rt',encoding='utf-8') as stream:
        for line in stream:
            c=json.loads(line)
            if c['set'].upper()!='FRA' or c.get('arena_id') is None or c['id'] in seen:continue
            identity.cards.append(c);identity.full[normalized_name(c['name'])].append(c)
            if c.get('card_faces'):identity.front[normalized_name(c['card_faces'][0]['name'])].append(c)
            seen.add(c['id']);added+=1
    return identity,dict(metadataSha256=sha(path),addedCurrentArenaPrintings=added,historicalHelperNotUsedAsCurrentCacheWhitelist=True)

def cross_format():
    guard();base=pathlib.Path('data/historical/cache/phase-b1/owned');formats={f:load(base/f'FRA_{f}_ALL_TIME_v3.json') for f in ('PremierDraft','QuickDraft')}
    identity,metadata=owned_identities();tables={};baselines={};diagnostics={}
    for fmt,value in formats.items():
        table={};excluded=[]
        for row in value['Payload']['data']:
            mapping=identity.resolve('FRA',row['name']);cid=mapping['identity']['CanonicalId']
            if mapping['category']=='basic lands':excluded.append(row['name']);continue
            if cid is None:
                # Exact expansion/provider-name statistical identity is shared by these
                # two caches. It is not a claim of Oracle or printing identification.
                from phase_b_core import normalized_name
                cid='provider-stat:FRA:'+normalized_name(row['name'])
            if cid in table:raise ValueError('Conflicting logical cache identity')
            table[cid]=row
        tables[fmt]=table;valid=[r for r in table.values() if valid_rate(r.get('ever_drawn_win_rate')) and r.get('ever_drawn_game_count',0)>0]
        if not valid:
            dump(OUT/'cross-format.json',dict(pairs=1,qualifiedIdentityPairs=0,generalizedQualified=False,status='BLOCKED: owned current card metadata does not establish cache identities',identityAudit=metadata,excluded=excluded))
            return
        baselines[fmt]=sum(r['ever_drawn_win_rate']*r['ever_drawn_game_count'] for r in valid)/sum(r['ever_drawn_game_count'] for r in valid)
        diagnostics[fmt]=dict(statisticalIdentities=len(table),printingIdentities=sum(not k.startswith('provider-stat:') for k in table),excludedNames=excluded)
    matched=sorted(cid for cid in tables['PremierDraft'].keys()&tables['QuickDraft'].keys() if all(valid_rate(tables[f][cid].get('ever_drawn_win_rate')) and tables[f][cid].get('ever_drawn_game_count',0)>0 for f in tables))
    results=[]
    for alternate,primary in (('PremierDraft','QuickDraft'),('QuickDraft','PremierDraft')):
        for policy in ('neutral','reference','bounded','generalized-unqualified'):
            estimates=[];truth=[];predq=[];trueq=[];rawq=[];counts=[];masked=[]
            for cid in matched:
                p=tables[primary][cid];a=tables[alternate][cid];ba=baselines[alternate]
                # Mask the focal primary rate in its environment baseline as well.
                usable=[r for r in tables[primary].values() if valid_rate(r.get('ever_drawn_win_rate')) and r.get('ever_drawn_game_count',0)>0]
                total=sum(r['ever_drawn_game_count'] for r in usable);wins=sum(r['ever_drawn_game_count']*r['ever_drawn_win_rate'] for r in usable)
                bp=(wins-p['ever_drawn_game_count']*p['ever_drawn_win_rate'])/(total-p['ever_drawn_game_count'])
                source=sha(base/f'FRA_{primary}_ALL_TIME_v3.json');altsha=sha(base/f'FRA_{alternate}_ALL_TIME_v3.json')
                pe=QualityEvidence('FRA',primary,cid,None,p['ever_drawn_game_count'],SEMANTIC,'owned cache','ALL_TIME',True,source)
                ae=QualityEvidence('FRA',alternate,cid,a['ever_drawn_win_rate'],a['ever_drawn_game_count'],SEMANTIC,'owned alternate cache','ALL_TIME',False,altsha)
                result=prior(pe,None if policy in ('neutral','generalized-unqualified') else ae,bp,ba,allowed_direction=(alternate,primary),bound=.08 if policy=='bounded' else None)
                n=p['ever_drawn_game_count'];estimates.append(result.effective_rate);truth.append(p['ever_drawn_win_rate']);predq.append(result.q)
                trueq.append(q((n*p['ever_drawn_win_rate']+500*bp)/(n+500),bp));rawq.append(q(p['ever_drawn_win_rate'],bp));counts.append(n);masked.append(bp)
            m=metrics(estimates,truth);m.update(QMAE=statistics.mean(abs(p-t) for p,t in zip(predq,trueq)),rawRelativeQMAE=statistics.mean(abs(p-t) for p,t in zip(predq,rawq)),signAccuracy=statistics.mean((p>b)==(t>b) for p,t,b in zip(estimates,truth,masked)))
            def ranks(values):return [(sum(v<x for v in values)+.5*sum(v==x for v in values))/len(values) for x in values]
            m['catastrophicRankFraction']=statistics.mean(abs(p-t)>=.5 for p,t in zip(ranks(estimates),ranks(truth)))
            m['sampleBuckets']={f'{lo}-{hi}':metrics([p for p,n in zip(estimates,counts) if lo<=n<hi],[t for t,n in zip(truth,counts) if lo<=n<hi]) for lo,hi in ((0,1000),(1000,5000),(5000,100000000))}
            results.append(dict(set='FRA',direction=alternate+'->'+primary,policy=policy,**m))
    p=[tables['PremierDraft'][cid]['ever_drawn_win_rate'] for cid in matched];a=[tables['QuickDraft'][cid]['ever_drawn_win_rate'] for cid in matched]
    dump(OUT/'cross-format.json',dict(pairs=1,qualifiedIdentityPairs=1,targetPairs=5,generalizedQualified=False,metadataAudit=metadata,baselines=baselines,identityAudit=diagnostics,matchedCards=len(matched),
        rateCorrelation=correlation(p,a),rankCorrelation=rank_correlation(p,a),residuals={str(qt):quantile([x-y for x,y in zip(p,a)],qt) for qt in (.05,.5,.95)},results=results,
        cacheHashes={f:sha(base/f'FRA_{f}_ALL_TIME_v3.json') for f in formats},
        maskedPrimaryBaseline=True,qTarget='Held true primary recomposed with k500 using focal-card-excluded primary baseline; raw-relative Q error also reported',
        interpretation='Owned caches only, no new provider aggregate request. Exact same-expansion provider names are statistical identities, not recovered printings/Oracle IDs. One previously observed set; observational populations and ALL_TIME windows. Leave-primary-rate-out diagnostics, not multi-set validation or fitted reliability',
        policies=dict(reference='.9*n_alt/(n_alt+500)*alternate delta',bounded='Reference delta clipped to +/- .08',generalized='Declines to neutral; fewer than five independent paired sets')))

def golden():
    guard();regression=load('tests/Fixtures/phase9e2b-p1p8-v1.6.2.json');provider=load('tests/Fixtures/phase9f2-live-pack-stat-provenance.json');altfixture=load('tests/Fixtures/phase9f2-live-pack-v1.6.1.json')
    primary={r['name']:r for r in provider['ProviderCache']['Payload']['data']};alternate={r['name']:r for r in altfixture['AlternateProviderCache']['Payload']['data']}
    bp=provider['ExpectedBaseline'];ba=altfixture['AlternateBaseline'];p=primary['Uldaros Theorix'];a=alternate['Uldaros Theorix']
    result=prior(QualityEvidence('fixture-set','QuickDraft','fixture-card',p['ever_drawn_win_rate'],p['ever_drawn_game_count'],SEMANTIC,'primary fixture','ALL_TIME',True,'frozen primary'),
        QualityEvidence('fixture-set','PremierDraft','fixture-card',a['ever_drawn_win_rate'],a['ever_drawn_game_count'],SEMANTIC,'alternate fixture','ALL_TIME',False,'frozen alternate'),bp,ba,allowed_direction=('PremierDraft','QuickDraft'))
    expected=regression['UldarosNonRegression'][0]['After'];w=expected['Weights'];effective=.5+expected['QualityRelevance']*(result.q-.5) if result.q>.5 else result.q
    weighted=w['Quality']*effective+w['Lane']*expected['LaneComponent']+w['Archetype']*expected['ArchetypeComponent']+w['DeckNeed']*expected['DeckNeedComponent']
    exact=50*soft_limit(.5+1.4*(weighted-.5),.45)
    if abs(result.q-expected['QualityComponent'])>1e-11 or abs(exact-50*expected['ContextualValue'])>1e-9:raise ValueError('Uldaros portability regression')
    uns=next(c for c in regression['Cards'] if c['Name']=='Unsummon')['After']
    dump(OUT/'golden-portability.json',dict(Uldaros=dataclasses.asdict(result),productionQ=expected['QualityComponent'],productionExact=50*expected['ContextualValue'],portableExact=exact,passed=True,
        goldenUsedForFit=False,formula='Generic identity/set/direction contract; preregistered reference .9/500, no FRA/card branch',
        Unsummon=dict(commitment=uns['ColorCommitment']['Strength'],relevance=uns['QualityRelevance'],playability=uns['DeckImpact']['Playability'],productionQ=uns['QualityComponent'],productionExact=50*uns['ContextualValue'],unchanged=True)))

if __name__=='__main__':cross_format();golden()
