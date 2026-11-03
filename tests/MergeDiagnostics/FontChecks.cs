using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using PartnerLive2D.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

// Managed test doubles follow the installed TMP 1.4 / TextCore table and atlas contracts.
// Native rasterization is deliberately not simulated as proof of a game rendering result.
namespace UnityEngine
{
    class Object { public string name; public static void DontDestroyOnLoad(Object value) { } }
    class Font : Object { }
    class Texture2D : Object
    {
        public int width, height, Applies;
        public bool isReadable = true;
        public bool Resize(int w, int h) { width = w; height = h; return true; }
        public void Apply(bool mipmaps, bool unreadable) { Applies++; isReadable = !unreadable; }
    }
    class Material : Object
    {
        public Material(Material other) { name = other.name; }
        public void SetTexture(int id, Texture2D value) { }
        public void SetFloat(int id, float value) { }
    }
    struct Scene { public bool IsValid() { return true; } }
    class GameObject { public Scene scene; }
    static class Resources
    {
        public static T Load<T>(string name) where T : class { return null; }
        public static T[] FindObjectsOfTypeAll<T>() { return new T[0]; }
    }
}
namespace UnityEngine.TextCore
{
    struct GlyphRect { public int width, height; }
    struct FaceInfo { public int pointSize; }
    class Glyph { public uint index; public GlyphRect glyphRect; }
}
namespace UnityEngine.TextCore.LowLevel
{
    enum GlyphRenderMode { SDFAA }
    enum GlyphPackingMode { BestShortSideFit }
    enum FontEngineError { Success, InvalidFont }
    static class FontEngine
    {
        public static int Calls, Loads, Resets;
        public static uint FailIndex, ThrowIndex;
        public static bool NullGlyph;
        public static FontEngineError FaceError;
        public static FontEngineError LoadFontFace(Font font, int size) { Loads++; return FaceError; }
        public static bool TryGetGlyphIndex(uint unicode, out uint index)
        { index = unicode == 0x2603 ? 0u : unicode == 'B' ? (uint)'A' : unicode; return index != 0; }
        internal static void ResetAtlasTexture(Texture2D texture) { Resets++; }
        internal static bool TryAddGlyphToTexture(uint index, int padding, GlyphPackingMode packing,
            List<GlyphRect> free, List<GlyphRect> used, GlyphRenderMode mode, Texture2D texture, out Glyph glyph)
        {
            Calls++;
            if (index == ThrowIndex) throw new InvalidOperationException("injected native failure");
            glyph = index == FailIndex || NullGlyph ? null : new Glyph { index = index, glyphRect = new GlyphRect { width = 12, height = 12 } };
            return index != FailIndex;
        }
        public static void Reset()
        {
            Calls = Loads = Resets = 0; FailIndex = ThrowIndex = 0;
            NullGlyph = false; FaceError = FontEngineError.Success;
        }
    }
}
namespace TMPro
{
    enum AtlasPopulationMode { Dynamic }
    class TMP_Character
    {
        public uint unicode;
        public Glyph glyph;
        public TMP_Character(uint value, Glyph g) { unicode = value; glyph = g; }
    }
    class TMP_FontAsset : UnityEngine.Object
    {
        // Names are part of the compatibility contract inspected by the production adapter.
        readonly List<GlyphRect> m_FreeGlyphRects = new List<GlyphRect> { new GlyphRect { width = 4095, height = 4095 } };
        readonly List<GlyphRect> m_UsedGlyphRects = new List<GlyphRect>();
        public Font sourceFontFile = new Font();
        public Texture2D atlasTexture = new Texture2D();
        public List<Glyph> glyphTable = new List<Glyph>();
        public List<TMP_Character> characterTable = new List<TMP_Character>();
        public Dictionary<uint, Glyph> glyphLookupTable = new Dictionary<uint, Glyph>();
        public Dictionary<uint, TMP_Character> characterLookupTable = new Dictionary<uint, TMP_Character>();
        public List<TMP_FontAsset> fallbackFontAssetTable;
        public int atlasWidth = 4096, atlasHeight = 4096, atlasPadding = 5;
        public GlyphRenderMode atlasRenderMode = GlyphRenderMode.SDFAA;
        public FaceInfo faceInfo = new FaceInfo { pointSize = 48 };
        public float normalStyle = 0, boldStyle = 0.75f;
        public int IndexedGlyphs;
        public static TMP_FontAsset CreateFontAsset(Font src, int size, int padding, GlyphRenderMode mode, int width, int height, AtlasPopulationMode population)
        { return new TMP_FontAsset { sourceFontFile = src }; }
        public void ReadFontAssetDefinition()
        {
            glyphLookupTable.Clear(); characterLookupTable.Clear();
            foreach (var g in glyphTable) glyphLookupTable.Add(g.index, g);
            foreach (var c in characterTable) characterLookupTable.Add(c.unicode, c);
            IndexedGlyphs = glyphTable.Count;
        }
        internal bool TryAddCharacterInternal(uint unicode, out TMP_Character character)
        { character = null; throw new NullReferenceException("TMP wrapper must not be used by warmup"); }
    }
    class TextMeshProUGUI
    {
        public TMP_FontAsset font;
        public Material fontSharedMaterial;
        public GameObject gameObject = new GameObject();
    }
    static class ShaderUtilities
    {
        public const int ID_MainTex = 0, ID_TextureWidth = 1, ID_TextureHeight = 2, ID_GradientScale = 3,
            ID_WeightNormal = 4, ID_WeightBold = 5;
    }
}
namespace MelonLoader
{
    static class MelonCoroutines
    {
        public static IEnumerator Last;
        public static object Start(IEnumerator work) { Last = work; return work; }
    }
}

partial class Checks
{
    static void WarmField(string name, object value) { typeof(NgoPixelFont).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value); }
    static T WarmField<T>(string name) { return (T)typeof(NgoPixelFont).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); }
    static IEnumerator Draw(TMP_FontAsset font, string text)
    {
        var result = new NgoFontCharacterScan.Result(text, 0, 0, false, null);
        return Draw(font, () => result);
    }
    static IEnumerator Draw(TMP_FontAsset font, Func<NgoFontCharacterScan.Result> completed)
    {
        WarmField("_processed", 0);
        return (IEnumerator)typeof(NgoPixelFont).GetMethod("Draw", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { font, completed });
    }
    static void Finish(IEnumerator draw) { int steps = 0; while (draw.MoveNext()) if (++steps > 20) throw new Exception("warmup did not finish"); }
    static NgoFontCharacterScan.Result WaitScan(NgoFontCharacterScan scan)
    {
        // Only the test harness waits; the production coroutine is timed independently below.
        var deadline = Stopwatch.StartNew();
        while (scan.Completed == null)
        {
            if (deadline.ElapsedMilliseconds > 30000) throw new Exception("scan did not finish");
            Thread.Sleep(1);
        }
        return scan.Completed;
    }
    static void FontScanChecks()
    {
        string fixture = Path.Combine(Environment.CurrentDirectory, "validation", "font-fixtures", Guid.NewGuid().ToString("N"));
        string root = Path.Combine(fixture, "StreamingAssets");
        string song = Path.Combine(root, "A000", "music", "001", "Music.xml");
        string cache = Path.Combine(fixture, "LocalAssets", "PartnerLive2D", "pixel_font_chars.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(song));
        File.WriteAllText(song, "<Name>\u9f8d\u9f98</Name>");
        var stamp = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(song, stamp);
        string ignored = Path.Combine(root, "A000", "movies", "001", "Music.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(ignored)); File.WriteAllText(ignored, "\u9f9c");
        Directory.CreateDirectory(Path.Combine(root, "A000", "music", "empty"));

        int main = Thread.CurrentThread.ManagedThreadId, worker = main, reads = 0;
        var first = WaitScan(new NgoFontCharacterScan(root, cache, file =>
        { reads++; worker = Thread.CurrentThread.ManagedThreadId; return File.ReadAllText(file) + "\u0001\ud800"; }));
        Check(!first.CacheUsed && first.Warning == null && first.Songs == 1 && reads == 1 && worker != main, "cache miss reads music on a background thread");
        Check(first.Characters.Contains("\u9f8d") && first.Characters.Contains("\u9f98") && !first.Characters.Contains("\u9f9c"), "only option/music/id Music.xml is collected");
        Check(first.Characters.Contains(" ") && first.Characters.Contains("~") && first.Characters.Contains("\u3000") && first.Characters.Contains("\u30ff") &&
            first.Characters.Contains("\uff01") && first.Characters.Contains("\uff9f"), "all original base ranges, including half-width kana, retained");
        bool sorted = true;
        for (int i = 0; i < first.Characters.Length; i++)
            if (char.IsControl(first.Characters[i]) || char.IsSurrogate(first.Characters[i]) || (i > 0 && first.Characters[i - 1] >= first.Characters[i])) sorted = false;
        Check(sorted && File.Exists(cache), "collected characters are sorted, unique, non-control BMP characters and cached");
        using (var reader = new BinaryReader(File.OpenRead(cache), System.Text.Encoding.UTF8))
        {
            Check(reader.ReadString() == "NGO pixel font characters 1" && reader.ReadString() == Path.GetFullPath(root) &&
                reader.ReadInt32() == 1 && reader.ReadInt64() == File.GetLastWriteTimeUtc(song).Ticks && reader.ReadString() == first.Characters,
                "cache persists version, root, Music.xml count, newest UTC ticks and characters");
        }

        reads = 0;
        Func<string, string> read = file => { reads++; return File.ReadAllText(file); };
        var hit = WaitScan(new NgoFontCharacterScan(root, cache, read));
        Check(hit.CacheUsed && hit.Warning == null && reads == 0 && hit.Characters == first.Characters, "cache hit validates fingerprint without reading XML");
        Mod.Lines.Clear(); FontEngine.Reset(); Finish(Draw(new TMP_FontAsset(), () => hit));
        Check(Mod.Lines.Exists(x => x.Contains("read in " + hit.ElapsedMs + " ms; cache=hit")), "cache-hit scan time and use logged at main-thread handoff");

        string second = Path.Combine(root, "B000", "music", "002", "Music.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(second)); File.WriteAllText(second, "\u9e7f");
        File.SetLastWriteTimeUtc(second, stamp.AddDays(-1)); reads = 0;
        var added = WaitScan(new NgoFontCharacterScan(root, cache, read));
        Check(!added.CacheUsed && added.Songs == 2 && reads == 2 && added.Characters.Contains("\u9e7f"), "count change invalidates cache even when newest timestamp is unchanged");
        File.WriteAllText(song, "\u9db4"); File.SetLastWriteTimeUtc(song, stamp.AddSeconds(2)); reads = 0;
        var changed = WaitScan(new NgoFontCharacterScan(root, cache, read));
        Check(!changed.CacheUsed && reads == 2 && changed.Characters.Contains("\u9db4") && !changed.Characters.Contains("\u9f98"), "newest timestamp change invalidates cache at the same count");
        File.Delete(second); reads = 0;
        var removed = WaitScan(new NgoFontCharacterScan(root, cache, read));
        Check(!removed.CacheUsed && removed.Songs == 1 && reads == 1 && !removed.Characters.Contains("\u9e7f"), "song removal invalidates cache and removes stale characters");

        File.WriteAllBytes(cache, new byte[] { 255, 0, 1 }); reads = 0;
        var corrupt = WaitScan(new NgoFontCharacterScan(root, cache, read));
        Check(!corrupt.CacheUsed && corrupt.Warning == null && reads == 1 && corrupt.Characters == removed.Characters,
            "truncated/corrupt cache safely rescans and is replaced: used=" + corrupt.CacheUsed + ", reads=" + reads + ", warning=" + corrupt.Warning);
        var bytes = File.ReadAllBytes(cache); bytes[bytes.Length - 1] ^= 1; File.WriteAllBytes(cache, bytes); reads = 0;
        var checksum = WaitScan(new NgoFontCharacterScan(root, cache, read));
        Check(!checksum.CacheUsed && reads == 1 && checksum.Characters == removed.Characters, "checksum corruption safely rescans");
        reads = 0;
        Check(WaitScan(new NgoFontCharacterScan(root, cache, read)).CacheUsed && reads == 0, "repaired cache is reusable");

        string otherRoot = Path.Combine(fixture, "other-root"); Directory.CreateDirectory(otherRoot);
        var other = WaitScan(new NgoFontCharacterScan(otherRoot, cache, read));
        Check(!other.CacheUsed && other.Songs == 0 && !other.Characters.Contains("\u9db4"), "cache from another StreamingAssets root cannot leak old song characters");
        var emptyHit = WaitScan(new NgoFontCharacterScan(otherRoot, cache, read));
        Check(emptyHit.CacheUsed && emptyHit.Songs == 0 && emptyHit.Characters == other.Characters, "empty music folder base set is cached");
        string blockedCache = Path.Combine(fixture, "cache-is-a-directory"); Directory.CreateDirectory(blockedCache);
        var blocked = WaitScan(new NgoFontCharacterScan(root, blockedCache, read));
        Check(!blocked.CacheUsed && blocked.Warning.Contains("cache not written") && blocked.Characters == removed.Characters,
            "unwritable cache preserves collected characters and reports a harmless warning");
        Check(Directory.GetFiles(fixture, "*.tmp", SearchOption.AllDirectories).Length == 0, "failed cache replacement leaves no temporary files");
        string failedCache = Path.Combine(fixture, "read-failed.cache");
        var failed = WaitScan(new NgoFontCharacterScan(root, failedCache, file => { throw new IOException("injected XML read failure"); }));
        Check(failed.Warning.Contains("injected XML read failure") && !File.Exists(failedCache) && failed.Characters == other.Characters,
            "XML failure publishes base characters but never caches a partial scan");
        Mod.Lines.Clear(); FontEngine.Reset(); Finish(Draw(new TMP_FontAsset(), () => failed));
        Check(WarmField<string>("_warmStatus") == "complete" && Mod.Lines.Exists(x => x.Contains("injected XML read failure")), "scan failure does not strand the coroutine or disable base warmup");
        var missing = WaitScan(new NgoFontCharacterScan(Path.Combine(fixture, "missing-root"), failedCache));
        Check(missing.Warning != null && missing.Characters == other.Characters && !File.Exists(failedCache), "missing root safely publishes uncached base characters");

        // Exercise the actual Prewarm entry point and its coroutine registration as well as the slow-reader seam.
        Mod.Lines.Clear();
        string oldRoot = Application.streamingAssetsPath, oldAssets = Mod.AssetRoot;
        Application.streamingAssetsPath = root; Mod.AssetRoot = Path.Combine(fixture, "entry-assets");
        var launch = Stopwatch.StartNew();
        typeof(NgoPixelFont).GetMethod("Prewarm", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { new TMP_FontAsset() });
        launch.Stop(); Application.streamingAssetsPath = oldRoot; Mod.AssetRoot = oldAssets;
        Check(launch.ElapsedMilliseconds < 100 && MelonLoader.MelonCoroutines.Last != null, "actual Prewarm promptly registers a polling coroutine");
        PlayStart(); var registered = MelonLoader.MelonCoroutines.Last;
        var entryDeadline = Stopwatch.StartNew();
        while (!Mod.Lines.Exists(x => x.Contains("cache=miss") && x.Contains("from 1 songs")))
        {
            Check(registered.MoveNext(), "registered coroutine yields while scan is pending or song is playing");
            if (entryDeadline.ElapsedMilliseconds > 5000) throw new Exception("Prewarm scan handoff did not finish");
            Thread.Sleep(1);
        }
        PlayEnd(); Finish(registered);

        Mod.Lines.Clear(); FontEngine.Reset(); var font = new TMP_FontAsset();
        using (var entered = new ManualResetEvent(false))
        {
            worker = main;
            var dispatch = Stopwatch.StartNew();
            var slow = new NgoFontCharacterScan(root, Path.Combine(fixture, "slow.cache"), file =>
            {
                worker = Thread.CurrentThread.ManagedThreadId; entered.Set();
                Thread.Sleep(20000); return File.ReadAllText(file);
            });
            var draw = Draw(font, () => slow.Completed); dispatch.Stop();
            Check(dispatch.ElapsedMilliseconds < 100 && entered.WaitOne(5000) && worker != main, "20-second XML read is dispatched off the main thread");
            long worstTicks = 0; var polls = Stopwatch.StartNew();
            for (int i = 0; i < 256; i++)
            {
                long start = Stopwatch.GetTimestamp(); bool yielded = draw.MoveNext();
                worstTicks = Math.Max(worstTicks, Stopwatch.GetTimestamp() - start);
                Check(yielded && draw.Current == null, "main-thread scan poll yields immediately");
            }
            polls.Stop();
            double worstMs = worstTicks * 1000.0 / Stopwatch.Frequency;
            Check(polls.ElapsedMilliseconds < 100 && worstMs < 50 && slow.Completed == null && FontEngine.Calls == 0 && FontEngine.Resets == 0,
                "main-thread polls never wait for the 20-second scan or perform atlas work while pending");
            Check(Mod.Lines.Count == 0, "background scan does not call mod/Unity logging");
            var done = WaitScan(slow);
            Check(done.ElapsedMs >= 20000 && done.Warning == null && !done.CacheUsed, "real 20-second scan finishes and publishes its duration");
            PlayStart(); Check(draw.MoveNext() && FontEngine.Calls == 0 && FontEngine.Resets == 0 && font.atlasTexture.Applies == 0,
                "scan completion during a song still defers all atlas work");
            PlayEnd(); Finish(draw);
            Check(WarmField<string>("_warmStatus") == "complete" && WarmField<int>("_processed") == done.Characters.Length && WarmField<int>("_songs") == 1,
                "background result is handed to unchanged batch warmup and finishes after song release");
            Check(Mod.Lines.Exists(x => x.Contains("read in " + done.ElapsedMs + " ms; cache=miss")), "cache-miss scan time and use logged");
            Console.WriteLine("Font scan: 20-second XML double took {0} ms; dispatch {1:F3} ms; 256 main-thread polls {2:F3} ms total, worst {3:F3} ms; cache miss/hit/fingerprint/corruption/failures verified.",
                done.ElapsedMs, dispatch.Elapsed.TotalMilliseconds, polls.Elapsed.TotalMilliseconds, worstMs);
        }
    }
    static void FontChecks()
    {
        FontScanChecks();
        FontEngine.Reset();
        var font = new TMP_FontAsset();
        var writer = new NgoFontWarmup(font);
        Check(FontEngine.Resets == 1 && font.atlasTexture.width == 4096 && font.atlasTexture.height == 4096, "zero-size TMP atlas resized and reset");
        writer.BeginBatch();
        Check(writer.Add('A') && writer.Add('A') && writer.Add('B'), "existing characters and shared glyph aliases supported");
        Check(FontEngine.Calls == 1 && font.glyphTable.Count == 1 && font.characterTable.Count == 2, "one real glyph for two characters, no duplicate inserts");
        Check(!writer.Add(0x2603), "missing source glyph retains fallback");
        FontEngine.FailIndex = 'C'; Check(!writer.Add('C') && writer.Add('D'), "atlas-full false result does not stop other glyphs");
        writer.Flush();
        Check(font.IndexedGlyphs == 2 && font.atlasTexture.Applies == 1 && font.atlasTexture.isReadable, "TMP index rebuild and one readable upload per batch");
        writer.Flush(); Check(font.atlasTexture.Applies == 1, "empty flush does not upload");
        FontEngine.NullGlyph = true; bool threw = false;
        try { writer.Add('E'); } catch (InvalidOperationException) { threw = true; }
        Check(threw && !font.characterLookupTable.ContainsKey('E') && font.glyphTable.Count == 2, "success without a glyph is rejected without poisoning TMP tables");
        FontEngine.NullGlyph = false; FontEngine.FaceError = FontEngineError.InvalidFont; threw = false;
        try { writer.BeginBatch(); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "source font errors are surfaced to the safe coroutine stop");

        FontEngine.Reset(); Mod.Lines.Clear(); font = new TMP_FontAsset();
        string text = ""; for (int i = 0; i < 70; i++) text += (char)('!' + i);
        var draw = Draw(font, text);
        PlayStart(); Check(draw.MoveNext() && FontEngine.Resets == 0 && FontEngine.Calls == 0, "song at coroutine start prevents resize, insertion and upload");
        PlayEnd(); Check(draw.MoveNext() && font.characterTable.Count == 32 && font.atlasTexture.Applies == 1, "first boot batch yields after 32 characters");
        PlayStart(); int calls = FontEngine.Calls, uploads = font.atlasTexture.Applies;
        Check(draw.MoveNext() && draw.MoveNext() && FontEngine.Calls == calls && font.atlasTexture.Applies == uploads, "mid-warmup song pauses atlas work");
        PlayEnd(); Finish(draw);
        Check(WarmField<string>("_warmStatus") == "complete" && WarmField<int>("_processed") == 70, "warmup resumes after release and completes");
        Check(WarmField<int>("_warmGlyphs") == font.glyphTable.Count && WarmField<int>("_warmCharacters") == font.characterTable.Count && font.glyphTable.Count == 69,
            "reported glyph/character counts use actual tables, including aliases");
        Check(Mod.Lines.Exists(x => x.Contains("boot warmup complete; table growth 69 glyphs, 70 characters")), "real boot counts logged");

        FontEngine.Reset(); font = new TMP_FontAsset(); FontEngine.ThrowIndex = 'D'; Mod.Lines.Clear();
        Finish(Draw(font, "ACDE"));
        Check(WarmField<string>("_warmStatus") == "stopped" && WarmField<int>("_processed") == 2 && font.glyphTable.Count == 2 && font.atlasTexture.Applies == 1,
            "partial native failure is harmless, uploads prior glyphs and records exact partial progress");
        Check(Mod.Lines.Exists(x => x.Contains("boot warmup stopped; table growth 2 glyphs")), "partial failure logs real count");
        FontEngine.Reset(); font = new TMP_FontAsset(); font.atlasTexture.isReadable = false; Mod.Lines.Clear();
        Finish(Draw(font, "A"));
        Check(WarmField<string>("_warmStatus") == "skipped" && FontEngine.Calls == 0 && FontEngine.Resets == 0, "unsupported atlas cleanly skips without an insertion");
        Console.WriteLine("Font adapter/coroutine: guarded atlas initialization, aliases, missing/full glyphs, exact counts, song pause/resume and failures verified; native rasterization not run.");
    }
}
