using System;
using MultiEar.Engine.Audio;
using NAudio.Wave;
using Xunit;

namespace MultiEar.Engine.Tests
{
    public class GainStageTests
    {
        [Fact]
        public void ApplyGain_FloatArray_ZeroVolume_ProducesSilence()
        {
            float[] samples = { 0.5f, -0.8f, 0.2f, -0.1f };
            GainStage.ApplyGain(samples, 0, samples.Length, 0.0f);

            foreach (var s in samples)
            {
                Assert.Equal(0.0f, s);
            }
        }

        [Fact]
        public void ApplyGain_FloatArray_HalfVolume_HalvesSamples()
        {
            float[] samples = { 0.5f, -0.8f, 0.2f, -0.4f };
            GainStage.ApplyGain(samples, 0, samples.Length, 0.5f);

            Assert.Equal(0.25f, samples[0], 4);
            Assert.Equal(-0.4f, samples[1], 4);
            Assert.Equal(0.1f, samples[2], 4);
            Assert.Equal(-0.2f, samples[3], 4);
        }

        [Fact]
        public void ApplyGain_FloatArray_Boost_ClampsToMaxLimit()
        {
            float[] samples = { 0.8f, -0.9f, 0.1f };
            GainStage.ApplyGain(samples, 0, samples.Length, 2.0f);

            Assert.Equal(1.0f, samples[0]);
            Assert.Equal(-1.0f, samples[1]);
            Assert.Equal(0.2f, samples[2], 4);
        }

        [Fact]
        public void ApplyGain_Pcm16_ClampsToShortMinMax()
        {
            var format = new WaveFormat(48000, 16, 2);
            byte[] buffer = new byte[4];
            short sample = 20000;
            BitConverter.GetBytes(sample).CopyTo(buffer, 0);

            // Boost by 2.0x -> should clamp to short.MaxValue (32767)
            GainStage.ApplyGain(buffer, 0, buffer.Length, 2.0f, format);
            short result = BitConverter.ToInt16(buffer, 0);

            Assert.Equal(short.MaxValue, result);
        }
    }
}
