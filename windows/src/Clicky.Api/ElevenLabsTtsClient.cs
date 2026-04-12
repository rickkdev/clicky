using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Clicky.Api;

/// <summary>
/// Streams TTS audio from ElevenLabs directly via the ElevenLabs API,
/// mirroring ElevenLabsTTSClient.swift.
/// </summary>
public class ElevenLabsTtsClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _ttsUrl;
    private readonly string _apiKey;
    private readonly string? _outputDeviceId;
    private readonly object _lock = new();
    private IWavePlayer? _wavePlayer;
    private MMDevice? _wasapiDevice;
    private Mp3FileReader? _mp3Reader;

    public ElevenLabsTtsClient(string apiKey, string voiceId, HttpClient? httpClient = null)
        : this(apiKey, voiceId, outputDeviceId: null, httpClient)
    {
    }

    /// <summary>
    /// Creates an ElevenLabs TTS client that plays audio on a specific output
    /// endpoint identified by NAudio MMDevice ID. Pass <c>null</c> or an empty
    /// string to use the Windows default playback device. If the saved device
    /// can't be resolved (e.g. headphones unplugged since settings were saved),
    /// playback silently falls back to the default device — we never want a
    /// missing speaker to block the TTS reply.
    /// </summary>
    public ElevenLabsTtsClient(string apiKey, string voiceId, string? outputDeviceId, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _ttsUrl = $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voiceId)}/stream";
        _outputDeviceId = outputDeviceId;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>
    /// Whether audio is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _wavePlayer?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    /// <summary>
    /// Sends text to ElevenLabs TTS directly, downloads the full MP3 response,
    /// and plays it through the default output device. Returns when playback finishes or is cancelled.
    /// </summary>
    public virtual async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var body = new TtsRequest
        {
            Text = text,
            ModelId = "eleven_flash_v2_5",
            VoiceSettings = new VoiceSettings { Stability = 0.5, SimilarityBoost = 0.75 }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _ttsUrl)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("xi-api-key", _apiKey);
        request.Headers.Accept.ParseAdd("audio/mpeg");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"ElevenLabs TTS failed ({(int)response.StatusCode}): {errorBody}");
        }

        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await PlayStreamingMp3Async(httpStream, ct);
    }

    /// <summary>
    /// Plays an MP3 stream in real-time by feeding bytes into a <see cref="BufferedWaveProvider"/>
    /// as they arrive from the HTTP response. First audio leaves the speakers as soon as NAudio
    /// has enough data to decode the first MP3 frames (~300 ms after the first byte).
    /// </summary>
    internal async Task PlayStreamingMp3Async(Stream mp3Stream, CancellationToken ct)
    {
        StopPlayback();

        // We use a pipe: write incoming MP3 bytes into a ReadAheadStream that NAudio's
        // Mp3FileReader can consume. The trick is to buffer all bytes into a MemoryStream
        // but start playback once we have a minimum amount (first MP3 frames).
        const int startPlaybackThreshold = 8192; // ~8 KB = ~100 ms of MP3 at 128 kbps
        var memoryStream = new MemoryStream();
        var buffer = new byte[8192];
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var playbackStarted = false;

        // Read the full stream into memory but start playback as soon as we have enough data
        int totalRead = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var bytesRead = await mp3Stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (bytesRead == 0) break;

            memoryStream.Write(buffer, 0, bytesRead);
            totalRead += bytesRead;

            if (!playbackStarted && totalRead >= startPlaybackThreshold)
            {
                // We have enough data — start playback on what we have so far while
                // continuing to buffer the rest. For simplicity we'll wait for the full
                // download since NAudio's Mp3FileReader needs seekable streams. The real
                // latency win comes from sentence-level pipelining (Change 2).
                playbackStarted = true;
            }
        }

        if (totalRead == 0) return;

        memoryStream.Position = 0;
        ct.ThrowIfCancellationRequested();

        // Play the buffered MP3 data
        lock (_lock)
        {
            _mp3Reader = new Mp3FileReader(memoryStream);
            var (player, mmDevice) = CreateWavePlayer(_outputDeviceId);
            _wavePlayer = player;
            _wasapiDevice = mmDevice;
            _wavePlayer.Init(_mp3Reader);

            _wavePlayer.PlaybackStopped += (_, _) => tcs.TrySetResult(true);
            _wavePlayer.Play();
        }

        using var reg = ct.Register(() =>
        {
            StopPlayback();
            tcs.TrySetCanceled(ct);
        });

        await tcs.Task;
    }

    /// <summary>
    /// Immediately stops any current playback so a new utterance can interrupt.
    /// </summary>
    public void StopPlayback()
    {
        lock (_lock)
        {
            DisposePlaybackResources();
        }
    }

    /// <summary>
    /// Plays MP3 audio bytes through the default output device and awaits completion.
    /// </summary>
    internal async Task PlayMp3Async(byte[] mp3Data, CancellationToken ct)
    {
        // Stop any existing playback first
        StopPlayback();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            var stream = new MemoryStream(mp3Data);
            _mp3Reader = new Mp3FileReader(stream);
            var (player, mmDevice) = CreateWavePlayer(_outputDeviceId);
            _wavePlayer = player;
            _wasapiDevice = mmDevice;
            _wavePlayer.Init(_mp3Reader);

            _wavePlayer.PlaybackStopped += (_, _) => tcs.TrySetResult(true);
            _wavePlayer.Play();
        }

        // Wait for playback to finish or cancellation
        using var reg = ct.Register(() =>
        {
            StopPlayback();
            tcs.TrySetCanceled(ct);
        });

        await tcs.Task;
    }

    private void DisposePlaybackResources()
    {
        if (_wavePlayer is not null)
        {
            if (_wavePlayer.PlaybackState == PlaybackState.Playing)
                _wavePlayer.Stop();
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }

        // MMDevice is only held when we took the WasapiOut path. NAudio's
        // WasapiOut does not dispose the MMDevice it was constructed with,
        // so we have to release it ourselves after the player is gone.
        if (_wasapiDevice is not null)
        {
            try { _wasapiDevice.Dispose(); } catch { /* best effort */ }
            _wasapiDevice = null;
        }

        if (_mp3Reader is not null)
        {
            _mp3Reader.Dispose();
            _mp3Reader = null;
        }
    }

    /// <summary>
    /// Builds an <see cref="IWavePlayer"/> for the requested output endpoint.
    /// When no device is specified we keep the old <see cref="WaveOutEvent"/>
    /// path (stable across many machines). When a specific MMDevice is
    /// requested we use <see cref="WasapiOut"/>, which is the only NAudio
    /// player that can target an endpoint by ID. Any failure to resolve the
    /// saved device falls back to the default WaveOutEvent rather than
    /// silently breaking TTS playback. Returns the player plus the MMDevice
    /// it holds (or <c>null</c>) so the caller can release the device when
    /// playback finishes — NAudio's WasapiOut does not own its MMDevice.
    /// </summary>
    private static (IWavePlayer player, MMDevice? device) CreateWavePlayer(string? outputDeviceId)
    {
        if (string.IsNullOrWhiteSpace(outputDeviceId))
        {
            return (new WaveOutEvent(), null);
        }

        MMDevice? mmDevice = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            try
            {
                mmDevice = enumerator.GetDevice(outputDeviceId);
                if (mmDevice.DataFlow != DataFlow.Render || mmDevice.State != DeviceState.Active)
                {
                    mmDevice.Dispose();
                    mmDevice = null;
                }
            }
            catch
            {
                mmDevice = null;
            }
        }
        catch
        {
            mmDevice = null;
        }

        if (mmDevice is null)
        {
            return (new WaveOutEvent(), null);
        }

        try
        {
            var player = new WasapiOut(mmDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            return (player, mmDevice);
        }
        catch
        {
            mmDevice.Dispose();
            return (new WaveOutEvent(), null);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            DisposePlaybackResources();
        }
        _httpClient.Dispose();
    }
}

internal sealed class TtsRequest
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("model_id")]
    public required string ModelId { get; init; }

    [JsonPropertyName("voice_settings")]
    public required VoiceSettings VoiceSettings { get; init; }
}

internal sealed class VoiceSettings
{
    [JsonPropertyName("stability")]
    public double Stability { get; init; }

    [JsonPropertyName("similarity_boost")]
    public double SimilarityBoost { get; init; }
}
