using System.Globalization;
using DraftTG.Application;
using DraftTG.Data;

internal static class ReferenceTests
{
    public static void Run()
    {
        var tests = new (string, Action)[]
        {
            ("rarity outranks land/color/title", () => Expect([2, 1], C(1, rarity:2), C(2, rarity:5, land:true))),
            ("nonlands precede lands within rarity", () => Expect([2, 1], C(1, land:true, color:1), C(2, color:0))),
            ("single colors follow WUBRG", () => Expect([5,4,3,2,1], C(1,color:16),C(2,color:8),C(3,color:4),C(4,color:2),C(5,color:1))),
            ("multicolor rank differs from numeric flag order", () => Expect([2,1], C(1,color:9),C(2,color:24))),
            ("colorless follows five colors", () => Expect([2,1], C(1,color:0),C(2,color:31))),
            ("lands and artifacts use identity, other spells use printed colors", () => {
                if (DraftSortReference.ColorRank(C(1,land:true,color:0,identity:2)) != 1
                    || DraftSortReference.ColorRank(C(1,artifact:true,color:0,identity:2)) != 1
                    || DraftSortReference.ColorRank(C(1,color:8,identity:2)) != 3) throw new Exception("Wrong color source."); }),
            ("titles use caller collation", () => {
                var cards = new[] { C(1,title:"Zebra"), C(2,title:"apple") };
                Check([2,1], DraftSortReference.Sort(cards,StringComparer.Create(CultureInfo.GetCultureInfo("en-US"),false)));
                Check([1,2], DraftSortReference.Sort(cards,StringComparer.Ordinal)); }),
            ("equal titles retain incoming IDs and duplicate occurrences", () => Expect([9,1,9],C(9),C(1),C(9))),
            ("null title precedes nonnull using default comparer", () => Expect([2,1],C(1),C(2,title:null))),
            ("existing model has a different final tie", () => {
                var cards = new[] { C(9), C(1) }; var keys = cards.ToDictionary(c=>c.GrpId,c=>c.HypothesisKeys);
                var rule = ArenaDisplayOrderModel.Hypotheses.Single(r=>r.Name=="rarity>landLast>color>title");
                if (!ArenaDisplayOrderModel.Sort(rule,[9,1],keys)!.SequenceEqual([1,9])) throw new Exception("Model difference lost.");
                Expect([9,1],cards); }),
        };
        foreach (var (name, test) in tests) { test(); Console.WriteLine("PASS " + name); }
        Console.WriteLine($"{tests.Length} focused reference tests passed.");
    }
    private static ReferenceCard C(int id,int rarity=2,bool land=false,bool artifact=false,int color=1,int identity=0,string? title="Same") =>
        new(id,rarity,land,artifact,color,identity,title,new ArenaCardSortKeys(id,5-rarity,DraftSortReference.ColorRank(land||artifact?identity:color),title,
            0,1,land?1:0,1,"1","TEST"));
    private static void Expect(int[] expected,params ReferenceCard[] cards) => Check(expected,DraftSortReference.Sort(cards,StringComparer.Ordinal));
    private static void Check(int[] expected,ReferenceCard[] actual)
    { if (!actual.Select(c=>c.GrpId).SequenceEqual(expected)) throw new Exception("Unexpected occurrence order."); }
}
