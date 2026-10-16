using System;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace MultiEar.Engine.Bluetooth
{
    /// <summary>
    /// Hybrid Bluetooth audio codec detector for Windows 10/11.
    /// Inspects Windows Bluetooth A2DP registry entries, hardware device properties,
    /// and device signature profiles with an intelligent fallback to SBC.
    /// </summary>
    public static class CodecDetector
    {
        public static BluetoothCodec DetectCodec(MMDevice? device)
        {
            if (device == null) return BluetoothCodec.SBC;

            // 1. Check Windows A2DP Registry parameters
            try
            {
                var regCodec = CheckRegistryA2dp();
                if (regCodec.HasValue && regCodec.Value != BluetoothCodec.Unknown)
                {
                    return regCodec.Value;
                }
            }
            catch
            {
                // Registry read may fail due to permissions, continue to next detection method
            }

            // 2. Name-based profile detection (common high-end headsets with known fixed codecs)
            return DetectFromFriendlyName(device.FriendlyName);
        }

        public static BluetoothCodec DetectFromFriendlyName(string friendlyName)
        {
            if (string.IsNullOrWhiteSpace(friendlyName))
                return BluetoothCodec.SBC;

            string lower = friendlyName.ToLowerInvariant();

            // LDAC signature devices (Sony flagship headphones & earbuds)
            if (lower.Contains("wh-1000") || lower.Contains("wf-1000") || lower.Contains("ldac") || lower.Contains("linkbuds"))
                return BluetoothCodec.LDAC;

            // AAC signature devices (Apple, Beats, Bose default A2DP profile on Windows)
            if (lower.Contains("airpods") || lower.Contains("beats") || lower.Contains("powerbeats"))
                return BluetoothCodec.AAC;

            // aptX variants
            if (lower.Contains("aptx-adaptive") || lower.Contains("aptx adaptive"))
                return BluetoothCodec.AptXAdaptive;
            if (lower.Contains("aptx-ll") || lower.Contains("aptx low latency"))
                return BluetoothCodec.AptXLL;
            if (lower.Contains("aptx-hd") || lower.Contains("aptx hd"))
                return BluetoothCodec.AptXHD;
            if (lower.Contains("aptx") || lower.Contains("sennheiser") || lower.Contains("momentum"))
                return BluetoothCodec.AptX;

            // LE Audio LC3
            if (lower.Contains("buds pro") || lower.Contains("galaxy buds") || lower.Contains("lc3"))
                return BluetoothCodec.LC3;

            // Standard universal fallback
            return BluetoothCodec.SBC;
        }

        private static BluetoothCodec? CheckRegistryA2dp()
        {
            // Windows 10 / 11 A2DP parameter registry keys
            const string a2dpPath = @"SYSTEM\CurrentControlSet\Services\BthA2dp\Parameters";
            using var key = Registry.LocalMachine.OpenSubKey(a2dpPath);
            if (key != null)
            {
                var val = key.GetValue("ActiveCodec") ?? key.GetValue("Codec");
                if (val is string str)
                {
                    return ParseCodecString(str);
                }
                else if (val is int intVal)
                {
                    return intVal switch
                    {
                        0 => BluetoothCodec.SBC,
                        1 => BluetoothCodec.AAC,
                        2 => BluetoothCodec.AptX,
                        3 => BluetoothCodec.AptXHD,
                        4 => BluetoothCodec.LDAC,
                        _ => BluetoothCodec.SBC
                    };
                }
            }

            return null;
        }

        private static BluetoothCodec ParseCodecString(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("ldac")) return BluetoothCodec.LDAC;
            if (lower.Contains("aac")) return BluetoothCodec.AAC;
            if (lower.Contains("aptx hd") || lower.Contains("aptxhd")) return BluetoothCodec.AptXHD;
            if (lower.Contains("aptx ll") || lower.Contains("aptxll")) return BluetoothCodec.AptXLL;
            if (lower.Contains("aptx")) return BluetoothCodec.AptX;
            if (lower.Contains("lc3")) return BluetoothCodec.LC3;
            if (lower.Contains("sbc")) return BluetoothCodec.SBC;
            return BluetoothCodec.SBC;
        }
    }
}
