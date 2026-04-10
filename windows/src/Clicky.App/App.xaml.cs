using System.Windows;
using Clicky.Companion;

namespace Clicky.App;

public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private CompanionPanelWindow? _companionPanel;
    private CompanionViewModel? _companionViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create the hidden host window (no taskbar entry, invisible).
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        // ViewModel that the panel binds to. Later stories will drive its
        // observable properties from the hotkey hook, mic capture, etc.
        _companionViewModel = new CompanionViewModel();
        _companionPanel = new CompanionPanelWindow(_companionViewModel);

        // Set up system tray icon with menu and left-click event.
        _trayIconManager = new TrayIconManager();
        _trayIconManager.TrayIconClicked += OnTrayIconClicked;

        // Register for auto-start on first launch (mirrors SMAppService.mainApp.register).
        AutoStartRegistration.EnsureRegistered();
    }

    private void OnTrayIconClicked(object? sender, System.EventArgs e)
    {
        _companionPanel?.Toggle();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIconManager is not null)
        {
            _trayIconManager.TrayIconClicked -= OnTrayIconClicked;
            _trayIconManager.Dispose();
        }
        _companionPanel?.Close();
        base.OnExit(e);
    }
}
