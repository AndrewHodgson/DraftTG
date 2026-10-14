"""Read existing 9F ledgers/build results; never captures, invokes Arena, scores or evaluates holdouts."""
from pathlib import Path
import json, hashlib, subprocess, collections, xml.etree.ElementTree as ET
from datetime import datetime, timezone

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/"docs/evidence/phase-9f4"
ART=ROOT/"artifacts/phase9f4"
def sha(data):return hashlib.sha256(data).hexdigest()
def write(name,data):(OUT/name).write_text(json.dumps(data,indent=2)+"\n",encoding="utf-8")
def metrics(values):
    values=sorted(v for v in values if isinstance(v,(int,float)))
    def q(p):return values[int((len(values)-1)*p)] if values else None
    return dict(samples=len(values),minMs=q(0),medianMs=q(.5),p95Ms=q(.95),maxMs=q(1))

def main():
    plan=json.loads((OUT/"plan-and-preservation.json").read_text())
    preservation={p:sha((ROOT/p).read_bytes())==h for p,h in plan["preserve"].items()}
    if not all(preservation.values()):raise ValueError("Frozen Phase 9F.3/rating artifacts changed")
    raw=(ART/"live-matrix.jsonl").read_bytes();raw=raw[:raw.rfind(b'\n')+1]
    (ART/"ledger-review-snapshot.jsonl").write_bytes(raw)
    records=[json.loads(line) for line in raw.splitlines()]
    runs=[r for r in records if r["Kind"]=="RunStarted"]
    latest=runs[-1]["RunId"]
    current=[r for r in records if r["RunId"]==latest]
    ui=[r["Data"] for r in current if r["Kind"]=="UiLocalization"]
    perf=[r["Data"] for r in current if r["Kind"]=="Performance"]
    deck=[r["Data"] for r in current if r["Kind"]=="DeckTiming"]
    times=[u["Timings"] for u in ui]
    steady=[x for p in perf for x,f in zip(p["ReadMs"],p["FirstAttach"]) if not f]
    first=[x for p in perf for x,f in zip(p["ReadMs"],p["FirstAttach"]) if f]
    detailed={k:metrics(t.get(k) for t in times) for k in ["DeckIdentityMs","DeckGeometryMs"]}
    detailed.update({k:metrics(d.get(k) for d in deck) for k in ["ProjectionMs","ModelMs"]})
    reasons=collections.Counter(reason for u in ui for reason in u.get("UnsupportedReasons",[]))
    physical=json.loads((OUT/"physical-confirmation.json").read_text())
    summary=dict(schema="phase9f4-live-summary-v1",asOf=datetime.now(timezone.utc).isoformat(),
        ledger="artifacts/phase9f4/live-matrix.jsonl",snapshot="artifacts/phase9f4/ledger-review-snapshot.jsonl",
        snapshotPrefixBytes=len(raw),snapshotSha256=sha(raw),latestRun=latest,arenaBuild=ui[-1].get("ArenaBuild") if ui else None,
        eventCounts=dict(collections.Counter(r["Kind"] for r in records)),latestRunEventCounts=dict(collections.Counter(r["Kind"] for r in current)),
        windows=list({json.dumps(u["Window"],sort_keys=True):u["Window"] for u in ui if u["Window"]}.values()),
        observedPackCounts=sorted({len(r["Data"]["DraftPack"]["OrderedGrpIds"]) for r in records if r["Kind"]=="UiLocalization" and r["Data"]["DraftPack"]["OrderedGrpIds"]}),
        printings=list({json.dumps(p,sort_keys=True):p for u in ui for p in u["DraftPack"]["PrintingIdentities"] if p["Reconciled"]}.values()),
        measured=dict(latestRunOnly=True,firstAttach=metrics(first),packRead=metrics(steady),
            totalDeckTraversal=metrics(x for p in perf for x in p["DeckReadMs"]),**detailed),
        unsupportedReasons=dict(reasons),physical=physical,
        staleEvents=[r for r in records if r["Kind"]=="StaleUiRejected"],
        moveEvents=[r for r in records if r["Kind"]=="Move"],resizeEvents=[r for r in records if r["Kind"]=="Resize"],
        dpiEvents=[r for r in records if r["Kind"]=="DPI"],
        caveats=["Non-attach pack reads include native deck traversal and declines; detailed UI timings sample state-change events, not all polls.",
            "Run-start historical/unplaced picks do not prove live retirement. Older ledger PickAccepted generation=0 events are startup replay.",
            "Deferred stale diagnostic rejection is not a physical false placement or live stale-badge proof.",
            "Live scroll-response latency and compositor timing unavailable unless explicitly measured."])
    write("live-summary.json",summary)
    paths=[p for p in ART.glob("*.txt") if any(s in p.name for s in ["-il","native-","rect-","camera-"])]
    write("static-audit.json",dict(method="Existing local managed metadata/IL audit and passive native getter byte disassembly; no target calls",
        files={str(p.relative_to(ROOT)).replace('\\','/'):sha(p.read_bytes()) for p in sorted(paths)},
        uiAssemblies={"UnityEngine.CoreModule":"7C8FAF1B9BEFE71EC2D2A5AAE0ACBB97C6FD8962D9514495D701F717A20B0014",
            "UnityEngine.UIModule":"DDF3665576C653F9E6F7B2B5F12569DFA1E81B3793FB5A7E8C912EAE1141BD84",
            "UnityEngine.UI":"4BF50572EE01F1A62147142CC9750058EC01DFB3790B46BE9B42D207A4BD3797"},
        loadedMvidChecked=True,nativeOffsetsScope="Pinned UnityPlayer/current build only",managedOffsets="Resolved by existing Mono field metadata, including inherited fields"))
    ns={"t":"http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results={str(p.relative_to(ROOT)).replace('\\','/'):ET.parse(p).find('.//t:Counters',ns).attrib for p in sorted((ART/"tests-verified").glob("*.trx"))}
    golden=json.loads((ART/"golden-verified/golden.json").read_text())
    sourcePaths=["src/DraftTG.Application/CaptureFreeDeckLayout.cs","src/DraftTG.Application/PreferredBasicPrintingReconciler.cs",
        "src/DraftTG.Application/DraftTGRuntime.cs","src/DraftTG.ArenaIntegration/Memory/ArenaUiMemorySnapshot.cs",
        "src/DraftTG.ArenaIntegration/Memory/ArenaOfferedPrintingReconciliation.cs","src/DraftTG.ArenaIntegration/Memory/ArenaDraftMemorySnapshot.cs",
        "src/DraftTG.ArenaIntegration/Memory/ArenaDraftMemoryWatcher.cs","src/DraftTG.ArenaIntegration/Memory/Windows/ArenaDraftMemoryReader.cs",
        "src/DraftTG.ArenaIntegration/Memory/Windows/ArenaDraftObjectGraph.cs","src/DraftTG.ArenaIntegration/Memory/Windows/ArenaUnityGeometryReader.cs",
        "src/DraftTG.ArenaIntegration/Memory/Windows/ArenaMonoRuntime.cs","src/DraftTG.App/ArenaUiLocalizationDebugCommand.cs",
        "src/DraftTG.App/ArenaMemoryDraftCommand.cs","src/DraftTG.App/ArenaMemoryWatchCommand.cs","src/DraftTG.App/CaptureFreeDesktopSession.cs",
        "src/DraftTG.App/CaptureFreeDiagnostics.cs","src/DraftTG.App/DeckQualityBadges.cs","src/DraftTG.App/OverlayViewModel.cs",
        "src/DraftTG.App/CardOverlayWindow.axaml","tools/DraftTG.RatingAudit/Program.cs","tools/DraftTG.RatingAudit/RatingAudit.cs",
        "tools/DraftTG.RatingAudit/StatRiskReview.cs","tests/DraftTG.Application.Tests/Phase9F4Tests.cs","tests/DraftTG.App.Tests/Phase9F4OverlayTests.cs"]
    sourceHashes={p:sha((ROOT/p).read_bytes()) for p in sourcePaths}
    buildBytes=(ART/"build.txt").read_bytes()
    buildText=buildBytes.decode("utf-16" if buildBytes.startswith((b'\xff\xfe',b'\xfe\xff')) else "utf-8-sig")
    diff=subprocess.check_output(["git","diff","--numstat","--","src/DraftTG.Domain","src/DraftTG.RecommendationEngine"],cwd=ROOT,text=True).strip()
    write("validation.json",dict(commit=subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,text=True).strip(),workingTree=True,
        sourceHashes=sourceHashes,sourceFingerprint=sha(json.dumps(sourceHashes,sort_keys=True).encode()),
        buildCommand="dotnet build DraftTG.sln -v quiet",buildLog="artifacts/phase9f4/build.txt",buildLogSha256=sha((ART/"build.txt").read_bytes()),
        buildPassed="Build succeeded." in buildText,tests=results,totalTests=sum(int(r['total']) for r in results.values()),
        failed=sum(int(r['failed']) for r in results.values()),skipped=sum(int(r['notExecuted']) for r in results.values()),golden=golden,
        preserved=preservation,productionSourceDiff=diff,scoringChanged=False,processRights="0x410",
        captureInitialized=False,historicalAcquisitionReplayEvaluation=False,MH3DFTConsumed=False))
    print("Evidence refreshed; physical matrix remains "+("passed" if physical["packMatrixPassed"] else "incomplete"))

if __name__=="__main__":main()
