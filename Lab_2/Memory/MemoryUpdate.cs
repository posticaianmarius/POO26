using System.Text.Json.Serialization;

public sealed record MemoryUpdate
(
    [property: JsonRequired] string Action,
    [property: JsonRequired] int Id,
    [property: JsonRequired] string Text,
    [property: JsonRequired] string Scope,
    [property: JsonRequired] string Evidence
);
