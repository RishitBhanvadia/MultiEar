using System;
using MultiEar.Engine.Bluetooth;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MultiEar.Engine.Audio
{
    public enum PipelineStatus
    {
        Streaming,
        Reconnecting,
        Disconnected
    }

    public class DevicePipeline : IDisposable
    {
        public string DeviceId { get; }
        public string FriendlyName { get; }
        public BluetoothCodec Codec { get; set; }
        public int CodecLatencyMs => CodecLatencyTable.GetLatencyMs(Codec);

        public int ManualOffsetMs { get; set; }
        public float VolumeLevel { get; set; } = 1.0f;
        public PipelineStatus Status { get; set; } = PipelineStatus.Streaming;

        public int TotalLatencyMs => Math.Max(0, CodecLatencyMs + ManualOffsetMs);

        public AudioRingBuffer RingBuffer { get; private set; }
        public ResamplingWaveProvider? Resampler { get; private set; }

        private WasapiOut? _wasapiOut;
        private WaveFormat _captureFormat;
        private byte[] _gainScratchBuffer = new byte[192000];
        private readonly object _writeLock = new();
        private bool _disposed;

        public event EventHandler<StoppedEventArgs>? Disconnected;

        public DevicePipeline(MMDevice device, WaveFormat captureFormat)
        {
            DeviceId = device.ID;
            FriendlyName = device.FriendlyName;
            _captureFormat = captureFormat;

            Codec = CodecDetector.DetectCodec(device);
            RingBuffer = new AudioRingBuffer(captureFormat, 1500);

            InitAudioOutput(device);
        }

        private void InitAudioOutput(MMDevice device)
        {
            try
            {
                // Query the device's native shared-mode mix format
                WaveFormat targetMixFormat;
                try
                {
                    targetMixFormat = device.AudioClient.MixFormat;
                }
                catch
                {
                    targetMixFormat = _captureFormat;
                }

                // Wrap ring buffer with the adaptive resampler to guarantee compatibility
                Resampler?.Dispose();
                Resampler = new ResamplingWaveProvider(RingBuffer, targetMixFormat);

                _wasapiOut?.Dispose();
                // Polling mode (useEventSync: false) is substantially more robust for Bluetooth endpoints
                _wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, false, 40);
                _wasapiOut.PlaybackStopped += OnPlaybackStopped;
                _wasapiOut.Init(Resampler);
            }
            catch (Exception ex)
            {
                Status = PipelineStatus.Disconnected;
                throw new InvalidOperationException($"Failed to initialize audio pipeline for '{FriendlyName}': {ex.Message}", ex);
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null && !_disposed)
            {
                Status = PipelineStatus.Disconnected;
                Disconnected?.Invoke(this, e);
            }
        }

        public void Play()
        {
            if (Status == PipelineStatus.Streaming && _wasapiOut != null)
            {
                try
                {
                    _wasapiOut.Play();
                }
                catch
                {
                    Status = PipelineStatus.Disconnected;
                }
            }
        }

        public void Stop()
        {
            try
            {
                _wasapiOut?.Stop();
            }
            catch { }
        }

        public void ProcessAndWrite(byte[] pcmData, int count, WaveFormat format)
        {
            if (Status != PipelineStatus.Streaming || count <= 0) return;

            lock (_writeLock)
            {
                if (Math.Abs(VolumeLevel - 1.0f) < 0.001f)
                {
                    RingBuffer.Write(pcmData, 0, count);
                }
                else
                {
                    if (_gainScratchBuffer.Length < count)
                    {
                        _gainScratchBuffer = new byte[count * 2];
                    }

                    Array.Copy(pcmData, 0, _gainScratchBuffer, 0, count);
                    GainStage.ApplyGain(_gainScratchBuffer, 0, count, VolumeLevel, format);
                    RingBuffer.Write(_gainScratchBuffer, 0, count);
                }
            }
        }

        public void Reconnect(MMDevice reconnectedDevice)
        {
            Stop();
            CleanupOutput();

            // Clear buffer to avoid any stale audio burst, pre-filling silence cushion
            RingBuffer.Clear();

            InitAudioOutput(reconnectedDevice);
            Status = PipelineStatus.Streaming;

            System.Threading.Thread.Sleep(50);
            Play();
        }

        public void VolumeUp(float step = 0.1f)
        {
            VolumeLevel = Math.Clamp(VolumeLevel + step, GainStage.MinVolume, GainStage.MaxVolume);
        }

        public void VolumeDown(float step = 0.1f)
        {
            VolumeLevel = Math.Clamp(VolumeLevel - step, GainStage.MinVolume, GainStage.MaxVolume);
        }

        private void CleanupOutput()
        {
            if (_wasapiOut != null)
            {
                _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
                try { _wasapiOut.Dispose(); } catch { }
                _wasapiOut = null;
            }

            Resampler?.Dispose();
            Resampler = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            CleanupOutput();
        }
    }
}
