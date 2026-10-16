using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MultiEar.Engine.Audio
{
    /// <summary>
    /// Adaptive audio resampler and format converter that wraps an input IWaveProvider (AudioRingBuffer)
    /// and adapts it in real-time to the target audio device's native MixFormat.
    /// Handles 48kHz <-> 44.1kHz, 32-bit Float <-> 16-bit PCM, and arbitrary format transitions.
    /// </summary>
    public class ResamplingWaveProvider : IWaveProvider, IDisposable
    {
        private readonly IWaveProvider _pipeline;
        private readonly IDisposable? _disposableResource;

        public WaveFormat WaveFormat { get; }

        public ResamplingWaveProvider(IWaveProvider source, WaveFormat targetFormat)
        {
            WaveFormat = targetFormat;

            // 1. If format already matches exactly, pass-through
            if (IsFormatMatch(source.WaveFormat, targetFormat))
            {
                _pipeline = source;
                _disposableResource = null;
                return;
            }

            try
            {
                // 2. High-performance managed resampling pipeline using WDL Resampler & SampleProviders
                ISampleProvider sampleProvider = source.WaveFormat.Encoding switch
                {
                    WaveFormatEncoding.IeeeFloat => source.ToSampleProvider(),
                    WaveFormatEncoding.Pcm when source.WaveFormat.BitsPerSample == 16 => new Pcm16BitToSampleProvider(source),
                    WaveFormatEncoding.Pcm when source.WaveFormat.BitsPerSample == 24 => new Pcm24BitToSampleProvider(source),
                    WaveFormatEncoding.Pcm when source.WaveFormat.BitsPerSample == 32 => new Pcm32BitToSampleProvider(source),
                    _ => source.ToSampleProvider()
                };

                // Channel count adjustment if required (e.g. Mono to Stereo)
                if (sampleProvider.WaveFormat.Channels != targetFormat.Channels)
                {
                    if (sampleProvider.WaveFormat.Channels == 1 && targetFormat.Channels == 2)
                    {
                        sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
                    }
                }

                // Sample rate conversion if required (e.g. 48kHz -> 44.1kHz or vice versa)
                if (sampleProvider.WaveFormat.SampleRate != targetFormat.SampleRate)
                {
                    sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetFormat.SampleRate);
                }

                // Convert back to target bit depth and encoding
                if (targetFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    _pipeline = new SampleToWaveProvider(sampleProvider);
                    _disposableResource = null;
                }
                else if (targetFormat.Encoding == WaveFormatEncoding.Pcm && targetFormat.BitsPerSample == 16)
                {
                    _pipeline = new SampleToWaveProvider16(sampleProvider);
                    _disposableResource = null;
                }
                else
                {
                    // For 24-bit or 32-bit PCM, use MediaFoundationResampler for maximum compatibility
                    var mfResampler = new MediaFoundationResampler(source, targetFormat)
                    {
                        ResamplerQuality = 60
                    };
                    _pipeline = mfResampler;
                    _disposableResource = mfResampler;
                }
            }
            catch
            {
                // Fallback to MediaFoundationResampler if managed conversion pipeline encounters unsupported types
                var mfResampler = new MediaFoundationResampler(source, targetFormat)
                {
                    ResamplerQuality = 60
                };
                _pipeline = mfResampler;
                _disposableResource = mfResampler;
            }
        }

        private static bool IsFormatMatch(WaveFormat a, WaveFormat b)
        {
            return a.SampleRate == b.SampleRate &&
                   a.Channels == b.Channels &&
                   a.BitsPerSample == b.BitsPerSample &&
                   a.Encoding == b.Encoding;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _pipeline.Read(buffer, offset, count);
        }

        public void Dispose()
        {
            _disposableResource?.Dispose();
        }
    }
}
