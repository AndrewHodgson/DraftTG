"""Render Phase 9F.4 stat reviews from the existing offline C# rating audit; no scoring or acquisition."""
import json
from pathlib import Path
import argparse

ROOT = Path(__file__).resolve().parents[2]

def table(headers, rows):
    return "\n".join(["| " + " | ".join(headers) + " |", "| " + " | ".join("---" for _ in headers) + " |"] +
        ["| " + " | ".join(str(x).replace("|", "\\|") for x in row) + " |" for row in rows])

def value(x):
    return "unavailable" if x is None else str(x)

def rate(x):
    return "null" if x is None else f"{x*100:.4f}%"

def prior_table(groups):
    return table(["Logical card", "GrpIds", "Primary n", "Alternate n", "Alt GIH − baseline", "Q", "Q − .5", "Risk"],
        [(g["Name"], ", ".join(map(str,g["GrpIds"])), value(g["Printings"][0]["PrimaryGames"]),
          value(g["Printings"][0]["AlternateGames"]),
          f'{(g["Printings"][0]["AlternateGIH"]-g["Printings"][0]["AlternateBaseline"])*100:+.4f} pp',
          f'{g["Printings"][0]["Q"]:.6f}', f'{g["PriorShift"]:+.6f}', g["Risk"]) for g in groups])

def main():
    parser=argparse.ArgumentParser();parser.add_argument("--input",type=Path,default=ROOT/"artifacts/phase9f4/rating-audit")
    args=parser.parse_args(); data=json.loads((args.input/"stat-risk-review.json").read_text()); summary=json.loads((args.input/"summary.json").read_text())
    if summary["Set"]!="FRA" or summary["Format"]!="QuickDraft" or data["ScoringChanged"]:
        raise ValueError("Exact FRA Quick unchanged-production audit required")
    plan=json.loads((ROOT/"docs/evidence/phase-9f4/plan-and-preservation.json").read_text())
    policy="High: alternate n <500, |Q−.5|≥.35, or uncertain identity. Medium: otherwise n <2000 or |Q−.5|≥.20. Low: otherwise. These prospective QA triage rules were recorded before ranking; they are not reliability parameters or scoring changes."
    intro=(f'Offline audit of the owned ALL_TIME caches, primary fetched {summary["FetchedAt"]}. '
        f'Primary baseline {rate(summary["PrimaryBaseline"])}; Premier baseline {rate(summary["AlternateBaseline"])}. '
        'Production `contextual-pick-score-v1.6.2`, k500, .9 FRA reliability, all other components and tiers remain unchanged. '
        'No historical replay, holdout evaluation or provider acquisition occurred.\n\n'
        'Grouping requires the owned Scryfall oracle ID plus complete normalized gameplay metadata agreement and colors; no name-only grouping. Printing-specific evidence is retained. '
        'Logical counts refer to this audit population, not booster collation.\n\n')
    neutral=table(["Logical card", "GrpIds", "Primary row / n / GIH", "Alternate row / n / GIH", "Alternate baseline", "Q", "Risk"],
        [(g["Name"], ", ".join(map(str,g["GrpIds"])),
          f'{g["Printings"][0]["PrimaryRowPresent"]} / {value(g["Printings"][0]["PrimaryGames"])} / {rate(g["Printings"][0]["PrimaryGIH"])}',
          f'{g["Printings"][0]["AlternateRowPresent"]} / {value(g["Printings"][0]["AlternateGames"])} / {rate(g["Printings"][0]["AlternateGIH"])}',
          rate(g["Printings"][0]["AlternateBaseline"]),g["Printings"][0]["Q"],g["Risk"]) for g in data["Neutral"]])
    (ROOT/"FRA_QUICK_NEUTRAL_STAT_REVIEW.md").write_text(
        '# FRA QuickDraft Neutral Stat Review — Phase 9F.4\n\n'+intro+
        f'**{data["NeutralLogicalCount"]} logical nonbasic cards / {data["NeutralPrintingCount"]} printings remain neutral.** '
        f'{data["BasicNeutralPrintingCount"]} basic-land missing-stat printings are excluded from nonbasic defects.\n\n'+neutral+'\n\n'+
        'Both format rows exist for all seven logical cards, but both primary and alternate GIH are null. Positive game counts do not invent a win rate. '
        'The existing prior requires finite alternate GIH and a positive sample, so it correctly declines; production Q=.5 remains explicit. '
        'No production scoring defect is established.\n\n'+policy+'\n\n'+
        'All seven require coverage review: Identity Echo, Loyal Tutor, Omnipresence, Entrust the Spark, Precise Redaction, Tam, the Possibility, and Gardenize. '
        'Recommendation for each: retain neutral Q and EST provenance, monitor normal authorized cache refreshes for usable exact-format GIH, and review newly available data without tuning to a desired rating. '
        'Null rates alone do not establish provider error or poor card quality.\n\n'
        'Untapped comparison unavailable: no legitimate reference supplied, zero cards compared, correlation unavailable.\n\n'
        '[Machine evidence](docs/evidence/phase-9f4/stat-risk-review.json); [input provenance](docs/evidence/phase-9f4/stat-inputs.json).\n',encoding="utf-8")
    dist=table(["Logical-card weighted measure","N","Min","P25","Median","P75","Max"],
        [(k,d["Count"],*[f'{d[q]:.6f}' for q in ["Min","P25","Median","P75","Max"]]) for k,d in data["Distributions"].items()])
    buckets={k:sum(g["Risk"]==k for g in data["Prior"]) for k in ["High","Medium","Low"]}
    (ROOT/"FRA_QUICK_ALTERNATE_PRIOR_REVIEW.md").write_text(
        '# FRA QuickDraft Alternate-Prior Review — Phase 9F.4\n\n'+intro+
        f'**{data["PriorLogicalCount"]} proven logical cards / {data["PriorPrintingCount"]} printings use the existing authorized prior.** '
        'Duplicated printings are not independent quality observations. Quantiles use sorted floor-index empirical values, one row per logical card; full per-printing evidence remains exported.\n\n'+dist+'\n\n'+policy+'\n\n'+
        f'Logical risk buckets: {buckets}. High is a review priority, not proof of a bad score. '
        'The sample effect remains n/(n+500), with .9 format reliability; Q is the existing quality normalization of the resulting value. '
        'The exported shift is Q_with_prior−.5, not a rerun of contextual pick score, draft-win improvement, or a causal quality estimate.\n\n'
        '## Top 20 absolute shifts\n\n'+prior_table(data["Top20Absolute"])+
        '\n\n## Largest positive shifts\n\n'+prior_table(data["TopPositive"])+
        '\n\n## Largest negative shifts\n\n'+prior_table(data["TopNegative"])+
        '\n\n## Recommendation\n\n'
        'Retain the narrow FRA prior unchanged. Review the extreme shifts and lower alternate samples using future authorized evidence; do not remove a prior solely for disagreement with intuition. '
        'There are no alternate samples below 500 in this population. Codie has primary n=17 and an extreme negative shift; primary n alone does not qualify GIH when the rate is null. '
        'Highest-risk rows and all 56 logical groups are in the machine evidence. No production scoring defect was established. '
        'Untapped comparison unavailable: no legitimate reference, zero matched cards, no correlation.\n\n'
        '[Machine evidence](docs/evidence/phase-9f4/stat-risk-review.json); [input provenance](docs/evidence/phase-9f4/stat-inputs.json).\n',encoding="utf-8")
    evidence=ROOT/"docs/evidence/phase-9f4"
    (evidence/"stat-risk-review.json").write_text(json.dumps(data,indent=2)+"\n",encoding="utf-8")
    (evidence/"stat-inputs.json").write_text(json.dumps({k:summary[k] for k in ["Model","Set","Format","FetchedAt","PrimarySource","AlternateSource","InputHashes","PrimaryBaseline","AlternateBaseline","NoNetworkAcquisition","ScoringChanged"]},indent=2)+"\n",encoding="utf-8")
    print(f'Wrote reviews: {data["NeutralLogicalCount"]} neutral, {data["PriorLogicalCount"]} prior logical cards')

if __name__=="__main__":main()
