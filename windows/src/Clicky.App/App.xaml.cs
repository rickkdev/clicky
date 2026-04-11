using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Clicky.Audio;
using Clicky.Capture;
using Clicky.Companion;
using Clicky.Hotkey;

namespace Clicky.App;

public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private CompanionPanelWindow? _companionPanel;
    private CompanionViewModel? _companionViewModel;
    private GlobalPushToTalkHook? _pushToTalkHook;
    private CompanionManager? _companionManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create the hidden host window (no taskbar entry, invisible).
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        // ViewModel that the panel binds to.
        _companionViewModel = new CompanionViewModel();
        _companionPanel = new CompanionPanelWindow(_companionViewModel);

        // Set up system tray icon with menu and left-click event.
        _trayIconManager = new TrayIconManager();
        _trayIconManager.TrayIconClicked += OnTrayIconClicked;

        // Register for auto-start on first launch (mirrors SMAppService.mainApp.register).
        AutoStartRegistration.EnsureRegistered();

        // Probe the Windows 10/11 microphone privacy gate in the background.
        _ = ProbeMicrophonePermissionAsync();

        // Probe screen capture availability.
        _ = ProbeScreenCapturePermissionAsync();

        // Install the global push-to-talk hook on the dispatcher thread.
        _pushToTalkHook = new GlobalPushToTalkHook(_companionViewModel.PushToTalkShortcut);
        _pushToTalkHook.Start();

        // Read worker base URL from appsettings.json.
        var workerBaseUrl = ReadWorkerBaseUrl();

        // Create and start the CompanionManager state machine that orchestrates
        // push-to-talk → capture → transcribe → Claude → TTS.
        _companionManager = new CompanionManager(
            _companionViewModel,
            _pushToTalkHook,
            workerBaseUrl,
            Dispatcher);
        _companionManager.Start();
    }

    private static string ReadWorkerBaseUrl()
    {
        const string defaultUrl = "https://your-worker-name.your-subdomain.workers.dev";
        try
        {
            var appDir = AppContext.BaseDirectory;
            var settingsPath = Path.Combine(appDir, "appsettings.json");
            if (!File.Exists(settingsPath)) return defaultUrl;

            var json = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("WorkerBaseUrl", out var urlProp))
            {
                return urlProp.GetString() ?? defaultUrl;
            }
        }
        catch
        {
            // Fall back to default on any error
        }
        return defaultUrl;
    }

    private async Task ProbeMicrophonePermissionAsync()
    {
        var granted = await MicrophonePermissions.ProbeAsync().ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            if (_companionViewModel is not null)
            {
                _companionViewModel.HasMicrophonePermission = granted;
            }
        });
    }

    private async Task ProbeScreenCapturePermissionAsync()
    {
        var granted = await ScreenCapturePermissions.ProbeAsync().ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            if (_companionViewModel is not null)
            {
                _companionViewModel.HasScreenCapturePermission = granted;
            }
        });
    }

    private void OnTrayIconClicked(object? sender, System.EventArgs e)
    {
        _companionPanel?.Toggle();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _companionManager?.Dispose();
        _companionManager = null;

        _pushToTalkHook?.Dispose();
        _pushToTalkHook = null;

        if (_trayIconManager is not null)
        {
            _trayIconManager.TrayIconClicked -= OnTrayIconClicked;
            _trayIconManager.Dispose();
        }
        _companionPanel?.Close();
        base.OnExit(e);
    }
}
