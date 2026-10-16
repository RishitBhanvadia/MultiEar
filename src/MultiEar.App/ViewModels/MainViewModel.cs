using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using MultiEar.Engine.Services;
using NAudio.CoreAudioApi;

namespace MultiEar_App.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly AudioEngineService _engine;
        private readonly DispatcherQueue _dispatcher;

        private string _defaultDeviceName = "Default Output";
        private string? _feedbackWarning;
        private bool _isMuted;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AudioEngineService Engine => _engine;

        public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();
        public ObservableCollection<MMDevice> AvailableDevices { get; } = new();

        public bool IsGlobalMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    _engine.SetGlobalMute(value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MuteButtonText));
                    OnPropertyChanged(nameof(MuteButtonIcon));
                }
            }
        }

        public string MuteButtonText => IsGlobalMuted ? "Unmute" : "Mute All";
        public string MuteButtonIcon => IsGlobalMuted ? "\uE74F" : "\uE767";

        public string DefaultDeviceName
        {
            get => _defaultDeviceName;
            set
            {
                if (_defaultDeviceName != value)
                {
                    _defaultDeviceName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? FeedbackWarning
        {
            get => _feedbackWarning;
            set
            {
                if (_feedbackWarning != value)
                {
                    _feedbackWarning = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasFeedbackWarning));
                }
            }
        }

        public bool HasFeedbackWarning => !string.IsNullOrEmpty(_feedbackWarning);
        public bool HasDevices => Devices.Count > 0;
        public bool HasNoDevices => Devices.Count == 0;

        public string ActiveDevicesCountText => Devices.Count switch
        {
            0 => "No Earphones",
            1 => "1 Earphone Active",
            _ => $"{Devices.Count} Earphones Active"
        };

        public bool IsStreamingActive => _engine.IsCapturing;

        public MainViewModel(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            _engine = new AudioEngineService();

            _engine.PipelinesChanged += OnEnginePipelinesChanged;
            _engine.FeedbackWarning += OnEngineFeedbackWarning;

            UpdateDefaultDevice();
            RefreshAvailableDevices();

            // Restore previously saved session
            _engine.RestoreSession();
            _engine.StartStreaming();
            SyncPipelines();
        }

        private void OnEngineFeedbackWarning(object? sender, string warning)
        {
            _dispatcher.TryEnqueue(() =>
            {
                FeedbackWarning = warning;
            });
        }

        private void OnEnginePipelinesChanged(object? sender, EventArgs e)
        {
            _dispatcher.TryEnqueue(() =>
            {
                SyncPipelines();
            });
        }

        private void SyncPipelines()
        {
            var activePipes = _engine.ActivePipelines;

            // Remove pipelines that are no longer active
            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (!activePipes.Any(p => p.DeviceId == Devices[i].DeviceId))
                {
                    Devices.RemoveAt(i);
                }
            }

            // Add new pipelines
            foreach (var pipe in activePipes)
            {
                var existing = Devices.FirstOrDefault(d => d.DeviceId == pipe.DeviceId);
                if (existing == null)
                {
                    Devices.Add(new DeviceItemViewModel(pipe, _engine));
                }
                else
                {
                    existing.NotifyStatusChanged();
                }
            }

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(HasNoDevices));
            OnPropertyChanged(nameof(ActiveDevicesCountText));
            RefreshAvailableDevices();
        }

        public void UpdateDefaultDevice()
        {
            _engine.RefreshDefaultDevice();
            DefaultDeviceName = _engine.DefaultRenderDevice?.FriendlyName ?? "Default Output";
        }

        public void RefreshAvailableDevices()
        {
            AvailableDevices.Clear();
            var all = _engine.GetAvailableRenderDevices();
            var activeIds = Devices.Select(d => d.DeviceId).ToHashSet();

            foreach (var d in all)
            {
                // Only show devices not currently in active session
                if (!activeIds.Contains(d.ID))
                {
                    AvailableDevices.Add(d);
                }
            }
        }

        public void AddDevice(MMDevice device)
        {
            FeedbackWarning = null;
            if (_engine.AddDevice(device))
            {
                SyncPipelines();
            }
        }

        public void RemoveDevice(DeviceItemViewModel item)
        {
            _engine.RemoveDevice(item.DeviceId);
            SyncPipelines();
        }

        public void ToggleMute()
        {
            IsGlobalMuted = !IsGlobalMuted;
        }

        public void DismissWarning()
        {
            FeedbackWarning = null;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Dispose()
        {
            _engine.SaveSession();
            _engine.Dispose();
        }
    }
}
