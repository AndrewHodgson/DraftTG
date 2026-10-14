"""Preregistered generic states; outputs frozen only after calibration selection."""
from phase_f_model import *
def fixtures():
 m={};q={};s='PROBE'
 def card(i,mv=3,colors=(0,),pips=None,creature=True,quality=.55,land=False,produced=(),removal=0,broad=None,unsupported=False):
  need=[0]*6
  if pips is None:
   for k in colors:need[k]=1
  else:need=list(pips)
  m[i]=dict(name=i,known=True,land=land,basic=False,manaValue=mv,creature=creature,colors=list(colors),pipNeed=need,pips=need[:5],pipsKnown=True,costSupported=not unsupported,sourceKnown=land and bool(produced),produced=list(produced),unknownNarrow=not creature and not land if broad is None else not broad,broadMainDeck=creature if broad is None else broad,roleProfile=dict(Roles='HardRemoval' if removal else 'Creature' if creature else 'None',RemovalWeight=removal));q[s,i]=quality
 card('earlyW',2);card('earlyU',2,(1,));card('midW',3);card('midU',3,(1,));card('fourW',4);card('fourU',4,(1,));card('fiveW',5,quality=.53);card('fiveU',5,(1,),quality=.53);card('weakW',3,quality=.40);card('weakU',3,(1,),quality=.40)
 card('modest',3,quality=.57);card('premium',3,quality=.85);card('bombR',5,(3,),quality=.9);card('doubleR',5,(3,),pips=[0,0,0,2,0,0],quality=.9)
 card('spellW',3,creature=False,broad=True);card('spellU',3,(1,),creature=False,broad=True);card('removal',3,creature=False,broad=True,removal=1);card('narrowUnknown',3,creature=False,broad=False,removal=1)
 card('dualWU',0,(),creature=False,land=True,produced=(0,1));card('dualWR',0,(),creature=False,land=True,produced=(0,3));card('dualUR',0,(),creature=False,land=True,produced=(1,3));card('unsupported',3,unsupported=True)
 base=dict(earlyW=2,earlyU=2,midW=4,midU=4,fourW=4,fourU=4,fiveW=1,fiveU=2)
 # Equal-size contrasts prevent draft maturity from masquerading as structural need.
 shortage=dict(midW=6,midU=6,fourW=4,fourU=4,fiveW=1,fiveU=2)
 topfirst=dict(earlyW=2,earlyU=2,midW=7,midU=6,fourW=3,fourU=3)
 topfifth=dict(earlyW=2,earlyU=2,midW=5,midU=4,fourW=3,fourU=3,fiveW=2,fiveU=2)
 noncreatures=dict(spellW=10,spellU=10,earlyW=2,earlyU=1)
 saturated=dict(earlyW=4,earlyU=4,midW=5,midU=5,fourW=3,fourU=2)
 fixerNeed=dict(base,bombR=2,dualWR=1,dualUR=1);fixerCovered=dict(base,bombR=2,dualWR=3,dualUR=1)
 weak=dict(weakW=12,weakU=11)
 return m,q,s,dict(base=base,shortage=shortage,topfirst=topfirst,topfifth=topfifth,noncreatures=noncreatures,saturated=saturated,fixerNeed=fixerNeed,fixerCovered=fixerCovered,weak=weak)
def castability_cases():
 m,q,s,pools=fixtures();source=[0]*6;out={}
 for need in (1,2,3):
  c=dict(m['earlyW'],manaValue=3,pipNeed=[need,0,0,0,0,0]);out[str(need)]=[castability(c,[n,9,0,0,0,0],17) for n in range(16)]
 passed=all(all(out[k][n]['score']<=out[k][n+1]['score']+1e-12 for n in range(15)) for k in out) and all(out['2'][n]['score']<=out['1'][n]['score']+1e-12 for n in range(16)) and out['1'][0]['score']==0 and not castability(m['unsupported'],[9,8,0,0,0,0],17)['supported']
 return dict(cases=out,passed=passed,interpretation='Source monotonicity and single/double/triple pip checks use same cost/land count. Marginal tails are inspectable adequacy approximations.')
def evaluate_probes(params,family):
 m,q,s,ps=fixtures();model=MarginalModel(m,q,params);r={}
 def score(pool,i):return model.marginal(pool,i,s,40,family,.5,.65)
 r['fifth-five-drop']=dict(needed=score(ps['topfirst'],'fiveW'),fifth=score(ps['topfifth'],'fiveW'))
 r['two-drop-hole']=dict(deficient=score(ps['shortage'],'earlyW'),filled=score(ps['base'],'earlyW'))
 r['creatures']=dict(shortage=score(ps['noncreatures'],'midW'),saturated=score(ps['saturated'],'midW'))
 r['removal']=dict(shortage=score(ps['base'],'removal'),covered=score(dict(ps['base'],removal=5),'removal'))
 r['fixing']=dict(need=score(ps['fixerNeed'],'dualWR'),covered=score(ps['fixerCovered'],'dualWR'),noNeed=score(ps['base'],'dualWU'))
 r['offcolor']=dict(noSources=score(ps['base'],'bombR'),supported=score(dict(ps['base'],dualWR=2,dualUR=2),'bombR'),doublePip=score(dict(ps['base'],dualWR=2,dualUR=2),'doubleR'))
 r['cutline']=dict(modest=score(ps['base'],'modest'),huge=score(ps['weak'],'premium'),nonstarter=score(ps['base'],'weakW'))
 r['narrow']=score(ps['base'],'narrowUnknown');r['unsupported']=score(ps['base'],'unsupported')
 d=lambda key,label:r[key][label]['DValue'];gates=dict(fifth=d('fifth-five-drop','fifth')<d('fifth-five-drop','needed'),early=d('two-drop-hole','deficient')>d('two-drop-hole','filled'),creature=d('creatures','shortage')>d('creatures','saturated'),removal=d('removal','shortage')>=d('removal','covered') and r['removal']['shortage'].get('RemovalDelta',0)>0,
 fixing=all(r['fixing'][k]['available'] for k in ('need','noNeed')) and d('fixing','need')>d('fixing','noNeed')+.01 and d('fixing','noNeed')<=.5,
 offcolor=d('offcolor','noSources')<=.2,splash=d('offcolor','supported')>d('offcolor','noSources')+.05 and d('offcolor','doublePip')<=.2,
 upgrades=d('cutline','huge')>d('cutline','modest') and .5<d('cutline','modest')<.8,nonstarter=d('cutline','nonstarter')<.5,narrow=r['narrow'].get('StructuralDelta',0)<=0 and not r['narrow'].get('OptionalPositiveRoleBonusAllowed',True),unsupported=not r['unsupported']['available'] and r['unsupported']['DValue']==.5)
 # Controlled broad-removal fixture is explicit evidence, not a new sideboard classifier for historical cards.
 return dict(results=r,gates=gates,passAll=all(gates.values()),castability=castability_cases(),fixtures=ps,fixtureScope='Generic controlled states with declared quality/roles, no set rules or learned outcome features; actual corpus role scope is reported separately.')
def probes():
 guard();selection=load(OUT/'selection.json');results={}
 for family in ('A','B','C'):results[family]=evaluate_probes(selection['families'][family]['params'],family)
 dump(OUT/'probes.json',dict(families=results,selectedFamily=selection['selectedFamily'],selectedPass=results[selection['selectedFamily']]['passAll'],noParameterTuningAfterProbes=True))
 cases=[]
 for family,value in results.items():
  for label in ('fifth-five-drop','two-drop-hole','fixing','offcolor','cutline'):
   for variant,r in value['results'][label].items():cases.append(dict(family=family,name=label+'/'+variant,result=r))
 dump(OUT/'selected-regressions.json',dict(selectionSha256=sha(OUT/'selection.json'),cases=cases))
