"""Targeted count, prior, split and cluster invariants; no duplicate golden suite."""
import dataclasses,json,pathlib,tempfile,unittest
from unittest.mock import patch
import phase_b1_core as c

class Compatibility(unittest.TestCase):
    def row(self,deck,opening,drawn,tutored=0):return dict(deck=deck,opening=opening,drawn=drawn,tutored=tutored,sideboard=0)
    def test_raw_counts_and_duplicate_game_weights(self):
        cases=[(self.row(1,1,0),1,1),(self.row(3,0,1),1,1),(self.row(3,0,3),1,3),
               (self.row(2,1,1),1,2),(self.row(2,0,0),0,0),(self.row(1,1,2),1,3),(self.row(1,0,0,1),0,0)]
        for row,presence,copies in cases:
            w=c.count_weights(row);self.assertEqual((presence,copies),(w['presence'],w['copies']))
        with self.assertRaises(ValueError):c.count_weights(self.row(1,-1,0))
        # One winning three-instance game plus one losing singleton: .5 presence vs .75 copies.
        p,n=c.rate_units([('win',1,3),('loss',0,1)]);self.assertEqual((.75,4),(p,n))

    def evidence(self,primary=True,rate=None,n=0):
        return c.QualityEvidence('SET','QuickDraft' if primary else 'PremierDraft','card',rate,n,c.SEMANTIC,'owned primary' if primary else 'owned alternate','ALL_TIME',primary,'source hash')
    def evaluate(self,p,a,**kwargs):return c.prior(p,a,.55,.56,allowed_direction=('PremierDraft','QuickDraft'),**kwargs)
    def test_primary_wins_and_alternate_strength_and_provenance(self):
        a=self.evidence(False,.65,5000);p=self.evidence(True,.6,1000)
        r=self.evaluate(p,a);self.assertEqual('primary',r.mode);self.assertIsNone(r.alternate_source)
        tiny=self.evaluate(self.evidence(),self.evidence(False,.65,1));large=self.evaluate(self.evidence(),a)
        self.assertLess(tiny.q-.5,.002);self.assertGreater(large.q,.9);self.assertEqual('alternate-prior',large.mode)
        self.assertEqual(('source hash','source hash'),large.provenance);self.assertEqual('owned alternate',large.alternate_source)
        self.assertEqual(.5,self.evaluate(self.evidence(),None).q)

    def test_identity_direction_semantics_and_period_fail_closed(self):
        p=self.evidence();a=self.evidence(False,.65,5000)
        for invalid in (dataclasses.replace(a,set='OTHER'),dataclasses.replace(a,card='same-name-other-printing'),
                        dataclasses.replace(a,format='QuickDraft'),dataclasses.replace(a,count_semantics='presence'),
                        dataclasses.replace(a,time_period='different'),dataclasses.replace(a,provenance='')):
            self.assertEqual(.5,self.evaluate(p,invalid).q)
        self.assertEqual(.5,self.evaluate(dataclasses.replace(p,count_semantics='unknown'),dataclasses.replace(a,count_semantics='unknown')).q)
        self.assertEqual(.5,self.evaluate(dataclasses.replace(p,rate=.65,sample_count=1000,count_semantics='unknown'),a).q)
        self.assertEqual(.5,c.prior(p,a,.55,.56,allowed_direction=('QuickDraft','PremierDraft')).q)
        self.assertEqual(.55+.01,self.evaluate(p,a,bound=.01).effective_rate)

    def test_nested_copy_samples_never_split_game_outcomes(self):
        rows=[('a',1,3),('b',0,2),('c',1,1)];small=c.sample_clusters(rows,1,'key');large=c.sample_clusters(rows,4,'key')
        self.assertEqual(small,large[:len(small)]);self.assertTrue(all(x in rows for x in small));self.assertEqual(rows,sorted(c.sample_clusters(rows,'full','key')))
        self.assertEqual(c.group_hash('SET','PremierDraft','draft'),c.group_hash('SET','PremierDraft','draft'))
        self.assertNotEqual(c.group_hash('SET','PremierDraft','draft'),c.group_hash('OTHER','PremierDraft','draft'))

    def test_split_and_all_phase_b_bytes_immutable(self):
        split=c.guard();self.assertEqual(['MH3','DFT'],split['UntouchedHoldout'])
        for s in ('BLB','DSK','MH3','DFT'):
            with self.assertRaises(ValueError):c.allow(s)
        original=c.sha
        with patch.object(c,'sha',side_effect=lambda p:'changed' if str(p)=='V2_PHASE_B1_SPLIT_MANIFEST.json' else original(p)):
            with self.assertRaises(ValueError):c.guard()

    def test_replay_equivalence_hash_covers_actual_complete_output(self):
        result=c.load(c.OUT/'replay-equivalence.json');self.assertTrue(result['passed']);self.assertGreater(result['decisions'],300)
        before=pathlib.Path('data/historical/evaluation/phase-b1/profile-before.json.trace.jsonl')
        after=pathlib.Path('data/historical/evaluation/phase-b1/profile-after.json.trace.jsonl')
        self.assertEqual(before.read_bytes(),after.read_bytes())
        self.assertEqual(result['hash'],c.digest(before.read_text(encoding='utf-8')))

    def test_real_golden_portability_and_unchanged_unsummon(self):
        from phase_b1_prior import golden
        golden();result=c.load(c.OUT/'golden-portability.json');self.assertTrue(result['passed']);self.assertFalse(result['goldenUsedForFit'])
        self.assertAlmostEqual(.9765137572943683,result['Uldaros']['q'],12);self.assertAlmostEqual(48.21998625015792,result['portableExact'],10)
        self.assertAlmostEqual(.9446064139941691,result['Unsummon']['commitment'],12);self.assertAlmostEqual(.10262390670553934,result['Unsummon']['relevance'],12)

if __name__=='__main__':unittest.main()
