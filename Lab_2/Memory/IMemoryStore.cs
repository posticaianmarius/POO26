public interface IMemoryStore
{
    IReadOnlyList<MemoryFact> Facts {get;}
    Task LoadAsync();
    Task<MemoryChangeSummary> ApplyUpdatesAsync(IReadOnlyList<MemoryUpdate> updates, string userMessage);
    Task<bool> ForgetAsync(int memoryId);
}
