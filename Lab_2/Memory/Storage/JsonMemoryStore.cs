
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zeya.Memory.Storage;

public class JsonMemoryStore : IMemoryStore
{
    // Memory limits
    private const int MaximumFacts = 50;
    private const int MaximumUpdates = 8;
    private const int MaximumFactLength = 240;
    private const int MaximumScopeLength = 60;
    private const int MaximumEvidenceLength = 4000;

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "users", "prefers", "prefer", "likes", "like", "loves", "love", "favorite", "favourite",
        "preference", "preferences", "the", "and", "for", "with", "that", "this", "has", "have", "are", "was", "were",
        "a", "an", "i", "my", "me", "we", "our", "you", "your", "they", "their", "he", "she", "it",
        "is", "am", "be", "in", "on", "to", "of", "as", "at", "do", "not", "now"
    };

    private List<MemoryFact> savedFacts = [];

    public string FilePath {get;}
    public IReadOnlyList<MemoryFact> Facts => savedFacts.AsReadOnly();

    public JsonMemoryStore() : this(CreateDefaultFilePath()) {}

    public JsonMemoryStore(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        if(string.IsNullOrEmpty(Path.GetFileName(FilePath))) throw new ArgumentException("A memory file name is required.", nameof(filePath));
    }

    // Resolve default storage path
    private static string CreateDefaultFilePath()
    {
        string applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if(string.IsNullOrWhiteSpace(applicationDataPath) || !Path.IsPathFullyQualified(applicationDataPath)) throw new InvalidOperationException("A local application-data directory is required.");
        return Path.Combine(applicationDataPath, "Zeya", "memory.json");
    }

    // Load saved memories
    public async Task LoadAsync()
    {
        string json;

        try {json = await File.ReadAllTextAsync(FilePath);}
        catch(FileNotFoundException) when(savedFacts.Count == 0) {return;}
        catch(DirectoryNotFoundException) when(savedFacts.Count == 0) {return;}

        var loadedFacts = JsonSerializer.Deserialize<List<MemoryFact>>(json, MemoryJson.Options) ?? throw new InvalidDataException("Memory must be a JSON array.");

        ValidateLoadedFacts(loadedFacts);
        savedFacts = loadedFacts;
    }

    // Apply AI-proposed changes
    public async Task<MemoryChangeSummary> ApplyUpdatesAsync(IReadOnlyList<MemoryUpdate> updates, string userMessage)
    {
        if(updates.Count > MaximumUpdates) throw new InvalidDataException("Too many memory changes.");

        // Stage changes before committing
        List<MemoryFact> stagedFacts = [.. savedFacts];
        HashSet<int> modifiedMemoryIds = [];

        int nextMemoryId = savedFacts.Count == 0 ? 1 : savedFacts.Max(fact => fact.Id) + 1;

        int addedCount = 0;
        int updatedCount = 0;
        int deletedCount = 0;

        foreach(MemoryUpdate update in updates)
        {
            ValidateUpdate(update, userMessage, modifiedMemoryIds);

            int existingIndex = stagedFacts.FindIndex(fact => fact.Id == update.Id);

            // Delete memory
            if(update.Action == "delete")
            {
                if(update.Text != "" || update.Scope != "") throw new InvalidDataException("Invalid deletion.");

                stagedFacts.RemoveAt(existingIndex);
                deletedCount ++;
                continue;
            }

            if(!IsValidText(update.Text, MaximumFactLength) || !IsValidText(update.Scope, MaximumScopeLength)) throw new InvalidDataException("Invalid memory fact.");

            string text = update.Text.Trim();
            string scope = update.Scope.Trim().ToLowerInvariant();

            // Different scopes create separate facts
            bool isAddition = update.Action == "add" || Normalize(stagedFacts[existingIndex].Scope) != Normalize(scope);

            int targetId = isAddition ? 0 : update.Id;
            string factKey = GetFactKey(text, scope);

            // Skip duplicates and unchanged facts
            if(stagedFacts.Any(fact => fact.Id != targetId && GetFactKey(fact.Text, fact.Scope) == factKey)) continue;
            if(!isAddition && GetFactKey(stagedFacts[existingIndex].Text, stagedFacts[existingIndex].Scope) == factKey) continue;

            if(!isAddition && !SharesSubject(stagedFacts[existingIndex].Text, text)) throw new InvalidDataException("An update must retain the existing fact's subject. Use add for independent facts.");

            if(isAddition && nextMemoryId == int.MaxValue) throw new InvalidDataException("Memory IDs exhausted.");

            var memory = new MemoryFact(isAddition ? nextMemoryId : update.Id, text, scope, update.Evidence, DateTimeOffset.UtcNow);

            if(isAddition)
            {
                nextMemoryId ++;
                stagedFacts.Add(memory);
                addedCount ++;
            }
            else
            {
                stagedFacts[existingIndex] = memory;
                updatedCount ++;
            }
        }

        if(stagedFacts.Count > MaximumFacts) throw new InvalidDataException($"Memory full ({MaximumFacts}). Use /forget <id>.");

        var changes = new MemoryChangeSummary(addedCount, updatedCount, deletedCount);

        if(changes.Total > 0) await SaveAsync(stagedFacts);

        return changes;
    }

    // Delete memory by ID
    public async Task<bool> ForgetAsync(int id)
    {
        var remainingFacts = savedFacts.Where(fact => fact.Id != id).ToList();

        if(remainingFacts.Count == savedFacts.Count) return false;

        await SaveAsync(remainingFacts);
        return true;
    }

    // Save through temporary file replacement
    private async Task SaveAsync(List<MemoryFact> updatedFacts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        string temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            string json = JsonSerializer.Serialize(updatedFacts, MemoryJson.Options);
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, FilePath, overwrite: true);

            savedFacts = updatedFacts;
        }
        catch(Exception error)
        {
            try {File.Delete(temporaryPath);}
            catch(Exception cleanupError)
            {
                throw new AggregateException("Memory save and cleanup failed.", error, cleanupError);
            }

            throw;
        }
    }

    // Validate stored JSON data
    private static void ValidateLoadedFacts(IReadOnlyList<MemoryFact> facts)
    {
        if(facts.Count > MaximumFacts) throw new InvalidDataException($"Memory limit: {MaximumFacts} facts.");

        HashSet<int> seenIds = [];
        HashSet<string> seenFacts = [];

        foreach(MemoryFact fact in facts)
        {
            if(!IsValidFact(fact) || !seenIds.Add(fact.Id)) throw new InvalidDataException("Invalid memory fact.");
            if(!seenFacts.Add(GetFactKey(fact.Text, fact.Scope))) throw new InvalidDataException("Duplicate memory facts.");
        }
    }

    // Validate memory fields
    private static bool IsValidFact(MemoryFact? fact)
    {
        return fact is not null && fact.Id > 0 && fact.Id < int.MaxValue && IsValidText(fact.Text, MaximumFactLength) && IsValidText(fact.Scope, MaximumScopeLength) && IsValidEvidence(fact.Evidence) && fact.UpdatedAt != default;
    }

    // Validate AI proposals
    private void ValidateUpdate(MemoryUpdate update, string userMessage, HashSet<int> modifiedMemoryIds)
    {
        if(update is null || update.Action is not ("add" or "update" or "delete") || !IsValidEvidence(update.Evidence) || !userMessage.Contains(update.Evidence, StringComparison.Ordinal)) throw new InvalidDataException("Memory change lacks user evidence.");

        if(update.Action == "add")
        {
            if(update.Id != 0) throw new InvalidDataException("Invalid memory ID.");
            return;
        }

        if(update.Id <= 0 || !modifiedMemoryIds.Add(update.Id)) throw new InvalidDataException("Invalid memory ID.");
        if(!savedFacts.Any(fact => fact.Id == update.Id)) throw new InvalidDataException("Unknown memory ID.");
    }

    private static bool IsValidText(string? value) => IsValidText(value, MaximumFactLength);

    private static bool IsValidText(string? value, int maximumLength) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);

    private static bool IsValidEvidence(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumEvidenceLength;

    // Generate normalized identity
    private static string GetFactKey(string text, string scope) => Normalize(scope) + "|" + Normalize(text);

    // Compare fact subjects
    private static bool SharesSubject(string previousText, string updatedText)
    {
        HashSet<string> previousWords = SubjectWords(previousText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return previousWords.Overlaps(SubjectWords(updatedText));
    }

    // Extract meaningful words
    private static IEnumerable<string> SubjectWords(string text)
    {
        return Regex.Matches(text, @"[\p{L}\p{N}]+").Select(match => match.Value).Where(word => !GenericWords.Contains(word));
    }

    private static string Normalize(string value)
    {
        string[] words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).TrimEnd('.', '!', '?').ToUpperInvariant();
    }
}
