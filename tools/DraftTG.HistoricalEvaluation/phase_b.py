"""Supported Phase B entry point; configures Windows process telemetry before frozen research runs.

No analysis functions or selected parameters are replaced. Use `summary` after the one final release.
"""
import argparse,ctypes,json,os,pathlib,subprocess

def initialize_telemetry():
    if os.name=='nt':
        from ctypes import wintypes
        ctypes.windll.psapi.GetProcessMemoryInfo.argtypes=[wintypes.HANDLE,ctypes.c_void_p,wintypes.DWORD]
        ctypes.windll.psapi.GetProcessMemoryInfo.restype=wintypes.BOOL

def main():
    initialize_telemetry()
    parser=argparse.ArgumentParser();parser.add_argument('command',choices=['acquire','qualify','audit','train','replay','final','report','summary','recover-training-summary']);args=parser.parse_args()
    if args.command=='acquire':
        from phase_b_source import acquire
        acquire()
        wide=pathlib.Path('data/historical/raw/phase-b-wide/acquisition-game_data-KHM-STX-AFR-VOW.json')
        if not wide.exists():
            subprocess.run(['python',str(pathlib.Path(__file__).with_name('acquire_pilot.py')),'--kind','game_data','--sets','KHM','STX','AFR','VOW',
                '--rows','20000','--prefix-bytes','8388608','--output','data/historical/raw/phase-b-wide'],check=True)
    elif args.command=='qualify':
        from phase_b_qualify import qualify
        qualify()
    elif args.command=='audit':
        from phase_b_audit import audit
        audit()
    elif args.command in ('train','final'):
        from phase_b_research import run
        run(args.command=='final')
    elif args.command=='replay':
        from phase_b_replay import replay
        replay(False)
    elif args.command=='recover-training-summary':
        # First execution completed ALL experiments/exports and froze selection, then failed in Win32 telemetry.
        # Reconstruct only already-exported run metadata; never rerun selection or inspect holdouts.
        from phase_b_research import OUT,fingerprint,dump
        from phase_b_core import peak_memory_bytes
        selection=json.loads((OUT/'selection.json').read_text())
        if selection['researchFingerprint']!=fingerprint():raise ValueError('Frozen research changed')
        datasets=json.loads((OUT/'training-baselines.json').read_text())
        result=dict(version=selection['version'],sets=sorted(d['set'] for d in datasets),datasets=len(datasets),
            cardCount=sum(d['baseline']['cards'] for d in datasets),games=sum(d['games'] for d in datasets),
            evidenceGames=sum(d['evidenceGames'] for d in datasets),targetGames=sum(d['targetGames'] for d in datasets),
            seconds=None,peakWorkingSetBytes=None,telemetryRecoveryOnly=True,
            reason='All training analysis and exports completed; final metadata write failed because the Win32 telemetry API lacked a 64-bit HANDLE argument prototype. Supported launcher supplies the prototype. Training wall time/peak were not recovered or invented. Selection and analytical source hashes unchanged.')
        dump(OUT/'training-result.json',result);print(json.dumps(result,indent=2))
    else:
        from phase_b_report import report,summary
        report() if args.command=='report' else summary()

if __name__=='__main__':main()
