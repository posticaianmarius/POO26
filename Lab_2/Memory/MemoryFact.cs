using System.Text.Json.Serialization;

public record MemoryFact
(
    [property: JsonRequired] int Id,
    [property: JsonRequired] string Text,
    [property: JsonRequired] string Scope,
    [property: JsonRequired] string Evidence,
    [property: JsonRequired] DateTimeOffset UpdatedAt
);
