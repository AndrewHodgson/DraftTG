using DraftTG.Domain;

namespace DraftTG.Domain.Tests;

public sealed class CardCatalogTests
{
    [Fact]
    public void EmptyCatalogIsSupported()
    {
        var catalog = new CardCatalog();
        Assert.Equal(0, catalog.Count);
        Assert.Empty(catalog.Cards);
        Assert.Empty(catalog.FindByExactName("Missing"));
    }

    [Fact]
    public void CatalogPreservesInputOrderAndIdentifierLookup()
    {
        var first = DomainTestSupport.Card("first", "First");
        var second = DomainTestSupport.Card("second", "Second");
        var catalog = new CardCatalog([second, first]);

        Assert.Equal([second, first], catalog.Cards);
        Assert.Equal(first, catalog.Find(first.Identifier));
        Assert.Null(catalog.Find(CardIdentifier.Create("unknown")));
    }

    [Fact]
    public void ExactNameLookupAllowsDuplicateNamesInInputOrder()
    {
        var first = DomainTestSupport.Card("shared-1", "Shared Name", setCode: "ONE");
        var other = DomainTestSupport.Card("other", "Other Name");
        var second = DomainTestSupport.Card("shared-2", "Shared Name", setCode: "TWO");
        var catalog = new CardCatalog([first, other, second]);

        Assert.Equal([first, second], catalog.FindByExactName("Shared Name"));
        Assert.Empty(catalog.FindByExactName("shared name"));
    }

    [Fact]
    public void DuplicateIdentifiersAreRejected()
    {
        var first = DomainTestSupport.Card("duplicate", "First", setCode: "ONE");
        var second = DomainTestSupport.Card("duplicate", "Second", setCode: "TWO");

        var error = Assert.Throws<DuplicateCardIdentifierException>(() => new CardCatalog([first, second]));
        Assert.Equal(first.Identifier, error.Identifier);
    }

    [Fact]
    public void EquivalentCatalogsCompareByCardSequence()
    {
        var card = DomainTestSupport.Card("one");
        Assert.Equal(new CardCatalog([card]), new CardCatalog([card]));
    }
}
