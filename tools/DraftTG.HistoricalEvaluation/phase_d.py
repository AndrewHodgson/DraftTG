"""Offline Phase D entry point; completed selection is immutable."""
import argparse
from phase_b import initialize_telemetry
from phase_d_core import *
def main():
    initialize_telemetry();p=argparse.ArgumentParser();p.add_argument('command',choices=['qualify','representation','train','completion','select','diagnostics','pairs','predict','replay','associations','cold','probes','benchmark','inference-benchmark','final','summary','report','validate']);cmd=p.parse_args().command.replace('-','_');guard()
    if (OUT/'completed-evidence.json').exists():
        for path,h in load(OUT/'completed-evidence.json')['files'].items():
            if sha(path)!=h:raise ValueError('Completed evidence changed: '+path)
        if cmd not in ('final','summary','report','validate'):raise ValueError('Phase D research is complete; use summary/report. No refit, new replay or model overwrite in this published workspace.')
    if (OUT/'selection.json').exists() and cmd in ('qualify','representation','train','completion','select'):raise ValueError('Selection frozen; no retuning or refitting initial analysis')
    if cmd in ('qualify','representation','train','completion','select'):
        import phase_d_research as module
    else:
        import phase_d_publish as module
    getattr(module,cmd)()
if __name__=='__main__':main()
