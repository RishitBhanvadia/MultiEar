using MultiEar.Engine.Bluetooth;
using Xunit;

namespace MultiEar.Engine.Tests
{
    public class CodecTests
    {
        [Theory]
        [InlineData(BluetoothCodec.SBC, 200)]
        [InlineData(BluetoothCodec.AAC, 160)]
        [InlineData(BluetoothCodec.AptX, 150)]
        [InlineData(BluetoothCodec.AptXHD, 155)]
        [InlineData(BluetoothCodec.AptXLL, 40)]
        [InlineData(BluetoothCodec.AptXAdaptive, 65)]
        [InlineData(BluetoothCodec.LDAC, 100)]
        [InlineData(BluetoothCodec.LC3, 20)]
        public void CodecLatencyTable_ReturnsDocumentedLatencies(BluetoothCodec codec, int expectedMs)
        {
            int latency = CodecLatencyTable.GetLatencyMs(codec);
            Assert.Equal(expectedMs, latency);
        }

        [Theory]
        [InlineData("Sony WH-1000XM5 Stereo", BluetoothCodec.LDAC)]
        [InlineData("WF-1000XM4 Hands-Free", BluetoothCodec.LDAC)]
        [InlineData("AirPods Pro 2", BluetoothCodec.AAC)]
        [InlineData("Beats Studio Buds", BluetoothCodec.AAC)]
        [InlineData("Sennheiser Momentum 4 aptX", BluetoothCodec.AptX)]
        [InlineData("Galaxy Buds Pro", BluetoothCodec.LC3)]
        [InlineData("Generic USB Bluetooth Earphones", BluetoothCodec.SBC)]
        public void CodecDetector_DetectsFromFriendlyName(string deviceName, BluetoothCodec expectedCodec)
        {
            var codec = CodecDetector.DetectFromFriendlyName(deviceName);
            Assert.Equal(expectedCodec, codec);
        }
    }
}
