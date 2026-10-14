"""Read-only ledger aggregation. Never marks a state physically validated from machine reads."""
import argparse, collections, json, pathlib, statistics

def summarize(path):
    kinds=collections.Counter(); reasons=collections.Counter(); states=[]; reads=[]; attach=[]; ui=[]; deck=[]
    placements=[]; captures=0; runs=set()
    for number,line in enumerate(pathlib.Path(path).read_text(encoding='utf-8-sig').splitlines(),1):
        if not line.strip(): continue
        try: row=json.loads(line)
        except ValueError as error: raise ValueError(f'Invalid ledger row {number}; no partial summary') from error
        kinds[row['Kind']]+=1; data=row['Data']; runs.add(row['RunId'])
        if row['Kind']=='UiLocalization':
            states.append(data)
            reasons.update(data.get('UnsupportedReasons',[]))
        elif row['Kind']=='Performance':
            flags=data['FirstAttach']
            for value,first in zip(data['ReadMs'],flags): (attach if first else reads).append(value)
            ui.extend(x for x in data.get('UiReadMs',[]) if x is not None)
            deck.extend(x for x in data.get('DeckReadMs',[]) if x is not None)
        elif row['Kind']=='UiPlacementTiming': placements.append(data['PlacementUpdateMs'])
        elif row['Kind']=='RunEnded': captures+=data.get('RejectedCaptureAttempts',0)
    def metric(xs):
        values=sorted(xs)
        return dict(n=len(values),meanMs=statistics.mean(values),p95Ms=values[min(len(values)-1,int(.95*len(values)))],maxMs=max(values)) if values else None
    return dict(schema='drafttg-phase9f3-summary-v1',runs=len(runs),events=dict(kinds),stateChanges=len(states),
        clientSizes=sorted({f"{s['Window']['Width']}x{s['Window']['Height']}@{s['Dpi']}" for s in states if s.get('Window')}),
        suppressionReasons=dict(reasons),packCounts=sorted({len(s['DraftPack']['OrderedGrpIds']) for s in states}),
        deckRowsObserved=sorted({len(s.get('DeckView',{}).get('Rows') or []) for s in states}),
        moveEvents=kinds['Move'],resizeInvalidations=kinds['Resize'],dpiEvents=kinds['DPI'],staleRejections=kinds['StaleUiRejected'],
        performance=dict(firstAttach=metric(attach),steadyRead=metric(reads),combinedUiTraversal=metric(ui),deckTraversal=metric(deck),placementUpdate=metric(placements)),
        captureAttempts=captures,physicalValidation='Not inferred; use PHASE_9F3_PHYSICAL_VALIDATION.md and explicit human confirmation.',
        localizationComplete=False,productionScoringChanged=False,latestSnapshot=states[-1] if states else None)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--ledger',required=True);parser.add_argument('--output')
    args=parser.parse_args(); result=summarize(args.ledger); text=json.dumps(result,indent=2,ensure_ascii=False)
    if args.output:
        path=pathlib.Path(args.output);path.parent.mkdir(parents=True,exist_ok=True);path.write_text(text+'\n',encoding='utf-8')
    print(text)
