"""New D1 offline commands; all prior namespaces immutable."""
import argparse
from phase_b import initialize_telemetry
from phase_d1_core import *
def main():
    initialize_telemetry();p=argparse.ArgumentParser();p.add_argument('command',choices=['verify','qualify','baseline','fit','utility','calibrate','completion','select','metadata-prior','otj','ablate','predict','replay','associate','cold','probes','benchmark','validate','final','summary','report']);cmd=p.parse_args().command.replace('-','_');guard()
    if (OUT/'completed-evidence.json').exists():
        for path,h in load(OUT/'completed-evidence.json')['files'].items():
            if sha(path)!=h:raise ValueError('Completed D1 evidence changed: '+path)
        if cmd not in ('verify','validate','final','summary','report'):raise ValueError('D1 research complete; use summary/report. No repeated fits/replays/parameter changes in this published workspace.')
    if cmd=='verify':print('Preregistration and frozen production/PhaseD hashes verified.');return
    if (OUT/'selection.json').exists() and cmd in ('qualify','baseline','fit','utility','calibrate','completion','select'):raise ValueError('D1 selection frozen; no refitting or retuning')
    if cmd=='qualify':qualification()
    elif cmd=='baseline':
        from phase_d1_baseline import fit_baselines
        fit_baselines()
    elif cmd in ('fit','utility','calibrate','completion','select','ablate','cold'):
        import phase_d1_research as module;getattr(module,cmd)()
    else:
        import phase_d1_publish as module;getattr(module,cmd)()
if __name__=='__main__':main()
