using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace MultiEar.Engine.Audio
{
    /// <summary>
    /// High-performance audio gain stage supporting attenuation and clean volume boosting (up to 3.0x / +9.5 dB).
    /// Prevents digital clipping/wrap-around distortion via hard limiting / clamping.
    /// </summary>
    public static class GainStage
    {
        public const float MinVolume = 0.0f;
        public const float MaxVolume = 3.0f;

        public static void ApplyGain(float[] buffer, int offset, int count, float volume)
        {
            if (Math.Abs(volume - 1.0f) < 0.001f) return;

            int end = offset + count;
            if (volume <= 0.0f)
            {
                Array.Clear(buffer, offset, count);
                return;
            }

            for (int i = offset; i < end; i++)
            {
                float val = buffer[i] * volume;
                if (val > 1.0f) val = 1.0f;
                else if (val < -1.0f) val = -1.0f;
                buffer[i] = val;
            }
        }

        public static void ApplyGain(byte[] buffer, int offset, int count, float volume, WaveFormat format)
        {
            if (Math.Abs(volume - 1.0f) < 0.001f) return;

            if (volume <= 0.0f)
            {
                Array.Clear(buffer, offset, count);
                return;
            }

            var byteSpan = buffer.AsSpan(offset, count);

            if (format.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                var floatSpan = MemoryMarshal.Cast<byte, float>(byteSpan);
                for (int i = 0; i < floatSpan.Length; i++)
                {
                    float sample = floatSpan[i] * volume;
                    if (sample > 1.0f) sample = 1.0f;
                    else if (sample < -1.0f) sample = -1.0f;
                    floatSpan[i] = sample;
                }
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
            {
                var shortSpan = MemoryMarshal.Cast<byte, short>(byteSpan);
                for (int i = 0; i < shortSpan.Length; i++)
                {
                    int sample = (int)(shortSpan[i] * volume);
                    if (sample > short.MaxValue) sample = short.MaxValue;
                    else if (sample < short.MinValue) sample = short.MinValue;
                    shortSpan[i] = (short)sample;
                }
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32)
            {
                var intSpan = MemoryMarshal.Cast<byte, int>(byteSpan);
                for (int i = 0; i < intSpan.Length; i++)
                {
                    long sample = (long)(intSpan[i] * volume);
                    if (sample > int.MaxValue) sample = int.MaxValue;
                    else if (sample < int.MinValue) sample = int.MinValue;
                    intSpan[i] = (int)sample;
                }
            }
        }
    }
}
