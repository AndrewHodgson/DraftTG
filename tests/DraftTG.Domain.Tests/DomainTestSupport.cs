using DraftTG.Domain;

namespace DraftTG.Domain.Tests;

internal static class DomainTestSupport
{
    public static Card Card(
        string identifier,
        string name = "Example Card",
        ColorSet colors = default,
        CardRarity rarity = CardRarity.Common,
        string setCode = "TST",
        string collectorNumber = "1") =>
        new(
            CardIdentifier.Create(identifier),
            name,
            colors,
            rarity,
            CardSetCode.Create(setCode),
            CollectorNumber.Create(collectorNumber));

    public static DraftPosition Position(int pack, int pick) =>
        new(PackNumber.Create(pack), PickNumber.Create(pick));
}
