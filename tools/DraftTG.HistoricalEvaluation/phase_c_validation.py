"""Publish final build/TRX/golden evidence and run only the Phase C research tests."""
import json,pathlib,re,subprocess,sys,xml.etree.ElementTree as ET
from phase_c_core import *

def validate():
    guard();logs=pathlib.Path('data/historical/evaluation/phase-c/validation');logs.mkdir(parents=True,exist_ok=True);python={}
    for name,file in [('phase-c','test_phase_c.py')]:
        result=subprocess.run([sys.executable,str(pathlib.Path(__file__).with_name(file))],capture_output=True,text=True)
        text=result.stdout+'\n'+result.stderr;(logs/(name+'.log')).write_text(text,encoding='utf-8')
        if result.returncode:raise ValueError('Research tests failed '+name)
        python[name]=int(re.search(r'Ran (\d+) tests',text).group(1))
    counts=collections.Counter()
    for file in pathlib.Path('data/historical/evaluation/phase-c/tests-completion').glob('*.trx'):
        c=next(x for x in ET.parse(file).iter() if x.tag.endswith('Counters'))
        for name in ('total','passed','failed','notExecuted'):counts[name]+=int(c.get(name,'0'))
    if counts['total']!=1295 or counts['failed'] or counts['notExecuted']:raise ValueError('Incomplete final solution results')
    golden=load('data/historical/evaluation/phase-c/golden/golden.json')
    if not golden['Passed'] or golden['Tested']!=40:raise ValueError('Golden suite failed')
    dump(OUT/'dotnet-golden.json',golden)
    build=pathlib.Path('data/historical/phase-c-build-completion.log').read_text(encoding='utf-16' if pathlib.Path('data/historical/phase-c-build-completion.log').read_bytes()[:2] in (b'\xff\xfe',b'\xfe\xff') else 'utf-8-sig')
    if 'Build succeeded.' not in build or '0 Warning(s)' not in build or '0 Error(s)' not in build:raise ValueError('Build not clean')
    sources={str(p):sha(p) for p in sorted(pathlib.Path(__file__).parent.glob('phase_c*.py'))}
    sources['tools/DraftTG.HistoricalEvaluation/test_phase_c.py']=sha('tools/DraftTG.HistoricalEvaluation/test_phase_c.py')
    for p in ('src/DraftTG.HistoricalEvaluation/ExperimentalLaneSubstitution.cs','tools/DraftTG.QualityEvidenceResearch/Program.cs','tests/DraftTG.HistoricalEvaluation.Tests/ExperimentalLaneTests.cs'):sources[p]=sha(p)
    dump(OUT/'execution-sources.json',dict(files=sources,fingerprint=digest(json.dumps(sources,sort_keys=True))))
    result=dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),dirtyTree=True,buildPassed=True,warnings=0,errors=0,
        CSharp=dict(counts),python=python,golden=40,preservedFiles=len(load(OUT/'preservation.json')['files']),productionChanged=False,
        buildLog='data/historical/phase-c-build-completion.log',testResults='data/historical/evaluation/phase-c/tests-completion',
        validationScope='Final build, complete C# suite, seven Phase C research tests and 40 golden regressions only; no research rerun',
        initialTestFailure='Existing WGC HundredScopedCaptures ResourcesBounded assertion failed once in solution-wide test; isolated and final full-suite reruns passed. No capture code changed; exact cause not established.',
        sourceFingerprint=digest(json.dumps(sources,sort_keys=True)),scoreAssemblySha256=sha('src/DraftTG.RecommendationEngine/bin/Debug/net10.0/DraftTG.RecommendationEngine.dll'),
        reproBaselineUnchanged=load(OUT/'contextual-replay.json')['Metrics']['current | overall']==load('docs/evidence/v2-phase-b1/contextual-replay.json')['Metrics']['current | overall'])
    dump(OUT/'validation.json',result);print(json.dumps(result,indent=2));guard()

if __name__=='__main__':validate()
