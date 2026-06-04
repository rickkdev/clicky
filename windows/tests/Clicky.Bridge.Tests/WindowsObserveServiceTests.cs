using System.Text;
using Clicky.Bridge;
using Xunit;

namespace Clicky.Tests;

public sealed class WindowsObserveServiceTests
{
    [Fact]
    public async Task ObserveScreen_DefaultsToMetadataOnlyAndDoesNotWriteFiles()
    {
        using var temp = new TempDirectory();
        var provider = FakeCaptureProvider.WithTwoScreens();
        var service = new WindowsObserveService(provider, new FixedClock(), new SequentialObservationIds());

        var response = await service.ObserveScreenAsync(new ObserveScreenRequest(OutputDirectory: temp.Path));

        Assert.True(response.Ok);
        Assert.Equal("observed", response.Status);
        Assert.Equal("metadataOnly", response.Images[0].Mode);
        Assert.Null(response.Images[0].Path);
        Assert.Null(response.Images[0].Data);
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task ObserveScreen_IncludesDisplayBoundsDimensionsScaleIdsAndCursorMetadata()
    {
        var provider = FakeCaptureProvider.WithTwoScreens();
        var service = new WindowsObserveService(provider, new FixedClock(), new SequentialObservationIds());

        var response = await service.ObserveScreenAsync(new ObserveScreenRequest());

        Assert.Equal("clicky.hermes.v1", response.ProtocolVersion);
        Assert.Equal("windows", response.Platform);
        Assert.Equal("obs_test_001", response.ObservationId);
        Assert.Equal("desktop_physical_pixels", response.CoordinateSpace);
        Assert.Equal(2, response.Displays.Count);

        var cursorDisplay = response.Displays.Single(d => d.IsCursorScreen);
        Assert.Equal("display-1", cursorDisplay.Id);
        Assert.True(cursorDisplay.Primary);
        Assert.Equal(0, cursorDisplay.Bounds.X);
        Assert.Equal(0, cursorDisplay.Bounds.Y);
        Assert.Equal(1920, cursorDisplay.Bounds.Width);
        Assert.Equal(1080, cursorDisplay.Bounds.Height);
        Assert.Equal(0.5, cursorDisplay.Scale);
        Assert.Equal(0.5, cursorDisplay.CoordinateScale.ScaleX);
        Assert.Equal(0.5, cursorDisplay.CoordinateScale.ScaleY);

        Assert.NotNull(response.Cursor);
        Assert.Equal("display-1", response.Cursor!.ScreenId);
        Assert.True(response.Cursor.IsOnScreen);
        Assert.Equal(500, response.Cursor.Position!.X);
        Assert.Equal(300, response.Cursor.Position!.Y);

        Assert.Equal(960, response.Images[0].Width);
        Assert.Equal(540, response.Images[0].Height);
        Assert.Equal("image/jpeg", response.Images[0].Mime);
        Assert.Equal("display-1", response.Images[0].DisplayId);
    }

    [Fact]
    public async Task ObserveScreen_CanReturnOnlyCursorScreen()
    {
        var provider = FakeCaptureProvider.WithTwoScreens();
        var service = new WindowsObserveService(provider, new FixedClock(), new SequentialObservationIds());

        var response = await service.ObserveScreenAsync(new ObserveScreenRequest(IncludeCursorScreenOnly: true));

        Assert.Single(response.Displays);
        Assert.Equal("display-1", response.Displays[0].Id);
        Assert.Single(response.Images);
    }

    [Fact]
    public async Task ObserveScreen_FileModeWritesOnlyWhenRequested()
    {
        using var temp = new TempDirectory();
        var provider = FakeCaptureProvider.WithTwoScreens();
        var service = new WindowsObserveService(provider, new FixedClock(), new SequentialObservationIds());

        var response = await service.ObserveScreenAsync(new ObserveScreenRequest(ImageMode: "file", OutputDirectory: temp.Path, IncludeCursorScreenOnly: true));

        Assert.Single(Directory.GetFiles(temp.Path, "*.jpg"));
        Assert.Equal("file", response.Images[0].Mode);
        Assert.NotNull(response.Images[0].Path);
        Assert.True(File.Exists(response.Images[0].Path));
        Assert.Null(response.Images[0].Data);
    }

    [Fact]
    public async Task ObserveScreen_Base64ModeReturnsBoundedFixtureBytes()
    {
        var provider = FakeCaptureProvider.WithTwoScreens();
        var service = new WindowsObserveService(provider, new FixedClock(), new SequentialObservationIds());

        var response = await service.ObserveScreenAsync(new ObserveScreenRequest(ImageMode: "base64", IncludeCursorScreenOnly: true, MaxBase64Bytes: 32));

        Assert.Equal("base64", response.Images[0].Mode);
        Assert.Equal(Convert.ToBase64String(FakeCaptureProvider.JpegBytes), response.Images[0].Data);
        Assert.False(response.Images[0].Truncated.GetValueOrDefault());
    }

    [Fact]
    public async Task ObserveScreen_Base64ModeRejectsOversizedImagesWithoutDumpingBytes()
    {
        var provider = FakeCaptureProvider.WithTwoScreens();
        var service = new WindowsObserveService(provider, new FixedClock(), new SequentialObservationIds());

        var response = await service.ObserveScreenAsync(new ObserveScreenRequest(ImageMode: "base64", IncludeCursorScreenOnly: true, MaxBase64Bytes: 2));

        Assert.Equal("base64", response.Images[0].Mode);
        Assert.Null(response.Images[0].Data);
        Assert.True(response.Images[0].Truncated.GetValueOrDefault());
        Assert.Equal("image_too_large", response.Images[0].Reason);
    }

    [Fact]
    public async Task ObserveScreen_PermissionDeniedReturnsStructuredProtocolError()
    {
        var provider = new PermissionDeniedCaptureProvider();
        var service = new WindowsObserveService(provider, new FixedClock(), new SequentialObservationIds());

        var error = await Assert.ThrowsAsync<BridgeProtocolException>(() => service.ObserveScreenAsync(new ObserveScreenRequest()));

        Assert.Equal("permission_denied", error.Error.Code);
        Assert.Contains("screen capture", error.Error.Message);
        Assert.False(error.Error.Retryable);
        Assert.Equal("screen_capture", error.Error.Permission);
    }

    private sealed class FakeCaptureProvider : IWindowsScreenCaptureProvider
    {
        public static readonly byte[] JpegBytes = Encoding.ASCII.GetBytes("fake-jpeg");

        public static FakeCaptureProvider WithTwoScreens() => new();

        public Task<IReadOnlyList<BridgeCapturedScreen>> CaptureAllScreensAsJpegAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<BridgeCapturedScreen> screens = new[]
            {
                new BridgeCapturedScreen(
                    DisplayId: "display-1",
                    Label: "screen 1 of 2 — cursor is on this screen (primary focus)",
                    IsCursorScreen: true,
                    IsPrimary: true,
                    DisplayBounds: new BridgeRect(0, 0, 1920, 1080),
                    ScreenshotPixelWidth: 960,
                    ScreenshotPixelHeight: 540,
                    ImageBytes: JpegBytes,
                    CursorPosition: new BridgePoint(500, 300)),
                new BridgeCapturedScreen(
                    DisplayId: "display-2",
                    Label: "screen 2 of 2 — secondary screen",
                    IsCursorScreen: false,
                    IsPrimary: false,
                    DisplayBounds: new BridgeRect(1920, 0, 1280, 720),
                    ScreenshotPixelWidth: 1280,
                    ScreenshotPixelHeight: 720,
                    ImageBytes: JpegBytes,
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

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class SequentialObservationIds : IObservationIdFactory
    {
        public string Create() => "obs_test_001";
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clicky-bridge-tests-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
