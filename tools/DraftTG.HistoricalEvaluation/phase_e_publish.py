"""Read frozen production scores, legal predictions and post-score labels separately."""
from phase_e_builders import *
import subprocess
DLL='data/historical/evaluation/phase-e/runner4/DraftTG.DeckFitResearch.dll'
def pair_inputs():
 guard();f=open(DATA/'pair-inputs-complete.jsonl','w',encoding='utf8')
 for s in SETS:
  tasks=list(readrows(DATA/f'{s}.pairs.jsonl.gz'));needed=collections.defaultdict(set)
  for r in tasks:
   if r['partition']=='target':needed[r['draft']].update((r['positive'],r['negative']))
  for r in rows(s):
   if r['draft'] in needed:f.write(json.dumps(dict(set=s,draft=r['draft'],pool=r['pool'],candidates=sorted(needed[r['draft']])))+'\n')
 f.close()
def import_pairs():
 d={}
 path=RUN/('current-pair-D-complete.jsonl' if (RUN/'current-pair-D-complete.jsonl').exists() else 'current-pair-D.jsonl')
 for line in open(path,encoding='utf8'):
  r=json.loads(line)
  if r['Key'] in d:raise ValueError('Duplicate pair baseline')
  d[r['Key']]=r['D']
 dump(DATA/'current-pair-D.json',d)
def prepare_replay():
 guard();m=meta();q=qualities();models=calibrated_models();t=time.perf_counter();summary=[];timings=[];qdiff=[]
 f=open(RUN/'compose-input.jsonl','w',encoding='utf8');side=open(RUN/'compose-sidecar.jsonl','w',encoding='utf8')
 for r in readrows(RUN/'production.jsonl.gz'):
  if not r['Complete']:continue
  pool=counts(r['PoolBeforePick']);cards=[];probs=[]
  for c in r['Cards']:
   i=c['Id'];start=time.perf_counter();x,a=feature(pool,i,r['SetCode'],m,q,r['GlobalPickIndex']);scores,p=dscores(x,models);timings.append((time.perf_counter()-start)*1000)
   if c['QualityComponent'] is not None:qdiff.append(abs(q.get((r['SetCode'],i),.5)-c['QualityComponent']))
   # Missing metadata/unknown inventory declines all substitutions, never guesses a truncated pack.
   ds=[c['DeckNeedComponent']]+[scores[k] if m[i]['known'] else c['DeckNeedComponent'] for k in ('A','B','C')]
   cards.append(dict(Weights=c['Weights'],EffectiveQualityComponent=c['EffectiveQualityComponent'] if c['EffectiveQualityComponent'] is not None else .5,LaneComponent=c['LaneComponent'],ArchetypeComponent=c['ArchetypeComponent'],D=ds))
   probs.append(p)
  f.write(json.dumps(dict(cards=cards),allow_nan=False)+'\n');side.write(json.dumps(dict(observation=r,probabilities=probs),allow_nan=False)+'\n')
 f.close();side.close()
 dump(OUT/'prediction-performance.json',dict(seconds=time.perf_counter()-t,candidateN=len(timings),candidateMeanMs=float(np.mean(timings)),candidateP95Ms=float(np.quantile(timings,.95)),frozenQMaxDifference=max(qdiff),modelBytes=(DATA/'models-calibrated.json').stat().st_size,cache='Legal pool projection cache within process only; no labels in cache.'))
def replay_summary():
 guard();metricsBy=collections.defaultdict(list);changes=collections.Counter();assoc=collections.defaultdict(list);maxerror=0;matched=0
 builds={s+'/'+r['draft']:r for s in SETS for r in rows(s)}
 with open(RUN/'compose-output.jsonl') as scores,open(RUN/'compose-sidecar.jsonl') as side:
  for line,metaLine in zip(scores,side,strict=True):
   values=json.loads(line);r=json.loads(metaLine);o=r['observation'];cards=o['Cards'];ids=[c['Id'] for c in cards];actual=o['ActualPicked'];modelMetrics={};matched+=1
   for k,name in enumerate(('current','A','B','C')):
    order=sorted(range(len(cards)),key=lambda j:(-values[j][k],-(cards[j]['QualityComponent'] or 0),-(cards[j]['StatisticalDataWeight'] or 0),cards[j]['PackIndex']))
    ranks=[order.index(j)+1 for j,i in enumerate(ids) if i==actual];rank=min(ranks)
    if k==0:
     if rank!=o['Rank']:raise ValueError('Production rank recomposition changed')
     maxerror=max(maxerror,max(abs(values[j][k]-cards[j]['ContextualValue']) for j in range(len(cards))))
     original=order;originalRank=rank
    else:
     changes[name+'/changed']+=order!=original;changes[name+'/improved']+=rank<originalRank;changes[name+'/regressed']+=rank>originalRank
    mm=dict(Top1=float(rank==1),Top3=float(rank<=3),MRR=1/rank,normalizedRank=(rank-1)/max(1,len(cards)-1))
    metricsBy[name+'/overall'].append(mm);metricsBy[name+'/'+o['SetCode']].append(mm)
   key=o['SetCode']+'/'+o['DraftId'];b=builds.get(key)
   if b:
    # Condition ONLY on the card actually picked; never call unpicked offered cards negative labels.
    n=b['pool'].get(actual,0)
    if n:
     index=ids.index(actual);y=b['build'].get(actual,0)/n;p=r['probabilities'][index];p['current-D']=cards[index]['DeckNeedComponent']
     for name in ('generic','A','B','C','current-D'):
      assoc[name].append(dict(p=p[name],y=y,set=o['SetCode'],pack=o['PackNumber'],commitment=o['InputColorCommitment'],record=o['Outcome'].get('FinalWins')))
 if maxerror>1e-12:raise ValueError('Frozen production score recomposition not exact')
 dump(OUT/'contextual-replay.json',dict(comparableDecisions=matched,metrics={k:mean_metrics_for(v) for k,v in metricsBy.items()},changes=dict(changes),baselineMaximumError=maxerror,replayTelemetry=load(RUN/'production.jsonl.gz.json'),productionAuthorityUnchanged=True))
 conditioned={}
 for k,rs in assoc.items():
  conditioned[k]=dict(all=metrics([r['p'] for r in rs],[r['y'] for r in rs]),includedMean=float(np.mean([r['p'] for r in rs if r['y']>0])),excludedMean=float(np.mean([r['p'] for r in rs if r['y']==0])),byOutcome={str(record):metrics([r['p'] for r in rs if r['record']==record],[r['y'] for r in rs if r['record']==record]) for record in sorted({r['record'] for r in rs},key=lambda v:-1 if v is None else v)})
 dump(OUT/'actual-pick-association.json',dict(conditionedActualPicksOnly=True,models=conditioned,currentDCaveat='Current D is a heuristic, not a calibrated inclusion probability; its Brier/logloss are diagnostic when numerically interpreted as a probability.'))
def mean_metrics_for(rs):return dict(n=len(rs),**{k:float(np.mean([r[k] for r in rs])) for k in ('Top1','Top3','MRR','normalizedRank')})
def probes():
 guard();m=meta();q=qualities();models=calibrated_models();s='DMU';ids=[i for i in m if (s,i) in q and not m[i]['land'] and m[i]['colors']==[2] and m[i]['creature']];ids=sorted(ids,key=lambda i:(abs(q[s,i]-.5),i));two=next(i for i in ids if m[i]['manaValue']==2);five=next(i for i in ids if m[i]['manaValue']==5);low=next(i for i in ids if m[i]['manaValue']<=3);other=next(i for i in m if (s,i) in q and m[i]['colors']==[1] and q[s,i]>.7 and not m[i]['land'])
 cases={};transitions=[]
 def evaluate(pool,i):
  f,a=feature(pool,i,s,m,q,sum(pool.values()));d,p=dscores(f,models);return dict(D=d,P=p,features=f,audit=a)
 cases['fifth-five-drop']=dict(before=evaluate({low:16,five:0},five),after=evaluate({low:12,five:4},five))
 cases['missing-two-drop']=dict(before=evaluate({five:8,low:8},two),after=evaluate({five:8,low:4,two:4},two))
 cases['off-color-bomb']=evaluate({low:20,two:4},other)
 cases['creature-saturation']=dict(before=evaluate({low:8},two),after=evaluate({low:20,two:5},two))
 noncreature=next((i for i in m if (s,i) in q and m[i]['colors']==[2] and not m[i]['creature'] and not m[i]['land'] and abs(q[s,i]-q[s,two])<.1),None)
 if noncreature:cases['creature-shortage']=dict(creature=evaluate({noncreature:20},two),noncreature=evaluate({noncreature:20},noncreature))
 cases['fixing-with-no-need']=dict(observed='Metadata-supported lands only; heuristic has no explicit castability-unlocking term. Fixing gain is not established by these probes.',qualified=False)
 cases['fixing-enables-splash']=dict(qualified=False,reason='Experimental structural projections are conservative two-color builds; cannot establish multiple newly castable splash cards.')
 cases['sideboard-card']=dict(qualified=False,reason='No reliable narrow-sideboard-only tag; missingness cannot distinguish this effect.')
 cases['removal-shortage']=dict(qualified=False,reason='Positive precision-first removal tags exist; a causal incremental shortage response is not established by tag correlations.')
 fixing=next((i for i in m if (s,i) in q and m[i]['land'] and 'Fixing' in m[i]['roleProfile']['Roles']),None)
 removal=next((i for i in m if (s,i) in q and m[i]['colors']==[2] and (role(i,'RemovalWeight',m) or 0)>0),None)
 if fixing:
  cases['fixing-with-no-need']['result']=evaluate({low:20,two:4},fixing)
  cases['fixing-enables-splash']['withoutSources']=evaluate({low:16,other:4},fixing)
  cases['fixing-enables-splash']['withSources']=evaluate({low:16,other:4,fixing:3},fixing)
 if removal:cases['removal-shortage'].update(shortage=evaluate({low:20},removal),covered=evaluate({low:17,removal:3},removal))
 negate=next((i for i in m if (s,i) in q and m[i]['name']=='Negate'),None)
 if negate:cases['sideboard-card']['result']=evaluate({low:20,two:4},negate)
 candidates=sorted(ids,key=lambda i:q[s,i]);weak,strong=candidates[0],candidates[-1]
 cases['cutline-upgrades']=dict(weak=evaluate({weak:23},two),huge=evaluate({weak:23},strong))
 for n in range(26):transitions.append(dict(n=n,**evaluate({low:n},two)['D']))
 safety=all(0<=v<=1 for row in transitions for v in (row['A'],row['B'],row['C']))
 empirical=cases['fifth-five-drop']['after']['D']['C']<cases['fifth-five-drop']['before']['D']['C'] and cases['missing-two-drop']['before']['D']['C']>cases['missing-two-drop']['after']['D']['C']
 dump(OUT/'failure-probes.json',dict(cases=cases,transition=transitions,largestTransition={k:max(abs(transitions[n+1][k]-transitions[n][k]) for n in range(25)) for k in ('A','B','C')},bounded=safety,mainStructuralChecksPass=empirical,allRequiredStrategicBehaviorQualified=False,synthetic='Controlled generic structural states using real card metadata; not historical win evidence. No tuning follows.'))
def structural_coverage():
 guard();m=meta();out=[]
 for s in SETS:
  c=collections.Counter();n=0
  for r in rows(s):
   for p in r['picks']:
    i=p['id'];n+=1;c['knownTypes']+=m[i]['known'];c['knownMana']+=m[i]['pipsKnown'];c['knownMV']+=m[i]['manaValue'] is not None
    for tag in ('HardRemoval','ConditionalRemoval','Fixing','EarlyPlay','CardAdvantage','CombatTrick','Finisher','Defensive','Ramp','Aggressive'):c['assigned'+tag]+=tag in m[i]['roleProfile']['Roles']
  out.append(dict(set=s,pickedOccurrences=n,counts=dict(c),missingRoleInterpretation='Unassigned remains unknown; no absence/counterfactual role label claimed.'))
 dump(OUT/'structural-coverage.json',dict(datasets=out,metadataSha256=sha(DATA/'metadata.json')))
def final_diagnostics():
 guard();m=meta();current=load(DATA/'current-builder-index.json');out={};truthLands=collections.Counter();availability=collections.Counter()
 for s in SETS:
  counter=collections.Counter()
  for r in rows(s):
   if r['partition']!='target':continue
   predicted=current[s+'/'+r['draft']];availability[predicted['availability']]+=1;land=sum(n for i,n in r['build'].items() if m[i]['land']);truthLands[land]+=1
   for i,n in r['pool'].items():
    if m[i]['land']:continue
    k=predicted['main'].get(i,0);truth=r['build'].get(i,0)
    counter['starterObservedStarter']+=min(k,truth);counter['starterObservedNonstarter']+=max(0,k-truth);counter['nonstarterObservedStarter']+=max(0,truth-k);counter['nonstarterObservedNonstarter']+=n-max(k,truth)
  out[s]=dict(counter)
 dump(OUT/'final-pool-diagnostics.json',dict(copyConfusionBySet=out,observedLandHistogram=dict(truthLands),currentAvailability=dict(availability),labelInterpretation='Quantity-equivalent interchangeable copies; not an assignment of physical picked occurrences.'))
def benchmark():
 from phase_b import initialize_telemetry
 from phase_b_core import peak_memory_bytes
 initialize_telemetry();models=calibrated_models();xs=[r['x'] for r in examples('DMU')[:500]];elapsed=[]
 for x in xs:
  t=time.perf_counter();dscores(x,models);elapsed.append((time.perf_counter()-t)*1000)
 dump(OUT/'linear-inference-benchmark.json',dict(n=len(xs),threeFamilyMeanMs=float(np.mean(elapsed)),threeFamilyP95Ms=float(np.quantile(elapsed,.95)),peakBytes=peak_memory_bytes(),excludesProjectionAndIO=True))
def gate():
 guard();inc=load(OUT/'inclusion.json');pairs=load(OUT/'pairwise.json');recon=load(OUT/'reconstruction.json');selection=load(OUT/'selection.json');family=selection['selectedFamily'];qualification=load(OUT/'qualification.json');coverage=load(OUT/'pair-task-freeze.json')
 avg=lambda xs:float(np.mean(xs));pairgain={s:pairs[s][family]['accuracy']-max(pairs[s]['current-D']['accuracy'],pairs[s]['current-optimizer']['accuracy'],pairs[s]['generic']['accuracy']) for s in DEV}
 # Each set compared against its strongest registered simple baseline; conservative macro.
 fp={s:recon['bySet'][s]['C']['nonlandF1']-max(recon['bySet'][s][k]['nonlandF1'] for k in ('current','highest-Q','curve-17')) for s in DEV}
 il={s:1-inc[s]['E-'+family]['LogLoss']/inc[s]['generic']['LogLoss'] for s in DEV};ib={s:1-inc[s]['E-'+family]['Brier']/inc[s]['generic']['Brier'] for s in DEV}
 contextual=load(OUT/'contextual-replay.json');m=contextual['metrics'];golden=load(OUT/'validation.json')
 passed=dict(pair=avg(list(pairgain.values()))>=.02 and sum(v>0 for v in pairgain.values())>=4 and min(pairgain.values())>=-.02,
 reconstruction=avg(list(fp.values()))>=.01 and sum(v>0 for v in fp.values())>=4 and all(abs(recon['bySet'][s]['C']['totalCards']-40)<1e-12 for s in DEV) and all(recon['bySet'][s]['C']['mainColorAgreement']>=recon['bySet'][s]['current']['mainColorAgreement']-.02 and recon['bySet'][s]['C']['landMAE']<=recon['bySet'][s]['current']['landMAE']+.25 for s in DEV),
 inclusion=avg(list(il.values()))>=.02 and avg(list(ib.values()))>=.01 and sum(il[s]>0 and ib[s]>0 for s in DEV)>=4 and all(inc[s]['E-'+family]['ECE']<=inc[s]['generic']['ECE']+.01 for s in DEV),
 coverage=coverage['healthyCoverage'] and all(next(r for r in qualification['datasets'] if r['set']==s)['partitions'].get('target',0)>=100 and inc[s]['generic']['n']>=1000 for s in DEV),
 contextual=contextual['comparableDecisions']>=50000 and all(m[family+'/'+s]['Top1']>=m['current/'+s]['Top1']-.005 and m[family+'/'+s]['MRR']>=m['current/'+s]['MRR']-.005 for s in DEV),
 cold=load(OUT/'cold-start.json')['passGate'],probes=load(OUT/'failure-probes.json')['allRequiredStrategicBehaviorQualified'],goldens=golden['goldenPassed'],frozenQCompatibility=load(OUT/'frozen-Q-compatibility.json')['exact'])
 dump(OUT/'development-gate.json',dict(gates=passed,passAll=all(passed.values()),selectedFamily=family,pairGainBySet=pairgain,reconstructionF1GainBySet=fp,loglossRelativeGainBySet=il,brierRelativeGainBySet=ib,QCompatibilityInterpretation='Additional input-contract integrity blocker, not a newly tuned predictive threshold; all original failed gates remain failed.'))
 dump(OUT/'holdout-status.json',dict(MH3='untouched',DFT='untouched',consumed=False,releaseLockCreated=False,results=None,reason='No release unless all frozen gates pass. Production unchanged.'))
def freeze_regressions():
 cases=[];probes0=load(OUT/'failure-probes.json')
 for name in ('fifth-five-drop','missing-two-drop','creature-saturation','cutline-upgrades'):
  for variant,r in probes0['cases'][name].items():
   if 'features' in r:cases.append(dict(name=name+'/'+variant,features=r['features'],D=r['D']))
 fixing=probes0['cases']['fixing-enables-splash'].get('withoutSources')
 if fixing:cases.append(dict(name='fixing-need',features=fixing['features'],D=fixing['D']))
 m=meta();q=qualities();models=calibrated_models()
 for s in DEV:
  for r in examples(s):
   if r['partition']=='target' and r['y'] in (0,1):
    name='observed-inclusion' if r['y']==1 else 'observed-exclusion'
    if not any(c['name']==name for c in cases):cases.append(dict(name=name,set=s,draft=r['draft'],pack=r['pack'],pick=r['pick'],features=r['x'],D=dscores(r['x'],models)[0]))
   if sum(c['name'].startswith('observed-') for c in cases)==2:break
  if sum(c['name'].startswith('observed-') for c in cases)==2:break
 dump(OUT/'selected-regressions.json',dict(selectionSha256=sha(OUT/'selection.json'),cases=cases,noParameterTuning=True))
def validate():
 import xml.etree.ElementTree as ET,re
 guard();p=subprocess.run([sys.executable,'tools/DraftTG.HistoricalEvaluation/test_phase_e.py'],capture_output=True,text=True);(RUN/'python-tests.log').write_text(p.stdout+p.stderr,encoding='utf8')
 if p.returncode:raise ValueError(p.stdout+p.stderr)
 counts0=collections.Counter()
 for f in (RUN/'tests').glob('*.trx'):
  c=next(x for x in ET.parse(f).iter() if x.tag.endswith('Counters'))
  for k in ('total','passed','failed','notExecuted'):counts0[k]+=int(c.get(k,0))
 gold=load(RUN/'golden/golden.json')
 if counts0['total']<1298 or counts0['failed'] or counts0['notExecuted'] or not gold['Passed'] or gold['Tested']!=40:raise ValueError('Build/test/golden validation incomplete')
 raw=(RUN/'build.log').read_bytes();txt=raw.decode('utf-16' if raw[:2] in (b'\xff\xfe',b'\xfe\xff') else 'utf-8-sig')
 if not all(v in txt for v in ('Build succeeded.','0 Warning(s)','0 Error(s)')):raise ValueError('Build not clean')
 files={p.as_posix():sha(p) for p in pathlib.Path('tools/DraftTG.HistoricalEvaluation').glob('*phase_e*.py')};files.update({p.as_posix():sha(p) for p in pathlib.Path('tools/DraftTG.DeckFitResearch').glob('*') if p.is_file()});files.update({p.as_posix():sha(p) for p in [pathlib.Path('src/DraftTG.HistoricalEvaluation/ExperimentalDeckFitSubstitution.cs'),pathlib.Path('tests/DraftTG.HistoricalEvaluation.Tests/ExperimentalDeckFitTests.cs')]})
 dump(OUT/'execution-sources.json',dict(files=files));dump(OUT/'validation.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),dirtyTree=True,CSharp=dict(counts0),pythonTests=int(re.search(r'Ran (\d+) tests',p.stderr).group(1)),golden=40,goldenPassed=True,buildPassed=True,warnings=0,errors=0,preservedFiles=len(load(OUT/'preservation.json')['files']),productionChanged=False,sourceFingerprint=digest(json.dumps(files,sort_keys=True)),scoreAssemblySha256=sha('src/DraftTG.RecommendationEngine/bin/Debug/net10.0/DraftTG.RecommendationEngine.dll')))
def finalize():
 guard();summary=dict(conclusion='No Phase E production integration; retain existing v1.6.2 D and builder.',qualification=load(OUT/'qualification.json'),selection=load(OUT/'selection.json'),pairTask=load(OUT/'pair-task-freeze.json'),pairwise=load(OUT/'pairwise.json'),reconstruction=load(OUT/'reconstruction.json'),inclusion=load(OUT/'inclusion.json'),cutline=load(OUT/'cutline.json'),contextual=load(OUT/'contextual-replay.json'),cold=load(OUT/'cold-start.json'),gate=load(OUT/'development-gate.json'),holdout=load(OUT/'holdout-status.json'),validation=load(OUT/'validation.json'),performance=load(OUT/'prediction-performance.json'))
 dump(OUT/'summary.json',summary)
 from phase_e_report import report
 report()
 files={p.as_posix():sha(p) for p in OUT.glob('*.json') if p.name!='completed-evidence.json'}
 files.update({p.as_posix():sha(p) for p in DATA.glob('*.json')});files.update({p.as_posix():sha(p) for p in DATA.glob('*.gz')});files.update(load(OUT/'execution-sources.json')['files']);files['V2_PHASE_E_FINAL23_DECK_FIT_RESEARCH.md']=sha('V2_PHASE_E_FINAL23_DECK_FIT_RESEARCH.md');dump(OUT/'completed-evidence.json',dict(files=files))
def summary_display():
 z=load(OUT/'summary.json');dev=list(DEV);mean=lambda fn:float(np.mean([fn(s) for s in dev]));p=z['pairwise'];r=z['reconstruction']['bySet'];inc=z['inclusion'];m=z['contextual']['metrics']
 print(json.dumps(dict(conclusion=z['conclusion'],qualifiedDrafts=z['qualification']['qualified'],supportedDecisions=z['qualification']['decisions'],selectedFamily=z['selection']['selectedFamily'],selectedHybrid=z['selection']['selectedHybrid'],pairCoverage=z['pairTask']['datasets'],developmentMacro=dict(pairCurrentD=mean(lambda s:p[s]['current-D']['accuracy']),pairC=mean(lambda s:p[s]['C']['accuracy']),reconstructionCurrentF1=mean(lambda s:r[s]['current']['nonlandF1']),reconstructionCF1=mean(lambda s:r[s]['C']['nonlandF1']),genericLogLoss=mean(lambda s:inc[s]['generic']['LogLoss']),CLogLoss=mean(lambda s:inc[s]['E-C']['LogLoss'])),contextual={k:m[k+'/overall'] for k in ('current','A','B','C')},gate=z['gate'],holdouts=z['holdout'],validation=z['validation']),indent=2))
