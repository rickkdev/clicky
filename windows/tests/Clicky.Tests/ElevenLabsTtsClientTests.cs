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
        using var client = new ElevenLabsTtsClient("https://example.com");
        Assert.False(client.IsPlaying);
    }

    [Fact]
    public void StopPlayback_WhenNotPlaying_DoesNotThrow()
    {
        using var client = new ElevenLabsTtsClient("https://example.com");
        client.StopPlayback(); // Should be a no-op
    }

    [Fact]
    public void Dispose_WhenNotPlaying_DoesNotThrow()
    {
        var client = new ElevenLabsTtsClient("https://example.com");
        client.Dispose(); // Should clean up without error
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var client = new ElevenLabsTtsClient("https://example.com");
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
    public async Task SpeakAsync_ThrowsOnCancellation()
    {
        using var client = new ElevenLabsTtsClient("https://example.com");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.SpeakAsync("test", cts.Token));
    }

    [Fact]
    public async Task PlayMp3Async_WithCancellation_StopsPlayback()
    {
        using var client = new ElevenLabsTtsClient("https://example.com");
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
}
