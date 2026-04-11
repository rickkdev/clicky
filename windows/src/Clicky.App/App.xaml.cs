using System.Collections.Generic;
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

        // Check if first-run setup is needed: required keys missing OR not onboarded.
        if (!HasRequiredKeys(_secretsStore, _settingsStore) || !_settingsStore.OnboardingComplete)
        {
            var vm = new SettingsViewModel(_secretsStore, _settingsStore);
            var settingsWindow = new SettingsWindow(vm, isFirstRun: true);
            bool saved = false;

            settingsWindow.SettingsSaved += (_, _) =>
            {
                saved = true;
                _settingsStore.OnboardingComplete = true;
                _companionViewModel.HasCompletedOnboarding = true;
                ClickyAnalytics.TrackOnboardingCompleted();
            };

            // ShowDialog blocks until the window is closed.
            // If the user closes via X or Quit without saving, the app exits
            // (handled in SettingsWindow.OnClosing).
            settingsWindow.ShowDialog();

            if (!saved)
            {
                // The app is shutting down — don't continue initialization.
                return;
            }
        }

        // All required keys are present — proceed with full app initialization.
        InitializeApp();
    }

    /// <summary>
    /// Full app initialization: tray icon, overlay, companion manager, permissions, auto-update.
    /// Called after keys are confirmed to be present (either from prior launch or first-run setup).
    /// </summary>
    private void InitializeApp()
    {
        _companionPanel = new CompanionPanelWindow(_companionViewModel!);

        // Set up system tray icon with menu and left-click event.
        _trayIconManager = new TrayIconManager();
        _trayIconManager.TrayIconClicked += OnTrayIconClicked;
        _trayIconManager.SettingsClicked += OnSettingsClicked;
        _trayIconManager.ModelSelected += OnModelSelected;

        // Register for auto-start on first launch (mirrors SMAppService.mainApp.register).
        AutoStartRegistration.EnsureRegistered();

        // Configure PostHog analytics (mirrors ClickyAnalytics.configure() in Mac).
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
            if (_companionViewModel!.IsOnboardingVisible)
            {
                _companionPanel.ShowForOnboarding();
            }
        }, DispatcherPriority.Loaded);

        // Install the global push-to-talk hook on the dispatcher thread.
        _pushToTalkHook = new GlobalPushToTalkHook(_companionViewModel!.PushToTalkShortcut);
        _pushToTalkHook.Start();

        // Create transparent overlay windows for each monitor (US-011).
        _overlayManager = new OverlayWindowManager();
        _overlayManager.Start();

        // Build and start the CompanionManager with current keys/config.
        BuildAndStartCompanionManager();

        // Set the initial model display name on the ViewModel.
        _companionViewModel!.LlmModelDisplay = GetModelDisplayName(
            _settingsStore!.LlmProvider, _settingsStore.LlmModel);

        // Populate the tray Model submenu.
        RefreshModelMenu();

        // Initialize WinSparkle auto-update (mirrors Sparkle integration in Mac).
        _autoUpdateService = new AutoUpdateService();
        _autoUpdateService.Initialize(
            "https://raw.githubusercontent.com/julianjear/makesomething-mac-app/main/appcast.xml");
    }

    /// <summary>
    /// Constructs an ILlmClient + audio clients from the current SecretsStore/SettingsStore
    /// state, creates a CompanionManager, wires up error handling, and starts it.
    /// </summary>
    private void BuildAndStartCompanionManager()
    {
        // Dispose old manager if rebuilding after a settings change.
        if (_companionManager is not null)
        {
            _companionManager.OpenSettingsRequested -= OnPipelineKeyError;
            _companionManager.Dispose();
            _companionManager = null;
        }

        var llmClient = BuildLlmClient();
        var transcriber = BuildTranscriber();
        var ttsClient = BuildTtsClient();

        _companionManager = new CompanionManager(
            _companionViewModel!,
            _pushToTalkHook!,
            llmClient,
            transcriber,
            ttsClient,
            Dispatcher,
            _overlayManager);
        _companionManager.OpenSettingsRequested += OnPipelineKeyError;
        _companionManager.Start();
    }

    private ILlmClient BuildLlmClient()
    {
        var provider = _settingsStore!.LlmProvider;
        var model = _settingsStore.LlmModel;

        if (provider == "zai")
        {
            var key = _secretsStore!.Read(SecretsStore.ZaiApiKey) ?? "";
            return new ZaiDirectClient(apiKey: key, model: model);
        }
        else
        {
            var key = _secretsStore!.Read(SecretsStore.AnthropicApiKey) ?? "";
            return new AnthropicDirectClient(apiKey: key, model: model);
        }
    }

    private AssemblyAiStreamingTranscriber BuildTranscriber()
    {
        var key = _secretsStore!.Read(SecretsStore.AssemblyAiApiKey) ?? "";
        return new AssemblyAiStreamingTranscriber(key);
    }

    private ElevenLabsTtsClient BuildTtsClient()
    {
        var key = _secretsStore!.Read(SecretsStore.ElevenLabsApiKey) ?? "";
        var voiceId = _settingsStore!.ElevenLabsVoiceId;
        return new ElevenLabsTtsClient(key, voiceId);
    }

    /// <summary>
    /// Builds the list of (provider, model) entries for the tray Model submenu.
    /// Items whose required API key is missing are disabled with a tooltip.
    /// </summary>
    internal List<ModelMenuEntry> BuildModelMenuEntries()
    {
        bool hasAnthropicKey = _secretsStore!.Exists(SecretsStore.AnthropicApiKey);
        bool hasZaiKey = _secretsStore!.Exists(SecretsStore.ZaiApiKey);
        string disabledAnthropicTip = "Add your Anthropic key in Settings to enable this model";
        string disabledZaiTip = "Add your z.ai key in Settings to enable this model";

        return new List<ModelMenuEntry>
        {
            new() { Provider = "anthropic", Model = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6", IsEnabled = hasAnthropicKey, DisabledTooltip = hasAnthropicKey ? null : disabledAnthropicTip },
            new() { Provider = "anthropic", Model = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5", IsEnabled = hasAnthropicKey, DisabledTooltip = hasAnthropicKey ? null : disabledAnthropicTip },
            new() { Provider = "anthropic", Model = "claude-opus-4-6", DisplayName = "Claude Opus 4.6", IsEnabled = hasAnthropicKey, DisabledTooltip = hasAnthropicKey ? null : disabledAnthropicTip },
            new() { Provider = "zai", Model = "glm-4.6v", DisplayName = "GLM-4.6V", IsEnabled = hasZaiKey, DisabledTooltip = hasZaiKey ? null : disabledZaiTip },
            new() { Provider = "zai", Model = "glm-4.5v", DisplayName = "GLM-4.5V", IsEnabled = hasZaiKey, DisabledTooltip = hasZaiKey ? null : disabledZaiTip },
        };
    }

    /// <summary>
    /// Returns a user-friendly display name for the current (provider, model) pair.
    /// </summary>
    internal static string GetModelDisplayName(string provider, string model)
    {
        return (provider, model) switch
        {
            ("anthropic", "claude-sonnet-4-6") => "Claude Sonnet 4.6",
            ("anthropic", "claude-haiku-4-5") => "Claude Haiku 4.5",
            ("anthropic", "claude-opus-4-6") => "Claude Opus 4.6",
            ("zai", "glm-4.6v") => "GLM-4.6V",
            ("zai", "glm-4.5v") => "GLM-4.5V",
            _ => model,
        };
    }

    private void RefreshModelMenu()
    {
        if (_trayIconManager is null || _settingsStore is null) return;
        var entries = BuildModelMenuEntries();
        _trayIconManager.UpdateModelMenu(entries, _settingsStore.LlmProvider, _settingsStore.LlmModel);
    }

    private async void OnModelSelected(object? sender, ModelSelectedEventArgs e)
    {
        if (_settingsStore is null || _secretsStore is null || _companionManager is null) return;

        var fromProvider = _settingsStore.LlmProvider;
        var fromModel = _settingsStore.LlmModel;

        // No-op if the user clicked the already-active model.
        if (fromProvider == e.Provider && fromModel == e.Model) return;

        // Update settings store.
        _settingsStore.LlmProvider = e.Provider;
        _settingsStore.LlmModel = e.Model;

        // Build new LLM client and swap it into the running CompanionManager.
        var newClient = BuildLlmClient();
        await _companionManager.SwapLlmClientAsync(newClient);

        // Update the ViewModel display.
        if (_companionViewModel is not null)
            _companionViewModel.LlmModelDisplay = GetModelDisplayName(e.Provider, e.Model);

        // Refresh the tray menu to reflect the new active model.
        RefreshModelMenu();

        // Track analytics event.
        ClickyAnalytics.TrackModelSwitched(fromProvider, fromModel, e.Provider, e.Model);
    }

    /// <summary>
    /// Checks whether the currently-selected provider's LLM key and both audio
    /// keys are present in the SecretsStore.
    /// </summary>
    internal static bool HasRequiredKeys(SecretsStore secrets, SettingsStore settings)
    {
        var provider = settings.LlmProvider;
        bool hasLlmKey = provider == "zai"
            ? secrets.Exists(SecretsStore.ZaiApiKey)
            : secrets.Exists(SecretsStore.AnthropicApiKey);

        return hasLlmKey
            && secrets.Exists(SecretsStore.AssemblyAiApiKey)
            && secrets.Exists(SecretsStore.ElevenLabsApiKey);
    }

    /// <summary>
    /// Called when CompanionManager detects a key-related pipeline error (e.g. 401).
    /// Surfaces the error to the user and auto-opens SettingsWindow.
    /// </summary>
    private void OnPipelineKeyError(object? sender, System.EventArgs e)
    {
        OpenSettingsWindow(isFirstRun: false);
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

    private void OnSettingsClicked(object? sender, System.EventArgs e)
    {
        ClickyAnalytics.TrackSettingsOpened();
        OpenSettingsWindow(isFirstRun: false);
    }

    private void OpenSettingsWindow(bool isFirstRun)
    {
        if (_settingsStore is null || _secretsStore is null) return;

        var vm = new SettingsViewModel(_secretsStore, _settingsStore);
        var window = new SettingsWindow(vm, isFirstRun);
        window.SettingsSaved += OnSettingsWindowSaved;
        window.Show();
    }

    private void OnSettingsWindowSaved(object? sender, System.EventArgs e)
    {
        // Settings were saved — rebuild all clients with the new keys/config.
        _companionViewModel?.ClearError();
        BuildAndStartCompanionManager();

        // Update model display and refresh menu (keys may have changed).
        if (_companionViewModel is not null && _settingsStore is not null)
            _companionViewModel.LlmModelDisplay = GetModelDisplayName(
                _settingsStore.LlmProvider, _settingsStore.LlmModel);
        RefreshModelMenu();
    }

    /// <summary>
    /// Polls permissions every 1.5 s so the onboarding panel reflects real-time
    /// state as the user grants access in Windows Settings.
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
    /// </summary>
    private static void MigrateRegistrySettings(SettingsStore settings)
    {
        try
        {
            if (!settings.OnboardingComplete && OnboardingService.HasCompletedOnboarding())
            {
                settings.OnboardingComplete = true;
            }

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

        if (_companionManager is not null)
        {
            _companionManager.OpenSettingsRequested -= OnPipelineKeyError;
            _companionManager.Dispose();
            _companionManager = null;
        }

        _overlayManager?.Dispose();
        _overlayManager = null;

        _pushToTalkHook?.Dispose();
        _pushToTalkHook = null;

        if (_trayIconManager is not null)
        {
            _trayIconManager.TrayIconClicked -= OnTrayIconClicked;
            _trayIconManager.SettingsClicked -= OnSettingsClicked;
            _trayIconManager.ModelSelected -= OnModelSelected;
            _trayIconManager.Dispose();
        }
        _companionPanel?.Close();
        base.OnExit(e);
    }
}
