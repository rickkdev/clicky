using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Clicky.Api;
using Clicky.Audio;
using Clicky.Capture;
using Clicky.Companion;
using Clicky.Hotkey;
using Clicky.Overlay;

namespace Clicky.App;

public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private CompanionPanelWindow? _companionPanel;
    private CompanionViewModel? _companionViewModel;
    private GlobalPushToTalkHook? _pushToTalkHook;
    private CompanionManager? _companionManager;
    private OverlayWindowManager? _overlayManager;
    private DispatcherTimer? _permissionPollTimer;
    private AutoUpdateService? _autoUpdateService;
    private SettingsStore? _settingsStore;
    private SecretsStore? _secretsStore;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create the hidden host window (no taskbar entry, invisible).
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        // Initialize settings and secrets stores under %APPDATA%\Clicky.
        _settingsStore = new SettingsStore();
        _secretsStore = new SecretsStore();

        // Migrate US-014 registry values to SettingsStore on first run.
        MigrateRegistrySettings(_settingsStore);

        // ViewModel that the panel binds to.
        _companionViewModel = new CompanionViewModel();

        // Load onboarding state from SettingsStore (migrated from registry above).
        _companionViewModel.HasCompletedOnboarding = _settingsStore.OnboardingComplete;

        _companionPanel = new CompanionPanelWindow(_companionViewModel);

        // Set up system tray icon with menu and left-click event.
        _trayIconManager = new TrayIconManager();
        _trayIconManager.TrayIconClicked += OnTrayIconClicked;

        // Register for auto-start on first launch (mirrors SMAppService.mainApp.register).
        AutoStartRegistration.EnsureRegistered();

        // Configure PostHog analytics (mirrors ClickyAnalytics.configure() in Mac).
        // Respects HKCU\Software\Clicky\analyticsOptOut registry flag.
        ClickyAnalytics.Configure();
        ClickyAnalytics.TrackAppOpened();

        // Probe the Windows 10/11 microphone privacy gate in the background.
        _ = ProbeMicrophonePermissionAsync();

        // Probe screen capture availability.
        _ = ProbeScreenCapturePermissionAsync();

        // Start a permission polling timer (mirrors Mac's 1.5s timer) so the
        // panel updates in real time as the user grants permissions in Settings.
        _permissionPollTimer = new DispatcherTimer
        {
            Interval = System.TimeSpan.FromSeconds(1.5),
        };
        _permissionPollTimer.Tick += OnPermissionPollTick;
        _permissionPollTimer.Start();

        // On first launch (no onboarded registry value) or if any permission
        // is missing on subsequent launches, auto-open the panel.
        // We defer this so the tray icon and window are fully initialized first.
        Dispatcher.InvokeAsync(() =>
        {
            if (_companionViewModel.IsOnboardingVisible)
            {
                _companionPanel.ShowForOnboarding();
            }
        }, DispatcherPriority.Loaded);

        // Install the global push-to-talk hook on the dispatcher thread.
        _pushToTalkHook = new GlobalPushToTalkHook(_companionViewModel.PushToTalkShortcut);
        _pushToTalkHook.Start();

        // Create transparent overlay windows for each monitor (US-011).
        // Must be created on the dispatcher thread before CompanionManager
        // so overlay HWNDs can be excluded from screen captures.
        _overlayManager = new OverlayWindowManager();
        _overlayManager.Start();

        // Read worker base URL from appsettings.json (still used for audio clients
        // until US-021 migrates them to direct API calls).
        var workerBaseUrl = ReadWorkerBaseUrl();

        // Construct the LLM client from SecretsStore key + SettingsStore model.
        var anthropicKey = _secretsStore.Read(SecretsStore.AnthropicApiKey) ?? "";
        var llmClient = new AnthropicDirectClient(
            apiKey: anthropicKey,
            model: _settingsStore.LlmModel);

        var transcriber = new AssemblyAiStreamingTranscriber(workerBaseUrl);
        var ttsClient = new ElevenLabsTtsClient(workerBaseUrl);

        // Create and start the CompanionManager state machine that orchestrates
        // push-to-talk → capture → transcribe → LLM → TTS.
        _companionManager = new CompanionManager(
            _companionViewModel,
            _pushToTalkHook,
            llmClient,
            transcriber,
            ttsClient,
            Dispatcher,
            _overlayManager);
        _companionManager.Start();

        // Initialize WinSparkle auto-update (mirrors Sparkle integration in Mac).
        // Runs a single background update check on launch; failures are logged
        // but never block startup. The appcast URL is a placeholder — the
        // maintainer must replace it with a real Windows-specific feed URL.
        _autoUpdateService = new AutoUpdateService();
        _autoUpdateService.Initialize(
            "https://raw.githubusercontent.com/julianjear/makesomething-mac-app/main/appcast.xml");
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

    /// <summary>
    /// Polls permissions every 1.5 s so the onboarding panel reflects real-time
    /// state as the user grants access in Windows Settings. Mirrors Mac's
    /// accessibilityCheckTimer in CompanionManager.swift.
    /// When all permissions are granted and onboarding hasn't been marked complete,
    /// sets HKCU\Software\Clicky\onboarded=1 and dismisses the onboarding view.
    /// </summary>
    private void OnPermissionPollTick(object? sender, System.EventArgs e)
    {
        _ = RefreshPermissionsAsync();
    }

    private async Task RefreshPermissionsAsync()
    {
        var micTask = MicrophonePermissions.ProbeAsync();
        var captureTask = ScreenCapturePermissions.ProbeAsync();

        var micGranted = await micTask.ConfigureAwait(false);
        var captureGranted = await captureTask.ConfigureAwait(false);

        await Dispatcher.InvokeAsync(() =>
        {
            if (_companionViewModel is null) return;

            _companionViewModel.HasMicrophonePermission = micGranted;
            _companionViewModel.HasScreenCapturePermission = captureGranted;

            // When all permissions are granted and onboarding hasn't been
            // completed yet, mark it done and hide the panel.
            if (_companionViewModel.AllPermissionsGranted &&
                !_companionViewModel.HasCompletedOnboarding)
            {
                if (_settingsStore is not null)
                    _settingsStore.OnboardingComplete = true;
                _companionViewModel.HasCompletedOnboarding = true;
            }
        });
    }

    /// <summary>
    /// Migrates onboarded and analyticsOptOut values from HKCU\Software\Clicky
    /// registry keys (US-014/US-016) to SettingsStore on first run of US-019 code.
    /// Only copies if the SettingsStore file doesn't already contain the value.
    /// </summary>
    private static void MigrateRegistrySettings(SettingsStore settings)
    {
        try
        {
            // Migrate onboarding state from registry if not yet set in SettingsStore.
            if (!settings.OnboardingComplete && OnboardingService.HasCompletedOnboarding())
            {
                settings.OnboardingComplete = true;
            }

            // Migrate analytics opt-out from registry if not yet set in SettingsStore.
            if (!settings.AnalyticsOptOut && ClickyAnalytics.IsOptedOut())
            {
                settings.AnalyticsOptOut = true;
            }
        }
        catch
        {
            // Migration is best-effort — don't block startup.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _permissionPollTimer?.Stop();
        _permissionPollTimer = null;

        _autoUpdateService?.Dispose();
        _autoUpdateService = null;

        ClickyAnalytics.Shutdown();

        _companionManager?.Dispose();
        _companionManager = null;

        _overlayManager?.Dispose();
        _overlayManager = null;

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
