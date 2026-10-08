using System.Text.Json;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

/// <summary>Phase 3: statistical identity (which 17Lands row describes an exact printing) and Pick Score evidence.
/// Each test states the expected behaviour before asserting it.</summary>
public sealed class SeventeenLandsStatisticalIdentityTests
{
    private static readonly LimitedStatisticsContext Context = new("FRA", LimitedStatisticsFormat.PremierDraft);
    private static ScryfallCardCatalogData Decode(params object[] cards) => new ScryfallCardCatalogDecoder().DecodeCatalogData(JsonSerializer.Serialize(cards));
    private static object Single(string id, string name, string set = "fra", string number = "1") => new
    { id, name, colors = new[] { "R" }, rarity = "common", set, collector_number = number, type_line = "Creature — Test", cmc = 3, mana_cost = "{2}{R}" };
    private static object Multi(string id, string layout, string front, string back, string set = "fra", string number = "2") => new
    {
        id, name = $"{front} // {back}", layout, colors = new[] { "R" }, rarity = "common", set, collector_number = number,
        type_line = "Creature — Test // Sorcery", cmc = 3,
        card_faces = new object[] { new { name = front, mana_cost = "{2}{R}", type_line = "Creature — Test" }, new { name = back, mana_cost = "{R}", type_line = "Sorcery" } }
    };
    private static SeventeenLandsRating Row(string name, double? gih = .60, int? games = 4000, double? alsa = 4.5) =>
        new(name, GameInHandGameCount: games, GameInHandWinRate: gih, AverageLastSeenAt: alsa);
    private static DraftSnapshot Pack(params Card[] cards) =>
        new(new(new(PackNumber.Create(1), PickNumber.Create(1)), cards.Select(c => c.Identifier)), new(), DraftFormat.BestOfOne);
    private static LimitedStatisticsMappingResult Map(ScryfallCardCatalogData data, params SeventeenLandsRating[] rows) =>
        LimitedStatisticsMapper.Map(rows, data.Catalog, Context, Pack(data.Catalog.Cards.ToArray()));
    private static Card Named(ScryfallCardCatalogData data, string name) => data.Catalog.Cards.Single(c => c.Name == name);
    private static ScryfallCardCatalogData RealFra() => new ScryfallCardCatalogDecoder().DecodeCatalogData(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "premier-fra-course-cards.json")));

    [Fact] public void RealFraPrepareCardsResolveTheirFrontFaceRowsAndSingleFacedCardsKeepExactRows()
    {
        // Expected: the real FRA fixture's three `prepare` cards (17Lands names them by front face only) receive those rows;
        // every single-faced card still receives exactly its own canonical-name row.
        var data = RealFra();
        var multiface = data.Catalog.Cards.Where(c => c.Name.Contains(" // ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, multiface.Length);
        Assert.All(multiface, c => Assert.Equal(CardLayout.Prepare, c.GameplayMetadata.Layout));
        var rows = data.Catalog.Cards.Select((c, i) => Row(SeventeenLandsStatisticalIdentity.FrontFaceAlias(c) ?? c.Name, .50 + i * .001)).DistinctBy(r => r.Name).ToArray();
        var mapping = Map(data, rows);
        foreach (var card in data.Catalog.Cards.DistinctBy(c => c.Identifier))
        {
            var resolution = mapping.Resolutions[card.Identifier];
            var expectedName = multiface.Contains(card) ? card.Name.Split(" // ")[0] : card.Name;
            Assert.Equal(multiface.Contains(card) ? StatisticalIdentityKind.MultifaceFrontFace : StatisticalIdentityKind.ExactName, resolution.Kind);
            Assert.Equal(expectedName, mapping.ResolvedNames[card.Identifier]);
            Assert.Equal(rows.Single(r => r.Name == expectedName).GameInHandWinRate, mapping.Catalog.StatisticsFor(card.Identifier)!.GameInHandWinRate);
        }
    }

    [Theory]
    [InlineData("prepare", true)] [InlineData("adventure", true)] [InlineData("transform", true)] [InlineData("modal_dfc", true)]
    [InlineData("split", false)] [InlineData("flip", false)]
    public void FrontFaceAliasIsAllowedOnlyForLayoutsWhoseFrontFaceIsTheDraftedCard(string layout, bool resolves)
    {
        // Expected: structured layouts whose front face is the drafted object resolve by front face. Split halves are
        // equal parts and unknown layouts fail closed; they need an exact provider name.
        var data = Decode(Multi("m", layout, "Front Card", "Back Spell"));
        var mapping = Map(data, Row("Front Card"));
        var card = data.Catalog.Cards.Single();
        Assert.Equal(resolves ? StatisticalIdentityKind.MultifaceFrontFace : StatisticalIdentityKind.NoProviderRow, mapping.Resolutions[card.Identifier].Kind);
        Assert.Equal(resolves, mapping.Catalog.StatisticsFor(card.Identifier) is not null);
    }

    [Fact] public void AnExactFullNameRowAlwaysWinsOverAFrontFaceRow()
    {
        // Expected: if the provider publishes the canonical "Front // Back" name, that row is used exactly as before;
        // a front-face row with different numbers is never consulted.
        var data = Decode(Multi("m", "adventure", "Front Card", "Back Spell"));
        var mapping = Map(data, Row("Front Card // Back Spell", .61), Row("Front Card", .45));
        var card = data.Catalog.Cards.Single();
        Assert.Equal(StatisticalIdentityKind.ExactName, mapping.Resolutions[card.Identifier].Kind);
        Assert.Equal(.61, mapping.Catalog.StatisticsFor(card.Identifier)!.GameInHandWinRate);
    }

    [Fact] public void BackFaceNamesAreNeverAliasesEvenWhenARowExists()
    {
        // Expected: FRA reuses back faces ("Soul Tether" on three cards). A back-face row identifies no single card,
        // so nothing is attached.
        var data = Decode(Multi("a", "prepare", "Heartwood Crafter", "Soul Tether", number: "105"),
            Multi("b", "prepare", "Woodwork Prodigy", "Soul Tether", number: "165"));
        var mapping = Map(data, Row("Soul Tether"));
        Assert.All(data.Catalog.Cards, c => Assert.Null(mapping.Catalog.StatisticsFor(c.Identifier)));
        Assert.All(data.Catalog.Cards, c => Assert.Equal(StatisticalIdentityKind.NoProviderRow, mapping.Resolutions[c.Identifier].Kind));
    }

    [Fact] public void SharedFrontFaceAcrossDifferentCardsIsAmbiguousAndFailsClosed()
    {
        // Expected: two different cards with the same front face but different back faces cannot be told apart by the
        // provider name, so neither receives the row.
        var data = Decode(Multi("a", "transform", "Twin Front", "Day Side"), Multi("b", "transform", "Twin Front", "Night Side", number: "3"));
        var mapping = Map(data, Row("Twin Front"));
        Assert.All(data.Catalog.Cards, c =>
        {
            Assert.Equal(StatisticalIdentityKind.AmbiguousMultifaceAlias, mapping.Resolutions[c.Identifier].Kind);
            Assert.Null(mapping.Catalog.StatisticsFor(c.Identifier));
        });
    }

    [Fact] public void FrontFaceThatIsAlsoAnotherCardsCanonicalNameStaysWithThatCard()
    {
        // Expected: a single-faced card named "Shared Name" owns the "Shared Name" row by exact match; the multiface card
        // whose front face is also "Shared Name" must not take it.
        var data = Decode(Single("s", "Shared Name"), Multi("m", "modal_dfc", "Shared Name", "Other Side", number: "4"));
        var mapping = Map(data, Row("Shared Name", .58));
        Assert.Equal(.58, mapping.Catalog.StatisticsFor(Named(data, "Shared Name").Identifier)!.GameInHandWinRate);
        var multiface = Named(data, "Shared Name // Other Side");
        Assert.Equal(StatisticalIdentityKind.AmbiguousMultifaceAlias, mapping.Resolutions[multiface.Identifier].Kind);
        Assert.Null(mapping.Catalog.StatisticsFor(multiface.Identifier));
    }

    [Fact] public void ConflictingProviderRowsForTheAliasFailClosed()
    {
        // Expected: two different rows with the same provider name are not resolved by picking one.
        var data = Decode(Multi("m", "prepare", "Front Card", "Back Spell"));
        var mapping = Map(data, Row("Front Card", .60), Row("Front Card", .52));
        var card = data.Catalog.Cards.Single();
        Assert.Equal(StatisticalIdentityKind.ConflictingProviderRows, mapping.Resolutions[card.Identifier].Kind);
        Assert.Null(mapping.Catalog.StatisticsFor(card.Identifier));
    }

    [Fact] public void ReprintsOfTheSameMultifaceCardShareItsRowButADifferentCardInAnotherSetDoesNot()
    {
        // Expected: the provider snapshot is already scoped to one set/format. Printings with the same canonical name are
        // the same card (as for single-faced reprints today). A different card elsewhere with a related name gains nothing.
        var data = Decode(Multi("fra", "prepare", "Front Card", "Back Spell", "fra", "10"), Multi("old", "prepare", "Front Card", "Back Spell", "old", "20"),
            Multi("other", "adventure", "Front Cards", "Back Spell", "woe", "30"));
        var mapping = Map(data, Row("Front Card", .57));
        Assert.Equal(2, data.Catalog.Cards.Count(c => mapping.Catalog.StatisticsFor(c.Identifier)?.GameInHandWinRate == .57));
        Assert.Null(mapping.Catalog.StatisticsFor(Named(data, "Front Cards // Back Spell").Identifier));
    }

    [Fact] public void EnvironmentBaselineIncludesFrontFaceNamedRowsOnce()
    {
        // Expected: the format baseline is the games-weighted mean of every resolvable provider row, so a front-face-named
        // multiface row now contributes, exactly once, through its own card.
        var data = Decode(Single("s", "Plain Card"), Multi("m", "prepare", "Front Card", "Back Spell"));
        var environment = LimitedStatisticsMapper.MapEnvironment([Row("Plain Card", .60, 1000), Row("Front Card", .50, 1000), Row("Unknown Card", .40, 1000)], data.Catalog, Context);
        Assert.Equal(2, environment.Count);
        Assert.Equal(.50, environment.StatisticsFor(Named(data, "Front Card // Back Spell").Identifier)!.GameInHandWinRate);
    }

    [Fact] public void PresentationAcceptsTheStructuralAliasButRejectsAnotherCardsName()
    {
        // Expected: a front-face-resolved row passes the badge identity check and is shown; a row name belonging to a
        // different card still rejects the whole occurrence.
        var data = Decode(Multi("m", "prepare", "Front Card", "Back Spell"), Single("s", "Other Card", number: "5"));
        var card = Named(data, "Front Card // Back Spell");
        var stats = new LimitedCardStatistics(card.Identifier, 4000, GameInHandWinRate: .6, AverageLastSeenAt: 4.5);
        Assert.True(new CurrentPackCardPresentation(new(0, card.Identifier), card, stats, "Front Card").IsIdentityConsistent);
        Assert.False(new CurrentPackCardPresentation(new(0, card.Identifier), card, stats, "Other Card").IsIdentityConsistent);
        Assert.False(new CurrentPackCardPresentation(new(0, card.Identifier), card, stats, "Back Spell").IsIdentityConsistent);
    }

    private static LimitedStatisticsUpdate Update(ScryfallCardCatalogData data, params SeventeenLandsRating[] rows)
    {
        var snapshot = Pack(data.Catalog.Cards.ToArray());
        var mapping = LimitedStatisticsMapper.Map(rows, data.Catalog, Context, snapshot);
        var environment = new LimitedCardStatisticsCatalog([new(CardIdentifier.Create("env"), 100000, GameInHandWinRate: .56)]);
        return new(snapshot, false, new(Context, Context, mapping.Catalog, LimitedStatisticsSource.Cache, null, null)
        { CardCatalog = data.Catalog, EnvironmentCatalog = environment, StatisticsResolvedNames = mapping.ResolvedNames, StatisticsResolutions = mapping.Resolutions });
    }

    [Theory]
    // name, gih, games, alsa -> estimated, evidence, displayed GIH, displayed ALSA
    [InlineData(.62, 4000, 4.5, false, PickScoreQualityEvidence.DirectCardStatistics, "62.0%", "ALSA 4.50")]
    [InlineData(.62, 4000, null, false, PickScoreQualityEvidence.DirectCardStatistics, "62.0%", "ALSA —")]
    [InlineData(null, 4000, 6.1, true, PickScoreQualityEvidence.ProviderRowWithoutUsableGih, "—", "ALSA 6.10")]
    [InlineData(.62, 0, 6.1, true, PickScoreQualityEvidence.ProviderRowWithoutUsableGih, "62.0%", "ALSA 6.10")]
    public void PartialRowsAreDirectOnlyWithAUsableGihAndNeverShowInventedValues(double? gih, int? games, double? alsa,
        bool estimated, PickScoreQualityEvidence evidence, string shownGih, string shownAlsa)
    {
        // Expected: Q is direct only with a real GIH rate and a positive sample; ALSA alone never makes it direct; missing
        // metrics display as dashes, never as the neutral prior.
        var data = Decode(Multi("m", "prepare", "Front Card", "Back Spell"), Single("s", "Plain Card", number: "6"));
        var update = Update(data, Row("Front Card", gih, games, alsa), Row("Plain Card", .55));
        var occurrence = update.Occurrences.Values.Single(o => o.Card!.Name == "Front Card // Back Spell");
        Assert.Equal(estimated, occurrence.IsPickScoreEstimate);
        Assert.Equal(evidence, occurrence.PickScore!.QualityEvidence);
        Assert.StartsWith(shownGih, occurrence.DisplayedGIH, StringComparison.Ordinal);
        Assert.Equal(shownAlsa, occurrence.DisplayedALSA);
        Assert.Contains(estimated ? "(Estimated)" : "/ 50;", occurrence.DiagnosticText, StringComparison.Ordinal);
    }

    [Fact] public void MissingRowsAreEstimatedWithAnExplanationThatKeepsOtherComponentsSeparate()
    {
        // Expected: no provider row -> estimated Q with a reason; the diagnostics still describe lane/archetype/deck need
        // as their own evidence rather than declaring the whole score evidence-free.
        var data = Decode(Multi("m", "prepare", "Front Card", "Back Spell"), Single("s", "Plain Card", number: "6"));
        var update = Update(data, Row("Plain Card", .55));
        var occurrence = update.Occurrences.Values.Single(o => o.Card!.Name == "Front Card // Back Spell");
        Assert.True(occurrence.IsPickScoreEstimate);
        Assert.Equal(PickScoreQualityEvidence.NoProviderRow, occurrence.PickScore!.QualityEvidence);
        Assert.Equal("—", occurrence.DisplayedGIH); Assert.Equal("ALSA —", occurrence.DisplayedALSA);
        Assert.Contains("Intrinsic quality: Estimated — no 17Lands row", occurrence.DiagnosticText, StringComparison.Ordinal);
        Assert.Contains("\nLane: ", occurrence.DiagnosticText, StringComparison.Ordinal);
        Assert.Contains("\nDeck need: ", occurrence.DiagnosticText, StringComparison.Ordinal);
        Assert.Contains("17Lands identity: No 17Lands row", occurrence.DiagnosticText, StringComparison.Ordinal);
    }

    [Fact] public void ResolvedMultifaceStatsAreDirectNotEstimated()
    {
        // Expected (regression): before Phase 3 this card was estimated because its front-face row was missed. With the row
        // resolved it is direct: no EST, real GIH/ALSA shown, and its score reflects the measured 62%.
        var data = Decode(Multi("m", "prepare", "Front Card", "Back Spell"), Single("s", "Plain Card", number: "6"));
        var update = Update(data, Row("Front Card", .62, 4000, 4.5), Row("Plain Card", .55));
        var occurrence = update.Occurrences.Values.Single(o => o.Card!.Name == "Front Card // Back Spell");
        Assert.False(occurrence.IsPickScoreEstimate);
        Assert.Equal(PickScoreQualityEvidence.DirectCardStatistics, occurrence.PickScore!.QualityEvidence);
        Assert.Equal("62.0%", occurrence.DisplayedGIH);
        Assert.True(occurrence.PickScore.Score0To50 > 25);
        Assert.Contains("front face of this multiface card", occurrence.DiagnosticText, StringComparison.Ordinal);
        Assert.Contains("contextual-pick-score-v1.6", occurrence.DiagnosticText, StringComparison.Ordinal);
    }
}
