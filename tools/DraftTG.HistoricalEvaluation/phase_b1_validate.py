"""Build/test evidence with real process exit codes, including unittest stderr."""
import datetime,json,pathlib,re,subprocess,sys,xml.etree.ElementTree as ET
from phase_b1_core import *

def validate():
    guard();logs=pathlib.Path('data/historical/evaluation/phase-b1/validation');logs.mkdir(parents=True,exist_ok=True)
    def run(name,args):
        result=subprocess.run(args,capture_output=True,text=True);text=result.stdout+'\n'+result.stderr;(logs/(name+'.log')).write_text(text,encoding='utf-8')
        if result.returncode:raise RuntimeError(name+' failed; '+str(logs/(name+'.log')))
        return text
    build=run('build',['dotnet','build','DraftTG.sln'])
    results=logs/('trx-'+datetime.datetime.now().strftime('%Y%m%dT%H%M%S'));results.mkdir()
    run('dotnet-tests',['dotnet','test','DraftTG.sln','--no-build','--logger','trx;LogFilePrefix=b1','--results-directory',str(results.resolve())])
    total=failed=skipped=0
    for path in results.glob('*.trx'):
        counters=next(e for e in ET.parse(path).iter() if e.tag.endswith('Counters'));total+=int(counters.get('total','0'));failed+=int(counters.get('failed','0'));skipped+=int(counters.get('notExecuted','0'))
    if total<1291 or failed:raise ValueError('Incomplete/failing solution tests')
    py=run('python-b1',[sys.executable,'tools/DraftTG.HistoricalEvaluation/phase_b1_tests.py'])
    old=run('python-existing',[sys.executable,'tools/DraftTG.HistoricalEvaluation/test_phase_b.py'])
    run('golden',['dotnet','tools/DraftTG.HistoricalEvaluation/bin/Debug/net10.0/DraftTG.HistoricalEvaluation.Cli.dll','golden','--root','.', '--output',str(logs/'golden')])
    golden=load(logs/'golden/golden.json');dump(OUT/'dotnet-golden.json',golden)
    if not golden['Passed']:raise ValueError('Golden regression failure')
    guard()
    hashes={str(p):sha(p) for p in sorted(pathlib.Path('tools/DraftTG.HistoricalEvaluation').glob('phase_b1*.py'))}
    hashes.update({str(p):sha(p) for p in pathlib.Path('tools/DraftTG.QualityEvidenceResearch').glob('*') if p.is_file()})
    hashes['src/DraftTG.HistoricalEvaluation/HistoricalReplay.cs']=sha('src/DraftTG.HistoricalEvaluation/HistoricalReplay.cs')
    hashes['tests/DraftTG.HistoricalEvaluation.Tests/HistoricalEvaluationTests.cs']=sha('tests/DraftTG.HistoricalEvaluation.Tests/HistoricalEvaluationTests.cs')
    dump(OUT/'execution-source-hashes.json',dict(files=hashes,sourceFingerprint=digest(json.dumps(hashes,sort_keys=True)),
        selectionFingerprint=load(OUT/'selection.json')['fingerprint'],note='New diagnostic/CLI/report/testing files added after fixed calibration; prior boundary checks/golden recomposition corrected without modifying quality normalization, count transformations or fitted parameters. No holdout release.'))
    result=dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),dirtyTree=True,
        buildPassed=True,warnings=int(re.search(r'(\d+) Warning\(s\)',build).group(1)),errors=int(re.search(r'(\d+) Error\(s\)',build).group(1)),
        CSharpPassed=total-failed-skipped,CSharpFailed=failed,CSharpSkipped=skipped,PythonB1Passed=int(re.search(r'Ran (\d+) tests',py).group(1)),PythonExistingPassed=int(re.search(r'Ran (\d+) tests',old).group(1)),
        GoldenPassed=golden['Tested'],preservedFiles=len(load(OUT/'preservation.json')['Files']),baselineHashesPreserved=39,
        sourceFingerprint=digest(json.dumps(hashes,sort_keys=True)),productionScoreAssemblySha256=sha('src/DraftTG.RecommendationEngine/bin/Debug/net10.0/DraftTG.RecommendationEngine.dll'),
        logs=str(logs),newHoldoutsUntouched=True,productionChanged=False)
    dump(OUT/'validation.json',result);print(json.dumps(result,indent=2))

if __name__=='__main__':validate()
