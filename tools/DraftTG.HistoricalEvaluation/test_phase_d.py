import copy,unittest
from phase_d_core import *
from phase_d_models import fit,Inference

class AffinityTests(unittest.TestCase):
    def metadata(self):return {'r':dict(colors=[3],basic=False,land=False,known=True),'g':dict(colors=[4],basic=False,land=False,known=True),'b':dict(colors=[],basic=True,land=True,known=True),'gold':dict(colors=[3,4],basic=False,land=False,known=True)}
    def build(self):return dict(Games=5,Configurations=1,MainDeck=[dict(Card=dict(CanonicalId='r'),Count=23),dict(Card=dict(CanonicalId='b'),Count=17)])
    def model(self):
        m=self.metadata();rs=[dict(cards={'r':2,'b':17} if j%2 else {'g':2,'b':17},key=str(j)) for j in range(80)]
        return fit(rs,m,latent=False)[0],m,rs
    def test_build_eligibility_known_mode_and_quantities(self):
        m=self.metadata();r=self.build();self.assertIsNone(build_eligibility(r,m));r['Configurations']=2;self.assertIsNotNone(build_eligibility(r,m));r['Configurations']=1;r['MainDeck'][0]['Count']=-1;self.assertIsNotNone(build_eligibility(r,m))
        r=self.build();r['Games']=2;self.assertIsNotNone(build_eligibility(r,m));r=self.build();r['MainDeck'][0]['Card']['CanonicalId']='missing';self.assertIsNotNone(build_eligibility(r,m))
    def test_vectors_basics_and_color_shell_splash(self):
        model,m,_=self.model();self.assertNotIn('b',model['vocabulary']);self.assertEqual('RG',shell({'r':12,'g':10},m));self.assertEqual('R',shell({'r':22,'g':1},m,'broad'))
        self.assertTrue(np.array_equal(vector({'r':3},['r','g'],m,'binary'),[1,0]));self.assertAlmostEqual(1,vector({'r':3,'g':1},['r','g'],m,'counts').sum())
    def test_no_outcome_or_target_input_to_fit(self):
        model,m,rs=self.model();bad=copy.deepcopy(rs);bad[0]['wins']=7
        with self.assertRaises(ValueError):fit(bad,m)
        for s in ('MH3','DFT','BLB','DSK'):
            with self.assertRaises(ValueError):allowed(s)
        self.assertTrue(audit_group('DMU','d')==audit_group('DMU','d'));guard()
    def test_frequency_shrinkage_and_low_pair_support(self):
        model,m,_=self.model();self.assertTrue(all(0<p<1 for d in model['shells'].values() for p in d['p']));self.assertEqual((0.,0.),pair_effect(2,2,2,2))
        e,c=pair_effect(40,20,60,80);self.assertGreater(e,0);self.assertLess(e,1);self.assertLess(c,1)
        e,_=pair_effect(0,30,50,50);self.assertLess(e,0)
    def test_soft_membership_pool_and_candidate_marginality(self):
        model,m,_=self.model();inf=Inference(model,m,{},'AAA');state=inf.pool({'r':4});self.assertAlmostEqual(1,state['shellweights'].sum());self.assertTrue(all(abs(x.sum()-1)<1e-10 for x in state['belief'].values()))
        r,g=inf.score(state,['r','g'],'A');self.assertGreater(r[0],g[0]);again=inf.score(state,['r','g'],'A');self.assertEqual([r,g],again)
        self.assertEqual(state['size'],1);self.assertNotIn('candidate',state)
    def test_cold_missing_and_empty_decline_confidence(self):
        model,m,_=self.model();inf=Inference(model,m,{},'AAA');self.assertEqual((0.,0.,False),inf.score(inf.pool({}),['r'])[0]);self.assertFalse(inf.score(inf.pool({'r':1}),['missing'])[0][2])
        one=inf.score(inf.pool({'r':1}),['g'],cold=500)[0];many=inf.score(inf.pool({'r':8}),['g'],cold=50)[0];self.assertLess(one[1],many[1]);self.assertLess(many[1],1)
    def test_factor_determinism_support_and_hybrid(self):
        x=np.array([[1,0,1],[0,1,1],[1,0,1],[0,1,1]],dtype=float);w,h=nmf(x,2,7);w1,h1=nmf(x,2,7);self.assertTrue(np.array_equal(h,h1));self.assertAlmostEqual(1,stability(h,h1));self.assertTrue(np.allclose(memberships(x,h).sum(axis=1),1))
    def test_common_matched_task_ties_and_no_labels_in_pool_api(self):
        ties=rank_metrics([1]*5);self.assertAlmostEqual(.2,ties['Top1']);self.assertAlmostEqual(.6,ties['Top3']);self.assertAlmostEqual(sum(1/i for i in range(1,6))/5,ties['MRR'])
        model,m,_=self.model();inf=Inference(model,m,{},'AAA');before=inf.score(inf.pool({'r':4}),['r','g']);after=inf.score(inf.pool({'r':4}),['r','g']);self.assertEqual(before,after)
        if (OUT/'archetype-regression-cases.json').exists():
            casefile=load(OUT/'archetype-regression-cases.json');selection=load(OUT/'selection.json');self.assertEqual(sha(OUT/'selection.json'),casefile['selectionSha256']);family=selection['best'];par=selection['parameters'][family]
            for case in casefile['cases']:
                self.assertNotIn(case['true'],case['pool']);self.assertFalse(evidence_group(case['set'],'PremierDraft',case['draft']))
                real=Inference(load(DATA/f"{case['set']}.model.json"),meta(),qualities(),case['set']);values=real.score(real.pool(case['pool'],par['weight']),case['candidates'],family,par['cold']);expected=[.5+c*(f-.5) if a else .5 for f,c,a in values]
                self.assertTrue(np.allclose(expected,case['scores'][family],rtol=0,atol=1e-12))
if __name__=='__main__':unittest.main()
