using System;
using System.IO;
using PartnerLive2D.Core;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        sealed class ShortReads : MemoryStream
        {
            public ShortReads(byte[] data) : base(data, false) { }
            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 13));
        }
        static void LowEndChecks()
        {
            Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); };
            check(new QualityPolicy("auto", 2048, 8).Low, "2 GB boundary");
            check(new QualityPolicy("auto", 4096, 4).Low, "4 CPU boundary");
            check(!new QualityPolicy("auto", 2049, 5).Low, "above both boundaries");
            check(!new QualityPolicy(" HIGH ", 1024, 2).Low, "explicit high override");
            check(new QualityPolicy("low", 8192, 16).Low, "explicit low override");
            check(new QualityPolicy("typo", 0, 0).Low, "invalid setting and unknown hardware conservative auto");
            var high = new QualityPolicy("high", 0, 0); var low = new QualityPolicy("low", 0, 0);
            check(high.RenderSize(1536) == 1536 && high.Slots == 80 && high.Grid == 20 && high.BetweenAhead == 10, "high defaults");
            check(low.RenderSize(1536) == 1024 && low.RenderSize(512) == 512 && low.PlanAhead * 2 < low.Slots, "low capacity and smaller custom target");
            const int frameBytes = 97, frames = 35;
            var data = new byte[frameBytes * frames]; new Random(173).NextBytes(data);
            var output = new byte[frameBytes];
            using (var reader = new FrameBlockReader(new ShortReads(data), frameBytes))
            {
                Action<int> read = f =>
                {
                    reader.Read(f, output);
                    for (int j = 0; j < frameBytes; j++) check(output[j] == data[f * frameBytes + j], "frame bytes changed: " + f);
                };
                // Alternate base/sub regions exactly as the scheduler does; two windows avoid rereads.
                for (int i = 0; i < 4; i++) { read(i); read(i + 16); }
                check(reader.Reads == 2 && reader.Hits == 6 && reader.Bytes == 8 * frameBytes, "two sequential windows");
                read(34); read(32); read(0); read(20); read(34);
                var random = new Random(37);
                for (int i = 0; i < 1000; i++) read(random.Next(frames));
                bool failed = false;
                try { reader.Read(frames, output); } catch (ArgumentOutOfRangeException) { failed = true; }
                check(failed, "EOF must reject an invalid frame");
                failed = false;
                try { reader.Read(int.MaxValue, output); } catch (ArgumentOutOfRangeException) { failed = true; }
                check(failed, "frame limit must not overflow");
                Console.WriteLine("lowend: two-window cache 8 alternating frames -> 2 disk reads, 6 hits; randomized/short-read/tail bytes exact");
            }
            bool partial = false;
            try { using (var r = new FrameBlockReader(new MemoryStream(new byte[100]), 97)) { } }
            catch (InvalidDataException) { partial = true; }
            check(partial, "partial pack must be rejected");
            Console.WriteLine("lowend: quality boundaries and override checks, 0 problems");
        }

        static void LowEndPackChecks(string manifest)
        {
            var lib = SpriteLibrary.Load(manifest);
            foreach (var ch in lib.Characters)
            {
                string path = Path.Combine(Path.GetDirectoryName(manifest), ch.Pack);
                int size = ch.TextureWidth * ch.TextureHeight;
                using (var expected = File.OpenRead(path))
                using (var reader = new FrameBlockReader(File.OpenRead(path), size))
                {
                    var direct = new byte[size]; var cached = new byte[size];
                    int[] frames = { 0, ch.Frames, 1, ch.Frames + 1, 2, ch.Frames + 2, 3, ch.Frames + 3,
                        ch.PackFrames - 1, ch.PackFrames - 2, ch.Frames / 2, ch.Frames / 2 + 1 };
                    foreach (int frame in frames)
                    {
                        reader.Read(frame, cached); expected.Position = (long)frame * size;
                        int at = 0;
                        while (at < size) { int n = expected.Read(direct, at, size - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                        for (int i = 0; i < size; i++) if (direct[i] != cached[i]) throw new Exception("installed pack mismatch");
                    }
                    Console.WriteLine("lowend pack {0}: {1} frames exact, {2} block reads, {3} cache hits, {4} disk bytes, max {5:0.00} ms (current drive only)",
                        ch.Name, frames.Length, reader.Reads, reader.Hits, reader.Bytes, reader.MaxReadMs);
                }
            }
            Console.WriteLine("lowend installed packs: 0 problems");
        }
    }
}
