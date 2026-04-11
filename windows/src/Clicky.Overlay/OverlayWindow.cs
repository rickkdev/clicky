using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Clicky.Overlay;

/// <summary>
/// A fullscreen, transparent, click-through, always-on-top overlay window
/// for a single monitor. Mirrors <c>OverlayWindow</c> from the Mac reference
/// implementation (OverlayWindow.swift).
///
/// Hosts a <see cref="BlueCursorControl"/> for rendering the animated blue cursor.
/// </summary>
public class OverlayWindow : Window
{
    /// <summary>The HWND of this overlay, available after the window is shown.</summary>
    public IntPtr Hwnd { get; private set; }

    /// <summary>The monitor bounds this overlay covers (in physical pixels).</summary>
    public Rectangle MonitorBounds { get; }

    /// <summary>The blue cursor control hosted on this overlay.</summary>
    public BlueCursorControl BlueCursor { get; }

    public OverlayWindow(Rectangle monitorBounds)
    {
        MonitorBounds = monitorBounds;

        // Borderless, transparent, always-on-top, no taskbar entry, not hit-testable
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;

        // Position and size to cover the monitor.
        // We use the raw pixel bounds here; WPF's layout system + PerMonitorV2
        // DPI awareness means WPF coordinates == physical pixels for windows
        // positioned with WindowStartupLocation.Manual + Left/Top/Width/Height.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitorBounds.X;
        Top = monitorBounds.Y;
        Width = monitorBounds.Width;
        Height = monitorBounds.Height;

        // Host the blue cursor control on a Canvas that fills the overlay
        BlueCursor = new BlueCursorControl();
        var canvas = new Canvas
        {
            IsHitTestVisible = false
        };
        canvas.Children.Add(BlueCursor.Visual);
        Content = canvas;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        Hwnd = helper.Handle;

        // Set extended window styles so the overlay:
        //  - WS_EX_TRANSPARENT: mouse clicks pass through to windows beneath
        //  - WS_EX_TOOLWINDOW: never appears in Alt+Tab
        //  - WS_EX_NOACTIVATE: never steals focus
        var exStyle = NativeMethods.GetWindowLong(Hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TRANSPARENT
                 | NativeMethods.WS_EX_TOOLWINDOW
                 | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(Hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    /// <summary>Disposes the hosted BlueCursorControl when the window closes.</summary>
    protected override void OnClosed(EventArgs e)
    {
        BlueCursor.Dispose();
        base.OnClosed(e);
    }
}
