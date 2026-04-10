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
    private CancellationTokenSource? _hookConsumerCts;

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

        // Probe the Windows 10/11 microphone privacy gate in the background
        // so the panel reflects current permission state. Mirrors Mac's
        // AVCaptureDevice.authorizationStatus check in BuddyDictationManager.
        _ = ProbeMicrophonePermissionAsync();

        // Probe screen capture availability. On Windows desktop apps this
        // is almost always true (no explicit user consent needed unlike macOS),
        // but WGC may be unavailable on very old builds or VMs.
        _ = ProbeScreenCapturePermissionAsync();

        // Install the global push-to-talk hook on the dispatcher thread.
        // Mirrors GlobalPushToTalkShortcutMonitor.start() on Mac.
        _pushToTalkHook = new GlobalPushToTalkHook(_companionViewModel.PushToTalkShortcut);
        _pushToTalkHook.Start();
        _hookConsumerCts = new CancellationTokenSource();
        _ = ConsumeHotkeyTransitionsAsync(_pushToTalkHook, _hookConsumerCts.Token);
    }

    private async Task ConsumeHotkeyTransitionsAsync(GlobalPushToTalkHook hook, CancellationToken ct)
    {
        try
        {
            await foreach (var transition in hook.Transitions.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_companionViewModel is not null)
                    {
                        _companionViewModel.IsShortcutPressed =
                            transition == ShortcutTransition.Pressed;
                    }
                });
            }
        }
        catch (System.OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async System.Threading.Tasks.Task ProbeMicrophonePermissionAsync()
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
        _hookConsumerCts?.Cancel();
        _pushToTalkHook?.Dispose();
        _pushToTalkHook = null;
        _hookConsumerCts?.Dispose();
        _hookConsumerCts = null;

        if (_trayIconManager is not null)
        {
            _trayIconManager.TrayIconClicked -= OnTrayIconClicked;
            _trayIconManager.Dispose();
        }
        _companionPanel?.Close();
        base.OnExit(e);
    }
}
