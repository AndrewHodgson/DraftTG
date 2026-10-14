namespace DraftTG.RatingAudit;

public sealed record StatRiskGroup(string LogicalIdentity,string Name,int[] GrpIds,bool IdentityProven,
    RatingAuditRow[] Printings,string Risk,double? PriorShift,string Reason);

/// <summary>Diagnostic triage only. Thresholds preregistered in Phase 9F.4 plan-and-preservation.json.</summary>
public static class StatRiskReview
{
    public static string Risk(int? alternateN,double? shift,bool proven)=>
        !proven||alternateN is null or <500||shift is null||Math.Abs(shift.Value)>=.35?"High":
        alternateN<2000||Math.Abs(shift.Value)>=.20?"Medium":"Low";

    public static StatRiskGroup[] Group(IEnumerable<RatingAuditRow> input)=>input.Where(r=>!r.IsBasicLand)
        .GroupBy(r=>r.ProvenLogicalIdentity??"unproven-printing:"+r.ArenaGrpId)
        .Select(g=>
        {
            var rows=g.OrderBy(r=>r.ArenaGrpId).ToArray();var first=rows[0];
            var proven=rows.All(r=>r.ProvenLogicalIdentity is not null)&&rows.Select(r=>r.Name).Distinct().Count()==1;
            // A proven gameplay identity does not authorize erasing printing-specific statistics.
            var shifts=rows.Select(r=>r.Q is {} q?q-.5:(double?)null).Distinct().ToArray();
            var shift=shifts.Length==1?shifts[0]:null;
            var risk=rows.Select(r=>Risk(r.AlternateGames,r.Q is {} q?q-.5:null,proven)).OrderBy(r=>r=="High"?0:r=="Medium"?1:2).First();
            var reason=first.Fallback?"Authorized FRA Quick/Premier prior; diagnostic shift from neutral Q=.5":
                !first.PrimaryRowPresent?"Missing primary row; existing prior contract declines":
                !first.AlternateRowPresent?"Missing alternate row; no usable alternate evidence":
                first.AlternateGIH is null?"Alternate GIH is null; existing prior contract declines":
                first.AlternateGames is null or <=0?"Alternate sample is missing/zero; existing prior contract declines":
                "Existing prior declined; inspect retained identity/context/provider evidence";
            return new StatRiskGroup(g.Key,first.Name,rows.Select(r=>r.ArenaGrpId!.Value).ToArray(),proven,rows,risk,shift,reason);
        }).OrderByDescending(g=>Math.Abs(g.PriorShift??0)).ThenBy(g=>g.LogicalIdentity,StringComparer.Ordinal).ToArray();
}
