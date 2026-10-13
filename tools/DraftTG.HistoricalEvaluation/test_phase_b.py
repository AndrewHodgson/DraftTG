"""Targeted research guard/math tests. No raw dataset/network dependency."""
import json, pathlib, tempfile, unittest
from phase_b_core import *

class PhaseBTests(unittest.TestCase):
    def test_registration_bytes_and_production_freeze(self):
        splits=verify_registration();self.assertEqual({'BLB','DSK'},allowed_sets(splits,True))
        self.assertFalse(allowed_sets(splits)&allowed_sets(splits,True))
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory)
            files=['docs/historical/splits/phase-b-v1.json','docs/evidence/v2-phase-b/preregistration.json',
                   'docs/evidence/v2-phase-b/preregistration-hashes.json','docs/evidence/v2-phase-a/baseline-freeze.json']
            freeze=json.loads(pathlib.Path(files[-1]).read_text(encoding='utf-8-sig'))
            for name in files+[f['Path'] for f in freeze['Files']]:
                p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(pathlib.Path(name).read_bytes())
            verify_registration(root)
            (root/files[0]).write_bytes((root/files[0]).read_bytes()+b' ')
            with self.assertRaisesRegex(ValueError,'Frozen registration changed'):verify_registration(root)

    def test_baseline_weighting_spread_and_shrinkage(self):
        b=baseline([dict(rate=1,n=3),dict(rate=0,n=1),dict(rate=None,n=9)])
        self.assertEqual(.75,b['weighted']);self.assertEqual(.5,b['unweighted']);self.assertEqual(.5,b['spread'])
        self.assertAlmostEqual(.55,shrink(.6,500,.5,500));self.assertEqual(.5,shrink(None,100,.5,500))
        self.assertEqual(.5,shrink(float('nan'),100,.5,500))
        with self.assertRaises(ValueError):shrink(.6,10,.5,-1)

    def test_derived_pack_local_positions(self):
        p=position_features({1:1,5:3})
        self.assertEqual(4,p['count']);self.assertEqual(4,p['mean']);self.assertEqual(5,p['median'])
        self.assertEqual({'1':1,'5':3},p['distribution']);self.assertIsNone(position_features({})['mean'])
        self.assertEqual(.5,percentile(5,[1,5,9]))

    def test_format_pairing_requires_same_set(self):
        rows=[dict(set='A',format='PremierDraft',card='x'),dict(set='B',format='QuickDraft',card='x'),
              dict(set='A',format='QuickDraft',card='x'),dict(set='A',format='QuickDraft',card='y')]
        self.assertEqual(1,len(pair_formats(rows,'A','PremierDraft','QuickDraft')))
        self.assertEqual([],pair_formats(rows,'B','PremierDraft','QuickDraft'))
        with self.assertRaises(ValueError):pair_formats(rows,'A','PremierDraft','PremierDraft')

    def test_simulation_is_deterministic_and_outcome_independent(self):
        obs=[(str(i),i%2,i%3==0) for i in range(100)]
        a=sample_observations(obs,50,'A/P/card');b=sample_observations(list(reversed(obs)),50,'A/P/card')
        self.assertEqual(a,b);self.assertEqual(a,sample_observations(obs,100,'A/P/card')[:50])
        inverted=[(g,1-w,o) for g,w,o in obs]
        self.assertEqual([x[0] for x in a],[x[0] for x in sample_observations(inverted,50,'A/P/card')])
        groups={evidence_group('A','PremierDraft',str(i)) for i in range(100)};self.assertEqual({True,False},groups)

    def test_disjoint_multistat_does_not_duplicate_outcomes(self):
        observations=[('1',1,True),('2',0,False)]
        self.assertEqual(.5,predict(observations,'full',.5,0,'decomposition'))
        self.assertAlmostEqual(predict(observations,'full',.5,500),predict(observations,'full',.5,500,'decomposition'))
        self.assertEqual(.5,predict([],50,.5,500))

    def test_holdout_release_is_single_use_and_rejects_retuning(self):
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory)/'release.json';selection=dict(researchFingerprint='frozen')
            with self.assertRaises(ValueError):final_lock(path,selection,'changed')
            self.assertFalse(path.exists());final_lock(path,selection,'frozen')
            with self.assertRaises(FileExistsError):final_lock(path,selection,'frozen')
            self.assertEqual('consumed',json.loads(path.read_text())['state'])

    def test_experimental_quality_range_and_missing_data(self):
        self.assertEqual('research-q-phase-b-v1',VERSION)
        self.assertEqual(.5,quality(None,.55,.03));self.assertEqual(.5,quality(.55,.55,.03))
        self.assertTrue(0<quality(.9,.55,.03)<1);self.assertTrue(0<quality(.2,.55,.03)<1)

    def test_holdout_quality_file_cannot_be_read_during_training(self):
        from phase_b_research import load_dataset
        with self.assertRaisesRegex(ValueError,'Holdout quality remains locked'):load_dataset('BLB','PremierDraft',{})

    def test_logical_consensus_preserves_ambiguity_and_rebalanced_scope(self):
        from phase_b_qualify import Identities
        resolver=Identities.__new__(Identities);resolver.policy=dict(BonusSets={},MKMListNames=[])
        resolver.full=collections.defaultdict(list);resolver.front=collections.defaultdict(list);resolver.map={};resolver.catalog={}
        resolver.full['CARD']=[dict(id='one',oracle_id='oracle',name='Card',set='aaa',rarity='common',type_line='Creature'),
                               dict(id='two',oracle_id='oracle',name='Card',set='aaa',rarity='common',type_line='Creature')]
        mapping=resolver.resolve('AAA','Card');self.assertTrue(mapping['consensus'])
        self.assertEqual(['one','two'],mapping['printings']);self.assertEqual('historical:aaa:oracle',mapping['identity']['CanonicalId'])
        self.assertIsNone(resolver.resolve('BBB','Card')['identity']['CanonicalId'])
        self.assertIsNone(resolver.resolve('AAA','A-Card')['identity']['CanonicalId'])
        resolver.map.clear();resolver.full['CARD'][1]['oracle_id']='different'
        self.assertIsNone(resolver.resolve('AAA','Card')['identity']['CanonicalId'])

if __name__=='__main__':unittest.main()
