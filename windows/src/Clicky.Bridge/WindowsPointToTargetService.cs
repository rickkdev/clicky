using System.Drawing;
using Clicky.Capture;
using Clicky.Pointing;

namespace Clicky.Bridge;

public interface IWindowsPointingTurnProvider
{
    Task<PointingTurnResult> GetPointingTurnAsync(
        PointToTargetRequest request,
        IReadOnlyList<CapturedScreen> screens,
        CancellationToken cancellationToken);
}

public interface IWindowsPointOverlayRenderer
{
    Task<bool> RenderPointAsync(Point point, Rectangle displayBounds, string label, CancellationToken cancellationToken);
}

public sealed class NoopPointOverlayRenderer : IWindowsPointOverlayRenderer
{
    public Task<bool> RenderPointAsync(Point point, Rectangle displayBounds, string label, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

public sealed class WindowsPointToTargetService
{
    private const string ProtocolVersion = "clicky.hermes.v1";
    private readonly IWindowsScreenCaptureProvider _captureProvider;
    private readonly IWindowsPointingTurnProvider _pointingProvider;
    private readonly IWindowsPointOverlayRenderer _overlayRenderer;

    public WindowsPointToTargetService(
        IWindowsScreenCaptureProvider captureProvider,
        IWindowsPointingTurnProvider pointingProvider,
        IWindowsPointOverlayRenderer? overlayRenderer = null)
    {
        _captureProvider = captureProvider;
        _pointingProvider = pointingProvider;
        _overlayRenderer = overlayRenderer ?? new NoopPointOverlayRenderer();
    }

    public async Task<PointToTargetResponse> PointToTargetAsync(PointToTargetRequest request, CancellationToken cancellationToken = default)
    {
        var target = request.Target.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new BridgeProtocolException(new BridgeProtocolError(
                "invalid_request",
                "target is required",
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
        var result = await _pointingProvider.GetPointingTurnAsync(request, screens, cancellationToken).ConfigureAwait(false);
        var directive = StructuredPointingTurnParser.ToDirective(result.PointIntent, screens);
        if (directive is null)
            return NonActionableResponse(target, result);

        var conversion = PointTagParser.ConvertToScreenCoordinatesDetailed(directive, screens);
        if (conversion is null)
            return NonActionableResponse(target, result, "no_target", "target_not_convertible");

        var displayId = ResolveDisplayId(directive, bridgeScreens);
        var normalized = new NormalizedPoint(
            Math.Round((double)directive.X / conversion.TargetScreen.ScreenshotPixelWidth, 4),
            Math.Round((double)directive.Y / conversion.TargetScreen.ScreenshotPixelHeight, 4));
        var physical = new PhysicalPoint(
            (int)Math.Round(conversion.ScreenPoint.X),
            (int)Math.Round(conversion.ScreenPoint.Y),
            displayId);

        var overlayRendered = false;
        if (request.RenderOverlay)
        {
            overlayRendered = await _overlayRenderer.RenderPointAsync(
                new Point(physical.X, physical.Y),
                conversion.DisplayBounds,
                directive.Label,
                cancellationToken).ConfigureAwait(false);
        }

        return new PointToTargetResponse(
            ProtocolVersion: ProtocolVersion,
            Ok: true,
            Status: "pointed",
            Target: target,
            OverlayRendered: overlayRendered,
            Label: directive.Label,
            Confidence: 1.0,
            Normalized: normalized,
            Physical: physical,
            Reasoning: result.SpokenText);
    }

    private static PointToTargetResponse NonActionableResponse(
        string target,
        PointingTurnResult result,
        string? statusOverride = null,
        string? reasonOverride = null)
    {
        var reason = reasonOverride ?? result.PointIntent.NoPointReason ?? "no_target";
        var status = statusOverride ?? (reason.Contains("confidence", StringComparison.OrdinalIgnoreCase)
            ? "low_confidence"
            : "no_target");
        var code = status == "low_confidence" ? "low_confidence_target" : "no_target";
        var message = status == "low_confidence"
            ? "Clicky could not identify the requested target confidently enough to point."
            : "Clicky could not identify a visible target to point at.";

        return new PointToTargetResponse(
            ProtocolVersion: ProtocolVersion,
            Ok: false,
            Status: status,
            Target: target,
            OverlayRendered: false,
            Label: target,
            Confidence: status == "low_confidence" ? 0.0 : null,
            Reasoning: string.IsNullOrWhiteSpace(result.SpokenText) ? reason : result.SpokenText,
            Error: new BridgeProtocolError(code, message, Retryable: true));
    }

    private static CapturedScreen ToCapturedScreen(BridgeCapturedScreen screen) => new()
    {
        ImageBytes = screen.ImageBytes,
        Label = screen.Label,
        IsCursorScreen = screen.IsCursorScreen,
        DisplayBounds = new Rectangle(
            screen.DisplayBounds.X,
            screen.DisplayBounds.Y,
            screen.DisplayBounds.Width,
            screen.DisplayBounds.Height),
        ScreenshotPixelWidth = screen.ScreenshotPixelWidth,
        ScreenshotPixelHeight = screen.ScreenshotPixelHeight,
    };

    private static string ResolveDisplayId(PointDirective directive, IReadOnlyList<BridgeCapturedScreen> screens)
    {
        if (screens.Count == 0)
            return "unknown";

        if (directive.ScreenNumber is { } screenNumber)
        {
            var index = screenNumber - 1;
            if (index >= 0 && index < screens.Count)
                return screens[index].DisplayId;
        }

        return screens.FirstOrDefault(s => s.IsCursorScreen)?.DisplayId ?? screens[0].DisplayId;
    }
}
