public sealed record MemoryChangeSummary
{
    public int Added { get; }
    public int Updated { get; }
    public int Deleted { get; }
    public int Total { get; }

    public MemoryChangeSummary(int added, int updated, int deleted)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(added);
        ArgumentOutOfRangeException.ThrowIfNegative(updated);
        ArgumentOutOfRangeException.ThrowIfNegative(deleted);
        Total = checked(added + updated + deleted);
        Added = added;
        Updated = updated;
        Deleted = deleted;
    }
}
