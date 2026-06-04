using System.Text.Json.Serialization;

namespace Clicky.Bridge;

public sealed record ExplainScreenRequest(
    string Task,
    string? ObservationId = null,
    string? ScreenId = null);

public sealed record ExplainScreenResponse(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("regions")] IReadOnlyList<ExplanationRegion>? Regions = null);

public sealed record ExplanationResult(
    string Explanation,
    IReadOnlyList<ExplanationRegion>? Regions = null)
{
    public static ExplanationResult Text(string explanation) => new(explanation, []);
}

public sealed record ExplanationRegion(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("normalized")] NormalizedPoint Normalized,
    [property: JsonPropertyName("physical")] PhysicalPoint? Physical = null,
    [property: JsonPropertyName("confidence")] double? Confidence = null);
