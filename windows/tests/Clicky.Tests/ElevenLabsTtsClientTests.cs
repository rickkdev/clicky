using System.Net;
using System.Text;
using System.Text.Json;
using Clicky.Api;
using Xunit;

namespace Clicky.Tests;

public class ElevenLabsTtsClientTests
{
    [Fact]
    public void IsPlaying_WhenNoPlayback_ReturnsFalse()
    {
        using var client = new ElevenLabsTtsClient("test-key", "test-voice-id");
        Assert.False(client.IsPlaying);
    }

    [Fact]
    public void StopPlayback_WhenNotPlaying_DoesNotThrow()
    {
        using var client = new ElevenLabsTtsClient("test-key", "test-voice-id");
        client.StopPlayback(); // Should be a no-op
    }

    [Fact]
    public void Dispose_WhenNotPlaying_DoesNotThrow()
    {
        var client = new ElevenLabsTtsClient("test-key", "test-voice-id");
        client.Dispose(); // Should clean up without error
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var client = new ElevenLabsTtsClient("test-key", "test-voice-id");
        client.Dispose();
        client.Dispose(); // Second dispose is a no-op
    }

    [Fact]
    public void TtsRequest_SerializesCorrectJsonShape()
    {
        var request = new TtsRequest
        {
            Text = "hello from clicky",
            ModelId = "eleven_flash_v2_5",
            VoiceSettings = new VoiceSettings { Stability = 0.5, SimilarityBoost = 0.75 }
        };

        var json = JsonSerializer.Serialize(request);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("hello from clicky", root.GetProperty("text").GetString());
        Assert.Equal("eleven_flash_v2_5", root.GetProperty("model_id").GetString());
        Assert.Equal(0.5, root.GetProperty("voice_settings").GetProperty("stability").GetDouble());
        Assert.Equal(0.75, root.GetProperty("voice_settings").GetProperty("similarity_boost").GetDouble());
    }

    [Fact]
    public async Task SpeakAsync_SendsDirectPostWithCorrectUrlAndHeaders()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 0xFF, 0xFB, 0x90, 0x00 })
        });
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var client = new ElevenLabsTtsClient("my-eleven-key", "kPzsL2i3teMYv0FxEYQ6", httpClient);

        try
        {
            await client.SpeakAsync("hello", CancellationToken.None);
        }
        catch
        {
            // MP3 playback will fail with fake data — that's fine, we're testing the HTTP request
        }

        Assert.NotNull(handler.CapturedRequest);
        Assert.Equal(HttpMethod.Post, handler.CapturedRequest!.Method);
        Assert.Equal(
            "https://api.elevenlabs.io/v1/text-to-speech/kPzsL2i3teMYv0FxEYQ6/stream",
            handler.CapturedRequest.RequestUri!.ToString());
        Assert.True(handler.CapturedRequest.Headers.TryGetValues("xi-api-key", out var apiKeyValues));
        Assert.Equal("my-eleven-key", apiKeyValues!.First());
        Assert.Contains("audio/mpeg", handler.CapturedRequest.Headers.Accept.ToString());
    }

    [Fact]
    public async Task SpeakAsync_DifferentVoiceId_UsesCorrectUrl()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 0xFF, 0xFB, 0x90, 0x00 })
        });
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var client = new ElevenLabsTtsClient("key", "my-custom-voice", httpClient);

        try { await client.SpeakAsync("hi", CancellationToken.None); } catch { }

        Assert.NotNull(handler.CapturedRequest);
        Assert.Contains("/my-custom-voice/stream", handler.CapturedRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task SpeakAsync_ThrowsOnCancellation()
    {
        using var client = new ElevenLabsTtsClient("test-key", "test-voice-id");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.SpeakAsync("test", cts.Token));
    }

    [Fact]
    public async Task PlayMp3Async_WithCancellation_StopsPlayback()
    {
        using var client = new ElevenLabsTtsClient("test-key", "test-voice-id");
        var cts = new CancellationTokenSource();

        // Generate a minimal valid MP3 frame (silence)
        // A real MP3 would be needed for actual playback, but we can test that
        // cancellation interrupts the wait correctly.
        // Use a tiny valid MP3 — NAudio will throw if it can't parse the header,
        // so we test that the cancellation path works with invalid data too.
        var invalidMp3 = new byte[] { 0xFF, 0xFB, 0x90, 0x00 }; // MP3 sync word + header

        // This will either fail to parse the MP3 (expected) or get cancelled
        // Either way, it should not hang
        cts.CancelAfter(100);
        try
        {
            await client.PlayMp3Async(invalidMp3, cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or InvalidDataException or EndOfStreamException)
        {
            // Expected — invalid MP3 data or cancellation
        }

        Assert.False(client.IsPlaying);
    }

    private sealed class CapturingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(response);
        }
    }
}
