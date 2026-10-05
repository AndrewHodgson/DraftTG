using System.Net;
using DraftTG.Domain;

namespace DraftTG.Data.Tests;

public sealed partial class SeventeenLandsCardRatingsClientTests
{
    [Fact]
    public void WoeEmbeddedProfileHasTenUniqueNormalizedPairsAndOfficialSource()
    {
        var profile = SetArchetypeProfileCatalog.Default.Find("WOE")!;
        Assert.Equal(10, profile.Archetypes.Count); Assert.Equal(10, profile.Archetypes.Select(a => a.Pair).Distinct().Count());
        Assert.All(profile.Archetypes, a => Assert.Equal(2, a.Pair.Colors.Count));
        Assert.Equal("Food Midrange", profile.Archetypes.Single(a => a.Pair.Code == "BG").Name);
        Assert.Equal("GU", profile.Archetypes.Single(a => a.Pair.Code == "UG").Pair.DisplayCode);
        Assert.Contains("magic.wizards.com", profile.Source); Assert.Null(SetArchetypeProfileCatalog.Default.Find("TST"));
    }

    [Fact]
    public void InvalidAndDuplicateProfilesRejectWithoutGuessingOrNumericCardRatings()
    {
        const string valid = """{"set":"WOE","source":"https://example.test/profile","archetypes":[{"colors":"BG","name":"Food","description":"Food midrange","story":"Story"}]}""";
        var malformed = new[]
        {
            valid.Replace("\"BG\"", "\"BB\""), valid.Replace("\"BG\"", "\"BGR\""), valid.Replace("\"BG\"", "\"BX\""),
            valid.Replace("\"WOE\"", "\"woe\""), valid.Replace("\"Food\"", "\"\""),
            valid.Replace("}]}", "},{\"colors\":\"GB\",\"name\":\"Other\",\"description\":\"Description\",\"story\":\"Story\"}]}"),
            valid.Replace("\"name\":\"Food\"", "\"name\":\"Food\",\"rating\":0.9"), "null", "{}"
        };
        foreach (var json in malformed) Assert.NotNull(Record.Exception(() => SetArchetypeProfileCatalog.Parse(json)));
        Assert.Null(SetArchetypeProfileCatalog.Parse(valid.Replace(",\"story\":\"Story\"", "")).Archetypes.Single().Story);
        Assert.Equal(ArchetypeColorPair.Create("BG"), ArchetypeColorPair.Create("GB"));
    }

    [Fact]
    public async Task PairQueryUsesVerifiedColorsParameterWubrgOrderAndExactFormats()
    {
        foreach (var format in Enum.GetValues<SeventeenLandsFormat>())
        {
            using var handler = new Handler(request =>
            {
                Assert.Equal($"https://www.17lands.com/api/card_data?expansion=WOE&event_type={format}&time_period=ALL_TIME&colors=UG", request.RequestUri!.AbsoluteUri);
                return RawApi(CurrentApiFixture);
            });
            var result = await Client(handler).LoadPairAsync("woe", format, ArchetypeColorPair.Create("GU"));
            Assert.Equal(format, result.Format); Assert.Equal("UG", result.ColorPair!.Code); Assert.Equal(900, Alpha(result.Rows).GameInHandGameCount);
            Assert.Equal(.575, Alpha(result.Rows).GameInHandWinRate); Assert.Equal(1, handler.Calls);
        }
    }

    [Fact]
    public async Task PairCacheIsDistinctValidatesIdentityAndKeepsStaleDataOnFailure()
    {
        using var handler = new Handler(_ => RawApi(CurrentApiFixture)); var bg = ArchetypeColorPair.Create("BG"); var gu = ArchetypeColorPair.Create("GU");
        var first = await Client(handler).LoadPairAsync("WOE", SeventeenLandsFormat.QuickDraft, bg);
        var cached = await Client(handler).LoadPairAsync("WOE", SeventeenLandsFormat.QuickDraft, bg);
        Assert.Equal(SeventeenLandsSource.Cache, cached.Source); Assert.Equal(first.Rows, cached.Rows); Assert.Equal(1, handler.Calls);
        var directory = Path.Combine(_directory, "limited-data", "17lands"); var path = Path.Combine(directory, "WOE_QuickDraft_ALL_TIME_v3_BG_pair_v1.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal("BG", json["DeckColors"]!.GetValue<string>()); Assert.Equal(1, json["PairSchemaVersion"]!.GetValue<int>());
        File.Copy(path, Path.Combine(directory, "WOE_QuickDraft_ALL_TIME_v3_UG_pair_v1.json"));
        await Client(handler).LoadPairAsync("WOE", SeventeenLandsFormat.QuickDraft, gu); Assert.Equal(2, handler.Calls);
        await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft); Assert.Equal(3, handler.Calls);
        _time.Now += SeventeenLandsCardRatingsClient.CacheTtl + TimeSpan.FromMinutes(1); handler.Response = _ => new(HttpStatusCode.ServiceUnavailable);
        var client = Client(handler); var stale = await client.LoadPairAsync("WOE", SeventeenLandsFormat.QuickDraft, bg);
        Assert.Equal(SeventeenLandsSource.StaleCache, stale.Source); Assert.Equal(bg, stale.ColorPair); Assert.Equal(first.Rows, stale.Rows);
        await client.LoadPairAsync("WOE", SeventeenLandsFormat.QuickDraft, bg); Assert.Equal(4, handler.Calls);
    }
}
