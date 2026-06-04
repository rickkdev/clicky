using System.Text.Json.Serialization;

namespace Clicky.Bridge;

public sealed record PointToTargetRequest(
    string Target,
    string? Task = null,
    string? ScreenId = null,
    bool RenderOverlay = true,
    bool UseRedesignedPointingProtocol = true);

public sealed record PointToTargetResponse(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("overlayRendered")] bool OverlayRendered,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("confidence")] double? Confidence = null,
    [property: JsonPropertyName("normalized")] NormalizedPoint? Normalized = null,
    [property: JsonPropertyName("physical")] PhysicalPoint? Physical = null,
    [property: JsonPropertyName("reasoning")] string? Reasoning = null,
    [property: JsonPropertyName("error")] BridgeProtocolError? Error = null);

public sealed record NormalizedPoint(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);

public sealed record PhysicalPoint(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("screen")] string Screen);
