using System.Text.Json;
using Clicky.Bridge;
using Xunit;

namespace Clicky.Tests;

public sealed class WindowsExplainScreenServiceTests
{
    [Fact]
    public async Task ExplainScreen_RequiresNonEmptyTask()
    {
        var service = new WindowsExplainScreenService(
            FakeCaptureProvider.WithCursorScreen(),
            new FakeExplanationProvider(ExplanationResult.Text("unused")));

        var error = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            service.ExplainScreenAsync(new ExplainScreenRequest(Task: "   ")));

        Assert.Equal("invalid_request", error.Error.Code);
        Assert.False(error.Error.Retryable);
    }

    [Fact]
    public async Task ExplainScreen_CapturesScreenAndReturnsConciseSemanticResponseWithRegions()
    {
        var capture = FakeCaptureProvider.WithCursorScreen();
        var provider = new FakeExplanationProvider(new ExplanationResult(
            Explanation: "This is a settings page. The save button is in the lower right.",
            Regions:
            [
                new ExplanationRegion(
                    Label: "save button",
                    Normalized: new NormalizedPoint(0.75, 0.8),
                    Physical: new PhysicalPoint(1440, 864, "display-1"),
                    Confidence: 0.91)
            ]));
        var service = new WindowsExplainScreenService(capture, provider);

        var response = await service.ExplainScreenAsync(new ExplainScreenRequest(
            Task: "explain what to do next",
            ObservationId: "obs-123",
            ScreenId: "display-1"));

        Assert.True(response.Ok);
        Assert.Equal("explained", response.Status);
        Assert.Equal("This is a settings page. The save button is in the lower right.", response.Explanation);
        Assert.Equal("obs-123", provider.LastRequest!.ObservationId);
        Assert.Equal("display-1", provider.LastRequest.ScreenId);
        Assert.Single(provider.LastScreens!);
        Assert.True(capture.WasCalled);
        var region = Assert.Single(response.Regions!);
        Assert.Equal("save button", region.Label);
        Assert.Equal(0.75, region.Normalized.X);
        Assert.Equal(0.8, region.Normalized.Y);
        Assert.Equal(1440, region.Physical!.X);
        Assert.Equal(864, region.Physical.Y);
        Assert.Equal("display-1", region.Physical.Screen);
        Assert.Equal(0.91, region.Confidence);
    }

    [Fact]
    public async Task ExplainScreen_ResponseDoesNotExposeProviderSpecificPayloads()
    {
        var service = new WindowsExplainScreenService(
            FakeCaptureProvider.WithCursorScreen(),
            new FakeExplanationProvider(ExplanationResult.Text("Concise explanation.")));

        var response = await service.ExplainScreenAsync(new ExplainScreenRequest(Task: "explain"));
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.DoesNotContain("raw", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anthropic", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("openai", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("model", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainScreen_CapturePermissionDeniedReturnsStructuredPermissionError()
    {
        var service = new WindowsExplainScreenService(
            new PermissionDeniedCaptureProvider(),
            new FakeExplanationProvider(ExplanationResult.Text("unused")));

        var error = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            service.ExplainScreenAsync(new ExplainScreenRequest(Task: "explain visible dialog")));

        Assert.Equal("permission_denied", error.Error.Code);
        Assert.Equal("screen_capture", error.Error.Permission);
        Assert.False(error.Error.Retryable);
    }

    private sealed class FakeCaptureProvider : IWindowsScreenCaptureProvider
    {
        public bool WasCalled { get; private set; }

        public static FakeCaptureProvider WithCursorScreen() => new();

        public Task<IReadOnlyList<BridgeCapturedScreen>> CaptureAllScreensAsJpegAsync(CancellationToken cancellationToken)
        {
            WasCalled = true;
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

    private sealed class FakeExplanationProvider(ExplanationResult result) : IWindowsExplanationProvider
    {
        public ExplainScreenRequest? LastRequest { get; private set; }
        public IReadOnlyList<Clicky.Capture.CapturedScreen>? LastScreens { get; private set; }

        public Task<ExplanationResult> ExplainAsync(
            ExplainScreenRequest request,
            IReadOnlyList<Clicky.Capture.CapturedScreen> screens,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastScreens = screens;
            return Task.FromResult(result);
        }
    }
}
