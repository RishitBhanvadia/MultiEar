using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using MultiEar.Engine.Audio;
using MultiEar.Engine.Bluetooth;
using MultiEar.Engine.Services;

namespace MultiEar_App.ViewModels
{
    public class DeviceItemViewModel : INotifyPropertyChanged
    {
        private readonly DevicePipeline _pipeline;
        private readonly AudioEngineService _engine;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DevicePipeline Pipeline => _pipeline;
        public string DeviceId => _pipeline.DeviceId;
        public string FriendlyName => _pipeline.FriendlyName;

        public IReadOnlyList<BluetoothCodec> AvailableCodecs { get; } = new List<BluetoothCodec>
        {
            BluetoothCodec.SBC,
            BluetoothCodec.AAC,
            BluetoothCodec.AptX,
            BluetoothCodec.AptXHD,
            BluetoothCodec.AptXLL,
            BluetoothCodec.AptXAdaptive,
            BluetoothCodec.LDAC,
            BluetoothCodec.LC3
        };

        public BluetoothCodec SelectedCodec
        {
            get => _pipeline.Codec;
            set
            {
                if (_pipeline.Codec != value)
                {
                    _pipeline.Codec = value;
                    _engine.RecalculateDelays();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CodecDisplayName));
                    OnPropertyChanged(nameof(CodecLatencyText));
                    OnPropertyChanged(nameof(TotalDelayText));
                }
            }
        }

        public string CodecDisplayName => CodecLatencyTable.GetDisplayName(SelectedCodec);
        public string CodecLatencyText => $"{_pipeline.CodecLatencyMs} ms";
        public string TotalDelayText => $"{_pipeline.TotalLatencyMs} ms sync";

        public double VolumePercent
        {
            get => Math.Round(_pipeline.VolumeLevel * 100.0);
            set
            {
                float newVol = (float)(Math.Clamp(value, 0, 300) / 100.0);
                if (Math.Abs(_pipeline.VolumeLevel - newVol) > 0.001f)
                {
                    _pipeline.VolumeLevel = newVol;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VolumeText));
                }
            }
        }

        public string VolumeText => $"{(int)VolumePercent}%";

        public int ManualTrimMs
        {
            get => _pipeline.ManualOffsetMs;
            set
            {
                if (_pipeline.ManualOffsetMs != value)
                {
                    _pipeline.ManualOffsetMs = Math.Clamp(value, -100, 100);
                    _engine.RecalculateDelays();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ManualTrimText));
                    OnPropertyChanged(nameof(TotalDelayText));
                }
            }
        }

        public string ManualTrimText => ManualTrimMs >= 0 ? $"+{ManualTrimMs} ms" : $"{ManualTrimMs} ms";

        public string StatusText => _pipeline.Status switch
        {
            PipelineStatus.Streaming => "Streaming",
            PipelineStatus.Reconnecting => "Reconnecting...",
            PipelineStatus.Disconnected => "Disconnected",
            _ => "Unknown"
        };

        public SolidColorBrush StatusBrush => _pipeline.Status switch
        {
            PipelineStatus.Streaming => new SolidColorBrush(Colors.MediumSeaGreen),
            PipelineStatus.Reconnecting => new SolidColorBrush(Colors.Orange),
            PipelineStatus.Disconnected => new SolidColorBrush(Colors.Crimson),
            _ => new SolidColorBrush(Colors.Gray)
        };

        private bool _isExpanded;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ExpandButtonText));
                    OnPropertyChanged(nameof(ExpandButtonGlyph));
                }
            }
        }

        public string ExpandButtonText => IsExpanded ? "Hide Tune" : "Tune";
        public string ExpandButtonGlyph => IsExpanded ? "\uE70E" : "\uE70D";

        public void ToggleExpand()
        {
            IsExpanded = !IsExpanded;
        }

        public void ResetTrim()
        {
            ManualTrimMs = 0;
        }

        public DeviceItemViewModel(DevicePipeline pipeline, AudioEngineService engine)
        {
            _pipeline = pipeline;
            _engine = engine;
        }

        public void NotifyStatusChanged()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
