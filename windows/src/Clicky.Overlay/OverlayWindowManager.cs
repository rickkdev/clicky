using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Clicky.Overlay;

/// <summary>
/// Manages one <see cref="OverlayWindow"/> per connected monitor.
/// Recreates overlays when displays are added or removed.
/// Mirrors <c>OverlayWindowManager</c> from the Mac reference
/// (OverlayWindow.swift -> setupOverlayWindows / handleDisplayConfigChange).
/// </summary>
public sealed class OverlayWindowManager : IDisposable
{
    private readonly List<OverlayWindow> _overlays = new();
    private readonly Func<List<MonitorRect>> _enumerateMonitors;
    private OverlayWindow? _activePointingOverlay;
    private DispatcherTimer? _pointingReturnTimer;
    private bool _disposed;
    private const double PointingReturnSeconds = 3.8;

    private static readonly PointerDebugOptions CalibrationOptions = new()
    {
        ShowTargetCrosshair = false,
        PinpointMode = true,
    };

    /// <summary>
    /// Optional logger callback for diagnostic logging of the overlay pipeline.
    /// Wired to <c>DebugLog.Write</c> by the host app.
    /// </summary>
    public Action<string>? Logger { get; set; }

    /// <summary>Debug behavior for normal pointer requests.</summary>
    public PointerDebugOptions DebugOptions { get; set; } = PointerDebugOptions.FromEnvironment();

    /// <summary>
    /// Creates the manager with a custom monitor enumerator (for testing).
    /// </summary>
    internal OverlayWindowManager(Func<List<MonitorRect>> enumerateMonitors)
    {
        _enumerateMonitors = enumerateMonitors;
    }

    /// <summary>
    /// Creates the manager using real monitor enumeration via EnumDisplayMonitors.
    /// </summary>
    public OverlayWindowManager()
        : this(EnumerateMonitorsFromSystem)
    {
    }

    /// <summary>
    /// Returns the HWNDs of all active overlay windows, for use with
    /// <c>ScreenCapture.CaptureAllScreensAsJpegAsync(excludeHwnds)</c>
    /// to exclude overlays from screenshots.
    /// </summary>
    public IReadOnlyList<IntPtr> OverlayHwnds
    {
        get
        {
            var hwnds = new List<IntPtr>(_overlays.Count);
            foreach (var overlay in _overlays)
            {
                if (overlay.Hwnd != IntPtr.Zero)
                    hwnds.Add(overlay.Hwnd);
            }
            return hwnds;
        }
    }

    /// <summary>
    /// Returns the active overlay windows.
    /// </summary>
    public IReadOnlyList<OverlayWindow> Overlays => _overlays.AsReadOnly();

    /// <summary>
    /// Flies the blue cursor to <paramref name="screenPoint"/> on the overlay whose
    /// monitor contains the point, showing <paramref name="bubbleText"/> on arrival.
    /// If <paramref name="displayBounds"/> is provided, it is used to select the overlay;
    /// otherwise the overlay whose bounds contain the point is chosen.
    /// Must be called on the WPF dispatcher thread.
    /// </summary>
    public void FlyTo(System.Windows.Point screenPoint, Rectangle? displayBounds = null, string? bubbleText = null)
    {
        FlyToInternal(screenPoint, displayBounds, bubbleText, DebugOptions);
    }

    /// <summary>
    /// Creates overlay windows for all connected monitors and subscribes
    /// to display configuration changes.
    /// Must be called on the WPF dispatcher thread.
    /// </summary>
    public void Start()
    {
        CreateOverlays();
        ResumeCursorFollowing();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Temporarily hides the blue cursor, typically before screen capture.</summary>
    public void SuspendCursorFollowing()
    {
        StopPointingReturnTimer();

        foreach (var overlay in _overlays)
        {
            overlay.BlueCursor.SuspendFollowing();
        }
    }

    /// <summary>Shows the blue cursor beside the user's real cursor on the current monitor.</summary>
    public void ResumeCursorFollowing()
    {
        var cursorOverlay = ResolveCursorOverlay();
        cursorOverlay?.ReassertTopmost();

        foreach (var overlay in _overlays)
        {
            overlay.BlueCursor.StartFollowing(
                overlay.MonitorBounds,
                overlay.DpiScaleX,
                overlay.DpiScaleY);
        }
    }

    /// <summary>
    /// Flies the blue cursor to the center of the cursor's monitor.
    /// </summary>
    public void TestFlyToCenter()
    {
        TestFlyToPreset("center");
    }

    /// <summary>
    /// Calibration path that bypasses model output and points to a known physical screen point.
    /// </summary>
    public void TestFlyToPreset(string presetId)
    {
        var target = ResolveCursorOverlay();
        if (target is null)
        {
            Logger?.Invoke($"[TEST] preset={presetId}: no overlays available");
            return;
        }

        var point = ComputeCalibrationPoint(target.MonitorBounds, presetId);
        Logger?.Invoke(
            $"[TEST] preset={presetId}: targetPhysical=({point.X:F1},{point.Y:F1}) " +
            $"bounds=({target.MonitorBounds.X},{target.MonitorBounds.Y},{target.MonitorBounds.Width},{target.MonitorBounds.Height}) " +
            $"dpiScale=({target.DpiScaleX:F3},{target.DpiScaleY:F3})");

        FlyToInternal(point, target.MonitorBounds, $"test {presetId}", CalibrationOptions);
    }

    internal static System.Windows.Point ComputeCalibrationPoint(Rectangle bounds, string presetId)
    {
        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right - 1;
        var bottom = bounds.Bottom - 1;
        var centerX = bounds.Left + bounds.Width / 2.0;
        var centerY = bounds.Top + bounds.Height / 2.0;
        var quarterX = bounds.Left + bounds.Width * 0.25;
        var quarterY = bounds.Top + bounds.Height * 0.25;
        var threeQuarterX = bounds.Left + bounds.Width * 0.75;
        var threeQuarterY = bounds.Top + bounds.Height * 0.75;

        return presetId.ToLowerInvariant() switch
        {
            "center" => new System.Windows.Point(centerX, centerY),
            "top-left" => new System.Windows.Point(left, top),
            "top-right" => new System.Windows.Point(right, top),
            "bottom-left" => new System.Windows.Point(left, bottom),
            "bottom-right" => new System.Windows.Point(right, bottom),
            "upper-mid" => new System.Windows.Point(centerX, quarterY),
            "lower-mid" => new System.Windows.Point(centerX, threeQuarterY),
            "left-mid" => new System.Windows.Point(quarterX, centerY),
            "right-mid" => new System.Windows.Point(threeQuarterX, centerY),
            "mouse" => GetCursorPoint(),
            _ => new System.Windows.Point(centerX, centerY),
        };
    }

    /// <summary>
    /// Tears down all overlay windows and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        DestroyOverlays();
    }

    private void FlyToInternal(
        System.Windows.Point screenPoint,
        Rectangle? displayBounds,
        string? bubbleText,
        PointerDebugOptions options)
    {
        foreach (var overlay in _overlays)
        {
            overlay.BlueCursor.SuspendFollowing();
            overlay.DebugCrosshair.Hide();
        }

        OverlayWindow? target = null;
        if (displayBounds.HasValue)
        {
            target = _overlays.FirstOrDefault(o => o.MonitorBounds == displayBounds.Value);
        }

        target ??= _overlays.FirstOrDefault(o =>
            screenPoint.X >= o.MonitorBounds.X &&
            screenPoint.X < o.MonitorBounds.X + o.MonitorBounds.Width &&
            screenPoint.Y >= o.MonitorBounds.Y &&
            screenPoint.Y < o.MonitorBounds.Y + o.MonitorBounds.Height);

        target ??= _overlays.FirstOrDefault();

        if (target is null)
        {
            Logger?.Invoke($"[POINT] overlay: no match - overlayCount={_overlays.Count}");
            return;
        }

        target.ReassertTopmost();

        var idx = _overlays.IndexOf(target);
        Logger?.Invoke(
            $"[POINT] overlay: matched overlay index={idx} bounds=({target.MonitorBounds.X},{target.MonitorBounds.Y},{target.MonitorBounds.Width},{target.MonitorBounds.Height}) " +
            $"windowDip=({target.Left:F2},{target.Top:F2},{target.Width:F2},{target.Height:F2}) " +
            $"screenPoint=({screenPoint.X:F1},{screenPoint.Y:F1}) overlayCount={_overlays.Count} " +
            $"crosshair={options.ShowTargetCrosshair} pinpoint={options.PinpointMode}");

        if (options.ShowTargetCrosshair)
        {
            target.DebugCrosshair.ShowAt(screenPoint, target.MonitorBounds, target.DpiScaleX, target.DpiScaleY);
        }

        if (_activePointingOverlay is not null)
        {
            _activePointingOverlay.BlueCursor.PointingCompleted -= OnPointingCompleted;
        }

        _activePointingOverlay = target;
        target.BlueCursor.PointingCompleted += OnPointingCompleted;
        target.BlueCursor.FlyTo(
            screenPoint,
            target.MonitorBounds,
            target.DpiScaleX,
            target.DpiScaleY,
            bubbleText,
            options.PinpointMode,
            Logger);
        StartPointingReturnTimer();
    }

    private void OnPointingCompleted(object? sender, EventArgs e)
    {
        ReturnCursorToMouse(forceHideActivePoint: false);
    }

    private void ReturnCursorToMouse(bool forceHideActivePoint)
    {
        StopPointingReturnTimer();

        if (_activePointingOverlay is not null)
        {
            _activePointingOverlay.BlueCursor.PointingCompleted -= OnPointingCompleted;
            if (forceHideActivePoint)
            {
                _activePointingOverlay.BlueCursor.SuspendFollowing();
            }
            _activePointingOverlay = null;
        }

        foreach (var overlay in _overlays)
        {
            overlay.DebugCrosshair.Hide();
        }

        ResumeCursorFollowing();
    }

    private void StartPointingReturnTimer()
    {
        StopPointingReturnTimer();
        _pointingReturnTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(PointingReturnSeconds),
        };
        _pointingReturnTimer.Tick += OnPointingReturnTimerTick;
        _pointingReturnTimer.Start();
    }

    private void StopPointingReturnTimer()
    {
        if (_pointingReturnTimer is null)
            return;

        _pointingReturnTimer.Stop();
        _pointingReturnTimer.Tick -= OnPointingReturnTimerTick;
        _pointingReturnTimer = null;
    }

    private void OnPointingReturnTimerTick(object? sender, EventArgs e)
    {
        ReturnCursorToMouse(forceHideActivePoint: true);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        DestroyOverlays();
        CreateOverlays();
        ResumeCursorFollowing();
    }

    private void CreateOverlays()
    {
        var monitors = _enumerateMonitors();

        foreach (var monitor in monitors)
        {
            var overlay = new OverlayWindow(monitor.Bounds, monitor.DpiScaleX, monitor.DpiScaleY);
            overlay.Show();
            overlay.ReassertTopmost();
            _overlays.Add(overlay);
        }
    }

    private void DestroyOverlays()
    {
        StopPointingReturnTimer();

        if (_activePointingOverlay is not null)
        {
            _activePointingOverlay.BlueCursor.PointingCompleted -= OnPointingCompleted;
            _activePointingOverlay = null;
        }

        foreach (var overlay in _overlays)
        {
            overlay.Close();
        }
        _overlays.Clear();
    }

    /// <summary>
    /// Enumerates monitors using EnumDisplayMonitors P/Invoke,
    /// returning bounds as <see cref="MonitorRect"/> records.
    /// </summary>
    private static List<MonitorRect> EnumerateMonitorsFromSystem()
    {
        var result = new List<MonitorRect>();
        s_monitorRectList = result;

        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero, IntPtr.Zero, MonitorEnumCallback, IntPtr.Zero);

        s_monitorRectList = null;
        return result;
    }

    [ThreadStatic]
    private static List<MonitorRect>? s_monitorRectList;

    private static bool MonitorEnumCallback(
        IntPtr hMonitor, IntPtr hdcMonitor,
        ref NativeMethods.RECT lprcMonitor, IntPtr dwData)
    {
        var info = new NativeMethods.MONITORINFOEX();
        info.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>();

        if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            var (scaleX, scaleY) = DpiHelper.GetDpiScale(hMonitor);
            s_monitorRectList?.Add(new MonitorRect(
                new Rectangle(
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    info.rcMonitor.Width,
                    info.rcMonitor.Height),
                scaleX,
                scaleY));
        }

        return true;
    }

    private OverlayWindow? ResolveCursorOverlay()
    {
        var cursor = GetCursorPoint();
        return _overlays.FirstOrDefault(o =>
            cursor.X >= o.MonitorBounds.Left &&
            cursor.X < o.MonitorBounds.Right &&
            cursor.Y >= o.MonitorBounds.Top &&
            cursor.Y < o.MonitorBounds.Bottom)
            ?? _overlays.FirstOrDefault();
    }

    private static System.Windows.Point GetCursorPoint()
    {
        NativeMethods.GetCursorPos(out var point);
        return new System.Windows.Point(point.X, point.Y);
    }
}

/// <summary>Monitor bounds (physical pixels) plus per-monitor DPI scale for overlay placement.</summary>
public record MonitorRect(Rectangle Bounds, double DpiScaleX = 1.0, double DpiScaleY = 1.0);
