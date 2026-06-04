using System.Text.Json.Serialization;

namespace Clicky.Bridge;

public sealed record ObserveScreenRequest(
    string ImageMode = "metadataOnly",
    bool IncludeCursorScreenOnly = false,
    string? OutputDirectory = null,
    bool DebugWriteScreenshots = false,
    int MaxBase64Bytes = 1_000_000);

public sealed record ObserveScreenResponse(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("observationId")] string ObservationId,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("capturedAt")] string CapturedAt,
    [property: JsonPropertyName("coordinateSpace")] string CoordinateSpace,
    [property: JsonPropertyName("displays")] IReadOnlyList<DisplayMetadata> Displays,
    [property: JsonPropertyName("images")] IReadOnlyList<ImageMetadata> Images,
    [property: JsonPropertyName("cursor")] CursorMetadata? Cursor);

public sealed record DisplayMetadata(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("primary")] bool Primary,
    [property: JsonPropertyName("bounds")] BridgeRect Bounds,
    [property: JsonPropertyName("scale")] double Scale,
    [property: JsonPropertyName("coordinateScale")] CoordinateScale CoordinateScale,
    [property: JsonPropertyName("isCursorScreen")] bool IsCursorScreen);

public sealed record ImageMetadata(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("mime")] string Mime,
    [property: JsonPropertyName("displayId")] string DisplayId,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("data")] string? Data = null,
    [property: JsonPropertyName("truncated")] bool? Truncated = null,
    [property: JsonPropertyName("reason")] string? Reason = null);

public sealed record CursorMetadata(
    [property: JsonPropertyName("screenId")] string? ScreenId,
    [property: JsonPropertyName("isOnScreen")] bool IsOnScreen,
    [property: JsonPropertyName("position")] BridgePoint? Position);

public sealed record BridgeRect(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height);

public sealed record BridgePoint(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

public sealed record CoordinateScale(
    [property: JsonPropertyName("scaleX")] double ScaleX,
    [property: JsonPropertyName("scaleY")] double ScaleY);

public sealed record BridgeProtocolError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable,
    [property: JsonPropertyName("permission")] string? Permission = null);

public sealed class BridgeProtocolException : Exception
{
    public BridgeProtocolError Error { get; }

    public BridgeProtocolException(BridgeProtocolError error) : base(error.Message)
    {
        Error = error;
    }
}

public sealed class ScreenCapturePermissionException : Exception
{
    public ScreenCapturePermissionException(string message) : base(message) { }
}
