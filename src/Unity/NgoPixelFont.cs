using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PartnerLive2D.Unity
{
    // Plain .NET only: even metadata/cache reads can stall behind antivirus or a slow disk. Publish one immutable
    // result after all I/O and sorting; the Unity coroutine only polls the volatile reference and never waits.
    sealed class NgoFontCharacterScan
    {
        const string CacheVersion = "NGO pixel font characters 1";
        internal sealed class Result
        {
            public readonly string Characters, Warning;
            public readonly int Songs;
            public readonly long ElapsedMs;
            public readonly bool CacheUsed;
            public Result(string characters, int songs, long elapsedMs, bool cacheUsed, string warning)
            { Characters = characters; Songs = songs; ElapsedMs = elapsedMs; CacheUsed = cacheUsed; Warning = warning; }
        }
        sealed class Cache
        {
            public string Root, Characters;
            public int Songs;
            public long Newest;
        }
        volatile Result _result;
        public Result Completed => _result;

        public NgoFontCharacterScan(string root, string cachePath, System.Func<string, string> readMusic = null)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                _result = Collect(root, cachePath, readMusic ?? File.ReadAllText));
        }

        static Result Collect(string root, string cachePath, System.Func<string, string> readMusic)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var set = new HashSet<char>();
            for (char c = ' '; c <= '~'; c++) set.Add(c);
            for (char c = '\u3000'; c <= '\u30ff'; c++) set.Add(c);
            for (char c = '\uff01'; c <= '\uff9f'; c++) set.Add(c);
            int songs = 0;
            string warning = null;
            try
            {
                root = Path.GetFullPath(root);
                var cached = ReadCache(cachePath);   // read first, then validate against the current folder fingerprint
                var files = new List<string>();
                long newest = 0;
                // Only StreamingAssets\<option>\music\<id>\Music.xml, not movies/sounds or the rest of the tree.
                foreach (var option in Directory.GetDirectories(root))
                {
                    var music = Path.Combine(option, "music");
                    if (!Directory.Exists(music)) continue;
                    foreach (var dir in Directory.GetDirectories(music))
                    {
                        var file = new FileInfo(Path.Combine(dir, "Music.xml"));
                        if (!file.Exists) continue;
                        files.Add(file.FullName);
                        songs++;
                        newest = System.Math.Max(newest, file.LastWriteTimeUtc.Ticks);
                    }
                }
                if (cached != null && string.Equals(cached.Root, root, System.StringComparison.OrdinalIgnoreCase) &&
                    cached.Songs == songs && cached.Newest == newest)
                    return new Result(cached.Characters, songs, sw.ElapsedMilliseconds, true, null);
                foreach (var file in files)
                    foreach (char c in readMusic(file))
                        if (c > '~' && !char.IsControl(c) && !char.IsSurrogate(c)) set.Add(c);
                var characters = Sorted(set);
                try { WriteCache(cachePath, root, songs, newest, characters); }
                catch (System.Exception e) { warning = "character cache not written: " + e.Message; }
                return new Result(characters, songs, sw.ElapsedMilliseconds, false, warning);
            }
            catch (System.Exception e) { warning = "song names not read: " + e.Message; }
            // Partial scans still warm the base/collected set, but never become a valid cache entry.
            return new Result(Sorted(set), songs, sw.ElapsedMilliseconds, false, warning);
        }

        static string Sorted(HashSet<char> set)
        {
            var chars = new char[set.Count]; set.CopyTo(chars);
            System.Array.Sort(chars);   // stable atlas packing across runs makes fill comparisons meaningful
            return new string(chars);
        }

        static byte[] Digest(string chars)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(chars));
        }

        static Cache ReadCache(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8))
                {
                    if (stream.Length > 1024 * 1024 || reader.ReadString() != CacheVersion) return null;
                    var cache = new Cache { Root = reader.ReadString(), Songs = reader.ReadInt32(),
                        Newest = reader.ReadInt64(), Characters = reader.ReadString() };
                    if (cache.Songs < 0 || cache.Newest < 0 || cache.Characters.Length == 0 || cache.Characters.Length > 65536) return null;
                    for (int i = 0; i < cache.Characters.Length; i++)
                        if (char.IsControl(cache.Characters[i]) || char.IsSurrogate(cache.Characters[i]) ||
                            (i > 0 && cache.Characters[i - 1] >= cache.Characters[i])) return null;
                    foreach (byte b in Digest(cache.Characters)) if (reader.ReadByte() != b) return null;
                    return stream.Position == stream.Length ? cache : null;
                }
            }
            catch (System.Exception) { return null; }   // missing, old or corrupt cache: rescan on this worker
        }

        static void WriteCache(string path, string root, int songs, long newest, string characters)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp))
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write(CacheVersion); writer.Write(root); writer.Write(songs); writer.Write(newest);
                    writer.Write(characters); writer.Write(Digest(characters));
                }
                if (File.Exists(path))
                {
                    try { File.Replace(temp, path, null); }
                    catch (System.UnauthorizedAccessException) { File.Copy(temp, path, true); }
                    catch (System.PlatformNotSupportedException) { File.Copy(temp, path, true); }
                    // Restricted ACLs/Mono may deny atomic replacement. A torn fallback copy fails the checksum next boot.
                }
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    // NGO skin: every TextMeshPro UI text is drawn in a pixel font (PixelMplus12, Japanese included). Unity 2018 cannot
    // load a TTF from disk, so the font bytes are written into the game's only Font object, LiberationSans
    // (tools/ngo_font_data.py), and a dynamic SDF font asset is made from it here. Each text's own material is cloned
    // onto the new atlas, so outlines, shadows and colours stay; glyphs the pixel font lacks fall back to the text's
    // original font. Enabled by LocalAssets/PartnerLive2D/pixel_font.txt.
    // a glyph added to a dynamic font while the game runs (rendered and uploaded on the spot): timed for PlayStats
    [HarmonyPatch]
    static class NgoGlyphTiming
    {
        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(TMP_FontAsset), "TryAddCharacterInternal", new[] { typeof(uint), typeof(TMP_Character).MakeByRefType() });
        static void Prefix(out long __state) { __state = PlayStats.Now(); }
        static void Postfix(long __state) { PlayStats.Spent(PlayStats.Part.Glyph, __state); }
    }

    [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
    static class NgoPixelFont
    {
        const int Sampling = 48;     // 4 x the font's 12 px grid
        const int Padding = 5;
        static TMP_FontAsset _pixel;
        static bool _failed;
        static int _lastTry = -1, _tries;
        static int _requested, _missing, _songs;
        static long _warmMs;
        static string _warmStatus = "pending";
        static int _warmCharacters, _warmGlyphs, _processed;
        public static object Info()
        {
            long occupied = 0;
            if (_pixel != null && _pixel.glyphTable != null)
                foreach (var g in _pixel.glyphTable) occupied += (long)(g.glyphRect.width + 2 * Padding) * (g.glyphRect.height + 2 * Padding);
            return new Dictionary<string, object> { { "ready", _pixel != null }, { "requested", _requested },
                { "missingOrAtlasFull", _missing }, { "songs", _songs }, { "warmMs", _warmMs }, { "materials", Mats.Count },
                { "warmStatus", _warmStatus }, { "warmCharacters", _warmCharacters }, { "warmGlyphs", _warmGlyphs }, { "warmProcessed", _processed },
                { "glyphs", _pixel != null ? _pixel.glyphTable.Count : 0 },
                { "atlasWidth", _pixel != null ? _pixel.atlasWidth : 0 }, { "atlasHeight", _pixel != null ? _pixel.atlasHeight : 0 },
                { "paddedGlyphAreaEstimate", occupied } };
        }
        static readonly Dictionary<Material, Material> Mats = new Dictionary<Material, Material>();
        public static int MaterialCount => Mats.Count;

        static bool Prepare() => File.Exists(Path.Combine(Mod.AssetRoot, "pixel_font.txt"));

        static TMP_FontAsset Pixel()
        {
            if (_pixel != null || _failed) return _pixel;
            if (Time.frameCount == _lastTry) return null;   // the font may not be loaded yet: try again next frame
            _lastTry = Time.frameCount;
            // the Font object is only reachable through what references it: load TMP's LiberationSans SDF first
            var sdf = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            Font src = sdf != null ? sdf.sourceFontFile : null;
            if (src == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                    if (f.name == "LiberationSans") { src = f; break; }
            if (src == null)
            {
                if (++_tries == 600) Mod.Warn("pixel font: LiberationSans font object not found");
                return null;
            }
            try { _pixel = TMP_FontAsset.CreateFontAsset(src, Sampling, Padding, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic); }
            catch (System.Exception e) { _failed = true; Mod.Warn("pixel font: CreateFontAsset skipped: " + e); return null; }
            if (_pixel == null) { _failed = true; Mod.Warn("pixel font: CreateFontAsset failed"); return null; }
            _pixel.name = "NGO Pixel SDF";
            Object.DontDestroyOnLoad(_pixel);
            if (_pixel.fallbackFontAssetTable == null) _pixel.fallbackFontAssetTable = new List<TMP_FontAsset>();
            Mod.Msg("pixel font: dynamic asset ready");
            Prewarm(_pixel);
            foreach (var t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())   // texts enabled before it was ready
                if (t.gameObject.scene.IsValid()) Swap(t);
            return _pixel;
        }

        // Every glyph a text is likely to need, drawn into the atlas in boot batches (owner 2026-11-02 night: "理应来说应该
        // 一点卡顿都没有的"): a glyph first needed in play is rendered and the 4096 x 4096 atlas uploaded on the spot, a
        // hitch of its own. ASCII, CJK punctuation, kana, full-width forms, and every character of every Music.xml (titles,
        // artists, genres) of the installed songs.
        static void Prewarm(TMP_FontAsset font)
        {
            // Capture Unity paths here; no Unity API or logging is called by the plain-.NET worker.
            var scan = new NgoFontCharacterScan(Application.streamingAssetsPath, Path.Combine(Mod.AssetRoot, "pixel_font_chars.cache"));
            MelonLoader.MelonCoroutines.Start(Draw(font, () => scan.Completed));
        }

        // TextCore does the insertion; TMP 1.4's wrappers are unsafe for this prewarm. Count the actual tables, since
        // several characters can share a glyph and texts enabled during boot may already have inserted some of them.
        static System.Collections.IEnumerator Draw(TMP_FontAsset font, System.Func<NgoFontCharacterScan.Result> completed)
        {
            // Scanning must cost no more than a few milliseconds on the main thread, even if disk I/O takes minutes:
            // just poll and yield. Never wait/join here, or enumerate files, read the cache/XML, collect or sort chars.
            NgoFontCharacterScan.Result scan;
            while ((scan = completed()) == null) yield return null;
            string chars = scan.Characters;
            _requested = chars.Length; _songs = scan.Songs;
            if (scan.Warning != null) Mod.Warn("pixel font: " + scan.Warning);
            Mod.Msg(string.Format("pixel font: {0} characters from {1} songs and the base set read in {2} ms; cache={3}; drawing them in batches",
                chars.Length, scan.Songs, scan.ElapsedMs, scan.CacheUsed ? "hit" : "miss"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (PlayStats.InSong) yield return null;
            int beforeCharacters = font.characterTable.Count, beforeGlyphs = font.glyphTable.Count;
            NgoFontWarmup warmup;
            try { warmup = new NgoFontWarmup(font); }
            catch (System.Exception e) { WarmResult(font, beforeCharacters, beforeGlyphs, 0, sw.ElapsedMilliseconds, "skipped", e); yield break; }
            _warmStatus = "drawing";
            int lacking = 0;
            for (int i = 0; i < chars.Length; i += 32)
            {
                // Slow boot/prewarming may overlap a fast entry into play. Never schedule atlas work in a song.
                while (PlayStats.InSong) yield return null;
                try
                {
                    warmup.BeginBatch();
                    for (int k = i; k < System.Math.Min(i + 32, chars.Length); k++)
                    {
                        if (!warmup.Add(chars[k])) lacking++;
                        _processed++;
                    }
                    warmup.Flush();
                }
                catch (System.Exception e)
                {
                    // Finish any earlier successful insertions in this batch, then leave later text to its fallbacks.
                    try { warmup.Flush(); } catch (System.Exception upload) { Mod.Warn("pixel font: partial atlas upload failed: " + upload.Message); }
                    WarmResult(font, beforeCharacters, beforeGlyphs, lacking, sw.ElapsedMilliseconds, "stopped", e);
                    yield break;
                }
                yield return null;
            }
            WarmResult(font, beforeCharacters, beforeGlyphs, lacking, sw.ElapsedMilliseconds, "complete", null);
        }

        static void WarmResult(TMP_FontAsset font, int beforeCharacters, int beforeGlyphs, int lacking, long elapsed, string status, System.Exception error)
        {
            _warmCharacters = font.characterTable.Count - beforeCharacters;
            _warmGlyphs = font.glyphTable.Count - beforeGlyphs;
            _missing = lacking; _warmMs = elapsed; _warmStatus = status;
            var message = string.Format("pixel font: boot warmup {0}; table growth {1} glyphs, {2} characters; atlas now {3} glyphs; " +
                "processed {4}/{5}, missing or atlas full {6}, {7} ms", status, _warmGlyphs, _warmCharacters, font.glyphTable.Count,
                _processed, _requested, lacking, elapsed);
            if (error == null) Mod.Msg(message); else Mod.Warn(message + "; " + error);
        }

        static void Prefix(TextMeshProUGUI __instance)
        {
            long t0 = PlayStats.Now();
            try { Swap(__instance); }
            finally { PlayStats.Spent(PlayStats.Part.Font, t0); }
        }

        static void Swap(TextMeshProUGUI __instance)
        {
            var orig = __instance.font;
            if (orig == null) return;
            var px = Pixel();
            if (px == null || orig == px) return;
            if (!px.fallbackFontAssetTable.Contains(orig)) px.fallbackFontAssetTable.Add(orig);
            var om = __instance.fontSharedMaterial;
            Material m = null;
            if (om != null && !Mats.TryGetValue(om, out m))
            {
                m = new Material(om) { name = om.name + " (NGO pixel)" };   // keeps shader, outline, underlay, colours
                m.SetTexture(ShaderUtilities.ID_MainTex, px.atlasTexture);
                m.SetFloat(ShaderUtilities.ID_TextureWidth, px.atlasWidth);
                m.SetFloat(ShaderUtilities.ID_TextureHeight, px.atlasHeight);
                m.SetFloat(ShaderUtilities.ID_GradientScale, Padding + 1);
                m.SetFloat(ShaderUtilities.ID_WeightNormal, px.normalStyle);
                m.SetFloat(ShaderUtilities.ID_WeightBold, px.boldStyle);
                Object.DontDestroyOnLoad(m);
                Mats[om] = m;
            }
            __instance.font = px;
            if (m != null) __instance.fontSharedMaterial = m;
        }
    }
}
