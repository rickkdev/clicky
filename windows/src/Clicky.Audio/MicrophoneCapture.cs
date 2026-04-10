using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Clicky.Audio;

/// <summary>
/// Captures the default microphone via WASAPI, downmixes + resamples the
/// stream to 16 kHz mono PCM16 little-endian, and yields 50 ms frames
/// through an <see cref="IAsyncEnumerable{T}"/> of <see cref="ReadOnlyMemory{T}"/>.
///
/// This mirrors BuddyDictationManager.swift's AVAudioEngine tap on Mac —
/// the output shape is what AssemblyAI's realtime websocket (US-008) and
/// Claude's audio pipeline expect.
/// </summary>
public sealed class MicrophoneCapture : IDisposable
{
    /// <summary>Target sample rate required by AssemblyAI streaming v3.</summary>
    public const int TargetSampleRate = 16_000;

    /// <summary>Frame length used for the push-to-talk websocket.</summary>
    public const int FrameDurationMilliseconds = 50;

    /// <summary>Number of 16-bit samples in a single 50 ms frame.</summary>
    public const int SamplesPerFrame = TargetSampleRate * FrameDurationMilliseconds / 1000;

    /// <summary>Size in bytes of a single 50 ms PCM16 mono frame.</summary>
    public const int BytesPerFrame = SamplesPerFrame * 2;

    private readonly object _lock = new();
    private WasapiCapture? _activeCapture;
    private bool _disposed;

    /// <summary>
    /// Starts the default WASAPI capture device and yields 50 ms PCM16 frames
    /// until <paramref name="cancellationToken"/> fires or the device stops.
    /// Only one session may be active at a time per instance.
    /// </summary>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        WasapiCapture capture;
        lock (_lock)
        {
            if (_activeCapture is not null)
            {
                throw new InvalidOperationException("A capture session is already running.");
            }
            capture = new WasapiCapture();
            _activeCapture = capture;
        }

        var sourceFormat = capture.WaveFormat;
        if (sourceFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            lock (_lock)
            {
                capture.Dispose();
                _activeCapture = null;
            }
            throw new NotSupportedException(
                $"WasapiCapture returned unsupported encoding {sourceFormat.Encoding}; expected IeeeFloat.");
        }

        var sourceChannels = sourceFormat.Channels;
        var sourceSampleRate = sourceFormat.SampleRate;

        var channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // Bytes of finished PCM16 LE waiting to be flushed as 50 ms frames.
        var frameBuffer = new List<byte>(BytesPerFrame * 4);

        capture.DataAvailable += (_, args) =>
        {
            try
            {
                if (args.BytesRecorded <= 0) return;

                int floatCount = args.BytesRecorded / sizeof(float);
                var floats = new float[floatCount];
                Buffer.BlockCopy(args.Buffer, 0, floats, 0, args.BytesRecorded);

                var mono = PcmResampler.DownmixToMono(floats, sourceChannels);
                var resampled = PcmResampler.ResampleLinearMono(mono, sourceSampleRate, TargetSampleRate);
                var pcm = PcmResampler.FloatToPcm16LE(resampled);

                frameBuffer.AddRange(pcm);
                while (frameBuffer.Count >= BytesPerFrame)
                {
                    var frame = new byte[BytesPerFrame];
                    frameBuffer.CopyTo(0, frame, 0, BytesPerFrame);
                    frameBuffer.RemoveRange(0, BytesPerFrame);
                    channel.Writer.TryWrite(frame);
                }
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        };

        capture.RecordingStopped += (_, args) =>
        {
            channel.Writer.TryComplete(args.Exception);
        };

        capture.StartRecording();

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            lock (_lock)
            {
                try { capture.StopRecording(); } catch { /* already stopped */ }
                capture.Dispose();
                if (ReferenceEquals(_activeCapture, capture))
                {
                    _activeCapture = null;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            if (_activeCapture is not null)
            {
                try { _activeCapture.StopRecording(); } catch { /* ignore */ }
                _activeCapture.Dispose();
                _activeCapture = null;
            }
        }
    }
}
