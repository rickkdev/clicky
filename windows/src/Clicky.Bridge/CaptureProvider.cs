using Clicky.Capture;

namespace Clicky.Bridge;

public interface IWindowsScreenCaptureProvider
{
    Task<IReadOnlyList<BridgeCapturedScreen>> CaptureAllScreensAsJpegAsync(CancellationToken cancellationToken);
}

public sealed record BridgeCapturedScreen(
    string DisplayId,
    string Label,
    bool IsCursorScreen,
    bool IsPrimary,
    BridgeRect DisplayBounds,
    int ScreenshotPixelWidth,
    int ScreenshotPixelHeight,
    byte[] ImageBytes,
    BridgePoint? CursorPosition);

public sealed class WindowsScreenCaptureProvider : IWindowsScreenCaptureProvider
{
    public async Task<IReadOnlyList<BridgeCapturedScreen>> CaptureAllScreensAsJpegAsync(CancellationToken cancellationToken)
    {
        if (!await ScreenCapturePermissions.ProbeAsync())
            throw new ScreenCapturePermissionException("screen capture is unavailable on this Windows session");

        IReadOnlyList<CapturedScreen> captured;
        try
        {
            captured = await ScreenCapture.CaptureAllScreensAsJpegAsync(null, cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ScreenCapturePermissionException(ex.Message);
        }

        return captured.Select((screen, index) => new BridgeCapturedScreen(
            DisplayId: BuildDisplayId(screen, index),
            Label: screen.Label,
            IsCursorScreen: screen.IsCursorScreen,
            IsPrimary: index == 0 && screen.IsCursorScreen,
            DisplayBounds: new BridgeRect(
                screen.DisplayBounds.X,
                screen.DisplayBounds.Y,
                screen.DisplayBounds.Width,
                screen.DisplayBounds.Height),
            ScreenshotPixelWidth: screen.ScreenshotPixelWidth,
            ScreenshotPixelHeight: screen.ScreenshotPixelHeight,
            ImageBytes: screen.ImageBytes,
            CursorPosition: null)).ToList();
    }

    private static string BuildDisplayId(CapturedScreen screen, int index)
    {
        var cursor = screen.IsCursorScreen ? "cursor" : "secondary";
        return $"windows-display-{index + 1}-{cursor}";
    }
}
