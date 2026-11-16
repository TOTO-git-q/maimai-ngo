using System;
using System.Diagnostics;
using System.IO;

namespace PartnerLive2D.Core
{
    // A frame-library pack read on the reader thread: raw (FrameBlockReader) or sparse1 (SparseFrameReader).
    public interface IPackReader : IDisposable
    {
        void Read(int frame, byte[] output);   // output.Length == the frame's raw BC7 size
        long Reads { get; }                     // disk reads
        long Bytes { get; }                     // bytes read from disk
        long Hits { get; }                      // frames served from the read-ahead windows
        long Seeks { get; }
        double MaxReadMs { get; }
        long CacheBytes { get; }                // read-ahead window memory
        double DecodeMsMax { get; }             // 0 for raw packs
        double DecodeMsTotal { get; }
    }

    public static class PackReader
    {
        public const string Raw = "raw", Sparse1 = "sparse1";
        public static bool Known(string format) { return format == Raw || format == Sparse1; }

        // checks the pack file against the library without reading frames (loader thread)
        public static void Validate(string path, string format, int textureWidth, int textureHeight, int packFrames)
        {
            long frame = (long)textureWidth * textureHeight;
            if (format == Sparse1)
            {
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                {
                    var h = SparsePackHeader.Read(s);
                    if (h.Columns * 4 != textureWidth || h.Rows * 4 != textureHeight || h.Frames != packFrames)
                        throw new InvalidDataException("sparse pack does not match the library: " + Path.GetFileName(path));
                }
            }
            else if (new FileInfo(path).Length != frame * packFrames)
                throw new InvalidDataException("pack size does not match the library: " + Path.GetFileName(path));
        }

        public static IPackReader Open(string path, string format, int frameBytes)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            try
            {
                if (format == Sparse1) return new SparseFrameReader(stream, frameBytes);
                return new FrameBlockReader(stream, frameBytes);
            }
            catch { stream.Dispose(); throw; }
        }
    }

    // sparse1 (tools/ngo_library_compact.py): header, offset table, then per frame a block mask + the non-background blocks
    public sealed class SparsePackHeader
    {
        public static readonly byte[] Magic = { (byte)'N', (byte)'G', (byte)'O', (byte)'S', (byte)'P', (byte)'K', (byte)'1', 0 };
        public const int HeaderBytes = 64;
        public int Columns, Rows, Frames, MaskBytes, LargestRecord;
        public readonly byte[] Background = new byte[16];
        public long[] Offsets;                 // Frames + 1 absolute offsets; the last one is the file length
        public int Blocks { get { return Columns * Rows; } }
        public int FrameBytes { get { return Blocks * 16; } }

        public static SparsePackHeader Read(Stream s)
        {
            var head = new byte[HeaderBytes];
            s.Position = 0; ReadFully(s, head, 0, head.Length);
            for (int i = 0; i < Magic.Length; i++) if (head[i] != Magic[i]) throw new InvalidDataException("not a sparse1 pack");
            var h = new SparsePackHeader();
            int version = BitConverter.ToInt32(head, 8), header = BitConverter.ToInt32(head, 12);
            h.Columns = BitConverter.ToInt32(head, 16); h.Rows = BitConverter.ToInt32(head, 20);
            h.Frames = BitConverter.ToInt32(head, 24); h.MaskBytes = BitConverter.ToInt32(head, 28);
            Buffer.BlockCopy(head, 32, h.Background, 0, 16);
            h.LargestRecord = BitConverter.ToInt32(head, 48);
            if (version != 1 || header != HeaderBytes || h.Columns <= 0 || h.Rows <= 0 || h.Columns > 1024 || h.Rows > 1024
                || h.Frames <= 0 || h.Frames > 1 << 22 || h.MaskBytes != (h.Blocks + 7) / 8
                || h.LargestRecord < h.MaskBytes || h.LargestRecord > h.MaskBytes + h.FrameBytes)
                throw new InvalidDataException("bad sparse1 header");
            var table = new byte[8 * (h.Frames + 1)];
            ReadFully(s, table, 0, table.Length);
            h.Offsets = new long[h.Frames + 1];
            long previous = HeaderBytes + table.Length;
            for (int f = 0; f <= h.Frames; f++)
            {
                long o = BitConverter.ToInt64(table, 8 * f);
                if (f == 0 ? o != previous : o - previous < h.MaskBytes || o - previous > h.LargestRecord)
                    throw new InvalidDataException("bad sparse1 offset table at frame " + f);
                h.Offsets[f] = o; previous = o;
            }
            if (h.Offsets[h.Frames] != s.Length) throw new InvalidDataException("sparse1 pack length does not match its offset table");
            return h;
        }

        internal static void ReadFully(Stream s, byte[] buffer, int at, int count)
        {
            int end = at + count;
            while (at < end)
            {
                int n = s.Read(buffer, at, end - at);
                if (n == 0) throw new EndOfStreamException("sparse1 pack");
                at += n;
            }
        }

        // one frame of only the background block (the decoder copies background runs out of it)
        public byte[] BackgroundFrame()
        {
            var frame = new byte[FrameBytes];
            for (int i = 0; i < frame.Length; i += 16) Buffer.BlockCopy(Background, 0, frame, i, 16);
            return frame;
        }
    }

    public static class SparseDecoder
    {
        // record = mask (bit i = block i, LSB first) + the literal blocks in block order. Runs of literal blocks and runs of
        // background blocks are each one copy; nothing is allocated. Throws if the record does not match its mask.
        public static void Decode(byte[] src, int at, int length, int blocks, int maskBytes, byte[] background, byte[] output)
        {
            if (output.Length != blocks * 16 || background.Length != output.Length || length < maskBytes || at < 0 || at + length > src.Length)
                throw new InvalidDataException("sparse1 record size");
            int literal = at + maskBytes, end = at + length, i = 0;
            while (i < blocks)
            {
                bool set = (src[at + (i >> 3)] & (1 << (i & 7))) != 0;
                int j = i + 1;
                while (j < blocks)
                {
                    if ((j & 7) == 0 && j + 8 <= blocks)
                    {
                        byte m = src[at + (j >> 3)];
                        if (m == (set ? 0xFF : 0x00)) { j += 8; continue; }
                    }
                    if (((src[at + (j >> 3)] & (1 << (j & 7))) != 0) != set) break;
                    j++;
                }
                int bytes = (j - i) * 16;
                if (set)
                {
                    if (literal + bytes > end) throw new InvalidDataException("sparse1 record shorter than its mask");
                    Buffer.BlockCopy(src, literal, output, i * 16, bytes);
                    literal += bytes;
                }
                else Buffer.BlockCopy(background, i * 16, output, i * 16, bytes);
                i = j;
            }
            if (literal != end) throw new InvalidDataException("sparse1 record longer than its mask");
        }
    }

    // The sparse twin of FrameBlockReader: two read-ahead windows of 4 consecutive frame records (the base pictures and the
    // in-between region stay hot at the same time), decoded into the caller's raw frame buffer. Owned by the reader thread;
    // every buffer is allocated in the constructor.
    public sealed class SparseFrameReader : IPackReader
    {
        readonly Stream _stream;
        readonly SparsePackHeader _h;
        readonly int _blockFrames;
        readonly byte[] _background;
        readonly byte[][] _windows;
        readonly int[] _first = { -1, -1 }, _count = new int[2];
        readonly long[] _used = new long[2];
        long _clock;
        public SparsePackHeader Header { get { return _h; } }
        public long Reads { get; private set; }
        public long Bytes { get; private set; }
        public long Hits { get; private set; }
        public long Seeks { get; private set; }
        public double MaxReadMs { get; private set; }
        public double DecodeMsMax { get; private set; }
        public double DecodeMsTotal { get; private set; }
        public long CacheBytes { get { return (long)_windows[0].Length * 2 + _background.Length; } }

        public SparseFrameReader(Stream stream, int frameBytes, int blockFrames = 4)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek || frameBytes <= 0 || blockFrames <= 0)
                throw new ArgumentException("invalid frame stream");
            _h = SparsePackHeader.Read(stream);
            if (_h.FrameBytes != frameBytes) throw new InvalidDataException("sparse1 frame size does not match the library");
            _stream = stream; _blockFrames = blockFrames;
            long window = 0;
            for (int f = 0; f < _h.Frames; f += blockFrames)
                window = Math.Max(window, _h.Offsets[Math.Min(_h.Frames, f + blockFrames)] - _h.Offsets[f]);
            _windows = new[] { new byte[checked((int)window)], new byte[checked((int)window)] };
            _background = _h.BackgroundFrame();
        }

        public void Read(int frame, byte[] output)
        {
            if (frame < 0 || frame >= _h.Frames || output == null || output.Length != _h.FrameBytes) throw new ArgumentOutOfRangeException("frame");
            int slot = -1;
            for (int i = 0; i < 2; i++)
                if (_first[i] >= 0 && frame >= _first[i] && frame < _first[i] + _count[i]) { slot = i; Hits++; break; }
            if (slot < 0)
            {
                slot = _first[0] < 0 ? 0 : _first[1] < 0 ? 1 : _used[0] <= _used[1] ? 0 : 1;
                int first = frame / _blockFrames * _blockFrames, count = Math.Min(_blockFrames, _h.Frames - first);
                long start = _h.Offsets[first];
                int bytes = (int)(_h.Offsets[first + count] - start);
                long t = Stopwatch.GetTimestamp();
                if (_stream.Position != start) { _stream.Position = start; Seeks++; }
                _first[slot] = -1;   // a failed read must not leave a half-filled window marked valid
                SparsePackHeader.ReadFully(_stream, _windows[slot], 0, bytes);
                Reads++; Bytes += bytes;
                MaxReadMs = Math.Max(MaxReadMs, (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency);
                _first[slot] = first; _count[slot] = count;
            }
            _used[slot] = ++_clock;
            long d = Stopwatch.GetTimestamp();
            long origin = _h.Offsets[_first[slot]];
            SparseDecoder.Decode(_windows[slot], (int)(_h.Offsets[frame] - origin), (int)(_h.Offsets[frame + 1] - _h.Offsets[frame]),
                _h.Blocks, _h.MaskBytes, _background, output);
            double ms = (Stopwatch.GetTimestamp() - d) * 1000.0 / Stopwatch.Frequency;
            DecodeMsTotal += ms; if (ms > DecodeMsMax) DecodeMsMax = ms;
        }

        public void Dispose() { _stream.Dispose(); }
    }
}
