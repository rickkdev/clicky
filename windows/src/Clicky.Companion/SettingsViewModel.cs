using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clicky.Companion;

/// <summary>
/// ViewModel backing the SettingsWindow. Handles field binding, Test button probes,
/// Save/Quit commands, and provider-dependent model list updates.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SecretsStore _secretsStore;
    private readonly SettingsStore _settingsStore;
    private readonly HttpClient _httpClient;

    private string _selectedProvider = "anthropic";
    private string _selectedModel = "claude-sonnet-4-6";

    private string _anthropicApiKey = "";
    private string _zaiApiKey = "";
    private string _assemblyAiApiKey = "";
    private string _elevenLabsApiKey = "";
    private string _elevenLabsVoiceId = "kPzsL2i3teMYv0FxEYQ6";

    // True when the field shows the saved placeholder instead of the real key.
    private bool _anthropicKeyIsSaved;
    private bool _zaiKeyIsSaved;
    private bool _assemblyAiKeyIsSaved;
    private bool _elevenLabsKeyIsSaved;

    private TestState _anthropicTestState = TestState.None;
    private TestState _zaiTestState = TestState.None;
    private TestState _assemblyAiTestState = TestState.None;
    private TestState _elevenLabsTestState = TestState.None;

    private string? _anthropicTestError;
    private string? _zaiTestError;
    private string? _assemblyAiTestError;
    private string? _elevenLabsTestError;

    // Inline validation errors shown when a required key is missing (first-run / partial config).
    private string? _anthropicKeyError;
    private string? _zaiKeyError;
    private string? _assemblyAiKeyError;
    private string? _elevenLabsKeyError;

    /// <summary>Raised after a successful Save.</summary>
    public event EventHandler? SettingsSaved;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(SecretsStore secretsStore, SettingsStore settingsStore, HttpClient? httpClient = null)
    {
        _secretsStore = secretsStore;
        _settingsStore = settingsStore;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        LoadFromStores();
    }

    // -- Provider / Model --

    public static IReadOnlyList<ProviderChoice> Providers { get; } = new[]
    {
        new ProviderChoice("anthropic", "Anthropic Claude"),
        new ProviderChoice("zai", "z.ai GLM"),
    };

    public string SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (_selectedProvider == value) return;
            _selectedProvider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailableModels));
            OnPropertyChanged(nameof(CanSave));

            // Auto-select first model for the new provider.
            SelectedModel = AvailableModels[0].Value;
        }
    }

    public IReadOnlyList<ModelChoice> AvailableModels => _selectedProvider == "zai"
        ? ZaiModels
        : AnthropicModels;

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (_selectedModel == value) return;
            _selectedModel = value;
            OnPropertyChanged();
        }
    }

    public static IReadOnlyList<ModelChoice> AnthropicModels { get; } = new[]
    {
        new ModelChoice("claude-sonnet-4-6", "Claude Sonnet 4.6"),
        new ModelChoice("claude-haiku-4-5", "Claude Haiku 4.5"),
        new ModelChoice("claude-opus-4-6", "Claude Opus 4.6"),
    };

    public static IReadOnlyList<ModelChoice> ZaiModels { get; } = new[]
    {
        new ModelChoice("glm-4.6v", "GLM-4.6V"),
        new ModelChoice("glm-4.5v", "GLM-4.5V"),
    };

    // -- API Keys --

    public string AnthropicApiKey
    {
        get => _anthropicApiKey;
        set
        {
            if (_anthropicApiKey == value) return;
            _anthropicApiKey = value;
            _anthropicKeyIsSaved = false;
            _anthropicTestState = TestState.None;
            _anthropicKeyError = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AnthropicKeyIsSaved));
            OnPropertyChanged(nameof(AnthropicTestState));
            OnPropertyChanged(nameof(AnthropicKeyError));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool AnthropicKeyIsSaved
    {
        get => _anthropicKeyIsSaved;
        private set { _anthropicKeyIsSaved = value; OnPropertyChanged(); }
    }

    public string ZaiApiKey
    {
        get => _zaiApiKey;
        set
        {
            if (_zaiApiKey == value) return;
            _zaiApiKey = value;
            _zaiKeyIsSaved = false;
            _zaiTestState = TestState.None;
            _zaiKeyError = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ZaiKeyIsSaved));
            OnPropertyChanged(nameof(ZaiTestState));
            OnPropertyChanged(nameof(ZaiKeyError));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool ZaiKeyIsSaved
    {
        get => _zaiKeyIsSaved;
        private set { _zaiKeyIsSaved = value; OnPropertyChanged(); }
    }

    public string AssemblyAiApiKey
    {
        get => _assemblyAiApiKey;
        set
        {
            if (_assemblyAiApiKey == value) return;
            _assemblyAiApiKey = value;
            _assemblyAiKeyIsSaved = false;
            _assemblyAiTestState = TestState.None;
            _assemblyAiKeyError = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AssemblyAiKeyIsSaved));
            OnPropertyChanged(nameof(AssemblyAiTestState));
            OnPropertyChanged(nameof(AssemblyAiKeyError));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool AssemblyAiKeyIsSaved
    {
        get => _assemblyAiKeyIsSaved;
        private set { _assemblyAiKeyIsSaved = value; OnPropertyChanged(); }
    }

    public string ElevenLabsApiKey
    {
        get => _elevenLabsApiKey;
        set
        {
            if (_elevenLabsApiKey == value) return;
            _elevenLabsApiKey = value;
            _elevenLabsKeyIsSaved = false;
            _elevenLabsTestState = TestState.None;
            _elevenLabsKeyError = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ElevenLabsKeyIsSaved));
            OnPropertyChanged(nameof(ElevenLabsTestState));
            OnPropertyChanged(nameof(ElevenLabsKeyError));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool ElevenLabsKeyIsSaved
    {
        get => _elevenLabsKeyIsSaved;
        private set { _elevenLabsKeyIsSaved = value; OnPropertyChanged(); }
    }

    public string ElevenLabsVoiceId
    {
        get => _elevenLabsVoiceId;
        set
        {
            if (_elevenLabsVoiceId == value) return;
            _elevenLabsVoiceId = value;
            OnPropertyChanged();
        }
    }

    // -- Test state --

    public TestState AnthropicTestState
    {
        get => _anthropicTestState;
        private set { _anthropicTestState = value; OnPropertyChanged(); OnPropertyChanged(nameof(AnthropicTestError)); }
    }

    public string? AnthropicTestError
    {
        get => _anthropicTestError;
        private set { _anthropicTestError = value; OnPropertyChanged(); }
    }

    public TestState ZaiTestState
    {
        get => _zaiTestState;
        private set { _zaiTestState = value; OnPropertyChanged(); OnPropertyChanged(nameof(ZaiTestError)); }
    }

    public string? ZaiTestError
    {
        get => _zaiTestError;
        private set { _zaiTestError = value; OnPropertyChanged(); }
    }

    public TestState AssemblyAiTestState
    {
        get => _assemblyAiTestState;
        private set { _assemblyAiTestState = value; OnPropertyChanged(); OnPropertyChanged(nameof(AssemblyAiTestError)); }
    }

    public string? AssemblyAiTestError
    {
        get => _assemblyAiTestError;
        private set { _assemblyAiTestError = value; OnPropertyChanged(); }
    }

    public TestState ElevenLabsTestState
    {
        get => _elevenLabsTestState;
        private set { _elevenLabsTestState = value; OnPropertyChanged(); OnPropertyChanged(nameof(ElevenLabsTestError)); }
    }

    public string? ElevenLabsTestError
    {
        get => _elevenLabsTestError;
        private set { _elevenLabsTestError = value; OnPropertyChanged(); }
    }

    // -- Field validation errors (for first-run / partial config highlighting) --

    public string? AnthropicKeyError
    {
        get => _anthropicKeyError;
        private set { _anthropicKeyError = value; OnPropertyChanged(); }
    }

    public string? ZaiKeyError
    {
        get => _zaiKeyError;
        private set { _zaiKeyError = value; OnPropertyChanged(); }
    }

    public string? AssemblyAiKeyError
    {
        get => _assemblyAiKeyError;
        private set { _assemblyAiKeyError = value; OnPropertyChanged(); }
    }

    public string? ElevenLabsKeyError
    {
        get => _elevenLabsKeyError;
        private set { _elevenLabsKeyError = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Highlights missing required fields with inline error text.
    /// Called on first-run or when reopened due to partial config.
    /// </summary>
    public void ValidateRequiredFields()
    {
        if (_selectedProvider == "anthropic" && !HasKey(_anthropicApiKey, _anthropicKeyIsSaved))
            AnthropicKeyError = "Required for your selected service";
        else
            AnthropicKeyError = null;

        if (_selectedProvider == "zai" && !HasKey(_zaiApiKey, _zaiKeyIsSaved))
            ZaiKeyError = "Required for your selected service";
        else
            ZaiKeyError = null;

        if (!HasKey(_assemblyAiApiKey, _assemblyAiKeyIsSaved))
            AssemblyAiKeyError = "Required";
        else
            AssemblyAiKeyError = null;

        if (!HasKey(_elevenLabsApiKey, _elevenLabsKeyIsSaved))
            ElevenLabsKeyError = "Required";
        else
            ElevenLabsKeyError = null;
    }

    // -- Save enablement --

    /// <summary>
    /// Save is enabled when the required keys for the selected provider are entered
    /// (or were previously saved), plus both audio keys are required.
    /// </summary>
    public bool CanSave
    {
        get
        {
            bool hasLlmKey = _selectedProvider == "zai"
                ? HasKey(_zaiApiKey, _zaiKeyIsSaved)
                : HasKey(_anthropicApiKey, _anthropicKeyIsSaved);

            bool hasAudioKeys = HasKey(_assemblyAiApiKey, _assemblyAiKeyIsSaved)
                             && HasKey(_elevenLabsApiKey, _elevenLabsKeyIsSaved);

            return hasLlmKey && hasAudioKeys;
        }
    }

    private static bool HasKey(string key, bool isSaved) =>
        isSaved || !string.IsNullOrWhiteSpace(key);

    // -- Commands --

    public void Save()
    {
        // Write keys (only write if the user entered a new value, not the placeholder).
        if (!_anthropicKeyIsSaved && !string.IsNullOrWhiteSpace(_anthropicApiKey))
            _secretsStore.Write(SecretsStore.AnthropicApiKey, _anthropicApiKey);

        if (!_zaiKeyIsSaved && !string.IsNullOrWhiteSpace(_zaiApiKey))
            _secretsStore.Write(SecretsStore.ZaiApiKey, _zaiApiKey);

        if (!_assemblyAiKeyIsSaved && !string.IsNullOrWhiteSpace(_assemblyAiApiKey))
            _secretsStore.Write(SecretsStore.AssemblyAiApiKey, _assemblyAiApiKey);

        if (!_elevenLabsKeyIsSaved && !string.IsNullOrWhiteSpace(_elevenLabsApiKey))
            _secretsStore.Write(SecretsStore.ElevenLabsApiKey, _elevenLabsApiKey);

        // Write non-secret settings.
        _settingsStore.LlmProvider = _selectedProvider;
        _settingsStore.LlmModel = _selectedModel;
        _settingsStore.ElevenLabsVoiceId = _elevenLabsVoiceId;

        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAnthropicKey() { AnthropicApiKey = ""; AnthropicKeyIsSaved = false; }
    public void ClearZaiKey() { ZaiApiKey = ""; ZaiKeyIsSaved = false; }
    public void ClearAssemblyAiKey() { AssemblyAiApiKey = ""; AssemblyAiKeyIsSaved = false; }
    public void ClearElevenLabsKey() { ElevenLabsApiKey = ""; ElevenLabsKeyIsSaved = false; }

    // -- Test probes --

    public async Task TestAnthropicAsync(CancellationToken ct = default)
    {
        var key = _anthropicKeyIsSaved
            ? _secretsStore.Read(SecretsStore.AnthropicApiKey) ?? ""
            : _anthropicApiKey;

        AnthropicTestState = TestState.Testing;
        AnthropicTestError = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", key);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(
                """{"model":"claude-haiku-4-5","max_tokens":1,"messages":[{"role":"user","content":"hi"}]}""",
                Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                AnthropicTestState = TestState.Success;
            }
            else
            {
                AnthropicTestError = FriendlyError("Anthropic", (int)response.StatusCode);
                AnthropicTestState = TestState.Failure;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AnthropicTestError = FriendlyError("Anthropic", ex);
            AnthropicTestState = TestState.Failure;
        }
    }

    public async Task TestZaiAsync(CancellationToken ct = default)
    {
        var key = _zaiKeyIsSaved
            ? _secretsStore.Read(SecretsStore.ZaiApiKey) ?? ""
            : _zaiApiKey;

        ZaiTestState = TestState.Testing;
        ZaiTestError = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.z.ai/api/paas/v4/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {key}");
            request.Content = new StringContent(
                """{"model":"glm-4.5v","max_tokens":1,"messages":[{"role":"user","content":"hi"}]}""",
                Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                ZaiTestState = TestState.Success;
            }
            else
            {
                ZaiTestError = FriendlyError("z.ai", (int)response.StatusCode);
                ZaiTestState = TestState.Failure;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ZaiTestError = FriendlyError("z.ai", ex);
            ZaiTestState = TestState.Failure;
        }
    }

    public async Task TestAssemblyAiAsync(CancellationToken ct = default)
    {
        var key = _assemblyAiKeyIsSaved
            ? _secretsStore.Read(SecretsStore.AssemblyAiApiKey) ?? ""
            : _assemblyAiApiKey;

        AssemblyAiTestState = TestState.Testing;
        AssemblyAiTestError = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://streaming.assemblyai.com/v3/token?expires_in_seconds=60");
            request.Headers.Add("Authorization", key); // Raw key, not Bearer.

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                AssemblyAiTestState = TestState.Success;
            }
            else
            {
                AssemblyAiTestError = FriendlyError("AssemblyAI", (int)response.StatusCode);
                AssemblyAiTestState = TestState.Failure;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AssemblyAiTestError = FriendlyError("AssemblyAI", ex);
            AssemblyAiTestState = TestState.Failure;
        }
    }

    public async Task TestElevenLabsAsync(CancellationToken ct = default)
    {
        var key = _elevenLabsKeyIsSaved
            ? _secretsStore.Read(SecretsStore.ElevenLabsApiKey) ?? ""
            : _elevenLabsApiKey;

        ElevenLabsTestState = TestState.Testing;
        ElevenLabsTestError = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.elevenlabs.io/v1/user/subscription");
            request.Headers.Add("xi-api-key", key);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                ElevenLabsTestState = TestState.Success;
            }
            else
            {
                ElevenLabsTestError = FriendlyError("ElevenLabs", (int)response.StatusCode);
                ElevenLabsTestState = TestState.Failure;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ElevenLabsTestError = FriendlyError("ElevenLabs", ex);
            ElevenLabsTestState = TestState.Failure;
        }
    }

    // -- Helpers --

    private void LoadFromStores()
    {
        _selectedProvider = _settingsStore.LlmProvider;
        _selectedModel = _settingsStore.LlmModel;
        _elevenLabsVoiceId = _settingsStore.ElevenLabsVoiceId;

        // Pre-populate saved-key placeholders.
        if (_secretsStore.Exists(SecretsStore.AnthropicApiKey))
            _anthropicKeyIsSaved = true;

        if (_secretsStore.Exists(SecretsStore.ZaiApiKey))
            _zaiKeyIsSaved = true;

        if (_secretsStore.Exists(SecretsStore.AssemblyAiApiKey))
            _assemblyAiKeyIsSaved = true;

        if (_secretsStore.Exists(SecretsStore.ElevenLabsApiKey))
            _elevenLabsKeyIsSaved = true;
    }

    internal static string FriendlyError(string service, int statusCode) => statusCode switch
    {
        401 => $"Couldn't verify your {service} key \u2014 check that you pasted it correctly.",
        403 => $"Your {service} key doesn't have the required permissions.",
        429 => $"{service} is rate-limiting this key \u2014 wait a moment and try again.",
        >= 500 => $"{service} seems to be having issues right now \u2014 try again in a minute.",
        _ => $"Couldn't reach {service} (HTTP {statusCode}) \u2014 check your key and internet connection.",
    };

    internal static string FriendlyError(string service, Exception ex) => ex switch
    {
        HttpRequestException => $"Couldn't reach {service} \u2014 check your internet connection.",
        TaskCanceledException => $"{service} didn't respond in time \u2014 try again.",
        _ => $"Something went wrong testing {service} \u2014 {ex.Message}",
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum TestState { None, Testing, Success, Failure }

public record ProviderChoice(string Value, string Display);
public record ModelChoice(string Value, string Display);
