using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using HarmonyLib;
using MAI2.Util;
using Manager;
using Process;
using UnityEngine;

namespace NgoPlatesMod
{
    // Standalone copy for sharing (owner 2026-11-07; the frames live in Package\LocalAssets\NgoPlates): the four animated NGO nameplates (雨極 / 雨将 / 雨神 / 雨舞舞,
    // plate ids 990001-990004, art from maimai-nameplate\final, packed by tools\ngo_plate_pack.py). Ame in menus and music
    // select; when a song starts (she goes on stream) she transforms into KAngel, and back to Ame on the next music select.
    //   <folder>\<plate id>\{ame,kangel,to_kangel,to_ame}.bc7: raw BC7 frames of 720x116 (rows bottom-up), 12 fps. The
    //   transforms start from loop frame 0 and the target loop continues from frame (transform length).
    // Every screen gets its plate through AssetManager.GetPlateTexture2D (AquaMai's local-image loader patches the same
    // method). For our ids this hands out one display texture per plate, held here for the whole run, and a GPU copy
    // (Graphics.CopyTexture, same format) puts the current frame into it 12 times a second. The frames are read on a
    // worker thread and turned into textures a few per frame, so no PNG is decoded and nothing heavy runs in a song.
    static class NgoPlates
    {
        const int W = 720, H = 116, FrameBytes = W * H;   // BC7: 16 bytes per 4x4 block = 1 byte a pixel
        const float Fps = 12f;
        const int WaitForLoopStart = 24;                   // a transform waits up to 2 s for loop frame 0 (its seam)
        static readonly Regex PlateFile = new Regex(@"UI_Plate_(\d+)\.png", RegexOptions.IgnoreCase);
        static readonly Dictionary<int, Plate> Plates = new Dictionary<int, Plate>();
        static bool _kangel, _broken;
        static float _next;

        // Harmony may patch before Initialize: the hooks only need the frames folder
        internal static string Folder => Path.Combine(Path.Combine(Path.GetDirectoryName(Application.dataPath), "LocalAssets"), "NgoPlates");
        static bool Wanted() => Directory.Exists(Folder);

        public static void Initialize()
        {
            var dir = Folder;
            if (!Directory.Exists(dir)) { MelonLoader.MelonLogger.Warning("ngo plates: folder missing: " + dir); return; }
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC7) || SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None)
            { MelonLoader.MelonLogger.Warning("ngo plates: this GPU cannot copy BC7 textures, the plates stay still"); return; }
            foreach (var sub in Directory.GetDirectories(dir))
            {
                int id;
                if (int.TryParse(Path.GetFileName(sub), out id) && File.Exists(Path.Combine(sub, "ame.bc7"))) Plates[id] = new Plate(id, sub);
            }
            MelonLoader.MelonLogger.Msg("ngo plates: " + Plates.Count + " animated plates (" + string.Join(", ", Array.ConvertAll(new List<int>(Plates.Keys).ToArray(), i => i.ToString())) + ")");
        }

        sealed class Clip
        {
            public byte[] Bytes; public int Count; public Texture2D[] Frames; public int Made;
            public bool Done => Frames != null && Made == Count;
        }

        sealed class Plate
        {
            public readonly int Id; public readonly string Dir;
            public Clip Ame, KAngel, ToKAngel, ToAme;
            public Texture2D Shown;          // the one texture every screen shows for this plate
            volatile bool _read; volatile string _readError; bool _loading;
            public bool KAngelForm; public Clip Loop, Transform; public int Frame;
            public Plate(int id, string dir) { Id = id; Dir = dir; }
            Clip[] Clips => new[] { Ame, KAngel, ToKAngel, ToAme };
            public bool Ready => _read && Ame.Done && KAngel.Done && ToKAngel.Done && ToAme.Done;

            public void StartLoading()
            {
                if (_loading) return;
                _loading = true;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Ame = Read("ame"); KAngel = Read("kangel"); ToKAngel = Read("to_kangel"); ToAme = Read("to_ame"); _read = true; }
                    catch (Exception e) { _readError = e.Message; }
                });
            }
            Clip Read(string name)
            {
                var bytes = File.ReadAllBytes(Path.Combine(Dir, name + ".bc7"));
                if (bytes.Length == 0 || bytes.Length % FrameBytes != 0) throw new InvalidDataException(name + ".bc7 is not whole 720x116 BC7 frames");
                return new Clip { Bytes = bytes, Count = bytes.Length / FrameBytes };
            }
            // main thread: up to `budget` frames become GPU textures; the bytes go once a clip is done
            public void MakeSome(int budget)
            {
                if (_readError != null) { MelonLoader.MelonLogger.Warning("ngo plate " + Id + ": " + _readError + "; it stays still"); _readError = null; _loading = true; return; }
                if (!_read) return;
                foreach (var c in Clips)
                {
                    if (c.Done) continue;
                    if (c.Frames == null) c.Frames = new Texture2D[c.Count];
                    var pin = GCHandle.Alloc(c.Bytes, GCHandleType.Pinned);
                    try
                    {
                        while (budget-- > 0 && c.Made < c.Count)
                        {
                            c.Frames[c.Made] = Texture(new IntPtr(pin.AddrOfPinnedObject().ToInt64() + (long)c.Made * FrameBytes), "ngo_plate_" + Id + "_" + c.Made);
                            c.Made++;
                        }
                    }
                    finally { pin.Free(); }
                    if (c.Done) c.Bytes = null;
                    if (budget <= 0) return;
                }
            }
            public void Begin(bool kangel) { KAngelForm = kangel; Loop = null; Transform = null; Frame = 0; }
            // the frame to show this tick, then step: loops wrap, a transform hands over to the other loop at its length
            public Texture2D Step(bool kangel)
            {
                if (Loop == null) Loop = KAngelForm ? KAngel : Ame;   // the clips exist once Ready
                if (Transform == null && kangel != KAngelForm && (Frame == 0 || Loop.Count - Frame > WaitForLoopStart))
                { Transform = kangel ? ToKAngel : ToAme; KAngelForm = kangel; Frame = 0; }
                if (Transform != null)
                {
                    var t = Transform.Frames[Frame++];
                    if (Frame >= Transform.Count) { Loop = KAngelForm ? KAngel : Ame; Frame = Transform.Count % Loop.Count; Transform = null; }
                    return t;
                }
                var f = Loop.Frames[Frame];
                Frame = (Frame + 1) % Loop.Count;
                return f;
            }
        }

        static Texture2D Texture(IntPtr data, string name)
        {
            var t = new Texture2D(W, H, TextureFormat.BC7, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontUnloadUnusedAsset };
            t.LoadRawTextureData(data, FrameBytes);
            t.Apply(false, true);   // GPU only from here
            return t;
        }

        // the still a screen shows until the frames are on the GPU: frame 0 of the current form, read straight from disk
        static Texture2D FirstFrame(Plate p)
        {
            var bytes = new byte[FrameBytes];
            using (var f = File.OpenRead(Path.Combine(p.Dir, (_kangel ? "kangel" : "ame") + ".bc7")))
                if (f.Read(bytes, 0, FrameBytes) != FrameBytes) throw new InvalidDataException("short plate file");
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return Texture(pin.AddrOfPinnedObject(), "ngo_plate_" + p.Id); }
            finally { pin.Free(); }
        }

        // after AquaMai's loader (Priority.Last): our plates get the animated display texture
        [HarmonyPatch(typeof(AssetManager), "GetPlateTexture2D", new[] { typeof(string) })]
        static class PlateTexture
        {
            static bool Prepare() => Wanted();
            [HarmonyPriority(Priority.Last)]
            static void Postfix(string filename, ref Texture2D __result)
            {
                if (_broken || filename == null) return;
                var m = PlateFile.Match(filename);
                Plate p; int id;
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out id) || !Plates.TryGetValue(id, out p)) return;
                try
                {
                    if (p.Shown == null) { p.Shown = FirstFrame(p); p.Begin(_kangel); }
                    p.StartLoading();
                    __result = p.Shown;
                }
                catch (Exception e) { _broken = true; MelonLoader.MelonLogger.Error("ngo plates off, the stills stay: " + e); }
            }
        }

        // the form follows the game: KAngel from the song's start, Ame again from the next music select or a new entry
        [HarmonyPatch(typeof(ProcessManager), "AddProcess")]
        static class Form
        {
            static bool Prepare() => Wanted();
            static void Postfix(ProcessBase process)
            {
                string name = process == null ? "" : process.GetType().Name;
                if (name == "GameProcess") _kangel = true;
                else if (name == "MusicSelectProcess" || name == "EntryProcess" || name == "AdvertiseProcess") _kangel = false;
            }
        }

        public static void Poll()
        {
            if (_broken || Plates.Count == 0) return;
            try
            {
                bool tick = Time.unscaledTime >= _next;
                if (tick) _next = Math.Max(_next + 1f / Fps, Time.unscaledTime - 1f / Fps);
                foreach (var p in Plates.Values)
                {
                    if (p.Shown == null) continue;
                    if (!p.Ready) { p.MakeSome(_kangel ? 2 : 8); continue; }
                    if (tick) Graphics.CopyTexture(p.Step(_kangel), p.Shown);
                }
            }
            catch (Exception e) { _broken = true; MelonLoader.MelonLogger.Error("ngo plates stopped, the current frame stays: " + e); }
        }
    }
}
