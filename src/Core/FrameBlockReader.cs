using System;
using System.Diagnostics;
using System.IO;

namespace PartnerLive2D.Core
{
    // Two windows keep base pictures and the pack's separate in-between region hot at the same time.
    // Owned by the reader thread; no disk work or large buffer allocation occurs on Unity's thread.
    public sealed class FrameBlockReader : IDisposable
    {
        readonly Stream _stream;
        readonly int _frameBytes, _blockFrames;
        readonly byte[][] _blocks;
        readonly long[] _start = { -1, -1 }, _used = new long[2];
        readonly int[] _length = new int[2];
        long _clock;
        public long Reads { get; private set; }
        public long Bytes { get; private set; }
        public long Hits { get; private set; }
        public long Seeks { get; private set; }
        public double MaxReadMs { get; private set; }
        public long CacheBytes => (long)_blocks.Length * _frameBytes * _blockFrames;

        public FrameBlockReader(Stream stream, int frameBytes, int blockFrames = 4)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek || frameBytes <= 0 || blockFrames <= 0)
                throw new ArgumentException("invalid frame stream");
            if (stream.Length % frameBytes != 0) throw new InvalidDataException("partial packed frame");
            _stream = stream; _frameBytes = frameBytes; _blockFrames = blockFrames;
            _blocks = new[] { new byte[checked(frameBytes * blockFrames)], new byte[checked(frameBytes * blockFrames)] };
        }

        public void Read(int frame, byte[] output)
        {
            if (frame < 0 || output == null || output.Length != _frameBytes || ((long)frame + 1) * _frameBytes > _stream.Length)
                throw new ArgumentOutOfRangeException("frame");
            long offset = (long)frame * _frameBytes;
            int slot = -1;
            for (int i = 0; i < 2; i++)
                if (_start[i] >= 0 && offset >= _start[i] && offset + _frameBytes <= _start[i] + _length[i]) { slot = i; Hits++; break; }
            if (slot < 0)
            {
                slot = _start[0] < 0 ? 0 : _start[1] < 0 ? 1 : _used[0] <= _used[1] ? 0 : 1;
                long start = (long)(frame / _blockFrames) * _blockFrames * _frameBytes;
                int count = (int)Math.Min(_blocks[slot].Length, _stream.Length - start);
                long t = Stopwatch.GetTimestamp();
                if (_stream.Position != start) { _stream.Position = start; Seeks++; }
                int at = 0;
                while (at < count)
                {
                    int n = _stream.Read(_blocks[slot], at, count - at);
                    if (n == 0) throw new EndOfStreamException("packed frame block");
                    at += n;
                }
                Reads++; Bytes += count;
                MaxReadMs = Math.Max(MaxReadMs, (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency);
                _start[slot] = start; _length[slot] = count;
            }
            _used[slot] = ++_clock;
            Buffer.BlockCopy(_blocks[slot], (int)(offset - _start[slot]), output, 0, _frameBytes);
        }
        public void Dispose() { _stream.Dispose(); }
    }
}
