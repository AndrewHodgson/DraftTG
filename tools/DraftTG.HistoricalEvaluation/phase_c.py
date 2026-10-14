"""Supported Phase C research commands. Production and holdouts remain protected."""
import argparse,json,pathlib,subprocess
from phase_b import initialize_telemetry
from phase_c_core import *

def main():
    initialize_telemetry();p=argparse.ArgumentParser();p.add_argument('command',choices=['positions','calibrate','predict','replay','metrics','compare','freeze','final','golden','report','summary']);command=p.parse_args().command;guard()
    if (OUT/'selection.json').exists() and command in ('positions','calibrate','predict','replay','metrics','compare'):
        print('Published Phase C selection is frozen. Use summary/report; reproduce initial stages in a separate workspace. No artifacts overwritten.');return
    if command=='positions':
        from phase_c_replay import metadata
        metadata()
        from phase_c_positions import qualify
        qualify()
        from phase_c_positions import audit_taken,audit_percentiles
        audit_taken();audit_percentiles()
    elif command in ('calibrate','predict'):
        from phase_c_research import calibrate,predict
        calibrate() if command=='calibrate' else predict()
    elif command=='replay':
        from phase_c_replay import replay
        replay()
    elif command in ('metrics','compare','freeze','final','golden'):
        from phase_c_metrics import metrics,compare,freeze,final,golden
        locals()[command]()
    else:
        from phase_c_report import report,summary
        report() if command=='report' else summary()

if __name__=='__main__':main()
