import copy,unittest
from phase_c_core import *

class LaneTests(unittest.TestCase):
    def feature(self,p=3,n=1000):return features(histogram({p:n}),n)
    def input(self,p,pack=1):return dict(SetCode='SET',Format='PremierDraft',DraftId='draft',PackNumber=pack,PickNumber=p,GlobalPickIndex=(pack-1)*14+p-1,
        OfferedCards=[dict(Card=dict(CanonicalId='red'),Count=1)])
    def resource(self,colors=[3],land=False):
        feat=self.feature();return {'SET/PremierDraft':dict(set={'all':feat},cards={'red':dict(seen={'all':feat})})},{'red':dict(colors=colors,land=land)},{('SET','red'):.8}
    def signal(self,i,f='A',colors=[3],land=False):
        return observation(i,*self.resource(colors,land),f,20,False)
    def test_index_expectation_confidence_and_surprise(self):
        with self.assertRaises(ValueError):histogram({0:1})
        with self.assertRaises(ValueError):card_signal(0,self.feature(),'A',20,self.feature())
        early,_=card_signal(2,self.feature(),'B',20,self.feature());late,_=card_signal(8,self.feature(),'B',20,self.feature());neutral,_=card_signal(3,self.feature(),'A',20,self.feature())
        self.assertLess(early,0);self.assertGreater(late,0);self.assertEqual(0,neutral);self.assertLess(confidence(20,20),confidence(10000,20))
        positions,meta,q=self.resource();q[('SET','red')]=.4
        self.assertEqual([0.]*5,observation(self.input(8),positions,meta,q,'A',20,False)[0])
        self.assertEqual(([0.]*5,[0.]*5,0.),observation(self.input(8),{},meta,q,'A',20,False))
    def test_open_start_and_clear_signal_are_smooth(self):
        state=Accumulator(16,.5,0)
        for p in (1,2):self.assertEqual([0.]*5,state.observe(self.input(p),self.signal(self.input(p)))['colors'])
        red=[]
        for p in range(3,9):red.append(state.observe(self.input(p),self.signal(self.input(p)))['colors'][3])
        self.assertEqual(sorted(red),red);self.assertLess(red[-1],1)
    def test_age_boundary_negative_and_recovery(self):
        state=Accumulator(4,.25,.25);result=None
        for p in range(1,9):result=state.observe(self.input(p),([0,0,0,0,1],[0]*5,1))
        before=result['colors'][4]
        for p in range(9,14):result=state.observe(self.input(p),([0,0,1,0,0],[0,0,0,0,-1],1))
        self.assertGreater(result['colors'][2],result['colors'][4]);self.assertLess(result['colors'][4],before)
        previous=max(result['colors']);new=state.observe(self.input(1,2),([0]*5,[0]*5,0));self.assertLess(max(new['colors']),previous)
        if (OUT/'behavior-probes.json').exists():
            probe=load(OUT/'behavior-probes.json')['candidates']['A']
            self.assertGreater(probe['blueAt14'],probe['blueAt8'])
            self.assertFalse(load(OUT/'selection.json')['convincing']) # Known contested-color counterexample remains unqualified.
    def test_gold_colorless_lands_and_one_card_does_not_define_forever(self):
        i=self.input(8);mono=self.signal(i);gold=self.signal(i,colors=[1,3]);colorless=self.signal(i,colors=[]);land=self.signal(i,colors=[3],land=True)
        self.assertLess(gold[0][3],mono[0][3]);self.assertEqual([0]*5,colorless[0]);self.assertEqual([0]*5,land[0])
        state=Accumulator(4,.25,0);first=state.observe(i,mono)
        for p in range(9,15):last=state.observe(self.input(p),([0]*5,[0]*5,0))
        self.assertLess(last['colors'][3],first['colors'][3])
    def test_state_determinism_labels_ignored_and_no_future(self):
        a=Accumulator(8,.5,0);b=Accumulator(8,.5,0);i=self.input(8);extra=copy.deepcopy(i);extra.update(FinalWins=7,FuturePicks=['black'],FinalDeck=['blue'])
        self.assertEqual(self.signal(i),self.signal(extra));self.assertEqual(a.observe(i,self.signal(i)),b.observe(extra,self.signal(extra)))
        with self.assertRaises(ValueError):a.observe(i,self.signal(i))
        with self.assertRaises(ValueError):a.observe(self.input(7),self.signal(self.input(7)))
    def test_future_labels_are_separate_and_invalid_windows_decline(self):
        from phase_c_research import future_mass
        rows=[dict(Inputs=self.input(p),Label=dict(ActualPickedCard=dict(CanonicalId='red'))) for p in range(1,6)]
        _,meta,q=self.resource();self.assertEqual([0,0,0,1,0],future_mass(rows,0,3,meta,q))
        broken=copy.deepcopy(rows);broken[2]['Inputs']['GlobalPickIndex']=99;self.assertIsNone(future_mass(broken,0,3,meta,q))
        broken=copy.deepcopy(rows);broken[2]['Inputs']['OfferedCards'][0]['Card']['CanonicalId']=None;self.assertIsNone(future_mass(broken,0,3,meta,q))
    def test_protected_sources_and_split(self):
        split=guard();self.assertEqual(['MH3','DFT'],split['UntouchedHoldout'])
        for s in ('BLB','DSK','MH3','DFT'):
            with self.assertRaises(ValueError):allowed(s)

if __name__=='__main__':unittest.main()
