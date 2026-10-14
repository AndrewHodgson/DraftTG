"""Phase E commands. Existing phases immutable; completed studies cannot retune."""
import argparse,subprocess
from phase_e_publish import *
def main():
 from phase_b import initialize_telemetry
 initialize_telemetry();p=argparse.ArgumentParser();p.add_argument('command',choices=['verify','qualify','features','pair-task','fit','calibrate','builder-audit','builders','pair-current','compare','replay','prepare-replay','contextual','cold','probes','validate','final','summary','report']);cmd=p.parse_args().command;guard()
 if (OUT/'completed-evidence.json').exists():
  for path,h in load(OUT/'completed-evidence.json')['files'].items():
   if sha(path)!=h:raise ValueError('Completed Phase E evidence changed: '+path)
  if cmd not in ('verify','summary','report'):raise ValueError('Phase E completed: use summary/report. No repeated fits, replay, selection or release in this published workspace.')
 if (OUT/'selection.json').exists() and cmd in ('qualify','features','pair-task','fit'):raise ValueError('Selection frozen; cannot change training/task inputs')
 if cmd=='verify':print('All frozen prior source and evidence hashes match.');return
 if cmd=='qualify':qualify()
 elif cmd=='features':features()
 elif cmd=='pair-task':pair_tasks()
 elif cmd=='fit':fit_models()
 elif cmd=='calibrate':probability_calibration();strict_noq_audit()
 elif cmd=='builder-audit':
  with open(DATA/'final-pools.jsonl','w',encoding='utf8') as f:
   for s in SETS:
    for r in rows(s):f.write(json.dumps({k:r[k] for k in ('set','draft','pool')})+'\n')
  subprocess.run(['dotnet',DLL,'--mode','builder','--input',str(DATA/'final-pools.jsonl'),'--output',str(RUN/'current-builder.jsonl')],check=True);index_current()
 elif cmd=='builders':evaluate_builders()
 elif cmd=='pair-current':
  pair_inputs();subprocess.run(['dotnet',DLL,'--mode','pairs','--input',str(DATA/'pair-inputs-complete.jsonl'),'--output',str(RUN/'current-pair-D.jsonl')],check=True);import_pairs()
 elif cmd=='compare':compare()
 elif cmd=='replay':
  if (RUN/'production.jsonl.gz').exists():raise ValueError('Production replay already exists. Use prepare-replay/contextual; no repeated expensive replay.')
  subprocess.run(['dotnet',DLL,'--mode','replay','--output',str(RUN/'production.jsonl.gz')],check=True)
 elif cmd=='prepare-replay':prepare_replay()
 elif cmd=='contextual':
  subprocess.run(['dotnet',DLL,'--mode','compose','--input',str(RUN/'compose-input.jsonl'),'--output',str(RUN/'compose-output.jsonl')],check=True);replay_summary()
 elif cmd=='cold':cold()
 elif cmd=='probes':probes();freeze_regressions()
 elif cmd=='validate':validate()
 elif cmd=='final':
  gate()
  if load(OUT/'development-gate.json')['passAll']:raise ValueError('All gates passed: final holdout release must use a separately frozen source-qualified release implementation; production promotion remains prohibited.')
  finalize()
 elif cmd=='summary':summary_display()
 elif cmd=='report':
  from phase_e_report import report
  report()
if __name__=='__main__':main()
