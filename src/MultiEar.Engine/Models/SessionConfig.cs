using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiEar.Engine.Bluetooth;

namespace MultiEar.Engine.Models
{
    public class DeviceSessionConfig
    {
        public string DeviceId { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string BluetoothAddress { get; set; } = string.Empty;
        public float VolumeLevel { get; set; } = 1.0f;
        public int ManualOffsetMs { get; set; } = 0;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BluetoothCodec? CodecOverride { get; set; }
    }

    public class SessionConfig
    {
        public List<DeviceSessionConfig> ActiveDevices { get; set; } = new();
        public bool GlobalMuted { get; set; } = false;
        public bool LaunchAtStartup { get; set; } = false;

        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiEar"
        );

        private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "session.json");

        public static SessionConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<SessionConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (config != null) return config;
                }
            }
            catch
            {
                // Fall back to default config if corrupted
            }

            return new SessionConfig();
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch
            {
                // Non-fatal if config save fails
            }
        }
    }
}
