namespace DraftTG.Domain.Tests;

public sealed class SuccessfulDeckCorpusTests
{
    private static readonly SuccessfulDeckKey Key = new("WOE", SuccessfulDeckFormat.QuickDraft, ArchetypeColorPair.Create("BG"));
    private static readonly DateTimeOffset Time = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
    private static SuccessfulDeckSample Sample(string id = "event") => new(Key, id, new(7, 1), 7, 1, Time,
        new Dictionary<string, int> { ["A"] = 2 }, new Dictionary<string, int> { ["A"] = 3 });
    private static SuccessfulDeckProvenance Provenance => new("test", "recent", "BG QuickDraft WOE", 100, 20, 2, "final");

    [Fact]
    public void CopiesAreImmutableAndSuccessfulEventCriteriaAreExplicit()
    {
        var counts = new Dictionary<string, int> { ["A"] = 3 };
        var sample = new SuccessfulDeckSample(Key, "event", new(7, 1), 7, 0, Time, counts, counts);
        counts["A"] = 1;
        Assert.Equal(3, sample.Maindeck["A"]); Assert.Equal(3, sample.Pool!["A"]);
        Assert.Equal(7, sample.Criteria.MaximumMatchWins); Assert.Equal(1, sample.Criteria.BestOf);
        Assert.Throws<ArgumentException>(() => new SuccessfulDeckSample(Key, "event", new(7, 1), 6, 0, Time, counts));
        Assert.Throws<ArgumentException>(() => new SuccessfulDeckSample(Key, "event", new(7, 1), 7, -1, Time, counts));
        Assert.Throws<ArgumentException>(() => new SuccessfulDeckSample(Key, "event", new(7, 1), 7, 0, Time,
            new Dictionary<string, int> { ["A"] = 4 }, counts));
        Assert.Throws<ArgumentException>(() => new SuccessfulDeckKey("../WOE", SuccessfulDeckFormat.QuickDraft, Key.Pair));
    }

    [Fact]
    public void CorpusRejectsDuplicateEventsWrongFormatEmptyAndFutureSamples()
    {
        var sample = Sample();
        var corpus = new SuccessfulDeckCorpus(Key, [sample], Time, Provenance);
        Assert.Single(corpus.Samples);
        Assert.Throws<ArgumentException>(() => new SuccessfulDeckCorpus(Key, [sample, sample], Time, Provenance));
        Assert.Throws<ArgumentException>(() => new SuccessfulDeckCorpus(Key, [], Time, Provenance));
        Assert.Throws<ArgumentException>(() => new SuccessfulDeckCorpus(Key, [sample], Time.AddHours(-1), Provenance));
        Assert.Throws<ArgumentException>(() => new SuccessfulDeckCorpus(
            new("WOE", SuccessfulDeckFormat.PremierDraft, Key.Pair), [sample], Time, Provenance));
    }
}
