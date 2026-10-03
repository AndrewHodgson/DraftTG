using System.Text.Json;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application.Tests;

public sealed class ArenaCardResolverTests
{
    [Fact]
    public void KnownArenaIdentifierResolvesToDomainIdentifier()
    {
        var resolver = CreateResolver((101, "domain-a"));

        var result = resolver.Resolve(ArenaCardIdentifier.Create(101));

        Assert.Equal(ArenaCardResolutionStatus.Resolved, result.Status);
        Assert.Equal(CardIdentifier.Create("domain-a"), result.CardIdentifier);
        Assert.Empty(result.Candidates);
        Assert.True(resolver.TryResolve(ArenaCardIdentifier.Create(101), out var identifier));
        Assert.Equal(CardIdentifier.Create("domain-a"), identifier);
    }

    [Fact]
    public void UnknownArenaIdentifierReportsNoMapping()
    {
        var resolver = CreateResolver((101, "domain-a"));

        var result = resolver.Resolve(ArenaCardIdentifier.Create(999));

        Assert.Equal(ArenaCardResolutionStatus.Missing, result.Status);
        Assert.Null(result.CardIdentifier);
        Assert.Empty(result.Candidates);
        Assert.False(resolver.TryResolve(ArenaCardIdentifier.Create(999), out var identifier));
        Assert.Null(identifier);
    }

    [Fact]
    public void AmbiguousArenaIdentifierReportsDeterministicCandidatesWithoutGuessing()
    {
        var resolver = CreateResolver(
            (101, "domain-a"),
            (101, "domain-b"),
            (101, "domain-c"));

        var result = resolver.Resolve(ArenaCardIdentifier.Create(101));

        Assert.Equal(ArenaCardResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.CardIdentifier);
        Assert.Equal(
            ["domain-a", "domain-b", "domain-c"],
            result.Candidates.Select(identifier => identifier.Value));
        Assert.False(resolver.TryResolve(ArenaCardIdentifier.Create(101), out var guessed));
        Assert.Null(guessed);
        var candidates = Assert.IsAssignableFrom<IList<CardIdentifier>>(result.Candidates);
        Assert.True(candidates.IsReadOnly);
    }

    [Fact]
    public void DifferentArenaIdentifiersResolveIndependently()
    {
        var resolver = CreateResolver((101, "domain-a"), (102, "domain-b"));

        Assert.True(resolver.TryResolve(ArenaCardIdentifier.Create(101), out var first));
        Assert.True(resolver.TryResolve(ArenaCardIdentifier.Create(102), out var second));
        Assert.Equal(CardIdentifier.Create("domain-a"), first);
        Assert.Equal(CardIdentifier.Create("domain-b"), second);
    }

    [Fact]
    public void ResolverUsesImmutableImportedMapping()
    {
        var data = CreateCatalogData((101, "domain-a"));
        var resolver = new ArenaCardResolver(data);

        var mapping = Assert.IsAssignableFrom<IDictionary<int, CardIdentifier>>(data.ArenaIdMappings);
        Assert.True(mapping.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => mapping.Add(102, CardIdentifier.Create("domain-b")));
        Assert.True(resolver.TryResolve(ArenaCardIdentifier.Create(101), out var identifier));
        Assert.Equal(CardIdentifier.Create("domain-a"), identifier);
    }

    internal static ArenaCardResolver CreateResolver(params (int ArenaId, string DomainId)[] mappings) =>
        new(CreateCatalogData(mappings));

    private static ScryfallCardCatalogData CreateCatalogData(
        params (int ArenaId, string DomainId)[] mappings)
    {
        var records = mappings.Select(mapping => new
        {
            id = mapping.DomainId,
            arena_id = mapping.ArenaId,
            name = $"Card {mapping.DomainId}",
            colors = new[] { "W" },
            rarity = "common",
            set = "tst",
            collector_number = mapping.ArenaId.ToString()
        });
        return new ScryfallCardCatalogDecoder().DecodeCatalogData(JsonSerializer.Serialize(records));
    }
}
