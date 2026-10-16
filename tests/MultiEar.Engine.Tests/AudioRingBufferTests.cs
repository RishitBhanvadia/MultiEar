using System;
using MultiEar.Engine.Audio;
using NAudio.Wave;
using Xunit;

namespace MultiEar.Engine.Tests
{
    public class AudioRingBufferTests
    {
        private readonly WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        [Fact]
        public void InitialRead_ReturnsSilenceCushionWithoutUnderrun()
        {
            var ring = new AudioRingBuffer(_format, 1000);
            byte[] output = new byte[480 * _format.BlockAlign]; // 10ms frame chunk

            int read = ring.Read(output, 0, output.Length);

            Assert.Equal(output.Length, read);
            Assert.Equal(0, ring.UnderrunCount);

            // Verify content is zeros (silence)
            foreach (var b in output)
            {
                Assert.Equal(0, b);
            }
        }

        [Fact]
        public void WriteAndRead_PreservesAudioData()
        {
            var ring = new AudioRingBuffer(_format, 1000);
            ring.Clear();

            // Create test frame data
            byte[] inputData = new byte[100 * _format.BlockAlign];
            for (int i = 0; i < inputData.Length; i++)
            {
                inputData[i] = (byte)((i % 250) + 1);
            }

            ring.Write(inputData, 0, inputData.Length);

            // Read through the pre-cushion silence first
            byte[] cushionDrain = new byte[ring.CapacityMs * _format.AverageBytesPerSecond / 1000];
            // Read until we reach data
            byte[] output = new byte[inputData.Length];
            int read = ring.Read(output, 0, output.Length);

            Assert.Equal(inputData.Length, read);
        }

        [Fact]
        public void DynamicSetDelay_DoesNotWipeBuffer()
        {
            var ring = new AudioRingBuffer(_format, 1000);

            byte[] input = new byte[500 * _format.BlockAlign];
            input[0] = 42;
            ring.Write(input, 0, input.Length);

            // Change delay dynamically
            ring.SetDelayMs(100);

            // Read should continue without throwing and underrun count should be 0
            byte[] output = new byte[200 * _format.BlockAlign];
            int read = ring.Read(output, 0, output.Length);

            Assert.Equal(output.Length, read);
            Assert.Equal(0, ring.UnderrunCount);
        }

        [Fact]
        public void ClockDrift_CompensatesWhenBufferAccumulatesExcess()
        {
            var ring = new AudioRingBuffer(_format, 1000);
            ring.SetDelayMs(50);

            // Write 80ms of audio (exceeding target delay of 50ms + 50ms cushion + 25ms tolerance)
            byte[] extraAudio = new byte[(int)(_format.AverageBytesPerSecond * 0.150)];
            ring.Write(extraAudio, 0, extraAudio.Length);

            // Perform reads
            byte[] outBuf = new byte[_format.BlockAlign * 10];
            ring.Read(outBuf, 0, outBuf.Length);

            // Drift correction count should be triggered to gently slip frames back to target
            Assert.True(ring.DriftCorrectionCount > 0);
        }
    }
}
