"""Targeted frozen-contract, quantity and physical-mana research checks."""
import unittest,inspect
from phase_f_probes import *
from phase_f_research import score_job
class PhaseFTests(unittest.TestCase):
 def setUp(self):
  self.m,self.q,self.s,self.pools=fixtures();selection=load(OUT/'selection.json');self.params=selection['families']['C']['params'];self.model=MarginalModel(self.m,self.q,self.params)
 def test_preservation_and_no_future_worker_boundary(self):
  guard()
  for s in ('MH3','DFT','BLB','DSK'):
   with self.assertRaises(ValueError):allowed(s)
  with self.assertRaises(ValueError):score_job(dict(pool={},set='DMU',picks=1,outcome={'wins':7}))
  self.assertNotIn('build',inspect.signature(MarginalModel.marginal).parameters);self.assertNotIn('outcome',inspect.signature(MarginalModel.marginal).parameters)
  self.assertFalse((OUT/'holdout-release.json').exists())
 def test_hypergeometric_and_pip_monotonicity(self):
  self.assertAlmostEqual(hyper_tail(1,10,1),.25);self.assertEqual(hyper_tail(0,10,1),0);self.assertEqual(hyper_tail(40,10,1),1)
  c=dict(self.m['earlyW'],manaValue=3,pipNeed=[2,0,0,0,0,0]);one=dict(c,pipNeed=[1,0,0,0,0,0]);scores=[castability(c,[i,0,0,0,0,0],17)['score'] for i in range(18)]
  self.assertEqual(scores,sorted(scores));self.assertLessEqual(scores[8],castability(one,[8,0,0,0,0,0],17)['score']);self.assertFalse(castability(self.m['unsupported'],[9,8,0,0,0,0],17)['supported'])
 def test_direct_fixing_and_basic_source_quantity(self):
  pool=dict(self.pools['base'],dualWU=2);b=self.model.build(pool,self.s,40);mana=b['mana'];expected=[0]*6
  for i,n in mana['nonbasic'].items():
   for k in self.m[i]['produced']:expected[k]+=n
  for k,n in mana['basics'].items():expected[int(k)]+=n
  self.assertEqual(expected,mana['sources']);self.assertEqual(sum(mana['basics'].values())+sum(mana['nonbasic'].values()),mana['lands']);self.assertGreater(mana['sources'][0]+mana['sources'][1],mana['lands'])
 def test_selected_copy_and_replacement_multisets(self):
  r=self.model.marginal(self.pools['base'],'modest',self.s,40,'C',.5,.65)
  for i,n in r['DisplacedCards'].items():self.assertEqual(n,r['BeforeMain'].get(i,0)-r['AfterMain'].get(i,0))
  for i,n in r['AddedCards'].items():self.assertEqual(n,r['AfterMain'].get(i,0)-r['BeforeMain'].get(i,0))
  self.assertEqual(r['CandidateSelected'],r['AfterMain'].get('modest',0)>r['BeforeMain'].get('modest',0))
  self.assertAlmostEqual(r['RawMarginalValue'],r['ReplacementValue']+r['StructuralDelta']+r['ManaConsistencyDelta'])
 def test_virtual_slot_is_not_an_owned_displacement(self):
  r=self.model.marginal({'midW':1},'earlyW',self.s,1,'C',.5,.65)
  self.assertGreater(r['VirtualSlotsBefore'],0);self.assertEqual(r['DisplacedCards'],{});self.assertGreater(r['VirtualReplacement'],0)
  self.assertLess(r['Confidence'],.2);self.assertEqual(virtual_level(44,3,.5),.35)
 def test_diminishing_structural_need_laws(self):
  state=dict(creatures=14,early=4,top=3,removal=3,mv3=6,mv4=4)
  def gain(key,n):
   a=dict(state);a[key]=n;b=dict(a);b[key]+=1;return sum(potential_parts(b).values())-sum(potential_parts(a).values())
  self.assertGreater(gain('early',0),gain('early',4));self.assertGreater(gain('creatures',10),gain('creatures',19));self.assertGreater(gain('top',2),gain('top',5));self.assertGreater(gain('removal',0),gain('removal',5))
 def test_absolute_eligibility_splash_and_narrow_fallback(self):
  no=self.model.marginal(self.pools['base'],'bombR',self.s,40,'C',.5,.99);yes=self.model.marginal(dict(self.pools['base'],dualWR=2,dualUR=2),'bombR',self.s,40,'C',.5,.2)
  self.assertEqual(no['AbsoluteEligibility'],0);self.assertEqual(no['DValue'],.1);self.assertGreater(yes['DValue'],no['DValue'])
  double=self.model.marginal(dict(self.pools['base'],dualWR=2,dualUR=2),'doubleR',self.s,40,'C',.5,.99);self.assertEqual(double['DValue'],.1)
  narrow=self.model.marginal(self.pools['base'],'narrowUnknown',self.s,40,'C');self.assertTrue(narrow['UnknownNarrow']);self.assertLessEqual(narrow['StructuralDelta'],0)
  unsupported=self.model.marginal(self.pools['base'],'unsupported',self.s,40,'C',.37);self.assertFalse(unsupported['available']);self.assertEqual(unsupported['DValue'],.37)
 def test_probability_is_confidence_only_and_selected_regressions(self):
  low=self.model.marginal(self.pools['base'],'modest',self.s,40,'C',.5,0);high=self.model.marginal(self.pools['base'],'modest',self.s,40,'C',.5,1)
  self.assertEqual(low['RawMarginalValue'],high['RawMarginalValue']);self.assertEqual(low['AbsoluteEligibility'],high['AbsoluteEligibility']);self.assertLessEqual(abs(low['DValue']-high['DValue']),.06+1e-12)
  artifact=load(OUT/'selected-regressions.json');self.assertEqual(artifact['selectionSha256'],sha(OUT/'selection.json'));results=load(OUT/'probes.json');self.assertFalse(results['selectedPass'])
  for case in artifact['cases']:self.assertTrue(0<=case['result']['DValue']<=1)
 def test_frozen_matching_and_final_deck_integrity(self):
  registered=load(EOUT/'pair-task-freeze.json')
  for s in DEV:self.assertEqual(sha(EDATA/f'{s}.pairs.jsonl.gz'),registered['matchingSha256'][s])
  result=load(OUT/'reconstruction.json');self.assertTrue(result['partialDecksNeverCounted']);self.assertFalse(result['allCandidateIntegrity'])
  for r in readrows(DATA/'builder-results.jsonl.gz'):
   p=r['projection'];self.assertTrue(15<=p['land']<=19)
   if p['complete']:self.assertEqual(sum(r['deck'].values()),40)
  corrected=OUT/'corrected-inputs';self.assertEqual(sha(OUT/'selection.json'),sha(corrected/'selection.json'))
  self.assertEqual(load(corrected/'prediction-run.json')['maxProductionQDifference'],0)
if __name__=='__main__':unittest.main()
