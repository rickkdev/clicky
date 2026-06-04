using System.Drawing;
using Clicky.Bridge;
using Clicky.Pointing;
using Xunit;

namespace Clicky.Tests;

public sealed class WindowsPointToTargetServiceTests
{
    [Fact]
    public async Task PointToTarget_UsesStructuredPointIntentAndReturnsProtocolCoordinates()
    {
        var renderer = new RecordingOverlayRenderer();
        var service = new WindowsPointToTargetService(
            FakeCaptureProvider.WithCursorScreen(),
            new FakePointingTurnProvider(new PointingTurnResult
            {
                SpokenText = "the settings tab is here.",
                PointIntent = new PointIntent { Kind = PointIntentKind.Point, X = 480, Y = 270, ScreenNumber = 1, Label = "Settings" },
                PointIntents = [new PointIntent { Kind = PointIntentKind.Point, X = 480, Y = 270, ScreenNumber = 1, Label = "Settings" }],
            }),
            renderer);

        var response = await service.PointToTargetAsync(new PointToTargetRequest(Target: "settings tab", RenderOverlay: false));

        Assert.True(response.Ok);
        Assert.Equal("pointed", response.Status);
        Assert.Equal("settings tab", response.Target);
        Assert.Equal("Settings", response.Label);
        Assert.Equal(0.5, response.Normalized!.X);
        Assert.Equal(0.5, response.Normalized.Y);
        Assert.Equal(960, response.Physical!.X);
        Assert.Equal(540, response.Physical.Y);
        Assert.Equal("display-1", response.Physical.Screen);
        Assert.Equal("the settings tab is here.", response.Reasoning);
        Assert.False(response.OverlayRendered);
        Assert.False(renderer.WasCalled);
    }

    [Fact]
    public async Task PointToTarget_RenderOverlayTrueDispatchesOverlay()
    {
        var renderer = new RecordingOverlayRenderer();
        var service = new WindowsPointToTargetService(
            FakeCaptureProvider.WithCursorScreen(),
            FakePointingTurnProvider.PointAt(100, 50, "button"),
            renderer);

        var response = await service.PointToTargetAsync(new PointToTargetRequest(Target: "button", RenderOverlay: true));

        Assert.True(response.OverlayRendered);
        Assert.True(renderer.WasCalled);
        Assert.Equal("button", renderer.LastLabel);
        Assert.Equal(200, renderer.LastPoint!.Value.X);
        Assert.Equal(100, renderer.LastPoint.Value.Y);
    }

    [Fact]
    public async Task PointToTarget_RenderOverlayFalseIsDryRun()
    {
        var renderer = new RecordingOverlayRenderer();
        var service = new WindowsPointToTargetService(
            FakeCaptureProvider.WithCursorScreen(),
            FakePointingTurnProvider.PointAt(100, 50, "button"),
            renderer);

        var response = await service.PointToTargetAsync(new PointToTargetRequest(Target: "button", RenderOverlay: false));

        Assert.False(response.OverlayRendered);
        Assert.False(renderer.WasCalled);
    }

    [Fact]
    public async Task PointToTarget_RenderOverlayTrueReturnsFalseWhenOverlayRendererFailsButKeepsCoordinates()
    {
        var renderer = new FailingOverlayRenderer();
        var service = new WindowsPointToTargetService(
            FakeCaptureProvider.WithCursorScreen(),
            FakePointingTurnProvider.PointAt(100, 50, "button"),
            renderer);

        var response = await service.PointToTargetAsync(new PointToTargetRequest(Target: "button", RenderOverlay: true));

        Assert.True(response.Ok);
        Assert.Equal("pointed", response.Status);
        Assert.NotNull(response.Normalized);
        Assert.NotNull(response.Physical);
        Assert.False(response.OverlayRendered);
        Assert.True(renderer.WasCalled);
    }

    [Fact]
    public async Task WindowsPointOverlayRenderer_UsesOverlayHostToRenderRealSecondCursor()
    {
        var host = new RecordingOverlayHost();
        var renderer = new WindowsPointOverlayRenderer(() => host);

        var rendered = await renderer.RenderPointAsync(
            new Point(200, 100),
            new Rectangle(0, 0, 1920, 1080),
            "button",
            CancellationToken.None);

        Assert.True(rendered);
        Assert.True(host.Started);
        Assert.Equal(new Point(200, 100), host.LastPoint);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), host.LastDisplayBounds);
        Assert.Equal("button", host.LastLabel);
    }

    [Fact]
    public async Task WindowsPointOverlayRenderer_ReturnsFalseWhenOverlayHostUnavailable()
    {
        var renderer = new WindowsPointOverlayRenderer(() => throw new InvalidOperationException("overlay unavailable"));

        var rendered = await renderer.RenderPointAsync(
            new Point(200, 100),
            new Rectangle(0, 0, 1920, 1080),
            "button",
            CancellationToken.None);

        Assert.False(rendered);
    }

    [Fact]
    public async Task PointToTarget_LowConfidenceReturnsNonActionableStatusWithoutCoordinates()
    {
        var renderer = new RecordingOverlayRenderer();
        var service = new WindowsPointToTargetService(
            FakeCaptureProvider.WithCursorScreen(),
            new FakePointingTurnProvider(new PointingTurnResult
            {
                SpokenText = "i can't identify that safely.",
                PointIntent = PointIntent.None("unsafe_low_confidence"),
                PointIntents = [PointIntent.None("unsafe_low_confidence")],
            }),
            renderer);

        var response = await service.PointToTargetAsync(new PointToTargetRequest(Target: "danger zone", RenderOverlay: true));

        Assert.False(response.Ok);
        Assert.Equal("low_confidence", response.Status);
        Assert.Null(response.Normalized);
        Assert.Null(response.Physical);
        Assert.False(response.OverlayRendered);
        Assert.False(renderer.WasCalled);
        Assert.Equal("low_confidence_target", response.Error!.Code);
    }

    [Fact]
    public async Task PointToTarget_MissingTargetReturnsStructuredInvalidRequest()
    {
        var service = new WindowsPointToTargetService(
            FakeCaptureProvider.WithCursorScreen(),
            FakePointingTurnProvider.PointAt(100, 50, "button"),
            new RecordingOverlayRenderer());

        var error = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            service.PointToTargetAsync(new PointToTargetRequest(Target: "   ")));

        Assert.Equal("invalid_request", error.Error.Code);
        Assert.False(error.Error.Retryable);
    }

    [Fact]
    public async Task PointToTarget_CapturePermissionDeniedReturnsStructuredPermissionError()
    {
        var service = new WindowsPointToTargetService(
            new PermissionDeniedCaptureProvider(),
            FakePointingTurnProvider.PointAt(100, 50, "button"),
            new RecordingOverlayRenderer());

        var error = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            service.PointToTargetAsync(new PointToTargetRequest(Target: "button")));

        Assert.Equal("permission_denied", error.Error.Code);
        Assert.Equal("screen_capture", error.Error.Permission);
        Assert.False(error.Error.Retryable);
    }

    private sealed class FakeCaptureProvider : IWindowsScreenCaptureProvider
    {
        public static FakeCaptureProvider WithCursorScreen() => new();

        public Task<IReadOnlyList<BridgeCapturedScreen>> CaptureAllScreensAsJpegAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<BridgeCapturedScreen> screens = new[]
            {
                new BridgeCapturedScreen(
                    DisplayId: "display-1",
                    Label: "screen 1",
                    IsCursorScreen: true,
                    IsPrimary: true,
                    DisplayBounds: new BridgeRect(0, 0, 1920, 1080),
                    ScreenshotPixelWidth: 960,
                    ScreenshotPixelHeight: 540,
                    ImageBytes: [0xFF, 0xD8, 0xFF],
                    CursorPosition: null),
            };
            return Task.FromResult(screens);
        }
    }

    private sealed class PermissionDeniedCaptureProvider : IWindowsScreenCaptureProvider
    {
        public Task<IReadOnlyList<BridgeCapturedScreen>> CaptureAllScreensAsJpegAsync(CancellationToken cancellationToken)
            => throw new ScreenCapturePermissionException("screen capture unavailable");
    }

    private sealed class FakePointingTurnProvider(PointingTurnResult result) : IWindowsPointingTurnProvider
    {
        public static FakePointingTurnProvider PointAt(int x, int y, string label) => new(new PointingTurnResult
        {
            SpokenText = $"{label} is here.",
            PointIntent = new PointIntent { Kind = PointIntentKind.Point, X = x, Y = y, ScreenNumber = 1, Label = label },
            PointIntents = [new PointIntent { Kind = PointIntentKind.Point, X = x, Y = y, ScreenNumber = 1, Label = label }],
        });

        public Task<PointingTurnResult> GetPointingTurnAsync(PointToTargetRequest request, IReadOnlyList<Clicky.Capture.CapturedScreen> screens, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }

    private sealed class RecordingOverlayRenderer : IWindowsPointOverlayRenderer
    {
        public bool WasCalled { get; private set; }
        public string? LastLabel { get; private set; }
        public Point? LastPoint { get; private set; }

        public Task<bool> RenderPointAsync(Point point, Rectangle displayBounds, string label, CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastPoint = point;
            LastLabel = label;
            return Task.FromResult(true);
        }
    }

    private sealed class FailingOverlayRenderer : IWindowsPointOverlayRenderer
    {
        public bool WasCalled { get; private set; }

        public Task<bool> RenderPointAsync(Point point, Rectangle displayBounds, string label, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(false);
        }
    }

    private sealed class RecordingOverlayHost : IWindowsPointOverlayHost
    {
        public bool Started { get; private set; }
        public Point? LastPoint { get; private set; }
        public Rectangle? LastDisplayBounds { get; private set; }
        public string? LastLabel { get; private set; }

        public void Start()
        {
            Started = true;
        }

        public void FlyTo(Point point, Rectangle displayBounds, string label)
        {
            LastPoint = point;
            LastDisplayBounds = displayBounds;
            LastLabel = label;
        }
    }
}
