"""Post-selection sanity checks using existing authorized regression fixtures, never calibration targets."""
import json,pathlib,statistics
from phase_b_core import *
from phase_b_source import sha

def golden():
    output=pathlib.Path('docs/evidence/v2-phase-b');selection=json.loads((output/'selection.json').read_text())
    fixture=pathlib.Path('tests/Fixtures/phase9e2b-p1p8-v1.6.2.json');cache=pathlib.Path('tests/Fixtures/phase9f2-live-pack-stat-provenance.json')
    regression=json.loads(fixture.read_text(encoding='utf-8-sig'));provider=json.loads(cache.read_text(encoding='utf-8-sig'))
    rows=provider['ProviderCache']['Payload']['data'];lookup={r['name']:r for r in rows};b=provider['ExpectedBaseline']
    spread=statistics.pstdev(r['ever_drawn_win_rate'] for r in rows if valid_rate(r.get('ever_drawn_win_rate')) and r.get('ever_drawn_game_count',0)>0)
    result=[]
    for card in regression['Cards']:
        baseline=card['After'];row=lookup.get(card['Name'],{});qualities={}
        p=row.get('ever_drawn_win_rate');n=row.get('ever_drawn_game_count') or 0
        for c in selection['candidates']:
            q=.5
            if c['id'] in ('A','B') and valid_rate(p) and n>0:
                q=quality(shrink(p,n,b,c['k']),b,spread,'standardized' if c['id']=='B' else 'delta')
            r=baseline['QualityRelevance'];effective=.5+r*(q-.5) if q>.5 else q
            w=baseline['Weights'];weighted=w['Quality']*effective+w['Lane']*baseline['LaneComponent']+w['Archetype']*baseline['ArchetypeComponent']+w['DeckNeed']*baseline['DeckNeedComponent']
            qualities[c['id']]=dict(Q=q,R=r,Qeffective=effective,exactScore=50*soft_limit(.5+1.4*(weighted-.5),.45),
                evidence='Illustrative transport of existing published aggregate/count; copy/presence semantics not proven equivalent' if c['id'] in ('A','B') and valid_rate(p) else 'Neutral: required direct/independent stratum evidence unavailable')
        # Check recomposition itself against the authoritative frozen regression fixture.
        w=baseline['Weights'];value=w['Quality']*baseline['EffectiveQualityComponent']+w['Lane']*baseline['LaneComponent']+w['Archetype']*baseline['ArchetypeComponent']+w['DeckNeed']*baseline['DeckNeedComponent']
        if abs(50*soft_limit(.5+1.4*(value-.5),.45)-50*baseline['ContextualValue'])>1e-10:raise ValueError('Golden recomposition differs from production')
        result.append(dict(name=card['Name'],productionQ=baseline['QualityComponent'],productionExact=50*baseline['ContextualValue'],productionRank=baseline['CurrentPackRank'],
            commitmentStrength=baseline['ColorCommitment']['Strength'] if baseline['ColorCommitment'] else None,playability=baseline['DeckImpact']['Playability'],candidates=qualities))
    for candidate in ('A','B','C'):
        ordered=sorted(result,key=lambda r:-r['candidates'][candidate]['exactScore'])
        for rank,r in enumerate(ordered,1):r['candidates'][candidate]['rank']=rank
    uld=regression['UldarosNonRegression'][0]['After']
    summary=dict(sourceFixtures={str(fixture):sha(fixture),str(cache):sha(cache)},usedForFit=False,parametersFrozen=True,
        primaryBaseline=b,aggregateSpread=spread,P1P8=[r for r in result if r['name'] in ('Unsummon','Arcane Amphisbaena')],
        Uldaros=dict(name='Uldaros Theorix',productionQ=uld['QualityComponent'],productionExact=50*uld['ContextualValue'],
            candidates={c:dict(Q=.5,exactScore=25.,reason='Missing usable direct FRA QuickDraft GIH; these research specifications do not retain the existing narrow alternate-format prior') for c in ('A','B','C')},
            portabilityGate='FAIL: cannot replace production Q with primary-only research missing-data rules; frozen production still retains its FRA alternate prior'),
        interpretation='No new provider requests; known regression fixtures only. Aggregate portability sensitivity, not raw game-presence proof. C cannot reconstruct disjoint OH/drawn strata from aggregate marginal rates. No retuning after these sanity checks.')
    (output/'golden-sanity.json').write_text(json.dumps(summary,indent=2));print(json.dumps(summary,indent=2))

if __name__=='__main__':golden()
