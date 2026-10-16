using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MultiEar.Engine.Audio;
using MultiEar.Engine.Bluetooth;
using MultiEar.Engine.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MultiEar.Engine.Services
{
    public class AudioEngineService : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator;
        private readonly List<DevicePipeline> _pipelines = new();
        private readonly object _pipelineLock = new();

        private WasapiLoopbackCapture? _capture;
        private WaveFormat? _captureFormat;
        private MMDevice? _defaultRenderDevice;

        private bool _isCapturing;
        private bool _isGlobalMuted;
        private readonly Dictionary<string, float> _preMuteVolumes = new();

        private readonly CancellationTokenSource _cts = new();
        private Task? _watchdogTask;

        public event EventHandler? PipelinesChanged;
        public event EventHandler<string>? FeedbackWarning;
        public event EventHandler<string>? ErrorOccurred;

        public IReadOnlyList<DevicePipeline> ActivePipelines
        {
            get
            {
                lock (_pipelineLock)
                {
                    return _pipelines.ToList();
                }
            }
        }

        public MMDevice? DefaultRenderDevice => _defaultRenderDevice;
        public bool IsGlobalMuted => _isGlobalMuted;
        public bool IsCapturing => _isCapturing;

        public AudioEngineService()
        {
            _enumerator = new MMDeviceEnumerator();
            RefreshDefaultDevice();
            StartWatchdog();
        }

        public void RefreshDefaultDevice()
        {
            try
            {
                _defaultRenderDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"No default audio device found: {ex.Message}");
            }
        }

        public IReadOnlyList<MMDevice> GetAvailableRenderDevices()
        {
            var list = new List<MMDevice>();
            try
            {
                var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var ep in endpoints)
                {
                    list.Add(ep);
                }
            }
            catch { }
            return list;
        }

        public bool AddDevice(MMDevice device, float volume = 1.0f, int manualOffset = 0, BluetoothCodec? codecOverride = null)
        {
            RefreshDefaultDevice();

            // Feedback loop guard: Do not route back to the loopback capture device
            if (_defaultRenderDevice != null && device.ID == _defaultRenderDevice.ID)
            {
                FeedbackWarning?.Invoke(this, $"Device '{device.FriendlyName}' is the current Windows default playback device. Routing audio to it would cause a feedback loop.");
                return false;
            }

            lock (_pipelineLock)
            {
                if (_pipelines.Any(p => p.DeviceId == device.ID))
                    return false; // Already added

                EnsureCaptureInitialized();

                if (_captureFormat == null)
                {
                    ErrorOccurred?.Invoke(this, "Could not initialize loopback capture format.");
                    return false;
                }

                try
                {
                    var pipeline = new DevicePipeline(device, _captureFormat)
                    {
                        VolumeLevel = _isGlobalMuted ? 0.0f : volume,
                        ManualOffsetMs = manualOffset
                    };

                    if (codecOverride.HasValue && codecOverride.Value != BluetoothCodec.Unknown)
                    {
                        pipeline.Codec = codecOverride.Value;
                    }

                    _pipelines.Add(pipeline);
                    RecalculateDelays();

                    if (_isCapturing)
                    {
                        pipeline.Play();
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Failed to add device '{device.FriendlyName}': {ex.Message}");
                    return false;
                }
            }

            PipelinesChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void RemoveDevice(string deviceId)
        {
            DevicePipeline? toRemove = null;
            lock (_pipelineLock)
            {
                toRemove = _pipelines.FirstOrDefault(p => p.DeviceId == deviceId);
                if (toRemove != null)
                {
                    _pipelines.Remove(toRemove);
                    RecalculateDelays();
                }
            }

            if (toRemove != null)
            {
                toRemove.Dispose();
                PipelinesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RecalculateDelays()
        {
            lock (_pipelineLock)
            {
                if (_pipelines.Count == 0) return;

                int maxLatency = _pipelines.Max(p => p.TotalLatencyMs);
                foreach (var p in _pipelines)
                {
                    int delayMs = Math.Max(0, maxLatency - p.TotalLatencyMs);
                    p.RingBuffer.SetDelayMs(delayMs);
                }
            }
        }

        public void SetGlobalMute(bool mute)
        {
            _isGlobalMuted = mute;
            lock (_pipelineLock)
            {
                foreach (var p in _pipelines)
                {
                    if (mute)
                    {
                        _preMuteVolumes[p.DeviceId] = p.VolumeLevel;
                        p.VolumeLevel = 0.0f;
                    }
                    else
                    {
                        p.VolumeLevel = _preMuteVolumes.TryGetValue(p.DeviceId, out float vol) ? vol : 1.0f;
                    }
                }
            }
        }

        private void EnsureCaptureInitialized()
        {
            if (_capture != null && _captureFormat != null) return;

            RefreshDefaultDevice();
            if (_defaultRenderDevice == null) return;

            try
            {
                _capture?.Dispose();
                _capture = new WasapiLoopbackCapture(_defaultRenderDevice);
                _captureFormat = _capture.WaveFormat;
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to open loopback capture: {ex.Message}");
            }
        }

        public void StartStreaming()
        {
            EnsureCaptureInitialized();
            if (_capture == null) return;

            try
            {
                _capture.StartRecording();
                _isCapturing = true;

                lock (_pipelineLock)
                {
                    foreach (var p in _pipelines)
                    {
                        p.Play();
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to start streaming: {ex.Message}");
            }
        }

        public void StopStreaming()
        {
            _isCapturing = false;
            try
            {
                _capture?.StopRecording();
            }
            catch { }

            lock (_pipelineLock)
            {
                foreach (var p in _pipelines)
                {
                    p.Stop();
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isCapturing || e.BytesRecorded <= 0 || _captureFormat == null) return;

            DevicePipeline[] activeCopy;
            lock (_pipelineLock)
            {
                activeCopy = _pipelines.ToArray();
            }

            foreach (var pipeline in activeCopy)
            {
                pipeline.ProcessAndWrite(e.Buffer, e.BytesRecorded, _captureFormat);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _isCapturing = false;
            if (e.Exception != null && !_cts.IsCancellationRequested)
            {
                // Loopback invalidated (e.g. default device changed). Re-initialize.
                Task.Delay(1000, _cts.Token).ContinueWith(_ =>
                {
                    if (!_cts.IsCancellationRequested)
                    {
                        RestartCaptureOnDefaultDevice();
                    }
                });
            }
        }

        private void RestartCaptureOnDefaultDevice()
        {
            RefreshDefaultDevice();
            if (_defaultRenderDevice == null) return;

            try
            {
                _capture?.Dispose();
                _capture = new WasapiLoopbackCapture(_defaultRenderDevice);
                _captureFormat = _capture.WaveFormat;
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                _isCapturing = true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to restart capture: {ex.Message}");
            }
        }

        private void StartWatchdog()
        {
            _watchdogTask = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(2000, _cts.Token);
                    }
                    catch (OperationCanceledException) { break; }

                    try
                    {
                        var currentDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                        lock (_pipelineLock)
                        {
                            foreach (var pipeline in _pipelines)
                            {
                                if (pipeline.Status == PipelineStatus.Disconnected)
                                {
                                    var reconnected = currentDevices.FirstOrDefault(d => d.ID == pipeline.DeviceId || d.FriendlyName == pipeline.FriendlyName);
                                    if (reconnected != null)
                                    {
                                        Task.Delay(600).Wait();
                                        pipeline.Reconnect(reconnected);
                                        PipelinesChanged?.Invoke(this, EventArgs.Empty);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }, _cts.Token);
        }

        public void SaveSession()
        {
            var config = new SessionConfig
            {
                GlobalMuted = _isGlobalMuted
            };

            lock (_pipelineLock)
            {
                foreach (var p in _pipelines)
                {
                    config.ActiveDevices.Add(new DeviceSessionConfig
                    {
                        DeviceId = p.DeviceId,
                        FriendlyName = p.FriendlyName,
                        VolumeLevel = p.VolumeLevel,
                        ManualOffsetMs = p.ManualOffsetMs,
                        CodecOverride = p.Codec
                    });
                }
            }

            config.Save();
        }

        public void RestoreSession()
        {
            var config = SessionConfig.Load();
            var available = GetAvailableRenderDevices();

            foreach (var savedDev in config.ActiveDevices)
            {
                var match = available.FirstOrDefault(d => d.ID == savedDev.DeviceId || d.FriendlyName == savedDev.FriendlyName);
                if (match != null)
                {
                    AddDevice(match, savedDev.VolumeLevel, savedDev.ManualOffsetMs, savedDev.CodecOverride);
                }
            }

            if (config.GlobalMuted)
            {
                SetGlobalMute(true);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            SaveSession();

            StopStreaming();

            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                try { _capture.Dispose(); } catch { }
                _capture = null;
            }

            lock (_pipelineLock)
            {
                foreach (var p in _pipelines)
                {
                    p.Dispose();
                }
                _pipelines.Clear();
            }

            _enumerator.Dispose();
            _cts.Dispose();
        }
    }
}
