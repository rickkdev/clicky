namespace Clicky.Bridge;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IObservationIdFactory
{
    string Create();
}

public sealed class ObservationIdFactory : IObservationIdFactory
{
    public string Create() => "obs_" + Guid.NewGuid().ToString("N");
}

public sealed class WindowsObserveService
{
    private const string ProtocolVersion = "clicky.hermes.v1";
    private const string Mime = "image/jpeg";
    private readonly IWindowsScreenCaptureProvider _captureProvider;
    private readonly IClock _clock;
    private readonly IObservationIdFactory _ids;

    public WindowsObserveService(IWindowsScreenCaptureProvider captureProvider, IClock? clock = null, IObservationIdFactory? ids = null)
    {
        _captureProvider = captureProvider;
        _clock = clock ?? new SystemClock();
        _ids = ids ?? new ObservationIdFactory();
    }

    public async Task<ObserveScreenResponse> ObserveScreenAsync(ObserveScreenRequest request, CancellationToken cancellationToken = default)
    {
        var imageMode = NormalizeImageMode(request.ImageMode);
        IReadOnlyList<BridgeCapturedScreen> captured;
        try
        {
            captured = await _captureProvider.CaptureAllScreensAsJpegAsync(cancellationToken);
        }
        catch (ScreenCapturePermissionException ex)
        {
            throw new BridgeProtocolException(new BridgeProtocolError(
                "permission_denied",
                $"Windows screen capture unavailable: {ex.Message}",
                Retryable: false,
                Permission: "screen_capture"));
        }

        var screens = captured.ToList();
        if (request.IncludeCursorScreenOnly)
            screens = screens.Where(s => s.IsCursorScreen).DefaultIfEmpty(screens.First()).ToList();

        var displays = screens.Select(ToDisplayMetadata).ToList();
        var images = new List<ImageMetadata>();
        foreach (var screen in screens)
            images.Add(await ToImageMetadataAsync(screen, imageMode, request, cancellationToken));

        var cursorScreen = captured.FirstOrDefault(s => s.IsCursorScreen);
        var cursor = new CursorMetadata(cursorScreen?.DisplayId, cursorScreen is not null, cursorScreen?.CursorPosition);

        return new ObserveScreenResponse(
            ProtocolVersion: ProtocolVersion,
            Ok: true,
            Status: "observed",
            ObservationId: _ids.Create(),
            Platform: "windows",
            CapturedAt: _clock.UtcNow.ToString("O"),
            CoordinateSpace: "desktop_physical_pixels",
            Displays: displays,
            Images: images,
            Cursor: cursor);
    }

    private static string NormalizeImageMode(string? imageMode) => imageMode switch
    {
        null or "" or "metadataOnly" => "metadataOnly",
        "file" => "file",
        "base64" => "base64",
        _ => throw new BridgeProtocolException(new BridgeProtocolError("invalid_request", "imageMode must be metadataOnly, file, or base64", false)),
    };

    private static DisplayMetadata ToDisplayMetadata(BridgeCapturedScreen screen)
    {
        var scaleX = Scale(screen.ScreenshotPixelWidth, screen.DisplayBounds.Width);
        var scaleY = Scale(screen.ScreenshotPixelHeight, screen.DisplayBounds.Height);
        return new DisplayMetadata(
            Id: screen.DisplayId,
            Label: screen.Label,
            Primary: screen.IsPrimary,
            Bounds: screen.DisplayBounds,
            Scale: scaleX,
            CoordinateScale: new CoordinateScale(scaleX, scaleY),
            IsCursorScreen: screen.IsCursorScreen);
    }

    private static async Task<ImageMetadata> ToImageMetadataAsync(BridgeCapturedScreen screen, string imageMode, ObserveScreenRequest request, CancellationToken cancellationToken)
    {
        if (imageMode == "metadataOnly" && !request.DebugWriteScreenshots)
            return BaseImage(screen, "metadataOnly");

        if (imageMode == "base64")
        {
            if (screen.ImageBytes.Length > request.MaxBase64Bytes)
            {
                return BaseImage(screen, "base64") with
                {
                    Truncated = true,
                    Reason = "image_too_large",
                };
            }

            return BaseImage(screen, "base64") with
            {
                Data = Convert.ToBase64String(screen.ImageBytes),
                Truncated = false,
            };
        }

        if (imageMode == "file" || request.DebugWriteScreenshots)
        {
            var outputDirectory = request.OutputDirectory ?? Path.Combine(Path.GetTempPath(), "clicky-hermes-observe");
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Sanitize(screen.DisplayId)}.jpg");
            await File.WriteAllBytesAsync(path, screen.ImageBytes, cancellationToken);
            return BaseImage(screen, imageMode) with { Path = path };
        }

        return BaseImage(screen, imageMode);
    }

    private static ImageMetadata BaseImage(BridgeCapturedScreen screen, string mode) => new(
        Mode: mode,
        Width: screen.ScreenshotPixelWidth,
        Height: screen.ScreenshotPixelHeight,
        Mime: Mime,
        DisplayId: screen.DisplayId);

    private static double Scale(int imagePixels, int displayPixels)
        => displayPixels <= 0 ? 1.0 : Math.Round((double)imagePixels / displayPixels, 4);

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
}
