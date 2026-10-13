# Phase 9F.2 live capture-free summary

Native live runs: 1; completed: True
Stable coherent observations / distinct semantic packs: 1; identified drafts: 0; client sizes: 1
DraftId: unknown=1
RuntimeProfile: unity-mono-x64-69dd7ca1-arena-2026-10-13-v1=1
CardCount: 14=1
ViewMode: list=1
Reconciliation: PackSetAgreement=1
Clients / DPI: {"ProcessId":46632,"X":3958,"Y":221,"Width":1600,"Height":900,"DPI":96,"IsWindowed":true} (1)
Unresolved GrpIds in distinct packs: 0
StaleUiRejected: 0
Move: 0
Resize: 0
DPI: 0
WindowUnavailable: 0
PickAccepted: 0
SourceDiagnostic: 1
Transitions: ProcessAppeared=1, PackAppeared=1
Process instances: 46632:639274936387968023
Reconciliation events: MemoryUnavailable=1, PackSetAgreement=1
Placement declines: Waiting for a coherent reconciled memory pack=4
Manual reviews: 0; incorrect order/placement reports: 0; all used layouts reviewed: False
Stale badges restored: 0; torn packs displayed: 0
Screen capture initialized: NO; blocked initialization attempts: 0
Read outcomes (poll counts): Success=130
First attach ms: n=1; median=1128.06; p95=1128.06; max=1128.06
Steady-state read ms: n=129; median=17.43; p95=19.16; max=23.12
Steady-state traversal ms: n=129; median=10.29; p95=11.25; max=12.67
Polling: 100 ms completion-based delay; no overlapping reads. CPU/allocations not isolated in this report.

Technical live gate: NOT PASSED.
Requires >=30 distinct coherent packs, >=2 identified drafts, >=2 sizes, completed capture-blocked runs, supported placement reviews, and no incorrect/stale/torn/capture events. Restart and physical coverage still require review; summary is not production approval.
