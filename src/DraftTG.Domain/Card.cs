namespace DraftTG.Domain;

public enum CardRarity
{
    Common,
    Uncommon,
    Rare,
    Mythic,
    Special,
    Bonus
}

public sealed record Card(
    CardIdentifier Identifier,
    string Name,
    ColorSet Colors,
    CardRarity Rarity,
    CardSetCode SetCode,
    CollectorNumber CollectorNumber)
{
    public CardGameplayMetadata GameplayMetadata { get; init; } = CardGameplayMetadata.Unknown;
}
