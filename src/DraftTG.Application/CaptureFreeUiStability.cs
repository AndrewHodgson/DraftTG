namespace DraftTG.Application;

/// <summary>Independent UI/geometry generation; never mixes old window geometry with new card/deck state.</summary>
public sealed class CaptureFreeUiStability
{
    private string? candidate;
    private DateTimeOffset since;
    private int reads;
    private DateTimeOffset lastTimestamp = DateTimeOffset.MinValue;
    public long Generation { get; private set; }
    public bool IsStable { get; private set; }
    public bool Observe(string? key, DateTimeOffset timestamp)
    {
        if (timestamp < lastTimestamp) return false;
        lastTimestamp = timestamp;
        if (key is null || key != candidate)
        {
            candidate = key; since = timestamp; reads = key is null ? 0 : 1;
            IsStable = false; Generation++; return false;
        }
        reads++;
        return IsStable = reads >= 2 && timestamp - since >= TimeSpan.FromMilliseconds(150);
    }
    public void Invalidate() { candidate = null; reads = 0; IsStable = false; Generation++; }
}
