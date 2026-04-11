using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clicky.Companion;
using Xunit;

namespace Clicky.Tests;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SecretsStore _secrets;
    private readonly SettingsStore _settings;

    public SettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ClickySettingsVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _secrets = new SecretsStore(Path.Combine(_tempDir, "secrets.bin"));
        _settings = new SettingsStore(Path.Combine(_tempDir, "settings.json"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { }
    }

    // -- CanSave tests --

    [Fact]
    public void CanSave_FalseByDefault()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void CanSave_TrueWhenAnthropicProviderAndRequiredKeysEntered()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "anthropic";
        vm.AnthropicApiKey = "sk-test";
        vm.AssemblyAiApiKey = "aai-test";
        vm.ElevenLabsApiKey = "el-test";

        Assert.True(vm.CanSave);
    }

    [Fact]
    public void CanSave_FalseWhenAnthropicProviderButMissingAnthropicKey()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "anthropic";
        // No anthropic key set.
        vm.AssemblyAiApiKey = "aai-test";
        vm.ElevenLabsApiKey = "el-test";

        Assert.False(vm.CanSave);
    }

    [Fact]
    public void CanSave_TrueWhenZaiProviderAndRequiredKeysEntered()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "zai";
        vm.ZaiApiKey = "zai-test";
        vm.AssemblyAiApiKey = "aai-test";
        vm.ElevenLabsApiKey = "el-test";

        Assert.True(vm.CanSave);
    }

    [Fact]
    public void CanSave_FalseWhenZaiProviderButMissingZaiKey()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "zai";
        // No zai key.
        vm.AssemblyAiApiKey = "aai-test";
        vm.ElevenLabsApiKey = "el-test";

        Assert.False(vm.CanSave);
    }

    [Fact]
    public void CanSave_FalseWhenMissingAssemblyAiKey()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "anthropic";
        vm.AnthropicApiKey = "sk-test";
        // No assemblyai key.
        vm.ElevenLabsApiKey = "el-test";

        Assert.False(vm.CanSave);
    }

    [Fact]
    public void CanSave_FalseWhenMissingElevenLabsKey()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "anthropic";
        vm.AnthropicApiKey = "sk-test";
        vm.AssemblyAiApiKey = "aai-test";
        // No elevenlabs key.

        Assert.False(vm.CanSave);
    }

    [Fact]
    public void CanSave_TrueWhenKeysAreSavedInStore()
    {
        // Pre-populate secrets store.
        _secrets.Write(SecretsStore.AnthropicApiKey, "sk-saved");
        _secrets.Write(SecretsStore.AssemblyAiApiKey, "aai-saved");
        _secrets.Write(SecretsStore.ElevenLabsApiKey, "el-saved");

        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "anthropic";

        // Keys are saved (placeholder shown), so CanSave should be true.
        Assert.True(vm.CanSave);
    }

    // -- Model list tests --

    [Fact]
    public void AvailableModels_ChangesWhenProviderChanges()
    {
        var vm = new SettingsViewModel(_secrets, _settings);

        vm.SelectedProvider = "anthropic";
        Assert.Equal(SettingsViewModel.AnthropicModels, vm.AvailableModels);

        vm.SelectedProvider = "zai";
        Assert.Equal(SettingsViewModel.ZaiModels, vm.AvailableModels);
    }

    [Fact]
    public void SelectedModel_AutoSelectsFirstWhenProviderChanges()
    {
        var vm = new SettingsViewModel(_secrets, _settings);

        vm.SelectedProvider = "zai";
        Assert.Equal("glm-4.6v", vm.SelectedModel);

        vm.SelectedProvider = "anthropic";
        Assert.Equal("claude-sonnet-4-6", vm.SelectedModel);
    }

    // -- Save persistence tests --

    [Fact]
    public void Save_WritesKeysToSecretsStore()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "anthropic";
        vm.AnthropicApiKey = "sk-new";
        vm.AssemblyAiApiKey = "aai-new";
        vm.ElevenLabsApiKey = "el-new";

        vm.Save();

        Assert.Equal("sk-new", _secrets.Read(SecretsStore.AnthropicApiKey));
        Assert.Equal("aai-new", _secrets.Read(SecretsStore.AssemblyAiApiKey));
        Assert.Equal("el-new", _secrets.Read(SecretsStore.ElevenLabsApiKey));
    }

    [Fact]
    public void Save_WritesSettingsToSettingsStore()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.SelectedProvider = "zai";
        vm.SelectedModel = "glm-4.5v";
        vm.ElevenLabsVoiceId = "custom-voice";
        vm.ZaiApiKey = "zai-key";
        vm.AssemblyAiApiKey = "aai-key";
        vm.ElevenLabsApiKey = "el-key";

        vm.Save();

        Assert.Equal("zai", _settings.LlmProvider);
        Assert.Equal("glm-4.5v", _settings.LlmModel);
        Assert.Equal("custom-voice", _settings.ElevenLabsVoiceId);
    }

    [Fact]
    public void Save_DoesNotOverwriteSavedKeyWithEmpty()
    {
        _secrets.Write(SecretsStore.AnthropicApiKey, "sk-original");

        var vm = new SettingsViewModel(_secrets, _settings);
        // The key is shown as saved placeholder — don't overwrite.
        vm.AssemblyAiApiKey = "aai-new";
        vm.ElevenLabsApiKey = "el-new";
        vm.Save();

        Assert.Equal("sk-original", _secrets.Read(SecretsStore.AnthropicApiKey));
    }

    [Fact]
    public void Save_RaisesSettingsSavedEvent()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        vm.AnthropicApiKey = "sk-test";
        vm.AssemblyAiApiKey = "aai-test";
        vm.ElevenLabsApiKey = "el-test";

        var raised = false;
        vm.SettingsSaved += (_, _) => raised = true;
        vm.Save();

        Assert.True(raised);
    }

    // -- Clear key tests --

    [Fact]
    public void ClearKey_ResetsSavedState()
    {
        _secrets.Write(SecretsStore.AnthropicApiKey, "sk-saved");
        var vm = new SettingsViewModel(_secrets, _settings);

        Assert.True(vm.AnthropicKeyIsSaved);

        vm.ClearAnthropicKey();

        Assert.False(vm.AnthropicKeyIsSaved);
        Assert.Equal("", vm.AnthropicApiKey);
    }

    // -- Pre-populated state tests --

    [Fact]
    public void LoadsFromStores_ProviderAndModel()
    {
        _settings.LlmProvider = "zai";
        _settings.LlmModel = "glm-4.5v";

        var vm = new SettingsViewModel(_secrets, _settings);

        Assert.Equal("zai", vm.SelectedProvider);
        Assert.Equal("glm-4.5v", vm.SelectedModel);
    }

    [Fact]
    public void LoadsFromStores_SavedKeyFlags()
    {
        _secrets.Write(SecretsStore.AnthropicApiKey, "sk-saved");
        _secrets.Write(SecretsStore.ElevenLabsApiKey, "el-saved");

        var vm = new SettingsViewModel(_secrets, _settings);

        Assert.True(vm.AnthropicKeyIsSaved);
        Assert.False(vm.ZaiKeyIsSaved);
        Assert.False(vm.AssemblyAiKeyIsSaved);
        Assert.True(vm.ElevenLabsKeyIsSaved);
    }

    // -- Friendly error tests --

    [Fact]
    public void FriendlyError_401_MentionsKey()
    {
        var msg = SettingsViewModel.FriendlyError("Anthropic", 401);
        Assert.Contains("key", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Anthropic", msg);
    }

    [Fact]
    public void FriendlyError_500_MentionsServiceIssues()
    {
        var msg = SettingsViewModel.FriendlyError("z.ai", 500);
        Assert.Contains("z.ai", msg);
        Assert.Contains("issues", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FriendlyError_HttpRequestException_MentionsInternet()
    {
        var msg = SettingsViewModel.FriendlyError("AssemblyAI", new HttpRequestException("connection refused"));
        Assert.Contains("internet", msg, StringComparison.OrdinalIgnoreCase);
    }

    // -- Test probe with fake HTTP --

    [Fact]
    public async Task TestAnthropicAsync_SuccessOnOk()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var vm = new SettingsViewModel(_secrets, _settings, httpClient);
        vm.AnthropicApiKey = "sk-test";

        await vm.TestAnthropicAsync();

        Assert.Equal(TestState.Success, vm.AnthropicTestState);
        Assert.Null(vm.AnthropicTestError);
    }

    [Fact]
    public async Task TestAnthropicAsync_FailureOn401()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler);
        var vm = new SettingsViewModel(_secrets, _settings, httpClient);
        vm.AnthropicApiKey = "sk-bad";

        await vm.TestAnthropicAsync();

        Assert.Equal(TestState.Failure, vm.AnthropicTestState);
        Assert.NotNull(vm.AnthropicTestError);
    }

    [Fact]
    public async Task TestZaiAsync_SuccessOnOk()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var vm = new SettingsViewModel(_secrets, _settings, httpClient);
        vm.ZaiApiKey = "zai-test";

        await vm.TestZaiAsync();

        Assert.Equal(TestState.Success, vm.ZaiTestState);
    }

    [Fact]
    public async Task TestAssemblyAiAsync_SuccessOnOk()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var vm = new SettingsViewModel(_secrets, _settings, httpClient);
        vm.AssemblyAiApiKey = "aai-test";

        await vm.TestAssemblyAiAsync();

        Assert.Equal(TestState.Success, vm.AssemblyAiTestState);
    }

    [Fact]
    public async Task TestElevenLabsAsync_SuccessOnOk()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var vm = new SettingsViewModel(_secrets, _settings, httpClient);
        vm.ElevenLabsApiKey = "el-test";

        await vm.TestElevenLabsAsync();

        Assert.Equal(TestState.Success, vm.ElevenLabsTestState);
    }

    [Fact]
    public async Task TestAnthropicAsync_UsesProbeUrlAndHeaders()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var vm = new SettingsViewModel(_secrets, _settings, httpClient);
        vm.AnthropicApiKey = "sk-capture";

        await vm.TestAnthropicAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/v1/messages", handler.LastRequest.RequestUri!.ToString());
        Assert.True(handler.LastRequest.Headers.Contains("x-api-key"));
        Assert.True(handler.LastRequest.Headers.Contains("anthropic-version"));
    }

    [Fact]
    public async Task TestAssemblyAiAsync_UsesRawAuthorizationHeader()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var vm = new SettingsViewModel(_secrets, _settings, httpClient);
        vm.AssemblyAiApiKey = "raw-key-123";

        await vm.TestAssemblyAiAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains("/v3/token", handler.LastRequest.RequestUri!.ToString());
        // AssemblyAI uses raw key, NOT "Bearer <key>".
        var authHeader = handler.LastRequest.Headers.GetValues("Authorization");
        Assert.Contains("raw-key-123", authHeader);
    }

    [Fact]
    public async Task TestElevenLabsAsync_UsesXiApiKeyHeader()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var vm = new SettingsViewModel(_secrets, _settings, httpClient);
        vm.ElevenLabsApiKey = "el-capture";

        await vm.TestElevenLabsAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains("/v1/user/subscription", handler.LastRequest.RequestUri!.ToString());
        Assert.True(handler.LastRequest.Headers.Contains("xi-api-key"));
    }

    // -- PropertyChanged notification tests --

    [Fact]
    public void CanSave_RaisesPropertyChangedWhenKeyEntered()
    {
        var vm = new SettingsViewModel(_secrets, _settings);
        var canSaveChanged = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.CanSave))
                canSaveChanged = true;
        };

        vm.AnthropicApiKey = "sk-test";

        Assert.True(canSaveChanged);
    }

    // -- Test helpers --

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public FakeHttpHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public HttpRequestMessage? LastRequest { get; private set; }

        public CapturingHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
