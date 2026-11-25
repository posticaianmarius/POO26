using System.Text.Json.Serialization;

namespace Zeya.Memory;

public sealed record MemoryUpdateBatch(
    [property: JsonRequired] List<MemoryUpdate> Actions
);
