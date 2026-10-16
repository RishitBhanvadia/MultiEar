using System;
using NAudio.Wave;

namespace MultiEar.Engine.Audio
{
    /// <summary>
    /// Thread-safe circular audio buffer implementing NAudio's IWaveProvider.
    /// Features:
    /// 1. Pre-cushioning with silence to eliminate startup clicks/underruns.
    /// 2. Glitch-free dynamic delay adjustment (no buffer wipe on slider changes).
    /// 3. Automatic hardware clock drift compensation (maintains sync across divergent Bluetooth DACs).
    /// </summary>
    public class AudioRingBuffer : IWaveProvider
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;
        private readonly object _lock = new();

        private int _writePos;
        private int _readPos;
        private int _availableBytes;

        private readonly int _safetyCushionBytes;
        private int _targetDelayBytes;
        private readonly int _driftToleranceBytes;

        private volatile int _underrunCount;
        private volatile int _overrunCount;
        private volatile int _driftCorrectionCount;

        public WaveFormat WaveFormat { get; }
        public int CapacityMs { get; }
        public int UnderrunCount => _underrunCount;
        public int OverrunCount => _overrunCount;
        public int DriftCorrectionCount => _driftCorrectionCount;

        public AudioRingBuffer(WaveFormat waveFormat, int capacityMs = 1500)
        {
            WaveFormat = waveFormat;
            CapacityMs = capacityMs;

            _capacity = (int)(waveFormat.AverageBytesPerSecond * (capacityMs / 1000.0));
            _capacity -= _capacity % waveFormat.BlockAlign;
            if (_capacity < waveFormat.BlockAlign * 16)
                _capacity = waveFormat.BlockAlign * 16;

            _buffer = new byte[_capacity];

            // 50ms safety cushion to absorb OS scheduling jitter and Bluetooth packet variance
            _safetyCushionBytes = (int)(waveFormat.AverageBytesPerSecond * (50.0 / 1000.0));
            _safetyCushionBytes -= _safetyCushionBytes % waveFormat.BlockAlign;

            // 25ms drift tolerance before applying gentle frame slip correction
            _driftToleranceBytes = (int)(waveFormat.AverageBytesPerSecond * (25.0 / 1000.0));
            _driftToleranceBytes -= _driftToleranceBytes % waveFormat.BlockAlign;

            _targetDelayBytes = _safetyCushionBytes;
            Clear();
        }

        /// <summary>
        /// Clears buffer content and pre-positions writer ahead of reader by the target delay cushion.
        /// The reader consumes silence (zeros) while capture begins writing real audio.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                int cushion = Math.Clamp(_targetDelayBytes, _safetyCushionBytes, _capacity - WaveFormat.BlockAlign);
                _readPos = 0;
                _writePos = cushion % _capacity;
                _availableBytes = cushion;
            }
        }

        /// <summary>
        /// Updates the target delay in milliseconds without wiping existing buffered audio.
        /// Smoothly slews buffer read pointer over subsequent reads.
        /// </summary>
        public void SetDelayMs(int delayMs)
        {
            lock (_lock)
            {
                int delayBytes = (int)(WaveFormat.AverageBytesPerSecond * (delayMs / 1000.0));
                delayBytes -= delayBytes % WaveFormat.BlockAlign;

                int newTarget = delayBytes + _safetyCushionBytes;
                _targetDelayBytes = Math.Clamp(newTarget, _safetyCushionBytes, _capacity - WaveFormat.BlockAlign);
            }
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (count <= 0) return;

            lock (_lock)
            {
                if (count > _capacity)
                {
                    int excess = count - _capacity;
                    offset += excess;
                    count = _capacity;
                }

                count -= count % WaveFormat.BlockAlign;
                if (count <= 0) return;

                int spaceAtEnd = _capacity - _writePos;
                if (count <= spaceAtEnd)
                {
                    Array.Copy(data, offset, _buffer, _writePos, count);
                }
                else
                {
                    Array.Copy(data, offset, _buffer, _writePos, spaceAtEnd);
                    Array.Copy(data, offset + spaceAtEnd, _buffer, 0, count - spaceAtEnd);
                }

                _writePos = (_writePos + count) % _capacity;

                if (_availableBytes + count > _capacity)
                {
                    // Buffer overrun: advance reader position to prevent stale playback
                    int overflow = (_availableBytes + count) - _capacity;
                    _readPos = (_readPos + overflow) % _capacity;
                    _availableBytes = _capacity;
                    _overrunCount++;
                }
                else
                {
                    _availableBytes += count;
                }
            }
        }

        public int Read(byte[] data, int offset, int count)
        {
            lock (_lock)
            {
                // Automatic Hardware Clock Drift Compensation:
                // If the buffer has accumulated more bytes than target + tolerance, gently drop 1 frame
                // If it has fewer bytes than target - tolerance, gently duplicate 1 frame
                if (_availableBytes > _targetDelayBytes + _driftToleranceBytes)
                {
                    // Too full: skip 1 frame
                    int frameSize = WaveFormat.BlockAlign;
                    _readPos = (_readPos + frameSize) % _capacity;
                    _availableBytes -= frameSize;
                    _driftCorrectionCount++;
                }
                else if (_availableBytes < _targetDelayBytes - _driftToleranceBytes && _availableBytes >= WaveFormat.BlockAlign)
                {
                    // Starving slightly: duplicate 1 frame by leaving readPos behind 1 frame
                    int frameSize = WaveFormat.BlockAlign;
                    _availableBytes += frameSize;
                    _driftCorrectionCount++;
                }

                // Handle underrun: if not enough data is buffered, emit silence for this read
                if (_availableBytes < count)
                {
                    _underrunCount++;
                    Array.Clear(data, offset, count);
                    return count;
                }

                int toRead = Math.Min(count, _availableBytes);
                if (toRead > 0)
                {
                    int spaceAtEnd = _capacity - _readPos;
                    if (toRead <= spaceAtEnd)
                    {
                        Array.Copy(_buffer, _readPos, data, offset, toRead);
                    }
                    else
                    {
                        Array.Copy(_buffer, _readPos, data, offset, spaceAtEnd);
                        Array.Copy(_buffer, 0, data, offset + spaceAtEnd, toRead - spaceAtEnd);
                    }

                    _readPos = (_readPos + toRead) % _capacity;
                    _availableBytes -= toRead;
                }

                if (toRead < count)
                {
                    Array.Clear(data, offset + toRead, count - toRead);
                }

                return count;
            }
        }
    }
}
