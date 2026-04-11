using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NAudio.Wave;

namespace Clicky.Api;

/// <summary>
/// Streams TTS audio from ElevenLabs via the Cloudflare Worker proxy,
/// mirroring ElevenLabsTTSClient.swift.
/// </summary>
public sealed class ElevenLabsTtsClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _ttsUrl;
    private readonly object _lock = new();
    private WaveOutEvent? _waveOut;
    private Mp3FileReader? _mp3Reader;

    public ElevenLabsTtsClient(string proxyUrl)
    {
        _ttsUrl = $"{proxyUrl.TrimEnd('/')}/tts";
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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
                return _waveOut?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    /// <summary>
    /// Sends text to ElevenLabs TTS via the worker proxy, downloads the full MP3 response,
    /// and plays it through the default output device. Returns when playback finishes or is cancelled.
    /// </summary>
    public async Task SpeakAsync(string text, CancellationToken ct = default)
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
        request.Headers.Accept.ParseAdd("audio/mpeg");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"ElevenLabs TTS failed ({(int)response.StatusCode}): {errorBody}");
        }

        var audioData = await response.Content.ReadAsByteArrayAsync(ct);
        ct.ThrowIfCancellationRequested();

        await PlayMp3Async(audioData, ct);
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
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_mp3Reader);

            _waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult(true);
            _waveOut.Play();
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
        if (_waveOut is not null)
        {
            if (_waveOut.PlaybackState == PlaybackState.Playing)
                _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }

        if (_mp3Reader is not null)
        {
            _mp3Reader.Dispose();
            _mp3Reader = null;
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
