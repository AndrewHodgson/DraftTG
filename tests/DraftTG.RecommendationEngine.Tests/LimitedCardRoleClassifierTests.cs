using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

/// <summary>Phase 4 role classification: precision first. Uncertain wording must stay unclassified.</summary>
public sealed class LimitedCardRoleClassifierTests
{
    private static Card Spell(string text, string type = "Instant", CardLayout layout = CardLayout.Normal, IEnumerable<CardFaceMetadata>? faces = null,
        string id = "spell", IEnumerable<ManaKind>? produced = null) =>
        new(CardIdentifier.Create(id), id, new([MagicColor.Black]), CardRarity.Common, CardSetCode.Create("FRA"), CollectorNumber.Create("7"))
        { GameplayMetadata = new(3, type == "Land" ? "" : "{2}{B}", type, layout, faces, text, producedMana: produced) };
    private static LimitedCardRole Removal(Card card) =>
        new LimitedCardRoleClassifier().Classify(card).Roles & (LimitedCardRole.HardRemoval | LimitedCardRole.ConditionalRemoval);

    [Theory]
    [InlineData("Destroy target creature.", LimitedCardRole.HardRemoval)]
    [InlineData("Exile target creature or planeswalker. You gain 1 life.", LimitedCardRole.HardRemoval)]
    [InlineData("Destroy up to one target creature or planeswalker that player controls.", LimitedCardRole.HardRemoval)]
    [InlineData("Exile target nonland permanent an opponent controls until this enchantment leaves the battlefield.", LimitedCardRole.HardRemoval)]
    [InlineData("Destroy target creature with flying.", LimitedCardRole.ConditionalRemoval)]
    [InlineData("Destroy target creature or planeswalker that's blue or red. You gain 1 life.", LimitedCardRole.ConditionalRemoval)]
    [InlineData("This spell deals 6 damage to target creature with flying.", LimitedCardRole.ConditionalRemoval)]
    [InlineData("It deals 4 damage to target attacking or blocking creature.", LimitedCardRole.ConditionalRemoval)]
    [InlineData("This spell deals 3 damage to any target.", LimitedCardRole.HardRemoval)]
    [InlineData("This spell deals 2 damage to any target.", LimitedCardRole.ConditionalRemoval)]
    [InlineData("This spell deals 1 damage to target creature.", LimitedCardRole.None)]
    [InlineData("Target creature gets -3/-3 until end of turn.", LimitedCardRole.HardRemoval)]
    [InlineData("Target creature gets −3/−3 until end of turn.", LimitedCardRole.HardRemoval)]
    [InlineData("Target creature gets -2/-2 until end of turn.", LimitedCardRole.ConditionalRemoval)]
    [InlineData("Target creature you control deals damage equal to its power to target creature an opponent controls.", LimitedCardRole.ConditionalRemoval)]
    // Precision guards: wording that is not reliable creature removal stays unclassified.
    [InlineData("Destroy target noncreature, nonland permanent.", LimitedCardRole.None)]
    [InlineData("Exile target creature you control, then return it to the battlefield under its owner's control.", LimitedCardRole.None)]
    [InlineData("Exile target creature card from a graveyard.", LimitedCardRole.None)]
    [InlineData("Return target creature to its owner's hand.", LimitedCardRole.None)]
    [InlineData("When this creature enters, it fights up to one target creature an opponent controls.", LimitedCardRole.None)]
    [InlineData("Destroy all creatures.", LimitedCardRole.None)]
    [InlineData("Target creature gets +3/+3 until end of turn.", LimitedCardRole.None)]
    public void OracleRemovalRulesArePrecisionFirst(string text, LimitedCardRole expected) => Assert.Equal(expected, Removal(Spell(text)));

    [Fact] public void PacifismAurasAreConditionalRemovalOnlyOnEnchantments()
    {
        Assert.Equal(LimitedCardRole.ConditionalRemoval, Removal(Spell("Enchant creature\nEnchanted creature can't attack or block.", "Enchantment — Aura")));
        Assert.Equal(LimitedCardRole.None, Removal(Spell("Enchanted creature can't attack or block.", "Instant")));
    }

    [Fact] public void OnlyCastableFacesCount()
    {
        // Expected: an Adventure/Prepare spell face is castable and counts; a transform back face is not cast and does not.
        CardFaceMetadata[] Faces() => [new("Front", "{1}{B}", 2, null, "Creature — Test", "Deathtouch", "2", "2"),
            new("Back", "{B}", 1, null, "Instant", "Destroy target creature.", null, null)];
        Assert.Equal(LimitedCardRole.HardRemoval, Removal(Spell("", "Creature — Test // Instant", CardLayout.Prepare, Faces())));
        Assert.Equal(LimitedCardRole.HardRemoval, Removal(Spell("", "Creature — Test // Instant", CardLayout.Adventure, Faces())));
        Assert.Equal(LimitedCardRole.None, Removal(Spell("", "Creature — Test // Instant", CardLayout.Transform, Faces())));
    }

    [Fact] public void FixingIsStructuredForLandsOnlyAndRampIsDeferred()
    {
        // Expected: a two-colour land is Fixing from structured produced_mana; a mana-producing creature is neither Fixing
        // nor Ramp (restricted/conditional nonland mana was unreliable in the FRA audit).
        var classifier = new LimitedCardRoleClassifier();
        Assert.True((classifier.Classify(Spell("", "Land", id: "dual", produced: [ManaKind.Red, ManaKind.Green])).Roles & LimitedCardRole.Fixing) != 0);
        var dork = classifier.Classify(Spell("{T}: Add one mana of any color.", "Creature — Elf", id: "dork", produced: [ManaKind.White, ManaKind.Blue, ManaKind.Black, ManaKind.Red, ManaKind.Green]));
        Assert.Equal(LimitedCardRole.None, dork.Roles & (LimitedCardRole.Fixing | LimitedCardRole.Ramp));
    }

    [Fact] public void ExplicitOverridesAddAndRemoveRolesByIdentifierOrPrinting()
    {
        // Expected: data-driven overrides correct the classifier without engine code changes; removals always win.
        var removal = Spell("Destroy target creature.", id: "false-positive");
        var vanilla = Spell("Draw a card.", id: "missed");
        var table = new LimitedCardRoleOverrideTable(new Dictionary<string, LimitedCardRoleOverride>
        {
            ["false-positive"] = new(LimitedCardRole.None, LimitedCardRole.HardRemoval),
            [LimitedCardRoleOverrideTable.PrintingKey("FRA", "7")] = new(LimitedCardRole.ConditionalRemoval, LimitedCardRole.None)
        });
        var classifier = new LimitedCardRoleClassifier(overrides: table);
        Assert.Equal(LimitedCardRole.None, classifier.Classify(removal).Roles & LimitedCardRole.HardRemoval);
        Assert.Equal("removed by override", classifier.Classify(removal).SourceOf(LimitedCardRole.HardRemoval));
        Assert.Equal(.5, classifier.Classify(vanilla).RemovalWeight);
        Assert.Equal("explicit override", classifier.Classify(vanilla).SourceOf(LimitedCardRole.ConditionalRemoval));
        Assert.Equal("high-confidence Oracle rule", new LimitedCardRoleClassifier().Classify(removal).SourceOf(LimitedCardRole.HardRemoval));
    }
}
