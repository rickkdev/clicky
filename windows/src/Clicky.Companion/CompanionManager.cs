using System.Net.Http;
using System.Text;
using System.Windows.Threading;
using Clicky.Api;
using Clicky.Audio;
using Clicky.Capture;
using Clicky.Hotkey;
using Clicky.Overlay;
using Clicky.Pointing;

namespace Clicky.Companion;

/// <summary>
/// Orchestrates the push-to-talk → capture → transcribe → Claude → TTS pipeline,
/// mirroring <c>CompanionManager.swift</c> in the Mac reference implementation.
/// </summary>
public sealed class CompanionManager : IDisposable
{
    /// <summary>Max conversation history exchanges to keep (prevents unbounded context growth).</summary>
    private const int MaxConversationHistory = 10;

    private readonly CompanionViewModel _viewModel;
    private readonly GlobalPushToTalkHook _hook;
    private ILlmClient _llmClient;
    private readonly object _swapLock = new();
    private readonly AssemblyAiStreamingTranscriber _transcriber;
    private readonly ElevenLabsTtsClient _ttsClient;
    private readonly Dispatcher _dispatcher;
    private readonly OverlayWindowManager? _overlayManager;

    private readonly List<Message> _conversationHistory = new();
    private CancellationTokenSource? _hookConsumerCts;
    private CancellationTokenSource? _responseCts;
    private Task? _currentResponseTask;

    // Active recording session state
    private MicrophoneCapture? _activeMicCapture;
    private TranscriptionSession? _activeTranscriptionSession;
    private CancellationTokenSource? _micCaptureCts;

    /// <summary>The system prompt sent to Claude, mirroring Mac's companionVoiceResponseSystemPrompt.</summary>
    internal static readonly string CompanionSystemPrompt = """
        you're clicky, a friendly always-on companion that lives in the user's system tray. the user just spoke to you via push-to-talk and you can see their screen(s). your reply will be spoken aloud via text-to-speech, so write the way you'd actually talk. this is an ongoing conversation — you remember everything they've said before.

        rules:
        - default to one or two sentences. be direct and dense. BUT if the user asks you to explain more, go deeper, or elaborate, then go all out — give a thorough, detailed explanation with no length limit.
        - all lowercase, casual, warm. no emojis.
        - write for the ear, not the eye. short sentences. no lists, bullet points, markdown, or formatting — just natural speech.
        - don't use abbreviations or symbols that sound weird read aloud. write "for example" not "e.g.", spell out small numbers.
        - if the user's question relates to what's on their screen, reference specific things you see.
        - if the screenshot doesn't seem relevant to their question, just answer the question directly.
        - you can help with anything — coding, writing, general knowledge, brainstorming.
        - never say "simply" or "just".
        - don't read out code verbatim. describe what the code does or what needs to change conversationally.
        - focus on giving a thorough, useful explanation. don't end with simple yes/no questions like "want me to explain more?" or "should i show you?" — those are dead ends that force the user to just say yes.
        - instead, when it fits naturally, end by planting a seed — mention something bigger or more ambitious they could try, a related concept that goes deeper, or a next-level technique that builds on what you just explained. make it something worth coming back for, not a question they'd just nod to. it's okay to not end with anything extra if the answer is complete on its own.
        - if you receive multiple screen images, the one labeled "primary focus" is where the cursor is — prioritize that one but reference others if relevant.

        element pointing:
        you have a small blue triangle cursor that can fly to and point at things on screen. use it whenever pointing would genuinely help the user — if they're asking how to do something, looking for a menu, trying to find a button, or need help navigating an app, point at the relevant element. err on the side of pointing rather than not pointing, because it makes your help way more useful and concrete.

        don't point at things when it would be pointless — like if the user asks a general knowledge question, or the conversation has nothing to do with what's on screen, or you'd just be pointing at something obvious they're already looking at. but if there's a specific UI element, menu, button, or area on screen that's relevant to what you're helping with, point at it.

        when you point, append a coordinate tag at the very end of your response, AFTER your spoken text. the screenshot images are labeled with their pixel dimensions. use those dimensions as the coordinate space. the origin (0,0) is the top-left corner of the image. x increases rightward, y increases downward.

        format: [POINT:x,y:label] where x,y are integer pixel coordinates in the screenshot's coordinate space, and label is a short 1-3 word description of the element (like "search bar" or "save button"). if the element is on the cursor's screen you can omit the screen number. if the element is on a DIFFERENT screen, append :screenN where N is the screen number from the image label (e.g. :screen2). this is important — without the screen number, the cursor will point at the wrong place.

        if pointing wouldn't help, append [POINT:none].

        examples:
        - user asks how to color grade in final cut: "you'll want to open the color inspector — it's right up in the top right area of the toolbar. click that and you'll get all the color wheels and curves. [POINT:1100,42:color inspector]"
        - user asks what html is: "html stands for hypertext markup language, it's basically the skeleton of every web page. curious how it connects to the css you're looking at? [POINT:none]"
        - user asks how to commit in xcode: "see that source control menu up top? click that and hit commit, or you can use command option c as a shortcut. [POINT:285,11:source control]"
        - element is on screen 2 (not where cursor is): "that's over on your other monitor — see the terminal window? [POINT:400,300:terminal:screen2]"
        """;

    /// <summary>
    /// Raised when the pipeline encounters a key-related error (401, empty key)
    /// that requires the user to open Settings and fix their configuration.
    /// </summary>
    public event EventHandler? OpenSettingsRequested;

    public CompanionManager(
        CompanionViewModel viewModel,
        GlobalPushToTalkHook hook,
        ILlmClient llmClient,
        AssemblyAiStreamingTranscriber transcriber,
        ElevenLabsTtsClient ttsClient,
        Dispatcher dispatcher,
        OverlayWindowManager? overlayManager = null)
    {
        _viewModel = viewModel;
        _hook = hook;
        _llmClient = llmClient;
        _transcriber = transcriber;
        _ttsClient = ttsClient;
        _dispatcher = dispatcher;
        _overlayManager = overlayManager;
    }

    /// <summary>
    /// Starts consuming hotkey transitions and reacting to push-to-talk.
    /// </summary>
    public void Start()
    {
        _hookConsumerCts = new CancellationTokenSource();
        _ = ConsumeHotkeyTransitionsAsync(_hookConsumerCts.Token);
    }

    /// <summary>Conversation history, exposed for testing.</summary>
    internal IReadOnlyList<Message> ConversationHistory => _conversationHistory;

    private async Task ConsumeHotkeyTransitionsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var transition in _hook.Transitions.ReadAllAsync(ct).ConfigureAwait(false))
            {
                // Marshal to UI thread for ViewModel updates and state management
                await _dispatcher.InvokeAsync(() => HandleShortcutTransition(transition));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    private void HandleShortcutTransition(ShortcutTransition transition)
    {
        switch (transition)
        {
            case ShortcutTransition.Pressed:
                HandlePressed();
                break;
            case ShortcutTransition.Released:
                HandleReleased();
                break;
        }
    }

    private void HandlePressed()
    {
        // Don't start a new recording if already listening
        if (_viewModel.VoiceState == VoiceState.Listening)
            return;

        // Cancel any in-progress response and TTS from a previous utterance
        // (mirrors currentResponseTask?.cancel() + elevenLabsTTSClient.stopPlayback())
        CancelCurrentResponse();

        _viewModel.IsShortcutPressed = true;
        _viewModel.VoiceState = VoiceState.Listening;

        // Start mic capture + transcription session on a background task
        _micCaptureCts = new CancellationTokenSource();
        _ = StartRecordingAsync(_micCaptureCts.Token);
    }

    private void HandleReleased()
    {
        _viewModel.IsShortcutPressed = false;

        if (_viewModel.VoiceState != VoiceState.Listening)
            return;

        _viewModel.VoiceState = VoiceState.Processing;

        // Stop mic capture and request final transcript
        _ = StopRecordingAndProcessAsync();
    }

    private async Task StartRecordingAsync(CancellationToken ct)
    {
        try
        {
            // Start transcription session
            _activeTranscriptionSession = await _transcriber.StartSessionAsync(ct: ct).ConfigureAwait(false);

            // Start mic capture and pipe frames to the transcription session
            _activeMicCapture = new MicrophoneCapture();
            await foreach (var frame in _activeMicCapture.CaptureFramesAsync(ct).ConfigureAwait(false))
            {
                if (_activeTranscriptionSession is not null)
                {
                    await _activeTranscriptionSession.SendAudioAsync(frame, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when recording is stopped
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recording error: {ex.Message}");
            await _dispatcher.InvokeAsync(() => _viewModel.VoiceState = VoiceState.Idle);
        }
    }

    private async Task StopRecordingAndProcessAsync()
    {
        // Stop mic capture
        _micCaptureCts?.Cancel();
        _activeMicCapture?.Dispose();
        _activeMicCapture = null;

        var session = _activeTranscriptionSession;
        _activeTranscriptionSession = null;

        if (session is null)
        {
            await _dispatcher.InvokeAsync(() => _viewModel.VoiceState = VoiceState.Idle);
            return;
        }

        try
        {
            // Request final transcript (waits up to 1.4s grace period)
            var transcript = await session.RequestFinalTranscriptAsync().ConfigureAwait(false);
            session.Dispose();

            if (string.IsNullOrWhiteSpace(transcript))
            {
                await _dispatcher.InvokeAsync(() => _viewModel.VoiceState = VoiceState.Idle);
                return;
            }

            // Send transcript to Claude with screenshots
            SendTranscriptToClaudeWithScreenshot(transcript);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Transcription error: {ex.Message}");
            session.Dispose();
            await _dispatcher.InvokeAsync(() => _viewModel.VoiceState = VoiceState.Idle);
        }
    }

    /// <summary>
    /// Captures screenshots, sends transcript + images to Claude, streams response,
    /// then plays TTS. Mirrors <c>sendTranscriptToClaudeWithScreenshot</c> on Mac.
    /// </summary>
    internal void SendTranscriptToClaudeWithScreenshot(string transcript)
    {
        CancelCurrentResponse();

        _responseCts = new CancellationTokenSource();
        var ct = _responseCts.Token;

        _currentResponseTask = Task.Run(async () =>
        {
            await _dispatcher.InvokeAsync(() => _viewModel.VoiceState = VoiceState.Processing);

            try
            {
                // Capture all connected screens (exclude overlay HWNDs so they
                // don't appear in the screenshots sent to Claude)
                var excludeHwnds = _overlayManager?.OverlayHwnds;
                var screens = await ScreenCapture.CaptureAllScreensAsJpegAsync(excludeHwnds).ConfigureAwait(false);

                if (ct.IsCancellationRequested) return;

                // Append dimension info to labels (so Claude knows the coordinate space)
                var labeledScreens = screens.Select(s => new CapturedScreen
                {
                    ImageBytes = s.ImageBytes,
                    Label = $"{s.Label} (image dimensions: {s.ScreenshotPixelWidth}x{s.ScreenshotPixelHeight} pixels)",
                    IsCursorScreen = s.IsCursorScreen,
                    DisplayBounds = s.DisplayBounds,
                    ScreenshotPixelWidth = s.ScreenshotPixelWidth,
                    ScreenshotPixelHeight = s.ScreenshotPixelHeight,
                }).ToList();

                // Build history snapshot for the API call
                var historySnapshot = _conversationHistory.ToList();

                // Stream Claude's response and accumulate the full text
                var responseBuilder = new StringBuilder();
                await foreach (var delta in _llmClient.SendAsync(
                    historySnapshot,
                    labeledScreens,
                    CompanionSystemPrompt,
                    transcript,
                    ct).ConfigureAwait(false))
                {
                    responseBuilder.Append(delta);
                }

                if (ct.IsCancellationRequested) return;

                var fullResponse = responseBuilder.ToString();

                // Parse [POINT:...] tags — extracts spoken text (tags stripped)
                // and an optional PointDirective with screenshot-space coordinates.
                var parseResult = PointTagParser.Parse(fullResponse);
                var spokenText = parseResult.SpokenText;

                // Save this exchange to conversation history (with point tags stripped
                // so they don't confuse future context — mirrors Mac behavior)
                _conversationHistory.Add(new Message(transcript, spokenText));
                if (_conversationHistory.Count > MaxConversationHistory)
                {
                    _conversationHistory.RemoveRange(0, _conversationHistory.Count - MaxConversationHistory);
                }

                // Dispatch point directive to overlay cursor if present.
                // Mac sets voiceState=Idle BEFORE setting pointing coordinates so the
                // triangle cursor becomes visible before the fly animation.
                if (parseResult.Directive is { } directive && _overlayManager is not null)
                {
                    var converted = PointTagParser.ConvertToScreenCoordinates(directive, screens);
                    if (converted.HasValue)
                    {
                        var (screenPoint, displayBounds) = converted.Value;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            _viewModel.VoiceState = VoiceState.Idle;
                            _overlayManager.FlyTo(
                                screenPoint,
                                displayBounds,
                                directive.Label);
                        });
                    }
                }

                // Play TTS if there's text to speak
                if (!string.IsNullOrWhiteSpace(spokenText))
                {
                    await _dispatcher.InvokeAsync(() => _viewModel.VoiceState = VoiceState.Responding);
                    await _ttsClient.SpeakAsync(spokenText, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // User spoke again — response was interrupted. Expected.
            }
            catch (HttpRequestException httpEx) when (IsKeyError(httpEx))
            {
                System.Diagnostics.Debug.WriteLine($"Key error in pipeline: {httpEx.Message}");
                await _dispatcher.InvokeAsync(() =>
                {
                    _viewModel.LastError = "Your API key is missing or invalid. Open Settings to fix.";
                    OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Response pipeline error: {ex.Message}");
            }

            if (!ct.IsCancellationRequested)
            {
                await _dispatcher.InvokeAsync(() => _viewModel.VoiceState = VoiceState.Idle);
            }
        }, ct);
    }

    /// <summary>
    /// Cancels any in-flight Claude request and TTS playback.
    /// </summary>
    private void CancelCurrentResponse()
    {
        _responseCts?.Cancel();
        _responseCts?.Dispose();
        _responseCts = null;
        _ttsClient.StopPlayback();
    }

    /// <summary>
    /// Detects whether an HttpRequestException indicates a key-related error
    /// (401 Unauthorized) that the user should fix in Settings.
    /// </summary>
    private static bool IsKeyError(HttpRequestException ex)
    {
        var msg = ex.Message;
        return msg.Contains("(401)") || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips [POINT:...] tags from Claude's response so TTS speaks clean text.
    /// Delegates to <see cref="PointTagParser.Parse"/> for consistency.
    /// </summary>
    internal static string StripPointTags(string text)
    {
        return PointTagParser.Parse(text).SpokenText;
    }

    /// <summary>
    /// Swaps the active LLM client at runtime without restarting the CompanionManager.
    /// If a response is in-flight, it is cancelled first. The next push-to-talk turn
    /// will use the new client. Safe to call from any VoiceState.
    /// </summary>
    public async Task SwapLlmClientAsync(ILlmClient newClient)
    {
        // Cancel any in-flight response/TTS before swapping.
        CancelCurrentResponse();

        // Wait for the current response task to finish if it's running.
        var task = _currentResponseTask;
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { }
        }

        lock (_swapLock)
        {
            _llmClient = newClient;
        }

        await _dispatcher.InvokeAsync(() => _viewModel.VoiceState = VoiceState.Idle);
    }

    public void Dispose()
    {
        _hookConsumerCts?.Cancel();
        CancelCurrentResponse();

        _micCaptureCts?.Cancel();
        _activeMicCapture?.Dispose();
        _activeTranscriptionSession?.Dispose();

        _ttsClient.Dispose();
        _hookConsumerCts?.Dispose();
        _micCaptureCts?.Dispose();
    }
}
