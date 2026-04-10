using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Clicky.Companion;

namespace Clicky.App;

/// <summary>
/// The borderless popup control panel that mirrors Mac's CompanionPanelView.
/// Anchors near the system tray, auto-hides on deactivation, and binds to
/// <see cref="CompanionViewModel"/> for voice state + permission rows.
/// </summary>
public partial class CompanionPanelWindow : Window
{
    private readonly CompanionViewModel _viewModel;

    public CompanionPanelWindow(CompanionViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
        Deactivated += OnDeactivated;
    }

    /// <summary>
    /// Toggle visibility. When showing, anchor to the bottom-right of the
    /// primary work area (next to the tray) and activate so Deactivated can
    /// drive the auto-hide.
    /// </summary>
    public void Toggle()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
            return;
        }

        AnchorNearTray();
        Show();
        Activate();
        Focus();
    }

    private void AnchorNearTray()
    {
        // SystemParameters.WorkArea is in DIPs on the primary monitor, which
        // is where the tray lives on a single-monitor default setup. Future
        // stories can refine this via Shell_NotifyIconGetRect when we need
        // multi-monitor tray accuracy.
        var workArea = SystemParameters.WorkArea;

        // Force layout so ActualHeight reflects the size-to-content measure.
        Measure(new Size(Width, double.PositiveInfinity));
        var height = DesiredSize.Height > 0 ? DesiredSize.Height : ActualHeight;
        if (height <= 0) height = 420;

        const double margin = 8;
        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - height - margin;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Mark the window as a tool window so it never appears in Alt+Tab,
        // matching the Mac menu-bar panel behavior.
        var helper = new WindowInteropHelper(this);
        var style = GetWindowLong(helper.Handle, GWL_EXSTYLE);
        SetWindowLong(helper.Handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW);
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    // --- Win32 interop for WS_EX_TOOLWINDOW ---

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
