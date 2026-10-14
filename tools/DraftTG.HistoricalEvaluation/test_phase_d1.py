"""Grouped D1 safety/statistical tests; existing D C# tests protect A-only recomposition."""
import unittest,copy
from phase_d1_models import *

class D1Tests(unittest.TestCase):
    def metadata(self):
        role=dict(ReliableRoles='None',Roles='None',RemovalWeight=0,HasStructuredMetadata=True)
        return {'r':dict(colors=[3],land=False,basic=False,known=True,typeKnown=True,creature=True,manaValue=2,roleProfile=role),
            'g':dict(colors=[4],land=False,basic=False,known=True,typeKnown=True,creature=False,manaValue=3,roleProfile=role),
            'b':dict(colors=[],land=True,basic=True,known=True,typeKnown=True,creature=False,manaValue=0,roleProfile=role)}
    def toy(self):
        m=self.metadata();rs=[dict(cards={'r':2,'b':17} if j%2 else {'g':2,'b':17},key=str(j)) for j in range(80)];d,b,groups=population('TST',rs,m);return m,rs,d,b,groups
    def test_freeze_and_disjoint_source_holdout_protection(self):
        guard()
        for s in ('MH3','DFT','BLB','DSK','OTHER'):
            with self.assertRaises(ValueError):allowed(s)
        key=rows('DMU','target')[0]['draft']
        with self.assertRaises(ValueError):ensure_fit('DMU',[dict(cards={},key=key)])
        self.assertNotEqual('utility','confidence');self.assertEqual(audit_part('DMU','key'),audit_part('DMU','key'))
    def test_generic_inclusion_structural_and_missing_roles(self):
        m,rs,d,b,_=self.toy();self.assertNotIn('b',d['vocabulary']);x=feature_matrix('TST',d,'R',m,{});self.assertEqual(x.shape,(2,len(FEATURES)));self.assertTrue(np.all(x[:,FEATURES.index('removalUnknown')]==1))
        bad=copy.deepcopy(rs);bad[0]['wins']=7
        with self.assertRaises(ValueError):population('TST',bad,m)
        fit=logistic_fit(np.array([[1,-1],[1,1]],dtype=float),np.array([.1,.9]),np.array([100,100]),10);p=sigmoid(np.array([[1,-1],[1,1]])@fit);self.assertLess(p[0],p[1]);self.assertTrue(np.isfinite(logloss([.2,.8],[0,1])).all())
    def test_residual_popularity_explained_and_low_support(self):
        delta,c,se=card_residual(np.array([20.,0]),200,np.array([.1,.1]));self.assertAlmostEqual(0,delta[0]);self.assertEqual(0,c[0]);self.assertEqual(0,c[1])
        _,low,_=card_residual(np.array([2.]),100,np.array([.01]));_,high,_=card_residual(np.array([100.]),1000,np.array([.05]));self.assertLess(low[0],high[0]);self.assertLessEqual(low[0],2/52)
    def test_utility_false_factor_and_uncertainty_support(self):
        self.assertEqual(0,paired_utility([.1]*9)['weight']);self.assertEqual(0,paired_utility([-.05]*100)['weight']);self.assertGreater(paired_utility([.05]*100)['weight'],0)
        a=np.array([[1,0],[0,1]],dtype=float);b=np.array([[0,-1],[1,0]],dtype=float);aligned,s=align_loadings(a,b);self.assertAlmostEqual(1,s);self.assertTrue(np.allclose(a,aligned))
    def test_pair_expected_support_sign_and_se(self):
        self.assertEqual((0.,0.),pair_uncertainty(2,2,2,2,[1,1],1));self.assertEqual((0.,0.),pair_uncertainty(40,20,50,50,[1,-1],15))
        e,c=pair_uncertainty(100,20,200,200,[1,1],10);self.assertGreater(e,0);self.assertLessEqual(abs(e),1);self.assertLess(c,1)
        e,_=pair_uncertainty(0,30,80,80,[-1,-1],10);self.assertLess(e,0)
    def test_monotone_cold_and_confidence_not_empty_pass(self):
        mapping=cold_envelope([50,100,250,500],[0,.5,.25,1]);w=[cold_weight(n,mapping) for n in (0,50,75,100,250,500,1000)];self.assertEqual(sorted(w),w);self.assertEqual(0,w[0]);self.assertLessEqual(mapping['weights'][1],.5)
        self.assertEqual([0.,.02,.02],pav([0,.04,0],[10,10,10]));self.assertEqual(0,bin_conf(0));self.assertEqual(4,bin_conf(1))
        if (OUT/'completion.json').exists():
            u=load(OUT/'completion.json')['uncertainty']
            if u['positiveConfidenceTasks']<100:self.assertFalse(u['qualified'])
    def test_factor_formulations_deterministic_and_generic_bias_separate(self):
        b=np.array([[1,0,1],[0,1,0],[1,1,0],[0,0,1]]*10,dtype=float);bias=logit(np.array([.5,.5,.5]))
        for method in ('logistic','residual-nmf'):
            u,v=latent_fit(b,bias,2,method);u1,v1=latent_fit(b,bias,2,method);self.assertTrue(np.array_equal(v,v1));self.assertTrue(np.isfinite(v).all());self.assertTrue(np.allclose(bias,logit([.5,.5,.5])))
    def test_pool_candidate_splash_and_empty_fallback(self):
        if not (DATA/'DMU.utility-model.json').exists():return
        model=load(DATA/'DMU.utility-model.json');inf=Residual(model,meta(),qualities());r=rows('DMU')[0];state=inf.pool(r['cards']);before=[a.copy() for a in state['latent'].values()];ids=list(r['cards'])
        inf.predict(state,ids,'B');self.assertTrue(all(np.array_equal(a,b) for a,b in zip(before,state['latent'].values())));self.assertAlmostEqual(1,sum(state['shellWeights']))
        self.assertTrue(all(v['confidence']==0 for v in inf.predict(None,ids,'C')));self.assertFalse(inf.predict(state,['unknown'],'C')[0]['applicable'])
        m=self.metadata();m['u']=dict(colors=[1],land=False);self.assertEqual('RG',shell({'r':12,'g':10,'u':1},m))
    def test_primary_matching_coverage_never_imputed(self):
        baseline=load(OUT/'baseline-freeze.json');self.assertFalse(baseline['fullPrimaryCoverage']);self.assertEqual(0,baseline['coverage']['MKM'])
        row=next(r for r in baseline['results'] if r['set']=='MKM' and r['partition']=='target');self.assertIsNone(row['metrics']['color-only']['MRR'])
        if (OUT/'selection.json').exists():self.assertFalse(load(OUT/'selection.json')['primaryGates']['coverage'])
    def test_scalar_logloss_and_tied_control_rank(self):
        self.assertTrue(np.allclose(logloss([.1,.9],[0,1]),-np.log([.9,.9])));tie=rank_metrics([.5]*5);self.assertAlmostEqual(.45666666666666667,tie['MRR']);self.assertAlmostEqual(.2,tie['Top1'])
if __name__=='__main__':unittest.main()
