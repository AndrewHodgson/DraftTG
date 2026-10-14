"""Phase F supported commands; earlier studies and production are read-only."""
import argparse
from phase_f_core import *
def main():
 from phase_b import initialize_telemetry
 initialize_telemetry();parser=argparse.ArgumentParser();parser.add_argument('command',choices=['verify','prepare','pair-inputs','validation-baseline','calibrate','pairs','builders','predict','contextual','cold','probes','benchmark','validate','final','summary','report']);cmd=parser.parse_args().command;guard()
 if (OUT/'completed-evidence.json').exists():
  for p,h in load(OUT/'completed-evidence.json')['files'].items():
   if sha(p)!=h:raise ValueError('Completed Phase F changed '+p)
  if cmd not in ('verify','summary','report'):raise ValueError('Phase F completed; use summary/report. No retuning, repeated research replay or holdout reset.')
 if (OUT/'selection.json').exists() and cmd in ('prepare','pair-inputs','validation-baseline','calibrate'):raise ValueError('Selection frozen; no changing its inputs or parameters')
 if cmd=='verify':print('Frozen prior work and production hashes match.');return
 if cmd=='prepare':prepare()
 elif cmd in ('pair-inputs','calibrate','pairs'):
  from phase_f_research import export_pair_inputs,calibrate,pair_evaluate
  {'pair-inputs':export_pair_inputs,'calibrate':calibrate,'pairs':pair_evaluate}[cmd]()
 else:
  import phase_f_publish as module;getattr(module,cmd.replace('-','_'))()
if __name__=='__main__':main()
