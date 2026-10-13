"""Qualify deterministic whole-draft samples and export the EXISTING canonical decision schema.

Qualification may handle holdout labels for storage, but cannot calculate their performance.
No provider aggregate ratings are fetched: current public API disallows outside use.
"""
import argparse, collections, csv, gzip, heapq, json, os, pathlib, sys, time
from phase_b_source import RAW, ROOT, TABLES, sha
from phase_b_core import *
sys.path.insert(0,str(ROOT/'cache/phase-b/python'))
import pyarrow as pa
import pyarrow.compute as pc
import pyarrow.parquet as pq

OUT=ROOT/'normalized/phase-b-v1'
EVIDENCE=pathlib.Path('docs/evidence/v2-phase-b')
PICK_DRAFTS=700
GAME_DRAFTS=8000

def dump(path,value):
    path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(value,indent=2,default=str),encoding='utf-8')

def source_path(table,s,f):return RAW/table/f'expansion={s}'/f'event={f}'/'data.parquet'

def select_drafts(path,s,f,count):
    ids=set()
    for batch in pq.ParquetFile(path).iter_batches(columns=['draft_id'],batch_size=131072):
        ids.update(pc.unique(batch.column(0)).to_pylist())
    return heapq.nsmallest(count,ids,key=lambda d:group_hash(s,f,d)),len(ids)

def filter_table(path,ids,columns=None):
    wanted=pa.array(ids); chunks=[]
    for batch in pq.ParquetFile(path).iter_batches(columns=columns,batch_size=131072):
        table=pa.Table.from_batches([batch]); keep=pc.is_in(table['draft_id'],value_set=wanted)
        filtered=table.filter(keep)
        if filtered.num_rows:chunks.append(filtered)
    if not chunks:return pa.table({})
    return pa.concat_tables(chunks)

GAMEPLAY=('name','oracle_id','layout','mana_cost','cmc','colors','color_identity','type_line','oracle_text','power','toughness','loyalty','card_faces','keywords','produced_mana')
FACE_FIELDS=('name','mana_cost','cmc','colors','type_line','oracle_text','power','toughness','loyalty')
def semantics(card):
    value={k:card.get(k) for k in GAMEPLAY}
    value['card_faces']=[{k:f.get(k) for k in FACE_FIELDS} for f in card.get('card_faces',[])]
    return value

class Identities:
    def __init__(self,sets):
        self.policy=json.loads(pathlib.Path('docs/historical/phase-b-identity-policy.json').read_text())
        helper=list(csv.DictReader(open(ROOT/'raw/cards.csv',encoding='utf-8')))
        helper_ids={int(x['id']) for x in helper}
        allowed=set(sets)|{s for v in self.policy['BonusSets'].values() for s in v}
        listnames={normalized_name(x) for x in self.policy['MKMListNames']}
        self.cards=[];self.full=collections.defaultdict(list);self.front=collections.defaultdict(list)
        self.map={};self.catalog={}
        bulk=pathlib.Path(os.environ['LOCALAPPDATA'])/'DraftTG/card-data/scryfall-default-cards.jsonl.gz'
        with gzip.open(bulk,'rt',encoding='utf-8') as stream:
            for line in stream:
                c=json.loads(line)
                if c.get('arena_id') not in helper_ids:continue
                if c['set'].upper() not in allowed and normalized_name(c['name']) not in listnames:continue
                self.cards.append(c);self.full[normalized_name(c['name'])].append(c)
                if c.get('card_faces'):self.front[normalized_name(c['card_faces'][0]['name'])].append(c)
        self.provenance=dict(helperSha256=sha(ROOT/'raw/cards.csv'),scryfallSha256=sha(bulk),policySha256=sha('docs/historical/phase-b-identity-policy.json'),metadataCards=len(self.cards))
    def resolve(self,s,name):
        key=s+'|'+name
        if key in self.map:return self.map[key]
        n=normalized_name(name);allowed={s}|set(self.policy['BonusSets'].get(s,[]))
        candidates=self.full.get(n,[])+self.front.get(n,[])
        scoped=[c for c in candidates if c['set'].upper()==s]
        if not scoped:scoped=[c for c in candidates if c['set'].upper() in allowed]
        list_rule=s=='MKM' and n in {normalized_name(x) for x in self.policy['MKMListNames']}
        if not scoped and list_rule:scoped=candidates
        scoped={c['id']:c for c in scoped};candidate_ids=sorted(scoped)
        variants={json.dumps(semantics(c),sort_keys=True) for c in scoped.values()}
        oracle_ids={c.get('oracle_id') for c in scoped.values()}
        category='genuinely ambiguous'
        if name.startswith('A-'):category='rebalanced/A- variants'
        elif any('Basic Land' in c.get('type_line','') for c in scoped.values()):category='basic lands'
        elif any(c.get('layout') in ('token','double_faced_token','emblem') for c in scoped.values()):category='tokens/non-draft objects'
        elif list_rule:category='explicit MKM Arena List'
        elif scoped and all(c['set'].upper()!=s for c in scoped.values()):category='special guest' if any(c['set']=='spg' for c in scoped.values()) else 'bonus sheet'
        elif any(c.get('card_faces') for c in scoped.values()):category='split/adventure cards' if any(c.get('layout') in ('adventure','split') for c in scoped.values()) else 'DFC naming'
        elif len(scoped)>1:category='same-name printings'
        elif scoped:category='exact/normalized name'
        else:category='set/schema discrepancy or unavailable Arena metadata'
        resolved=bool(scoped) and len(oracle_ids)==1 and None not in oracle_ids and len(variants)==1 and category!='tokens/non-draft objects'
        cid=None;printing=None
        if resolved:
            if len(scoped)==1 and not (list_rule and all(c['set'].upper()!=s for c in scoped.values())):
                cid=candidate_ids[0];c=scoped[cid];printing=c['set'].upper();self.catalog[cid]=c
                rule='Exact declared Arena printing'+('; publisher MKM List exception' if list_rule else '')
            else:
                oracle=next(iter(oracle_ids));cid=f'historical:{s.lower()}:{oracle}'
                # All values are consensus-equal. Construct logical gameplay metadata, not a chosen printing.
                c=json.loads(next(iter(variants)));c.update(id=cid,set=s.lower(),collector_number='logical-'+oracle,
                    rarity='common',object='card',arena_id=None)
                rarities={x['rarity'] for x in scoped.values()}
                if len(rarities)==1:c['rarity']=next(iter(rarities))
                self.catalog[cid]=c;printing=s
                rule='Unanimous oracle/gameplay consensus; original printing remains ambiguous; explicit research logical identity'
        else:rule='Declined: no unique oracle/gameplay identity in explicitly declared Arena scope'
        identity=dict(SourceIdentity=name,CanonicalId=cid,PrintingSet=printing,Status='ExplicitIdentifier' if resolved else 'Ambiguous' if scoped else 'Unresolved',Rule=rule,Candidates=candidate_ids)
        self.map[key]=dict(identity=identity,category=category,rarity=self.catalog[cid]['rarity'] if cid else None,
                           oracleId=next(iter(oracle_ids)) if resolved else None,printings=candidate_ids,consensus=resolved and len(scoped)>1)
        return self.map[key]
    def quantity(self,s,name,count):return dict(Card=self.resolve(s,name)['identity'],Count=int(count))

def decision_key(r):return r['draft_id'],int(r['pack_number']),int(r['pick_number'])
def game_key(r,match):return (r['draft_id'],r['build_index'],r.get('match_number') if match else None,r['game_number'])

def qualify_picks(s,f,identity,sources):
    start=time.perf_counter();path=source_path('picks',s,f)
    ids,source_drafts=select_drafts(path,s,f,PICK_DRAFTS)
    picks=filter_table(path,ids).to_pylist();picks.sort(key=decision_key)
    packs=collections.defaultdict(list);pools=collections.defaultdict(list)
    for table,dest,quantity in [('pack_cards',packs,'pack_card'),('pool_cards',pools,'pool')]:
        for r in filter_table(source_path(table,s,f),ids).to_pylist():
            dest[decision_key(r)].append((r['card'],r[quantity]))
    maximum=max(r['pick_number'] for r in picks)+1
    waterfall=collections.Counter(imported=len(picks));issues=collections.Counter();categories=collections.Counter()
    positions=collections.defaultdict(lambda:dict(seen=collections.Counter(),taken=collections.Counter(),packs=collections.defaultdict(collections.Counter)))
    identity_total=identity_good=0;previous=collections.defaultdict(list);prevpack=collections.defaultdict(dict)
    normalized=OUT/f'{s}.{f}.decisions.jsonl.gz';subset=OUT/f'{s}.{f}.replay.jsonl.gz'
    replay_drafts=set(sorted(ids,key=lambda d:group_hash(s,f,d))[:8])
    with gzip.open(normalized,'wt',encoding='utf-8',compresslevel=1) as out,gzip.open(subset,'wt',encoding='utf-8',compresslevel=1) as replay:
        for ordinal,r in enumerate(picks,1):
            key=decision_key(r);draft,pack,pick=key;offer=sorted(packs[key]);pool=sorted(pools[key]);global_pick=pack*maximum+pick
            schema_valid=0<=pack<=2 and 0<=pick<maximum and r['expansion']==s and r['event_type']==f
            schema_valid &= all(isinstance(n,int) and n>0 for name,n in offer+pool)
            if not schema_valid:issues['schema']+=1;continue
            waterfall['validSchema']+=1
            offered_valid=bool(offer) and len({name for name,n in offer})==len(offer) and r['pick'] in {name for name,n in offer} and sum(n for _,n in offer)<=maximum-pick
            if not offered_valid:issues['offeredPack']+=1;continue
            waterfall['validOfferedPack']+=1
            all_ok=True
            for name,n in offer+pool+[(r['pick'],1)]:
                mapping=identity.resolve(s,name);identity_total+=n;categories[mapping['category']]+=n
                if mapping['identity']['CanonicalId']:identity_good+=n
                else:all_ok=False;issues['identity:'+mapping['category']]+=n
            poolcomplete=sum(n for _,n in pool)==global_pick
            if all_ok:waterfall['identitiesResolved']+=1
            has_inputs=all_ok and poolcomplete and pick<14 and source_path('games',s,f).exists()
            if has_inputs:waterfall['scoreInputsAvailable']+=1
            else:
                if not all_ok:issues['identityDecision']+=1
                elif not poolcomplete:issues['incompletePool']+=1
                elif pick>=14:issues['unsupportedPick15']+=1
                else:issues['noGameStatistics']+=1
            count=sum(n for _,n in offer)
            eligible=has_inputs and count>=2
            if eligible:waterfall['atLeastTwoChoices']+=1;waterfall['eligible']+=1
            elif has_inputs:issues['singleton']+=1
            for name,n in offer:
                positions[name]['seen'][pick+1]+=n;positions[name]['packs'][pack+1][pick+1]+=n
            positions[r['pick']]['taken'][pick+1]+=1
            inputs=dict(SetCode=s,Format=f,DraftId=draft,DraftTimestamp=r['draft_time'].isoformat()+'Z',PackNumber=pack+1,PickNumber=pick+1,
                GlobalPickIndex=global_pick,OfferedCards=[identity.quantity(s,name,n) for name,n in offer],
                PoolBeforePick=[identity.quantity(s,name,n) for name,n in pool],PriorPicks=previous[draft].copy(),PoolIsComplete=poolcomplete)
            wins=r.get('event_match_wins');losses=r.get('event_match_losses');terminal=wins==7 or losses==3
            record=dict(DatasetVersion='historical-decision-v1',DatasetId=f'phase-b-{s}-{f}',Inputs=inputs,
                Label=dict(ActualPickedCard=identity.resolve(s,r['pick'])['identity'],ObservedMaindeckRate=r.get('pick_maindeck_rate'),ObservedSideboardInRate=r.get('pick_sideboard_in_rate')),
                Outcome=dict(FinalWins=wins if terminal else None,FinalLosses=losses if terminal else None,ReportedEventWins=wins,ReportedEventLosses=losses,
                             OutcomeEvidence='Terminal Bo1 record' if terminal else 'Reported record, completion unproven'),
                Provenance=dict(Provider='17LandsPublic',Uri=sources['picks']['url'],Sha256=sources['picks']['sha256'],Row=ordinal,Schema='public-parquet-to-historical-v1; sample ordinal, source key retained'),
                PlayerMetadata=dict(SourceRank=r.get('rank',r.get('user_rank')),LifetimeBuckets={k:str(v) for k,v in r.items() if k.startswith('user_') and k not in ('user_rank',) and v is not None},TemporalScope='Source lifetime bucket; pick-time availability not proven; analysis only'))
            line=json.dumps(record,separators=(',',':'))+'\n';out.write(line)
            if draft in replay_drafts:replay.write(line)
            previous[draft].append(dict(Pack=pack+1,Pick=pick+1,Card=record['Label']['ActualPickedCard']))
        # Labels/history never enter the inputs of an earlier decision.
    features=[]
    for name,v in sorted(positions.items()):
        seen=position_features(v['seen']);taken=position_features(v['taken'])
        features.append(dict(set=s,format=f,card=name,SeenPosition=seen,TakenPosition=taken,
            packSpecificSeen={str(k):position_features(h) for k,h in v['packs'].items()}))
    seen_means=[x['SeenPosition']['mean'] for x in features if x['SeenPosition']['mean'] is not None]
    taken_means=[x['TakenPosition']['mean'] for x in features if x['TakenPosition']['mean'] is not None]
    expectation=statistics.mean(seen_means)
    for x in features:
        x.update(SeenPositionPercentile=percentile(x['SeenPosition']['mean'],seen_means),TakenPositionPercentile=percentile(x['TakenPosition']['mean'],taken_means),
                 SeenLaterThanSetExpectation=x['SeenPosition']['mean']-expectation if x['SeenPosition']['mean'] is not None else None)
    dump(OUT/f'{s}.{f}.positions.json',features)
    summary=dict(set=s,format=f,sourceRows=pq.ParquetFile(path).metadata.num_rows,sourceDrafts=source_drafts,sampledDrafts=len(ids),
        waterfall=dict(waterfall),exclusions=dict(issues),identityOccurrences=identity_total,resolvedOccurrences=identity_good,
        identityCategories=dict(categories),picksPerPack=maximum,sourceFiles=sources,normalizedBytes=normalized.stat().st_size,
        seconds=time.perf_counter()-start,minimumDate=min(r['draft_time'] for r in picks),maximumDate=max(r['draft_time'] for r in picks),
        replayDrafts=len(replay_drafts),sampling='Lowest SHA256 whole-draft IDs across full source, fixed 700/dataset; no outcome selection',
        eligibility='Potential frozen-engine rankability: complete pack/pool identities, complete inventory, supported pick coordinate, exact-format games available, >=2 occurrences. Actual score availability checked separately by production replay subset.')
    return summary

def qualify_wide_games(s,f,identity,acquisition,declined):
    start=time.perf_counter();path=pathlib.Path(acquisition['file']);observations=[];decks=collections.defaultdict(list);gameinfo={};ids=set();bad=0
    with path.open(encoding='utf-8',newline='') as stream:
        reader=csv.DictReader(stream);names=[x[5:] for x in reader.fieldnames if x.startswith('deck_')]
        for ordinal,r in enumerate(reader,1):
            draft=r['draft_id'];ids.add(draft);key=(draft,int(r['build_index']),None,ordinal);won=int(r['won'].lower() in ('true','1'))
            gameinfo[key]=dict(won=won,opening=int(r['game_number'])==1)
            for name in names:
                try:counts={kind:int(float(r.get(kind+'_'+name,0))) for kind in ('deck','sideboard','opening_hand','drawn','tutored')}
                except (ValueError,TypeError):bad+=1;continue
                if any(v<0 for v in counts.values()):bad+=1;continue
                if not any(counts.values()):continue
                mapping=identity.resolve(s,name)
                observations.append(dict(draft=draft,game=json.dumps(key,separators=(',',':')),card=name,won=won,
                    opening=counts['opening_hand'],drawn=counts['drawn'],tutored=counts['tutored'],deck=counts['deck'],sideboard=counts['sideboard'],
                    evidence=evidence_group(s,f,draft),resolved=mapping['identity']['CanonicalId'] is not None,
                    rarity=mapping['rarity'],rank=r.get('rank'),colors=r.get('main_colors'),build=int(r['build_index'])))
                if counts['deck']:decks[key].append((name,counts['deck']))
    normalized=OUT/f'{s}.{f}.observations.parquet';pq.write_table(pa.Table.from_pylist(observations),normalized,compression='zstd')
    buildgames=collections.defaultdict(list)
    for key,deck in decks.items():buildgames[key[0]].append((key,tuple(sorted(deck))))
    builds=collections.Counter();primary=[]
    for draft,entries in buildgames.items():
        configs=collections.Counter(sig for key,sig in entries);eligible={sig:n for sig,n in configs.items() if sum(q for _,q in sig)==40}
        builds['draftsWithObservedDeck']+=1;builds['observedConfigurations']+=len(configs)
        builds['gameDecksExactly40']+=sum(eligible.values());builds['non40GameDecks']+=len(entries)-sum(eligible.values())
        if not eligible:continue
        maximum=max(eligible.values());modal=[sig for sig,n in eligible.items() if n==maximum]
        if len(modal)!=1:builds['ambiguousModal40']+=1;continue
        builds['observedPrimaryBuild']+=1
        opening=[sig for key,sig in entries if gameinfo[key]['opening']]
        agree=len(opening)==1 and opening[0]==modal[0];builds['openingObservedAndModalAgree']+=int(agree)
        primary.append(dict(DraftId=draft,MainDeck=[identity.quantity(s,name,n) for name,n in modal[0]],
            Evidence='Unique modal observed 40-card configuration in bounded wide CSV prefix; not proven final submitted or pre-sideboard deck',
            Games=len(entries),Wins=sum(gameinfo[key]['won'] for key,sig in entries),Configurations=len(configs),OpeningObservedAgreement=agree))
    with gzip.open(OUT/f'{s}.{f}.primary-builds.jsonl.gz','wt',encoding='utf-8',compresslevel=1) as stream:
        for r in primary:stream.write(json.dumps(r,separators=(',',':'))+'\n')
    return dict(set=s,format=f,sourceRows=acquisition['rows'],sourceDrafts=len(ids),sampledDrafts=len(ids),sampledGames=len(gameinfo),uniquelyJoinableGames=len(gameinfo),
        ambiguousGameKeys=0,unjoinedCardRows=0,invalidCardRows=bad,duplicateCardKeys=0,cardGameObservations=len(observations),sourceFiles=acquisition,
        builds=dict(builds),normalizedBytes=normalized.stat().st_size,seconds=time.perf_counter()-start,
        keySemantics='Atomic source wide CSV row; source ordinal retained. Repeated game numbers are not joined/deduplicated across ambiguous keys.',
        sampling=acquisition['sampling'],declinedParquetJoinAudit=declined,
        outcomeBoundary='Qualification stores labels but computes no holdout performance')

def qualify_games(s,f,identity,sources):
    start=time.perf_counter();path=source_path('games',s,f)
    ids,totaldrafts=select_drafts(path,s,f,GAME_DRAFTS);games=filter_table(path,ids).to_pylist()
    match='match_number' in pq.ParquetFile(path).schema_arrow.names
    indexed=collections.defaultdict(list)
    for r in games:indexed[game_key(r,match)].append(r)
    ambiguous={k for k,v in indexed.items() if len(v)!=1}
    valid={k:v[0] for k,v in indexed.items() if k not in ambiguous}
    schema=pq.ParquetFile(source_path('game_cards',s,f)).schema_arrow
    if ('match_number' in schema.names)!=match:raise ValueError('Game/card key schema mismatch')
    observations=[];unjoined=badcounts=0;buildgames=collections.defaultdict(list);decks=collections.defaultdict(list);duplicate_card_keys=set();seen_keys=set()
    for batch in filter_table(source_path('game_cards',s,f),ids).to_batches(max_chunksize=65536):
        for r in batch.to_pylist():
            key=game_key(r,match);g=valid.get(key)
            if g is None:unjoined+=1;continue
            if any(r[k] is None or r[k]<0 for k in ('deck','sideboard','opening_hand','drawn','tutored')):badcounts+=1;continue
            cardkey=key+(r['card'],)
            if cardkey in seen_keys:duplicate_card_keys.add(cardkey);continue
            seen_keys.add(cardkey);mapping=identity.resolve(s,r['card'])
            observations.append(dict(draft=r['draft_id'],game=json.dumps(key,separators=(',',':')),card=r['card'],won=int(g['won']),
                opening=r['opening_hand'],drawn=r['drawn'],tutored=r['tutored'],deck=r['deck'],sideboard=r['sideboard'],
                evidence=evidence_group(s,f,r['draft_id']),resolved=mapping['identity']['CanonicalId'] is not None,
                rarity=mapping['rarity'],rank=g.get('rank'),colors=g.get('main_colors'),build=r['build_index']))
            if r['deck']>0:decks[key].append((r['card'],r['deck']))
    bad_games={k[:-1] for k in duplicate_card_keys}
    if bad_games:
        observations=[o for o in observations if tuple(json.loads(o['game'])) not in bad_games]
    normalized=OUT/f'{s}.{f}.observations.parquet';pq.write_table(pa.Table.from_pylist(observations),normalized,compression='zstd')
    for key,deck in decks.items():
        if key in bad_games:continue
        signature=tuple(sorted(deck));buildgames[key[0]].append((key,signature))
    builds=collections.Counter();primary=[];games_by_draft=collections.defaultdict(list)
    for k,v in valid.items():games_by_draft[k[0]].append(v)
    for draft,entries in buildgames.items():
        configs=collections.Counter(sig for key,sig in entries);eligible={sig:n for sig,n in configs.items() if sum(q for _,q in sig)==40}
        builds['draftsWithObservedDeck']+=1;builds['observedConfigurations']+=len(configs)
        builds['gameDecksExactly40']+=sum(n for sig,n in eligible.items());builds['non40GameDecks']+=sum(n for sig,n in configs.items() if sig not in eligible)
        if not eligible:continue
        top=max(eligible.values());modal=[sig for sig,n in eligible.items() if n==top]
        if len(modal)!=1:builds['ambiguousModal40']+=1;continue
        builds['observedPrimaryBuild']+=1
        first=[sig for key,sig in entries if (key[2]==1 and key[3]==1) or (not match and key[3]==1)]
        builds['openingObservedAndModalAgree']+=int(len(first)==1 and first[0]==modal[0])
        valid_games=games_by_draft[draft]
        primary.append(dict(DraftId=draft,MainDeck=[identity.quantity(s,name,n) for name,n in modal[0]],
            Evidence='Unique modal observed 40-card configuration; not proven final submitted deck or pre-sideboard deck',
            Games=len(valid_games),Wins=sum(int(x['won']) for x in valid_games),Configurations=len(configs),
            OpeningObservedAgreement=len(first)==1 and first[0]==modal[0]))
    with gzip.open(OUT/f'{s}.{f}.primary-builds.jsonl.gz','wt',encoding='utf-8',compresslevel=1) as stream:
        for r in primary:stream.write(json.dumps(r,separators=(',',':'))+'\n')
    return dict(set=s,format=f,sourceRows=pq.ParquetFile(path).metadata.num_rows,sourceDrafts=totaldrafts,sampledDrafts=len(ids),
        sampledGames=len(games),uniquelyJoinableGames=len(valid),ambiguousGameKeys=len(ambiguous),unjoinedCardRows=unjoined,
        invalidCardRows=badcounts,duplicateCardKeys=len(duplicate_card_keys),cardGameObservations=len(observations),
        sourceFiles=sources,builds=dict(builds),normalizedBytes=normalized.stat().st_size,seconds=time.perf_counter()-start,
        keySemantics='draft/build/match/game' if match else 'draft/build/game; uniqueness audited, conflicts excluded',
        outcomeBoundary='Stored for later gated research only; qualification reports schema/counts, never holdout win rates')

def qualify(refresh_set=None):
    splits=verify_registration();OUT.mkdir(parents=True,exist_ok=True);identity=Identities(splits)
    sources=json.loads((RAW/'sources.json').read_text());grouped=collections.defaultdict(dict)
    for f in sources['files']:
        if 'set' in f:grouped[f['set'],f['format']][f['table']]=f
    summaries=[]
    widefiles=ROOT/'raw/phase-b-wide/acquisition-game_data-KHM-STX-AFR-VOW.json'
    wide={r['set']:r for r in json.loads(widefiles.read_text())} if widefiles.exists() else {}
    for (s,f),files in sorted(grouped.items()):
        target=OUT/f'{s}.{f}.qualification.json'
        if target.exists() and s!=refresh_set:
            cached=json.loads(target.read_text());summaries.append(cached);print(s,f,'cached',flush=True)
            # Restore mapping/catalog needed by later datasets and replay, without recomputing labels.
            for name in cached.get('vocabulary',[]):identity.resolve(s,name)
            if s in wide and f=='PremierDraft' and 'declinedParquetJoinAudit' not in cached['games']:
                old=cached['games'];audit={k:old[k] for k in ('sampledGames','uniquelyJoinableGames','ambiguousGameKeys','unjoinedCardRows','duplicateCardKeys')}
                cached['games']=qualify_wide_games(s,f,identity,wide[s],audit)
                cached['vocabulary']=[key.split('|',1)[1] for key in identity.map if key.startswith(s+'|')]
                dump(target,cached)
            continue
        print(s,f,'qualifying',flush=True)
        result=dict(set=s,format=f,partition=splits[s],picks=None,games=None)
        if all(t in files for t in ('picks','pack_cards','pool_cards')):result['picks']=qualify_picks(s,f,identity,{t:files[t] for t in ('picks','pack_cards','pool_cards')})
        if all(t in files for t in ('games','game_cards')):result['games']=qualify_games(s,f,identity,{t:files[t] for t in ('games','game_cards')})
        if s in wide and f=='PremierDraft':
            old=result['games'];audit={k:old[k] for k in ('sampledGames','uniquelyJoinableGames','ambiguousGameKeys','unjoinedCardRows','duplicateCardKeys')}
            result['games']=qualify_wide_games(s,f,identity,wide[s],audit)
        result['vocabulary']=[key.split('|',1)[1] for key in identity.map if key.startswith(s+'|')]
        dump(target,result);summaries.append(result)
        print(s,f,'done',result['picks']['waterfall'] if result['picks'] else 'no picks',flush=True)
    dump(OUT/'catalog.json',list(identity.catalog.values()));dump(OUT/'identity-map.json',identity.map)
    dump(EVIDENCE/'source-provenance.json',dict(**sources,wideFallbacks=list(wide.values())))
    dump(EVIDENCE/'qualification.json',dict(version='phase-b-qualification-v1',registrationCommit='d2cb723',
        datasets=[{k:v for k,v in r.items() if k!='vocabulary'} for r in summaries],identityProvenance=identity.provenance,
        optionalDependency=dict(name='pyarrow',version=pa.__version__,scope='ignored task-local Python directory; no production dependency')))
    dump(EVIDENCE/'identity-summary.json',dict(provenance=identity.provenance,
        vocabulary=len(identity.map),unresolved={k:v for k,v in identity.map.items() if v['identity']['CanonicalId'] is None},
        consensusLogicalIdentities=sum(v['consensus'] for v in identity.map.values()),
        scope='Score-relevant logical identity; exact original printing remains ambiguous for consensus groups'))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--refresh-set');args=parser.parse_args();qualify(args.refresh_set)
