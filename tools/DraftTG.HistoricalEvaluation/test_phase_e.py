"""Targeted Phase E invariants and selected-result regressions; no fitting/replay."""
import unittest,inspect
from phase_e_publish import *
class PhaseETests(unittest.TestCase):
 def test_frozen_sources_and_protected_sets(self):
  guard()
  for s in ('MH3','DFT','BLB','DSK'):
   with self.assertRaises(ValueError):allowed(s)
  self.assertFalse((OUT/'holdout-release.json').exists())
 def test_pick_time_boundary_and_role_missingness(self):
  self.assertEqual(list(inspect.signature(feature).parameters),['pool','i','s','m','q','index'])
  m=meta();q=qualities();r=rows('DMU')[0];p=r['picks'][0];x,a=feature({},p['id'],'DMU',m,q,0)
  self.assertEqual(x['poolSize'],0);self.assertIn('removalUnknown',x);self.assertIn('pipUnknown',x)
  self.assertEqual(x['removalUnknown'],float(not x['removal']))
 def test_exact_build_subset_includes_quantities(self):
  m={'a':dict(name='Spell',basic=False),'b':dict(name='Plains',basic=True),'c':dict(name='Snow-Covered Plains',basic=True)}
  self.assertTrue(subset({'a':2},{'a':2,'b':17},m));self.assertFalse(subset({'a':1},{'a':2,'b':17},m));self.assertFalse(subset({}, {'c':1},m))
  with self.assertRaises(ValueError):counts([{'Card':{'CanonicalId':'a'},'Count':1},{'Card':{'CanonicalId':'a'},'Count':1}])
 def test_real_join_and_copy_label_conservation(self):
  m=meta()
  for s in SETS:
   for r in rows(s):
    self.assertTrue(subset(r['pool'],r['build'],m));self.assertEqual(sum(r['build'].values()),40)
    self.assertEqual(collections.Counter(p['id'] for p in r['picks']),r['pool'])
    for i,n in r['pool'].items():
     if not m[i]['basic']:self.assertEqual(n*(r['build'].get(i,0)/n),r['build'].get(i,0))
 def test_fractional_probabilities_and_calibration(self):
  self.assertAlmostEqual(metrics([.5],[2/3])['Brier'],.25)
  self.assertAlmostEqual(metrics([.5],[2/3])['LogLoss'],math.log(2))
  models=calibrated_models()
  self.assertFalse({'Q','cutDelta','qualityDelta'}&set(models['NO-Q-strict']['features']))
  for model in models.values():
   self.assertNotIn('set',model['features']);self.assertNotIn('id',model['features']);self.assertNotIn('outcome',model['features'])
   self.assertEqual(len(model['features']),len(model['coefficients']))
 def test_curve_and_copy_reconstruction(self):
  m={'a':dict(name='a',land=False,manaValue=2,creature=True,colors=[0],roleProfile={'RemovalWeight':0}),'b':dict(name='b',land=False,manaValue=5,creature=False,colors=[0],roleProfile={'RemovalWeight':0})}
  self.assertEqual(curve({'a':3,'b':2},m),[0,0,3,0,0,2,0,0])
  r=reconstruction({'a':2,'b':1,'basic:W':17},{'a':3,'b':2},m)
  self.assertAlmostEqual(r['nonlandRecall'],3/5);self.assertAlmostEqual(r['nonlandF1'],6/8)
 def test_matching_integrity_and_determinism(self):
  m=meta();q=qualities();freeze=load(OUT/'pair-task-freeze.json')
  for s in DEV:
   self.assertEqual(sha(DATA/f'{s}.pairs.jsonl.gz'),freeze['matchingSha256'][s]);seen=set()
   drafts={r['draft']:r for r in rows(s)}
   for r in readrows(DATA/f'{s}.pairs.jsonl.gz'):
    key=(r['draft'],r['positive'],r['negative']);self.assertNotIn(key,seen);seen.add(key)
    b=drafts[r['draft']];self.assertIn(r['positive'],b['build']);self.assertNotIn(r['negative'],b['build'])
    self.assertLessEqual(abs(q.get((s,r['positive']),.5)-q.get((s,r['negative']),.5)),.1)
 def test_selected_regressions_and_bounded_transitions(self):
  models=calibrated_models();cases=load(OUT/'selected-regressions.json')
  self.assertEqual(cases['selectionSha256'],sha(OUT/'selection.json'))
  for r in cases['cases']:
   d,p=dscores(r['features'],models)
   for k in d:self.assertAlmostEqual(d[k],r['D'][k],places=12);self.assertTrue(0<=d[k]<=1)
  self.assertFalse(load(OUT/'failure-probes.json')['allRequiredStrategicBehaviorQualified'])
if __name__=='__main__':unittest.main()
