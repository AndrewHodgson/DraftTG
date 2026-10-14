"""Requalify distributions without target/self-future leakage; calibration-only confidence fit."""
import collections,math,time
from phase_c_core import *

def collect(s,f,subgroup=None):
    h=collections.defaultdict(lambda:collections.defaultdict(collections.Counter));drafts=collections.defaultdict(lambda:collections.defaultdict(set));taken=collections.defaultdict(lambda:collections.defaultdict(collections.Counter));tdrafts=collections.defaultdict(lambda:collections.defaultdict(set));issues=collections.Counter()
    for d in decisions(s,f):
        i=d['Inputs'];draft=i['DraftId']
        if not evidence_group(s,f,draft):continue
        if subgroup is not None and (int(digest(SEED+'/position/'+s+'/'+f+'/'+draft)[:8],16)%2==0)!=subgroup:continue
        p=i['PickNumber'];pack=str(i['PackNumber'])
        if not 1<=p<=15:raise ValueError('Unsupported source position')
        for item in i['OfferedCards']:
            cid=item['Card']['CanonicalId']
            if cid is None:issues['unresolvedOffered']+=item['Count'];continue
            for key in ('all',pack):h[cid][key][p]+=item['Count'];drafts[cid][key].add(draft)
        cid=d['Label']['ActualPickedCard']['CanonicalId']
        if cid is not None:
            for key in ('all',pack):taken[cid][key][p]+=1;tdrafts[cid][key].add(draft)
    cards={cid:dict(seen={p:features(histogram(v),len(drafts[cid][p])) for p,v in values.items()},
                    taken={p:features(histogram(v),len(tdrafts[cid][p])) for p,v in taken[cid].items()}) for cid,values in h.items()}
    sets={}
    for key in ('all','1','2','3'):
        combined=collections.Counter()
        for values in h.values():combined.update(values[key])
        sets[key]=features(histogram(combined),len(set().union(*(drafts[cid][key] for cid in h))))
    return dict(set=sets,cards=cards,exclusions=dict(issues))

def crps(hist,forecast):
    n=sum(hist)
    if not n:return None
    # CDF squared error over integer cuts; training uses independent supporting drafts.
    return sum(hist[p]*sum((forecast[t]-(p<=t))**2 for t in range(1,15))/14 for p in range(1,16))/n

def qualify():
    split=guard();DATA.mkdir(parents=True,exist_ok=True);start=time.perf_counter();positions={};audits=[];selection_scores=collections.defaultdict(list)
    datasets=[]
    for s,p in split['Sets'].items():
        if p=='Holdout':continue
        base=B1 if s in ('ONE','MOM') else OLD
        for file in sorted(base.glob(s+'.*.decisions.jsonl.gz')):datasets.append((s,file.name.split('.')[1]))
    metadata=load(DATA/'metadata.json');quality={(r['set'],r['canonicalId']):r['currentQ'] for r in load('docs/evidence/v2-phase-b1/q-values.json')}
    for s,f in datasets:
        print('positions',s,f,flush=True);fit=collect(s,f,True);audit=collect(s,f,False);full=collect(s,f);positions[s+'/'+f]=full
        rows=[]
        for specific in (False,True):
            for lam in (0,20,100,400):
                errors=[];meanerr=[];medianerr=[];trimerr=[];strata=collections.defaultdict(list)
                for cid,row in audit['cards'].items():
                    for pack,target in row['seen'].items():
                        if pack=='all':continue
                        feature=fit['cards'].get(cid,{}).get('seen',{}).get(pack if specific else 'all');base=fit['set'][pack if specific else 'all']
                        c=confidence(feature['drafts'],lam) if feature else 0
                        forecast=[c*feature['cdf'][t]+(1-c)*base['cdf'][t] if feature else base['cdf'][t] for t in range(16)]
                        score=crps(target['hist'],forecast);errors.append(score)
                        if feature:
                            meanerr.append(sum(n*abs(p-feature['mean']) for p,n in enumerate(target['hist']))/target['n'])
                            medianerr.append(sum(n*abs(p-feature['median']) for p,n in enumerate(target['hist']))/target['n'])
                            trimerr.append(sum(n*abs(p-feature['trimmed']) for p,n in enumerate(target['hist']))/target['n'])
                        meta=metadata.get(cid,{});n=feature['n'] if feature else 0
                        for label in ('pack='+pack,'sample='+('low' if n<100 else 'medium' if n<1000 else 'high'),'rarity='+meta.get('rarity','unknown'),
                                      'color='+''.join(COLORS[c] for c in meta.get('colors',[])),'quality='+('above-neutral' if quality.get((s,cid),.5)>.5 else 'neutral-or-lower')):strata[label].append(score)
                result=dict(set=s,format=f,packSpecific=specific,lambdaDrafts=lam,cardPackGroups=len(errors),CDFCRPS=statistics.mean(errors) if errors else None,
                    meanMAE=statistics.mean(meanerr) if meanerr else None,medianMAE=statistics.mean(medianerr) if medianerr else None,trimmedMAE=statistics.mean(trimerr) if trimerr else None,
                    strata={k:dict(n=len(v),CRPS=statistics.mean(v)) for k,v in strata.items()})
                rows.append(result)
                if s in ('DMU','BRO','WOE') and f=='PremierDraft':selection_scores[(specific,lam)].append(result['CDFCRPS'])
        audits.extend(rows)
    chosen=min(selection_scores,key=lambda k:(statistics.mean(selection_scores[k]),k[0],k[1]))
    policy=dict(version=VERSION,packSpecific=chosen[0],lambdaDrafts=chosen[1],calibrationCRPS=statistics.mean(selection_scores[chosen]),
        metric='Equal-set/equal-card-pack empirical CDF CRPS on independent audit half of evidence drafts; no target positions',
        scoreGrid=[dict(packSpecific=specific,lambdaDrafts=lam,CRPS=statistics.mean(values)) for (specific,lam),values in selection_scores.items()],
        MeanMedianTrimmed='All compared; family A/B explicitly retain means, C uses CDF. No method chosen from future color/human outcomes.',seconds=time.perf_counter()-start)
    dump(DATA/'positions.json',positions);dump(DATA/'quality.json',[dict(set=s,id=cid,Q=q) for (s,cid),q in quality.items()]);dump(OUT/'position-policy.json',policy);dump(OUT/'position-audit.json',audits)
    # Supply the actual Phase9B implementation with global disjoint seen means; no ATA substitution or Q change.
    alsa=[dict(Set=s.split('/')[0],Format=s.split('/')[1],Id=cid,ALSA=row['seen']['all']['mean']) for s,d in positions.items() for cid,row in d['cards'].items()]
    dump(DATA/'current-alsa.json',alsa)
    dump(OUT/'position-provenance.json',dict(seed=SEED,positionFileHash=sha(DATA/'positions.json'),qualityFileHash=sha(DATA/'quality.json'),metadataHash=sha(DATA/'metadata.json'),
        sourceFiles={str((B1 if s in ('ONE','MOM') else OLD)/f'{s}.{f}.decisions.jsonl.gz'):sha((B1 if s in ('ONE','MOM') else OLD)/f'{s}.{f}.decisions.jsonl.gz') for s,f in datasets},
        targetDraftsExcluded=True,physicalCardOrPackIDsAvailable=False,distributionsAreDerivedNotOfficialALSA=True,datasets=len(datasets)))
    print('position policy',policy,flush=True)

def audit_taken():
    """Separate taken-position stability lens; never replaces the selected seen inputs."""
    split=guard();metadata=load(DATA/'metadata.json');quality={(r['set'],r['canonicalId']):r['currentQ'] for r in load('docs/evidence/v2-phase-b1/q-values.json')};results=[]
    for s,p in split['Sets'].items():
        if p=='Holdout':continue
        base=B1 if s in ('ONE','MOM') else OLD
        for file in sorted(base.glob(s+'.*.decisions.jsonl.gz')):
            f=file.name.split('.')[1];fit=collect(s,f,True);audit=collect(s,f,False)
            for specific in (False,True):
                values=collections.defaultdict(list);strata=collections.defaultdict(list);missing=0
                for cid,row in audit['cards'].items():
                    for pack,target in row['taken'].items():
                        if pack=='all' or not target['n']:continue
                        feature=fit['cards'].get(cid,{}).get('taken',{}).get(pack if specific else 'all')
                        if not feature or not feature['n']:missing+=1;continue
                        for method in ('mean','median','trimmed'):
                            values[method].append(sum(n*abs(pick-feature[method]) for pick,n in enumerate(target['hist']))/target['n'])
                        score=crps(target['hist'],feature['cdf']);values['CDFCRPS'].append(score)
                        meta=metadata.get(cid,{});n=feature['n']
                        for label in ('pack='+pack,'sample='+('low' if n<20 else 'medium' if n<100 else 'high'),'rarity='+meta.get('rarity','unknown'),
                                      'color='+''.join(COLORS[c] for c in meta.get('colors',[])),'quality='+('unknown' if (s,cid) not in quality else 'above-neutral' if quality[(s,cid)]>.5 else 'neutral-or-lower')):strata[label].append(score)
                results.append(dict(set=s,format=f,packSpecific=specific,cardPackGroups=len(values['CDFCRPS']),missingTrainingGroups=missing,
                    metrics={k:statistics.mean(v) if v else None for k,v in values.items()},strata={k:dict(n=len(v),CRPS=statistics.mean(v)) for k,v in strata.items()}))
            print('taken audit',s,f,flush=True)
    dump(OUT/'taken-position-audit.json',dict(results=results,scope='Independent audit-half taken positions; denominators are actual human picks, not offered multiplicity; descriptive, no substitution into L inputs'))

def audit_percentiles():
    """Set-normalized card-mean ranks are descriptors, never substitutes for late surprise."""
    from phase_b_core import percentile,rank_correlation
    guard();full=load(DATA/'positions.json');out=[];rows=[]
    for key,d in full.items():
        s,f=key.split('/');fit=collect(s,f,True);audit=collect(s,f,False);item=dict(set=s,format=f,stability={})
        for tag in ('seen','taken'):
            means=[r[tag]['all']['mean'] for r in d['cards'].values() if r[tag].get('all',{}).get('n',0)]
            left=[r[tag]['all']['mean'] for r in fit['cards'].values() if r[tag].get('all',{}).get('n',0)]
            right=[r[tag]['all']['mean'] for r in audit['cards'].values() if r[tag].get('all',{}).get('n',0)]
            a=[];b=[]
            for cid,r in d['cards'].items():
                feature=r[tag].get('all',{})
                if feature.get('n',0):rows.append(dict(set=s,format=f,id=cid,stat=tag,mean=feature['mean'],count=feature['n'],drafts=feature['drafts'],setPercentile=percentile(feature['mean'],means)))
                l=fit['cards'].get(cid,{}).get(tag,{}).get('all',{});rr=audit['cards'].get(cid,{}).get(tag,{}).get('all',{})
                if l.get('n',0) and rr.get('n',0):a.append(percentile(l['mean'],left));b.append(percentile(rr['mean'],right))
            item['stability'][tag]=dict(matchedCards=len(a),rankCorrelation=rank_correlation(a,b),percentileMAE=statistics.mean(abs(x-y) for x,y in zip(a,b)) if a else None)
        out.append(item);print('percentiles',key,flush=True)
    dump(OUT/'position-percentile-audit.json',dict(datasets=out,descriptors=rows,positionInputHash=sha(DATA/'positions.json'),usedAsLaneInput=False,
        definition='Empirical midrank over same-set/format card means; fit/audit distributions independently recomputed inside evidence drafts. This is not official ALSA/ATA or card-survival probability.'))
