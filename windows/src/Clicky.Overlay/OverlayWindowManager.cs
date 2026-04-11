using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Clicky.Overlay;

/// <summary>
/// Manages one <see cref="OverlayWindow"/> per connected monitor.
/// Recreates overlays when displays are added or removed.
/// Mirrors <c>OverlayWindowManager</c> from the Mac reference
/// (OverlayWindow.swift → setupOverlayWindows / handleDisplayConfigChange).
/// </summary>
public sealed class OverlayWindowManager : IDisposable
{
    private readonly List<OverlayWindow> _overlays = new();
    private readonly Func<List<MonitorRect>> _enumerateMonitors;
    private bool _disposed;

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
    /// Returns the active overlay windows (exposed for future stories
    /// like US-012 that need to render on specific overlays).
    /// </summary>
    public IReadOnlyList<OverlayWindow> Overlays => _overlays.AsReadOnly();

    /// <summary>
    /// Creates overlay windows for all connected monitors and subscribes
    /// to display configuration changes.
    /// Must be called on the WPF dispatcher thread.
    /// </summary>
    public void Start()
    {
        CreateOverlays();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
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

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Recreate overlays for the new monitor layout.
        // SystemEvents fires on the UI thread when the app has a message pump.
        DestroyOverlays();
        CreateOverlays();
    }

    private void CreateOverlays()
    {
        var monitors = _enumerateMonitors();

        foreach (var monitor in monitors)
        {
            var overlay = new OverlayWindow(monitor.Bounds);
            overlay.Show();
            _overlays.Add(overlay);
        }
    }

    private void DestroyOverlays()
    {
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
            s_monitorRectList?.Add(new MonitorRect(new Rectangle(
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Width,
                info.rcMonitor.Height)));
        }

        return true;
    }
}

/// <summary>Simple record wrapping a monitor's bounds for overlay placement.</summary>
public record MonitorRect(Rectangle Bounds);
