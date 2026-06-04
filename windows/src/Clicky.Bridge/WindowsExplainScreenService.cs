using System.Drawing;
using Clicky.Capture;

namespace Clicky.Bridge;

public interface IWindowsExplanationProvider
{
    Task<ExplanationResult> ExplainAsync(
        ExplainScreenRequest request,
        IReadOnlyList<CapturedScreen> screens,
        CancellationToken cancellationToken);
}

public sealed class WindowsExplainScreenService
{
    private const string ProtocolVersion = "clicky.hermes.v1";
    private readonly IWindowsScreenCaptureProvider _captureProvider;
    private readonly IWindowsExplanationProvider _explanationProvider;

    public WindowsExplainScreenService(
        IWindowsScreenCaptureProvider captureProvider,
        IWindowsExplanationProvider explanationProvider)
    {
        _captureProvider = captureProvider;
        _explanationProvider = explanationProvider;
    }

    public async Task<ExplainScreenResponse> ExplainScreenAsync(ExplainScreenRequest request, CancellationToken cancellationToken = default)
    {
        var task = request.Task.Trim();
        if (string.IsNullOrWhiteSpace(task))
        {
            throw new BridgeProtocolException(new BridgeProtocolError(
                "invalid_request",
                "task is required",
                Retryable: false));
        }

        IReadOnlyList<BridgeCapturedScreen> bridgeScreens;
        try
        {
            bridgeScreens = await _captureProvider.CaptureAllScreensAsJpegAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ScreenCapturePermissionException ex)
        {
            throw new BridgeProtocolException(new BridgeProtocolError(
                "permission_denied",
                $"Windows screen capture unavailable: {ex.Message}",
                Retryable: false,
                Permission: "screen_capture"));
        }

        var screens = bridgeScreens.Select(ToCapturedScreen).ToList();
        var normalizedRequest = request with { Task = task };
        var result = await _explanationProvider.ExplainAsync(normalizedRequest, screens, cancellationToken).ConfigureAwait(false);
        var explanation = result.Explanation.Trim();
        if (string.IsNullOrWhiteSpace(explanation))
        {
            throw new BridgeProtocolException(new BridgeProtocolError(
                "model_empty_response",
                "Clicky explanation provider returned an empty explanation.",
                Retryable: true));
        }

        return new ExplainScreenResponse(
            ProtocolVersion: ProtocolVersion,
            Ok: true,
            Status: "explained",
            Explanation: explanation,
            Regions: result.Regions ?? []);
    }

    private static CapturedScreen ToCapturedScreen(BridgeCapturedScreen screen) => new()
    {
        ImageBytes = screen.ImageBytes,
        Label = $"{screen.Label} (image dimensions: {screen.ScreenshotPixelWidth}x{screen.ScreenshotPixelHeight} pixels)",
        IsCursorScreen = screen.IsCursorScreen,
        DisplayBounds = new Rectangle(
            screen.DisplayBounds.X,
            screen.DisplayBounds.Y,
            screen.DisplayBounds.Width,
            screen.DisplayBounds.Height),
        ScreenshotPixelWidth = screen.ScreenshotPixelWidth,
        ScreenshotPixelHeight = screen.ScreenshotPixelHeight,
    };
}
