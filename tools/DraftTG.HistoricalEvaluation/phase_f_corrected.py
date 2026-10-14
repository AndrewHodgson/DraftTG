"""Same frozen candidates, independently identified production-Q input correction.

Never refits, retunes, modifies previous outputs or reruns production scoring.
"""
import argparse,shutil
import phase_f_core as core
import phase_f_research as research
import phase_f_publish as publish
import phase_f_probes as probesModule
from phase_f_core import *
from phase_e_models import calibrated_models as emodels,predict as epredict
ORIGINAL_OUT=OUT;ORIGINAL_DATA=DATA;ORIGINAL_RUN=RUN
NEW_OUT=OUT/'corrected-inputs';NEW_DATA=DATA/'corrected-inputs';NEW_RUN=RUN/'corrected-inputs'
def route():
 core.DATA=NEW_DATA;core.RUN=NEW_RUN
 for module in (research,publish,probesModule):module.OUT=NEW_OUT;module.DATA=NEW_DATA;module.RUN=NEW_RUN
def prepare_correction():
 guard();NEW_OUT.mkdir(parents=True,exist_ok=True);NEW_DATA.mkdir(parents=True,exist_ok=True);NEW_RUN.mkdir(parents=True,exist_ok=True)
 if (NEW_OUT/'correction-registration.json').exists():raise ValueError('Correction already registered; no reset')
 originalFiles={p.as_posix():sha(p) for root in (ORIGINAL_OUT,ORIGINAL_DATA) for p in root.glob('*') if p.is_file()}
 frozenSelection=load(ORIGINAL_OUT/'selection.json');q=equalities()
 # Exact observed production mapping deviations were already audited in Phase E.
 for example in load(EOUT/'frozen-Q-compatibility.json')['examples']:q[example['set'],example['id']]=example['productionQ']
 dump(NEW_DATA/'quality.json',[dict(set=s,id=i,Q=v) for (s,i),v in sorted(q.items())]);shutil.copyfile(ORIGINAL_DATA/'metadata.json',NEW_DATA/'metadata.json');shutil.copyfile(ORIGINAL_OUT/'selection.json',NEW_OUT/'selection.json')
 mE=load(EDATA/'metadata.json');qE=equalities();models=emodels();restored=0
 from phase_e_core import feature as efeature
 for s in SETS:
  jobs=list(readrows(ORIGINAL_DATA/f'{s}.pair-jobs.jsonl.gz'))
  for r in jobs:
   for i in r['candidates']:
    if r['probabilities'][i] is None and abs(q.get((s,i),.5)-qE.get((s,i),.5))<1e-9:
     pool=dict(r['pool']);pool[i]-=1
     if not pool[i]:del pool[i]
     x,a=efeature(pool,i,s,mE,qE,44);r['probabilities'][i]=epredict(models['E-C'],x);restored+=1
  write_rows(NEW_DATA/f'{s}.pair-jobs.jsonl.gz',jobs)
 dump(NEW_OUT/'correction-registration.json',dict(originalSelectionSha256=sha(ORIGINAL_OUT/'selection.json'),correctedSelectionSha256=sha(NEW_OUT/'selection.json'),frozenModelSourceSha256=sha('tools/DraftTG.HistoricalEvaluation/phase_f_model.py'),frozenEProbabilitySha256=sha(EDATA/'models-calibrated.json'),qualitySha256=sha(NEW_DATA/'quality.json'),originalFiles=originalFiles,restoredSupportingInference=restored,parameterChanges=False,newCalibration=False,newProductionReplay=False,reason='Correct the over-broad exact-full-display-name neutralization. Reuse original frozen quality export and only its previously proven production neutral exception. Retain original compatibility-failing run and its independent diagnostics.'))
def check():
 guard();r=load(NEW_OUT/'correction-registration.json')
 for p,h in r['originalFiles'].items():
  if sha(p)!=h:raise ValueError('Original F run changed '+p)
 if sha(ORIGINAL_OUT/'selection.json')!=sha(NEW_OUT/'selection.json') or sha('tools/DraftTG.HistoricalEvaluation/phase_f_model.py')!=r['frozenModelSourceSha256']:raise ValueError('Corrected run retuned selection/model')
 if sha(NEW_DATA/'quality.json')!=r['qualitySha256']:raise ValueError('Corrected quality changed')
 if (ORIGINAL_OUT/'completed-evidence.json').exists():raise ValueError('Phase F complete; corrected experiment is frozen. Use phase_f.py summary/report.')
def main():
 from phase_b import initialize_telemetry
 initialize_telemetry();parser=argparse.ArgumentParser();parser.add_argument('command',choices=['prepare','pairs','builders','predict','contextual','benchmark','mana-audit','cold']);cmd=parser.parse_args().command
 if cmd=='prepare':prepare_correction();return
 check();route()
 {'pairs':research.pair_evaluate,'builders':publish.builders,'predict':publish.predict,'contextual':publish.contextual,'benchmark':publish.benchmark,'mana-audit':publish.mana_audit,'cold':publish.corrected_cold}[cmd]()
if __name__=='__main__':main()
