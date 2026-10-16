using System.Collections.Generic;

namespace MultiEar.Engine.Bluetooth
{
    public enum BluetoothCodec
    {
        Unknown,
        SBC,
        AAC,
        AptX,
        AptXHD,
        AptXLL,
        AptXAdaptive,
        LDAC,
        LC3
    }

    public static class CodecLatencyTable
    {
        public static readonly Dictionary<BluetoothCodec, int> DefaultLatencies = new()
        {
            { BluetoothCodec.Unknown, 200 },
            { BluetoothCodec.SBC, 200 },
            { BluetoothCodec.AAC, 160 },
            { BluetoothCodec.AptX, 150 },
            { BluetoothCodec.AptXHD, 155 },
            { BluetoothCodec.AptXLL, 40 },
            { BluetoothCodec.AptXAdaptive, 65 },
            { BluetoothCodec.LDAC, 100 },
            { BluetoothCodec.LC3, 20 }
        };

        public static int GetLatencyMs(BluetoothCodec codec)
        {
            return DefaultLatencies.TryGetValue(codec, out var latency) ? latency : 200;
        }

        public static string GetDisplayName(BluetoothCodec codec)
        {
            return codec switch
            {
                BluetoothCodec.AptX => "aptX",
                BluetoothCodec.AptXHD => "aptX HD",
                BluetoothCodec.AptXLL => "aptX LL",
                BluetoothCodec.AptXAdaptive => "aptX Adaptive",
                _ => codec.ToString()
            };
        }
    }
}
