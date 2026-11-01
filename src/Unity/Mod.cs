using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using PartnerLive2D.Core;
using UnityEngine;

[assembly: MelonInfo(typeof(PartnerLive2D.Unity.Mod), "PartnerLive2D", PartnerLive2D.Unity.Mod.Version, "local")]

namespace PartnerLive2D.Unity
{
    public sealed class Mod : MelonMod
    {
        public const string Version = "0.2.0";

        static MelonLogger.Instance _log;
        static readonly List<PartnerConfig> _configs = new List<PartnerConfig>();
        static readonly Dictionary<string, Texture2D> ImageCache = new Dictionary<string, Texture2D>();
        static string _root;
        static ControlServer _server;

        public static bool Ready { get; private set; }
        public static bool Live2DAvailable { get; private set; }
        public static IList<PartnerConfig> Configs => _configs;

        public override void OnInitializeMelon()
        {
            _log = LoggerInstance;
            var gameDir = Path.GetDirectoryName(Application.dataPath);   // ...\Package
            // The Cubism core only draws Live2D models. It used to gate the whole mod: on a cabinet without it the mod
            // returned here, yet its Harmony hooks were already patched in and ran with default settings (select view
            // off, stock select under the NGO textures). Now only Live2D-model partners depend on it.
            if (!File.Exists(Path.Combine(gameDir, "Live2DCubismCore.dll")))
                Warn("Live2DCubismCore.dll is missing next to Sinmai.exe: Live2D-model partners are off; sprite and stock partners, the select view and the NGO screens still run.");
            else
            {
                try
                {
                    uint v = CubismModel.CoreVersion;
                    Live2DAvailable = true;
                    Msg(string.Format("Cubism Core {0}.{1}.{2}", v >> 24, (v >> 16) & 0xFF, v & 0xFFFF));
                }
                catch (Exception e) { Warn("Cubism Core could not be loaded; Live2D-model partners are off. " + e.Message); }
            }

            _root = Path.Combine(gameDir, Path.Combine("LocalAssets", "PartnerLive2D"));
            Directory.CreateDirectory(_root);
            Rescan();
            Ready = true;

            // settings.ini (optional): http = true|false, port = 8791
            var settings = ReadSettings(Path.Combine(_root, "settings.ini"));
            MusicSelectSession.Initialize(_root);
            string themeFolder;
            settings.TryGetValue("theme", out themeFolder);
            ThemeOverride.Initialize(_root, themeFolder);
            NgoTint.Load(_root);
            string ngoBackground;
            settings.TryGetValue("ngoBackground", out ngoBackground);
            NgoBackground.Initialize(_root, ngoBackground);
            string inputLog;
            TestHarness.InputLog = settings.TryGetValue("inputLog", out inputLog) && inputLog == "true";
            string frameLog;
            TestHarness.FrameLog = settings.TryGetValue("frameLog", out frameLog) && frameLog == "true";
            string cleanupGap; float gap;
            if (settings.TryGetValue("cleanupMinGap", out cleanupGap) && float.TryParse(cleanupGap, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out gap))
                NgoCleanupTiming.MinGap = gap;
            string charaLevel, equipPartner; uint lv; int pid;
            if (settings.TryGetValue("charaLevel", out charaLevel) && uint.TryParse(charaLevel, out lv)) AccountTweaks.CharaLevel = lv;
            if (settings.TryGetValue("equipPartner", out equipPartner) && int.TryParse(equipPartner, out pid)) AccountTweaks.EquipPartner = pid;
            string freedomEntry;
            FreedomEntry.Enabled = settings.TryGetValue("freedomEntry", out freedomEntry) && freedomEntry == "true";
            string freedomInfinity;
            settings.TryGetValue("freedomInfinity", out freedomInfinity);
            FreedomInfinity.Enabled = freedomInfinity == "true" || freedomInfinity == "hidden";
            FreedomInfinity.MarkHidden = freedomInfinity == "hidden";
            string testInput;
            if (settings.TryGetValue("testInput", out testInput) && testInput == "true")
            {
                try { TestHarness.InstallVirtualInput(); } catch (Exception e) { Error("testInput hook failed: " + e); }
                // tests only: the owner's card plays as a never-played card (local copy; uploads keep the real values)
                string firstPlay;
                TestFirstPlay.Enabled = settings.TryGetValue("testFirstPlay", out firstPlay) && firstPlay == "true";
            }
            string httpOn, portText;
            bool http = !settings.TryGetValue("http", out httpOn) || httpOn != "false";
            int port = settings.TryGetValue("port", out portText) ? int.Parse(portText, CultureInfo.InvariantCulture) : 8791;
            if (http)
            {
                try { _server = new ControlServer(port); _server.Start(); }
                catch (Exception e) { Error("control server could not start on port " + port + ": " + e.Message); }
            }
        }

        public override void OnApplicationQuit()
        {
            if (_server != null) _server.Stop();
            ThemeOverride.SetEnabled(false);
            MusicSelectPngCache.Clear();
        }

        bool _overlayDone;

        public override void OnUpdate()
        {
            MainThread.Drain();
            TestHarness.TrackGameWindow();
            TestHarness.PollInput();
            TestHarness.PollFrame();
            PlayStats.Poll();
            if (!Ready || _overlayDone || Time.realtimeSinceStartup < 20f) return;
            _overlayDone = true;
            foreach (var c in _configs)
            {
                if (!c.DebugOverlay) continue;
                Msg("debug overlay for " + c.Name);
                try { PartnerView.AttachOverlay(c, true); } catch (Exception e) { Error("overlay: " + e); }
            }
        }

        // (re)read every partner.ini; live views follow their partner, views of removed partners close
        public static void Rescan()
        {
            var fresh = PartnerConfig.LoadAll(_root, Warn);
            // without the Cubism core a model-only partner cannot be drawn: leave its screens to the game's own partner
            if (!Live2DAvailable)
                fresh.RemoveAll(c =>
                {
                    if (!string.IsNullOrEmpty(c.SpritePath)) return false;
                    Warn(string.Format("partner '{0}' is a Live2D model and the Cubism core is missing: the game's own partner shows instead", c.Name));
                    return true;
                });
            foreach (var v in PartnerView.All.ToArray())
            {
                if (v == null) continue;
                var match = fresh.Find(c => c.Name == v.Config.Name);
                if (match == null) { v.Close(); continue; }
                v.SetConfig(match);
                v.OnConfigChanged(true);
            }
            _configs.Clear();
            _configs.AddRange(fresh);
            foreach (var c in _configs)
                Msg(string.Format("partner '{0}': naviCharaId {1}, partnerId {2}, model {3}", c.Name, c.NaviCharaId, c.PartnerId, Path.GetFileName(c.ModelPath)));
            if (_configs.Count == 0) Msg("no partner.ini found under " + _root);
            ImageCache.Clear();
        }

        public static PartnerConfig ReloadPartner(PartnerConfig old)
        {
            var c = PartnerConfig.Load(old.Folder);
            int i = _configs.IndexOf(old);
            if (i >= 0) _configs[i] = c; else _configs.Add(c);
            foreach (var v in PartnerView.All.ToArray())
                if (v != null && v.Config == old) { v.SetConfig(c); v.OnConfigChanged(true); }
            ImageCache.Clear();
            return c;
        }

        internal static Dictionary<string, string> ReadSettings(string path)
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(path)) return d;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        // LocalAssets\PartnerLive2D, usable before OnInitializeMelon (Harmony asks the patch classes' Prepare first)
        internal static string AssetRoot => Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("LocalAssets", "PartnerLive2D"));

        public static PartnerConfig ForNavi(int naviCharaId)
        {
            foreach (var c in _configs) if (c.NaviCharaId == naviCharaId) return c;
            return null;
        }

        public static PartnerConfig ForPartner(int partnerId)
        {
            foreach (var c in _configs) if (c.PartnerId == partnerId) return c;
            return null;
        }

        public static Texture2D LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            Texture2D tex;
            if (ImageCache.TryGetValue(path, out tex) && tex != null) return tex;
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            tex.wrapMode = TextureWrapMode.Clamp;
            // e.g. "ngo_pair/icon.png", so UI dumps (/api/test/ui) can tell our pictures from the game's
            tex.name = Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path);
            UnityEngine.Object.DontDestroyOnLoad(tex);
            ImageCache[path] = tex;
            return tex;
        }

        public static void Msg(string s) { if (_log != null) _log.Msg(s); }
        public static void Warn(string s) { if (_log != null) _log.Warning(s); }
        public static void Error(string s) { if (_log != null) _log.Error(s); }
    }
}
