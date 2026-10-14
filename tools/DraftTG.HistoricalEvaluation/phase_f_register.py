"""Prospective Phase F registration; no new candidate performance inspected."""
import pathlib,json,hashlib,subprocess
OUT=pathlib.Path('docs/evidence/v2-phase-f')
def sha(p):
 with open(p,'rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def dump(p,v):p.write_text(json.dumps(v,indent=2,allow_nan=False)+'\n',encoding='utf8')
OUT.mkdir(parents=True,exist_ok=True)
if (OUT/'registration.json').exists():raise SystemExit('Already registered')
plan=dict(version='research-d-phase-f-v1',seed='DraftTG-phase-f-v1',calibration=['DMU','BRO','WOE'],development=['LCI','MKM','OTJ','ONE','MOM'],untouched=['MH3','DFT'],spent=['BLB','DSK'],
 inherited='Reuse frozen Phase E 3708 qualified exact pool/build joins, all matched-pair definitions and original target/validation groups; no acquisition/requalification. Reuse actual 54283-decision production replay and its 50285 comparable rows; only recompute experimental D.',
 replacement='Difference of selected spell multisets, retaining occurrence quantities and added/removed identities. Replacement=sum added Q*cast_after minus removed Q*cast_before minus virtualUtility*net_added_owned_spells. Retained mana delta is separate. Generated basics and land-count changes reported, not mistaken for spell replacement.',
 castability='Deterministic hypergeometric marginal probabilities in 40 cards. Plain generic/WUBRG and explicit C costs supported, other symbols decline. Report on-curve and by-MV+2. Score=min(P enough total lands, all required color/source tail probabilities)/.8 clipped[0,1]. This is a source adequacy approximation, not exact joint casting probability; overlapping dual-source/colorless demands and tapped tempo remain explicit limitations.',
 mana='Primary two-color pairs; full pools enumerate15..19 lands, early incomplete pools17. Direct known drafted land sources only, max4 nonbasic copies, plus supplied ordinary basics. Basics by pip demand with six minimum per main color where possible. Colorless source support counted explicitly; unknown/indirect/faces not silently credited.',
 splash='At most one outside color, single outside pip, Q>=.65, at most2 splash spell copies, >=2 direct drafted outside-color sources, at most1 splash basic. Reject if base-color source adequacy drops more than .04. No arbitrary third-color access. Missing special costs decline candidate substitution.',
 structure='Deck-potential differences, with quadratic shortage/saturation: creatures below14/above18, early plays below4, top end above3; broad MV1-2/3/4/5+ counts; optional detailed buckets diagnostic only. Unknown narrow noncreatures receive no positive optional role-need bonus. Reliable creature/early metadata remain usable. Removal need saturates below3 only when role and broad-main-deck eligibility are qualified; otherwise unknown/no bonus.',
 roles='Reuse existing precision-first classifier; do not invent name/Oracle-text sideboard classifier. Narrow status remains unknown without qualified structured evidence. Direct fixing is valued by source/retained-castability delta, never an abstract fixing tag.',
 builder='Deterministic greedy marginal selection plus one local replacement pass; rank viable pairs using total cast-adjusted utility and bounded structure, with a soft pair plausibility/near-tie stability diagnostic. No set/card-ID model feature; IDs only tie-break equivalent occurrences. Incomplete owned projections have explicit virtual slots. Final reconstruction requires exactly40 and legal quantities; declined/incomplete outcomes score zero reconstruction success.',
 families={'F-A':'Actual cast-adjusted replacement only; absolute selected/castability gate. Land estimator unavailable -> decline to production D, not invented spell cutline.',
 'F-B':'F-A plus diminishing structural potential change; no learned inclusion.',
 'F-C':'F-B plus retained-spell mana/fixing delta; frozen E-C probability modifies confidence by at most15%, never raw sign or eligibility.'},
 mapping='D=.1+absoluteEligibility*(.4+.4*confidence*tanh(raw/scale)); confidence=ownedStarterCompleteness*picks/(picks+8)*(.5+.5*pairPlausibility). Unsupported candidate metadata declines to unchanged production D. Explicit zero castability overrides residual neutrality.',
 calibrationPlan=dict(grid=dict(scale=[.05,.10],structureWeight=[.02,.04],virtualBase=[.45,.50,.55]),selection='Calibration-validation set-macro pair accuracy; tie favors simpler/smaller structural weight then larger scale then virtualBase nearest.5. Fit no new inclusion model. Freeze all families independently and select best validation pair family, ties favor A then B then C. No contextual-development/probe tuning.',
 virtual='Utility=clip(base+.05*tanh((remaining*.8/max(1,openSlots)-1)/3),.35,.65); no remaining picks uses .35 for genuine open slots; actual owned replacement for full shell.',
 probability='Use frozen Phase E calibrated E-C and original feature contract read-only. Disable supporting probability for any observed actual-Q/PhaseE-Q discrepancy; no refit. F replacement uses actual production-compatible Q; exact-name unavailable row becomes neutral only in F lookup, production unchanged.'),
 primary=dict(pair='>=2pp development set-macro accuracy above BOTH actual production D and generic inclusion control; positive in>=4/5 sets; no set loss>2pp; >=500 pairs and>=100 distinct drafts EACH set. Preserve Phase E tasks exactly.',
 reconstruction='>=1pp macro nonland copy F1 and >=.5pp copy overlap above strongest current/E-C builder per set; positive F1 in>=4/5; 100% candidate deck integrity or explicit decline counted zero success; main-color agreement loss<=2pp and land MAE increase<=.25 versus current.',
 castability='Monotonic source-count tails, same-cost double-pip <=single-pip, splash absent/present, total-source shortage, unsupported symbols explicit decline; all controlled source-demand tests pass.',
 probes='All frozen required directions pass for selected family: first vs fifth5drop, early deficit vs filled, shortage vs creature saturation, qualified removal shortage/saturation, fixing benefit vs no need, committed two-color off-color bomb vs enabled single-pip splash, modest vs huge replacement, nonstarter<.5, narrow-role fallback, land separated from spell replacement. Unknown evidence is unqualified, never a pass.',
 contextual='>=50000 complete comparable rows. Top1 loss<=.0025 and MRR loss<=.002 overall AND each development set. Actual production baseline ranks exactly reproduce; Q/L/A/R/weights/provenance/ties immutable.',
 cold='No same-development-set build fitting; calibrate on3 whole calibration sets only. Leave-calibration-set-out grid selection and transfer; macro pair loss<=.005 versus full calibration selected family. No set-identity features or adaptation.',
 compatibility='Actual production raw Q must match F lookup for every baseline candidate. Frozen E-C input model unchanged and unsupported usage declined. Preserve all prior source/artifact hashes and all40 production goldens.'),
 bootstrap='500 deterministic whole-draft resamples per development set; report paired accuracy gain interval against currentD and generic; no naive independent-pair significance.',
 probes='Preregister generic controlled complete 2-color states, fixed quality/metadata and equal pool-size contrasts. Mono-color pivot is not evidence of an uncastable third-color card. Do not tune after development probes. Freeze representative regression outputs only after calibration selection.',
 holdouts='No acquisition/read/release lock if ANY frozen gate fails. If all pass, freeze inputs/formulas first and allow exactly one separately identified final MH3/DFT release; no post-holdout tuning. Production integration never automatic.',
 production='All production v1.6.2 Q/L/A/D/R/weights/k500/FRA/tier/builder/Phase9F/localization/overlay source immutable. No next phase.')
dump(OUT/'preregistration.json',plan)
paths=set()
for root in ('src','tests','docs/evidence'):
 for p in pathlib.Path(root).rglob('*'):
  if p.is_file() and 'v2-phase-f' not in str(p) and not any(x in p.parts for x in ('bin','obj','.git')):paths.add(p)
for p in pathlib.Path('tools').rglob('*'):
 if p.is_file() and p.suffix in ('.py','.cs','.csproj') and not any(x in p.parts for x in ('bin','obj','__pycache__')) and not p.name.startswith(('phase_f','test_phase_f')):paths.add(p)
paths.update(pathlib.Path('.').glob('V2_PHASE_*'))
for p,h in json.loads(pathlib.Path('docs/evidence/v2-phase-e/completed-evidence.json').read_text())['files'].items():paths.add(pathlib.Path(p))
paths.update(pathlib.Path('data/historical/evaluation/phase-e').glob('production.jsonl.gz*'))
paths.update(pathlib.Path('data/historical/evaluation/phase-e').glob('compose-*.jsonl'))
dump(OUT/'preservation.json',dict(files={p.as_posix():sha(p) for p in sorted(paths)}))
dump(OUT/'registration.json',dict(planSha256=sha(OUT/'preregistration.json'),preservationSha256=sha(OUT/'preservation.json'),splitSha256=sha('V2_PHASE_B1_SPLIT_MANIFEST.json'),baseCommit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),newPerformanceNotRead=True))
print('Prospective Phase F registration ready to commit.')
