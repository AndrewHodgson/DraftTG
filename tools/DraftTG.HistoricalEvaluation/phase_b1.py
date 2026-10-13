"""Supported B.1 entry point. No new/spent holdout performance release exists."""
import argparse,json
from phase_b import initialize_telemetry
from phase_b1_core import *

def main():
    initialize_telemetry();parser=argparse.ArgumentParser()
    parser.add_argument('command',choices=['acquire','qualify','audit','train','prior','golden','profile-before','profile-after','prepare-replay','replay','summary','report'])
    command=parser.parse_args().command;guard()
    if command in ('acquire','qualify'):
        import phase_b1_source as source
        getattr(source,command)()
    elif command in ('audit','train'):
        from phase_b1_research import provider_audit,train
        if command=='audit':provider_audit()
        elif (OUT/'selection.json').exists():
            # Published calibration is read-only in this workspace. A fresh study
            # requires its own namespace; this never overwrites a frozen selection.
            print(json.dumps(load(OUT/'calibration-summary.json'),indent=2))
        else:train()
    elif command in ('prior','golden'):
        from phase_b1_prior import cross_format,golden
        cross_format() if command=='prior' else golden()
    elif command in ('profile-before','profile-after','prepare-replay','replay'):
        from phase_b1_replay import profile,prepare,bulk
        if command.startswith('profile-'):profile(command.split('-')[1],command.endswith('after'))
        elif command=='prepare-replay':prepare()
        else:bulk()
    else:
        from phase_b1_report import report,summary
        report() if command=='report' else summary()

if __name__=='__main__':main()
