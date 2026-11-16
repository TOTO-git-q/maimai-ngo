using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PartnerLive2D.Core;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        // ---- pair partner optimisation (2026-11-13): sparse packs, autoEquip = once, missing-partner fallback

        static void PairOptChecks()
        {
            SparseChecks();
            AutoEquipChecks();
            Console.WriteLine("pair-opt: sparse packs, autoEquip and partner fallback, 0 problems");
        }

        // the same encoder as tools/ngo_library_compact.py, for synthetic packs
        static byte[] EncodeSparse(byte[][] frames, int columns, int rows, byte[] background)
        {
            int blocks = columns * rows, maskBytes = (blocks + 7) / 8;
            var records = new List<byte[]>(); int largest = 0;
            foreach (var f in frames)
            {
                var mask = new byte[maskBytes]; var lit = new MemoryStream();
                for (int i = 0; i < blocks; i++)
                {
                    bool bg = true;
                    for (int k = 0; k < 16; k++) if (f[i * 16 + k] != background[k]) { bg = false; break; }
                    if (!bg) { mask[i >> 3] |= (byte)(1 << (i & 7)); lit.Write(f, i * 16, 16); }
                }
                var rec = new byte[maskBytes + lit.Length];
                Buffer.BlockCopy(mask, 0, rec, 0, maskBytes); Buffer.BlockCopy(lit.ToArray(), 0, rec, maskBytes, (int)lit.Length);
                records.Add(rec); largest = Math.Max(largest, rec.Length);
            }
            var o = new MemoryStream(); var w = new BinaryWriter(o);
            w.Write(SparsePackHeader.Magic); w.Write(1); w.Write(64); w.Write(columns); w.Write(rows); w.Write(frames.Length); w.Write(maskBytes);
            w.Write(background); w.Write(largest); w.Write(new byte[12]);
            long pos = 64 + 8L * (frames.Length + 1);
            foreach (var r in records) { w.Write(pos); pos += r.Length; }
            w.Write(pos);
            foreach (var r in records) w.Write(r);
            return o.ToArray();
        }

        static void SparseChecks()
        {
            Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception("sparse: " + message); };
            var rnd = new Random(1113);
            var bgBlock = new byte[16]; bgBlock[0] = 0x40;
            foreach (var size in new[] { new[] { 3, 5 }, new[] { 8, 4 }, new[] { 181, 3 }, new[] { 1, 1 } })
            {
                int columns = size[0], rows = size[1], blocks = columns * rows, frameBytes = blocks * 16;
                var frames = new List<byte[]>();
                for (int n = 0; n < 23; n++)
                {
                    var f = new byte[frameBytes];
                    // patterns: all background, all literal (incl. blocks that differ from the background in one byte only),
                    // random density, long runs crossing mask-byte borders
                    double density = n == 0 ? 0 : n == 1 ? 1 : n % 3 == 0 ? .97 : rnd.NextDouble();
                    for (int i = 0; i < blocks; i++)
                    {
                        bool lit = n >= 20 ? (i / 11) % 2 == 0 : rnd.NextDouble() < density;
                        if (lit) { if (rnd.Next(4) == 0) { Buffer.BlockCopy(bgBlock, 0, f, i * 16, 16); f[i * 16 + rnd.Next(16)] ^= 1; } else for (int k = 0; k < 16; k++) f[i * 16 + k] = (byte)rnd.Next(256); }
                        else Buffer.BlockCopy(bgBlock, 0, f, i * 16, 16);
                    }
                    frames.Add(f);
                }
                var pack = EncodeSparse(frames.ToArray(), columns, rows, bgBlock);
                var output = new byte[frameBytes];
                using (var reader = new SparseFrameReader(new ShortReads(pack), frameBytes))
                {
                    var order = new List<int>();
                    for (int i = 0; i < frames.Count; i++) order.Add(i);
                    for (int i = 0; i < 300; i++) order.Add(rnd.Next(frames.Count));
                    foreach (int f in order)
                    {
                        Array.Clear(output, 0, output.Length);
                        reader.Read(f, output);
                        for (int j = 0; j < frameBytes; j++) check(output[j] == frames[f][j], "frame " + f + " byte " + j + " (" + columns + "x" + rows + ")");
                    }
                    check(reader.Hits + reader.Reads == order.Count && reader.Reads > 0, "every read counted");
                    bool failed = false;
                    try { reader.Read(frames.Count, output); } catch (ArgumentOutOfRangeException) { failed = true; }
                    check(failed, "frame past the end");
                }
            }
            // the read-ahead behaves as FrameBlockReader's: base pictures and their far-away in-betweens alternate, two windows
            {
                var frames = new byte[24][];
                for (int i = 0; i < frames.Length; i++) { frames[i] = new byte[16 * 6]; for (int k = 16; k < 32; k++) frames[i][k] = (byte)(i + 1); }
                var output = new byte[16 * 6];
                using (var reader = new SparseFrameReader(new MemoryStream(EncodeSparse(frames, 3, 2, bgBlock)), 16 * 6))
                {
                    for (int i = 0; i < 4; i++) { reader.Read(i, output); check(output[16] == i + 1, "window a"); reader.Read(i + 16, output); check(output[16] == i + 17, "window b"); }
                    check(reader.Reads == 2 && reader.Hits == 6, "two windows: 2 disk reads, 6 hits (got " + reader.Reads + "/" + reader.Hits + ")");
                }
            }
            // broken packs are refused at open, not half-read
            var good = EncodeSparse(new[] { new byte[16 * 6], new byte[16 * 6] }, 3, 2, bgBlock);
            Func<byte[], bool> refused = bytes =>
            {
                try { using (new SparseFrameReader(new MemoryStream(bytes), 16 * 6)) { } return false; }
                catch (InvalidDataException) { return true; }
                catch (EndOfStreamException) { return true; }
            };
            check(!refused(good), "a good pack opens");
            var b = (byte[])good.Clone(); b[0] = (byte)'X'; check(refused(b), "magic");
            b = new byte[good.Length - 1]; Buffer.BlockCopy(good, 0, b, 0, b.Length); check(refused(b), "truncated pack");
            b = (byte[])good.Clone(); b[16] = 9; check(refused(b), "columns vs mask size");
            b = (byte[])good.Clone(); b[64 + 8] ^= 1; check(refused(b), "offset table");
            bool wrong = false;
            try { using (new SparseFrameReader(new MemoryStream(good), 16 * 7)) { } } catch (InvalidDataException) { wrong = true; }
            check(wrong, "frame size must match the library");
            // a record whose mask promises more literal blocks than it holds
            var rec = new byte[] { 0x03, 1, 2, 3 }; bool shortRec = false;
            try { SparseDecoder.Decode(rec, 0, rec.Length, 3, 1, new byte[48], new byte[48]); } catch (InvalidDataException) { shortRec = true; }
            check(shortRec, "short record");
            check(PackReader.Known("raw") && PackReader.Known("sparse1") && !PackReader.Known("sparse2"), "known formats");
            Console.WriteLine("pair-opt: sparse1 decode exact on 4 grid sizes x 23 frames (random order, short reads); 6 broken packs refused");
        }

        static void AutoEquipChecks()
        {
            Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception("autoEquip: " + message); };
            var E = PartnerLogin.AutoEquip.Equip;
            check(PartnerLogin.Decide(false, true, false, 1, 502, false) == PartnerLogin.AutoEquip.NotRegistered, "guest");
            check(PartnerLogin.Decide(true, false, false, 1, 502, false) == PartnerLogin.AutoEquip.NoGameData, "no data");
            check(!PartnerLogin.Records(PartnerLogin.AutoEquip.NoGameData), "no data is not recorded");
            check(PartnerLogin.Decide(true, true, false, 1, 502, false) == E, "first login equips");
            check(PartnerLogin.Decide(true, true, true, 1, 502, false) == PartnerLogin.AutoEquip.AlreadyHandled, "a later switch sticks");
            check(PartnerLogin.Decide(true, true, false, 502, 502, false) == PartnerLogin.AutoEquip.AlreadyEquipped, "already equipped");
            check(PartnerLogin.Records(PartnerLogin.AutoEquip.AlreadyEquipped) && PartnerLogin.Records(E), "recorded");
            check(PartnerLogin.Decide(true, true, false, 1, 503, true) == PartnerLogin.AutoEquip.OtherPartnerTookIt, "two autoEquip partners");
            check(PartnerLogin.ParseAutoEquip("once") && !PartnerLogin.ParseAutoEquip("off") && !PartnerLogin.ParseAutoEquip(""), "parse");
            bool bad = false; try { PartnerLogin.ParseAutoEquip("always"); } catch (FormatException) { bad = true; }
            check(bad, "unknown autoEquip value");

            // missing partner data -> stock partner 1
            var known = new HashSet<int> { 1, 2, 502 };
            check(PartnerLogin.Fallback(502, known.Contains) == -1, "data present: leave it");
            check(PartnerLogin.Fallback(503, known.Contains) == 1, "data missing: stock default");
            check(PartnerLogin.Fallback(0, known.Contains) == 1, "id 0");
            check(PartnerLogin.Fallback(503, id => false) == -1, "no stock data either: leave it");

            // ledger: salted hashes only, survives a reload, different salt per file
            string dir = Path.Combine(Path.GetTempPath(), "pl2d_autoequip_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, AutoEquipLedger.FileName);
                const ulong user = 1234567890123UL, other = 42UL;
                var l = new AutoEquipLedger(path);
                check(!l.Contains(user) && l.Count == 0, "empty");
                l.Add(user); l.Add(user);
                check(l.Contains(user) && !l.Contains(other) && l.Count == 1, "add");
                var again = new AutoEquipLedger(path);
                check(again.Contains(user) && !again.Contains(other), "reload");
                string text = File.ReadAllText(path);
                check(!text.Contains(user.ToString()) && text.Contains("salt = "), "no raw user id in the file");
                again.Add(other);
                check(new AutoEquipLedger(path).Contains(other) && new AutoEquipLedger(path).Count == 2, "second account");
                string path2 = Path.Combine(dir, "b.txt"); new AutoEquipLedger(path2).Add(user);
                check(File.ReadAllText(path2) != text, "own salt per file");
                File.WriteAllText(path, "garbage\nsalt = zz\n");
                check(!new AutoEquipLedger(path).Contains(user), "damaged file reads as empty");
                // the full login sequence: first login equips, the player switches, the next login keeps the switch
                File.Delete(path);
                int equipped = 1;
                for (int login = 0; login < 3; login++)
                {
                    var ledger = new AutoEquipLedger(path);
                    var d = PartnerLogin.Decide(true, true, ledger.Contains(user), equipped, 502, false);
                    if (d == E) equipped = 502;
                    if (PartnerLogin.Records(d)) ledger.Add(user);
                    if (login == 0) { check(equipped == 502, "login 1 equips"); equipped = 7; }   // the player picks partner 7 in game
                    else check(equipped == 7, "login " + (login + 1) + " keeps the player's choice");
                }
            }
            finally { Directory.Delete(dir, true); }
            Console.WriteLine("pair-opt: autoEquip decisions, ledger (salted hashes, reload, damage) and missing-partner fallback");
        }

        // Headless.exe packcheck <sparse library dir> <raw library dir>: every frame through the game's decoder vs the raw pack
        static void PackCheck(string sparseDir, string rawDir)
        {
            var sparse = SpriteLibrary.Load(Path.Combine(sparseDir, "library.json"));
            var raw = SpriteLibrary.Load(Path.Combine(rawDir, "library.json"));
            for (int c = 0; c < 2; c++)
            {
                var s = sparse.Characters[c]; var r = raw.Characters[c];
                if (s.PackFormat != PackReader.Sparse1 || r.PackFormat != PackReader.Raw) throw new Exception("expected a sparse1 and a raw library");
                if (s.Name != r.Name || s.TextureWidth != r.TextureWidth || s.TextureHeight != r.TextureHeight || s.PackFrames != r.PackFrames)
                    throw new Exception("libraries differ: " + s.Name);
                string sp = Path.Combine(sparseDir, s.Pack), rp = Path.Combine(rawDir, r.Pack);
                PackReader.Validate(sp, s.PackFormat, s.TextureWidth, s.TextureHeight, s.PackFrames);
                PackReader.Validate(rp, r.PackFormat, r.TextureWidth, r.TextureHeight, r.PackFrames);
                int size = s.TextureWidth * s.TextureHeight;
                var got = new byte[size]; var want = new byte[size];
                long mismatched = 0; var wall = Stopwatch.StartNew();
                using (var reader = PackReader.Open(sp, s.PackFormat, size))
                using (var expected = new FileStream(rp, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                {
                    for (int f = 0; f < s.PackFrames; f++)
                    {
                        reader.Read(f, got);
                        int at = 0;
                        while (at < size) { int n = expected.Read(want, at, size - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                        if (!BytesEqual(got, want)) { mismatched++; if (mismatched < 5) Console.WriteLine("  MISMATCH {0} frame {1}", s.Name, f); }
                    }
                    if (expected.Read(want, 0, 1) != 0) throw new Exception("raw pack longer than the library says");
                    Console.WriteLine("packcheck {0}: {1}/{2} frames byte-exact; decode {3:0.000} ms/frame avg, {4:0.000} ms max; {5} disk reads, {6} window hits, {7:0} MB read; window cache {8:0.0} MB; wall {9:0.0} s",
                        s.Name, s.PackFrames - mismatched, s.PackFrames, reader.DecodeMsTotal / s.PackFrames, reader.DecodeMsMax,
                        reader.Reads, reader.Hits, reader.Bytes / 1048576.0, reader.CacheBytes / 1048576.0, wall.Elapsed.TotalSeconds);
                }
                // random access as the select page reads (frames, then their in-betweens far away in the pack)
                var rnd = new Random(7); long randomBad = 0; var t = Stopwatch.StartNew();
                using (var reader = PackReader.Open(sp, s.PackFormat, size))
                using (var expected = new FileStream(rp, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess))
                {
                    for (int k = 0; k < 2000; k++)
                    {
                        int f = rnd.Next(s.PackFrames);
                        reader.Read(f, got);
                        expected.Position = (long)f * size; int at = 0;
                        while (at < size) { int n = expected.Read(want, at, size - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                        if (!BytesEqual(got, want)) randomBad++;
                    }
                    Console.WriteLine("packcheck {0}: 2000 random frames {1}; read+decode {2:0.000} ms/frame avg (warm file cache), disk read max {3:0.00} ms",
                        s.Name, randomBad == 0 ? "byte-exact" : randomBad + " MISMATCHED", t.Elapsed.TotalMilliseconds / 2000, reader.MaxReadMs);
                }
                if (mismatched + randomBad > 0) throw new Exception(s.Name + ": " + (mismatched + randomBad) + " frames differ");
            }
            Console.WriteLine("packcheck: 100 % byte-exact");
        }

        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            fixed (byte* pa = a, pb = b)
            {
                long* la = (long*)pa, lb = (long*)pb; int n = a.Length / 8;
                for (int i = 0; i < n; i++) if (la[i] != lb[i]) return false;
                for (int i = n * 8; i < a.Length; i++) if (pa[i] != pb[i]) return false;
            }
            return true;
        }
    }
}
