using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using MelonLoader.NativeUtils;
using PartnerLive2D.Framework;
using Process;

namespace PartnerLive2D.Unity
{
    // Test-only helpers so automated runs can play with the window off screen.
    // - process log: every ProcessManager.AddProcess is recorded (always on, cheap).
    // - virtual keys: with settings.ini "testInput = true", user32!GetAsyncKeyState is hooked inside
    //   the game and answers ONLY from keys pressed through /api/test/key; the real keyboard is ignored,
    //   so nothing typed elsewhere reaches the game and nothing the tests press reaches other programs.
    static class TestHarness
    {
        static readonly object Gate = new object();
        static readonly List<Dictionary<string, object>> Procs = new List<Dictionary<string, object>>();
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static readonly long[] DownUntil = new long[256];

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate short GetAsyncKeyStateFn(int vk);
        static GetAsyncKeyStateFn _detour;
        static NativeHook<GetAsyncKeyStateFn> _hook;

        public static bool InputHooked { get; private set; }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        public static void InstallVirtualInput()
        {
            var fn = GetProcAddress(GetModuleHandle("user32.dll"), "GetAsyncKeyState");
            if (fn == IntPtr.Zero) { Mod.Error("testInput: GetAsyncKeyState not found"); return; }
            _detour = VirtualKeyState;
            _hook = new NativeHook<GetAsyncKeyStateFn>(fn, Marshal.GetFunctionPointerForDelegate(_detour));
            _hook.Attach();
            // segatools / the card reader only read keys while the game window is in front; with the
            // window parked off screen, report the game's own window as the foreground one
            var fg = GetProcAddress(GetModuleHandle("user32.dll"), "GetForegroundWindow");
            _fgDetour = GameWindowAsForeground;
            _fgHook = new NativeHook<GetForegroundWindowFn>(fg, Marshal.GetFunctionPointerForDelegate(_fgDetour));
            _fgHook.Attach();
            InputHooked = true;
            Mod.Warn("testInput is ON: the real keyboard is ignored, keys come only from /api/test/key");
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate IntPtr GetForegroundWindowFn();
        static GetForegroundWindowFn _fgDetour;
        static NativeHook<GetForegroundWindowFn> _fgHook;
        static IntPtr _gameWindow;

        // called from Mod.OnUpdate until the main window exists
        public static void TrackGameWindow()
        {
            if (!InputHooked || _gameWindow != IntPtr.Zero) return;
            var h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (h != IntPtr.Zero) { _gameWindow = h; Mod.Msg("testInput: game window " + h); }
        }

        static IntPtr GameWindowAsForeground()
        {
            return _gameWindow != IntPtr.Zero ? _gameWindow : _fgHook.Trampoline();
        }

        static short VirtualKeyState(int vk)
        {
            return Interlocked.Read(ref DownUntil[vk & 0xFF]) > Clock.ElapsedMilliseconds ? unchecked((short)0x8000) : (short)0;
        }

        public static object Key(object body)
        {
            if (!InputHooked) throw new ApiError(409, "testInput is off (settings.ini testInput = true)");
            int vk = (int)Json.GetFloat(body, "vk", -1f);
            int ms = (int)Json.GetFloat(body, "ms", 120f);
            if (vk < 1 || vk > 254) throw new ApiError(400, "vk must be 1..254");
            if (ms < 10 || ms > 10000) throw new ApiError(400, "ms must be 10..10000");
            Interlocked.Exchange(ref DownUntil[vk], Clock.ElapsedMilliseconds + ms);
            return new Dictionary<string, object> { { "vk", vk }, { "ms", ms } };
        }

        // tests only (testInput on): one touch-down on a touch-panel area (A1..E8) for player 1, for screens that take only
        // touches (e.g. the collection's partner card -> partner room on B4/B5). Injected in GameInputLog.TriggerOn: the
        // InputManager.GetTouchPanelAreaDown wrappers are small enough to be inlined, so a hook there would be skipped.
        static int _touchArea = -1, _touchFrom = -1, _touchHit = -1;
        public static object Touch(object body)
        {
            // no testInput needed: the touch goes in through a Harmony hook, not the keyboard hook
            Manager.InputManager.TouchPanelArea area;
            try { area = (Manager.InputManager.TouchPanelArea)Enum.Parse(typeof(Manager.InputManager.TouchPanelArea), Json.GetString(body, "area") ?? "", true); }
            catch (Exception) { throw new ApiError(400, "area: A1..A8, B1..B8, C1, C2, D1..D8, E1..E8"); }
            _touchFrom = UnityEngine.Time.frameCount; _touchHit = -1; _touchArea = (int)area;
            return new Dictionary<string, object> { { "area", area.ToString() } };
        }

        // frame pacing for recordings (2026-11-02 night: with the laptop's display off the game waits for vertical blanks
        // that come half as often and runs at 30 frames a second; vsync 0 + 60 keeps it at 60)
        public static object FrameRate(object body)
        {
            var before = new Dictionary<string, object> { { "vsync", UnityEngine.QualitySettings.vSyncCount }, { "fps", UnityEngine.Application.targetFrameRate } };
            if (Json.Get(body, "vsync") != null) UnityEngine.QualitySettings.vSyncCount = Json.GetInt(body, "vsync");
            if (Json.Get(body, "fps") != null) UnityEngine.Application.targetFrameRate = Json.GetInt(body, "fps");
            return new Dictionary<string, object> { { "before", before }, { "vsync", UnityEngine.QualitySettings.vSyncCount }, { "fps", UnityEngine.Application.targetFrameRate } };
        }

        // what the partner room does after a present (CollectionProcess.InputPartnerPresentConfirmSequence), without giving
        // one: tests must not spend the owner's presents
        public static object RoomPresent()
        {
            int rooms = 0;
            foreach (var room in UnityEngine.Object.FindObjectsOfType<PartnerRoom>())
            {
                if (!room.isActiveAndEnabled) continue;
                room.PlayPartnerEmotionEffectOnceTime(); room.PlayPartnerFunLoopOnceTime(); rooms++;
            }
            return new Dictionary<string, object> { { "rooms", rooms } };
        }

        [HarmonyPatch]
        static class TouchInject
        {
            // GameInputLog is internal to the game assembly: look it up by name
            static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("IO.TouchPanel.GameInputLog"), "TriggerOn");
            static bool Prefix(uint side, Manager.InputManager.TouchPanelArea type, ref bool __result)
            {
                if (_touchArea < 0 || side != 0 || (int)type != _touchArea) return true;
                int frame = UnityEngine.Time.frameCount;
                // pending until the game first asks for this area (a screen may lock input for a while), then a
                // touch-down for every query of that one frame only; dropped if nobody asks within ~5 s
                if (_touchHit < 0)
                {
                    if (frame > _touchFrom + 300) { Mod.Msg("[test] touch " + type + " dropped: nothing asked for it"); _touchArea = -1; return true; }
                    _touchHit = frame; Mod.Msg("[test] touch " + type + " delivered, " + (frame - _touchFrom) + " frames after the request");
                }
                if (frame != _touchHit) { _touchArea = -1; return true; }
                __result = true;
                return false;
            }
        }

        // tests only (testInput on): the game's own autoplay (GameManager.AutoPlay), to reach the high-rank result effects.
        // AquaMai UX.DontRuinMyAccount keys off GameManager.IsAutoPlay(): the score update is skipped and the song's
        // record restored, so the account's scores stay as they were.
        public static object Autoplay(object body)
        {
            if (!InputHooked) throw new ApiError(409, "testInput is off (settings.ini testInput = true)");
            var name = Json.GetString(body, "mode") ?? "None";
            Manager.GameManager.AutoPlayMode mode;
            try { mode = (Manager.GameManager.AutoPlayMode)Enum.Parse(typeof(Manager.GameManager.AutoPlayMode), name, true); }
            catch (Exception) { throw new ApiError(400, "mode: None, Critical, Perfect, Great, Good, Miss, Random"); }
            AccessTools.Property(typeof(Manager.GameManager), "AutoPlay").SetValue(null, mode, null);
            return new Dictionary<string, object> { { "autoplay", Manager.GameManager.AutoPlay.ToString() } };
        }

        // tests only (testInput on): the result screen plays the clear-rank animation of this rank instead of the real
        // one (only the animation; the score, record and upload are untouched), to look at the high-rank effects
        public static Manager.MusicClearrankID? ForcedRank;
        public static object ForceRank(object body)
        {
            if (!InputHooked) throw new ApiError(409, "testInput is off (settings.ini testInput = true)");
            var name = Json.GetString(body, "rank");
            if (string.IsNullOrEmpty(name) || name == "None") ForcedRank = null;
            else
            {
                try { ForcedRank = (Manager.MusicClearrankID)Enum.Parse(typeof(Manager.MusicClearrankID), name, true); }
                catch (Exception) { throw new ApiError(400, "rank: Rank_D .. Rank_SSSP, or None"); }
            }
            return new Dictionary<string, object> { { "rank", ForcedRank?.ToString() ?? "None" } };
        }

        [HarmonyPatch(typeof(ResultTimelineManager), "SetData")]
        static class ForcedRankPatch
        {
            static void Prefix(ref Manager.MusicClearrankID clearRank, ref bool isClear)
            {
                if (ForcedRank == null) return;
                clearRank = ForcedRank.Value; isClear = true;
                Mod.Msg("test: result rank animation forced to " + clearRank);
            }
        }

        // settings.ini "inputLog = true": log every 1P/2P ring-button press/release the game itself sees
        public static bool InputLog;
        static readonly bool[,] LastPush = new bool[2, 9];

        // coin key (side button F3) on the attract screen: counted for the quick-login helper (auto-login.py)
        static int _quickLogin;
        static int _coinPoll;
        static long _lastCoins = -1;
        static string _lastScreen = "";

        public static object QuickLogin()
        {
            return new Dictionary<string, object> { { "count", Interlocked.CompareExchange(ref _quickLogin, 0, 0) } };
        }

        public static void PollInput()
        {
            string cur;
            lock (Gate) cur = Procs.Count > 0 ? (string)Procs[Procs.Count - 1]["name"] : "";
            // the coin key (side button F3) inserts a coin; on the attract / card screen that asks for a quick login.
            // Watch the coin bookkeeping total: AquaMai LockCredits pins the credit count, so that never moves.
            // The game may react to the coin in its own update first, so look at the previous poll's screen too
            if (++_coinPoll >= 6)
            {
                _coinPoll = 0;
                long coins = AMDaemon.Credit.Bookkeeping.TotalCoin;
                bool idle = cur.EndsWith("AdvertiseProcess") || cur.EndsWith("EntryProcess") || _lastScreen.EndsWith("AdvertiseProcess");
                if (_lastCoins >= 0 && coins > _lastCoins && idle)
                {
                    Interlocked.Increment(ref _quickLogin); Mod.Msg("quick login requested (coin / F3)");
                }
                _lastCoins = coins;
                _lastScreen = cur;
            }
            if (!InputLog) return;
            for (int p = 0; p < 2; p++)
                for (int b = 0; b < 9; b++)
                {
                    bool down = Manager.InputManager.GetButtonPush(p, (Manager.InputManager.ButtonSetting)b);
                    if (down == LastPush[p, b]) continue;
                    LastPush[p, b] = down;
                    var name = ((Manager.InputManager.ButtonSetting)b).ToString();
                    string screen;
                    lock (Gate) screen = Procs.Count > 0 ? (string)Procs[Procs.Count - 1]["name"] : "";
                    Mod.Msg(string.Format("[input] {0}P {1} {2} ({3})", p + 1, name, down ? "down" : "up", screen));
                }
        }

        // settings.ini "frameLog = true": log every frame slower than 25 ms (a visible hitch at 60 Hz) with the
        // screen it happened on and whether a garbage collection ran in that frame; plus a 10 s summary
        public static bool FrameLog;
        static int _lastGc0, _lastGc2, _spikes, _frames, _summaries;
        static Dictionary<string, int> _matNames = new Dictionary<string, int>();
        static float _worst, _summaryAt;

        // a timestamped marker in the frame log, to line hitches up with events
        public static void Mark(string what)
        {
            if (FrameLog) Mod.Msg("[frame] mark " + what);
        }

        public static void PollFrame()
        {
            if (!FrameLog) return;
            float dt = UnityEngine.Time.unscaledDeltaTime;
            float now = UnityEngine.Time.realtimeSinceStartup;
            int gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);
            _frames++;
            if (dt > 0.025f)
            {
                _spikes++; if (dt > _worst) _worst = dt;
                string screen;
                lock (Gate) screen = Procs.Count > 0 ? (string)Procs[Procs.Count - 1]["name"] : "";
                Mod.Msg(string.Format("[frame] {0:0.0} ms ({1}){2}{3}", dt * 1000f, screen,
                    gc0 != _lastGc0 ? " gc0" : "", gc2 != _lastGc2 ? " gc2" : ""));
            }
            _lastGc0 = gc0; _lastGc2 = gc2;
            if (now - _summaryAt >= 10f)
            {
                Mod.Msg(string.Format("[frame] summary {0} frames, {1} over 25 ms, worst {2:0.0} ms", _frames, _spikes, _worst * 1000f));
                _summaryAt = now; _frames = 0; _spikes = 0; _worst = 0f;
                // every minute: what keeps growing (managed heap and live engine objects by type)
                if (++_summaries % 6 == 0)
                    Mod.Msg(string.Format("[mem] heap {0:0} MB, gc {1}, Material {2}, Texture2D {3}, Sprite {4}, RenderTexture {5}, Mesh {6}, GameObject {7}, TMP font {8}, pixel mats {9}",
                        GC.GetTotalMemory(false) / 1048576.0, GC.CollectionCount(0),
                        UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Material>().Length, UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Texture2D>().Length,
                        UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Sprite>().Length, UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.RenderTexture>().Length,
                        UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>().Length, UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>().Length,
                        UnityEngine.Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>().Length, NgoPixelFont.MaterialCount));
                if (_summaries % 6 == 0)   // which materials pile up: the names that grew most since the last minute
                {
                    var now2 = new Dictionary<string, int>();
                    foreach (var m in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Material>())
                    {
                        var n = m.name; int c; now2.TryGetValue(n, out c); now2[n] = c + 1;
                    }
                    var grew = new List<KeyValuePair<string, int>>();
                    foreach (var kv in now2) { int before; _matNames.TryGetValue(kv.Key, out before); if (kv.Value - before > 0) grew.Add(new KeyValuePair<string, int>(kv.Key, kv.Value - before)); }
                    grew.Sort((a, b) => b.Value.CompareTo(a.Value));
                    var sb = new System.Text.StringBuilder("[mem] materials grew:");
                    for (int i = 0; i < grew.Count && i < 8; i++) sb.Append(" ").Append(grew[i].Key).Append(" +").Append(grew[i].Value).Append(" (=").Append(now2[grew[i].Key]).Append(")");
                    if (_matNames.Count > 0) Mod.Msg(sb.ToString());
                    _matNames = now2;
                    HeapCensus.Run();
                }
            }
        }

        // card screen: which confirm step the entry state machine is in (auto-login.py presses "yes" only once a
        // card has really been read, never on the guest-play prompt)
        static Process.EntryProcess _entry;
        static readonly System.Reflection.FieldInfo EntryContext = AccessTools.Field(typeof(Process.EntryProcess), "_context");

        [HarmonyPatch(typeof(Process.EntryProcess), "OnStart")]
        static class EntryTrack { static void Postfix(Process.EntryProcess __instance) { _entry = __instance; } }

        public static object EntryState()
        {
            var r = new Dictionary<string, object>();
            var ctx = _entry == null ? null : EntryContext.GetValue(_entry) as Process.Entry.State.Context;
            if (ctx != null)
            {
                int key; Process.Entry.State.State state;
                ctx.GetCurrentState(out key, out state);
                r["state"] = ((Process.Entry.State.StateType)key).ToString();
                r["mode"] = state == null ? null : AccessTools.Field(state.GetType(), "_mode")?.GetValue(state)?.ToString();
            }
            return r;
        }

        // every visible UI image / sprite on screen with its texture and screen rect (pixels, origin bottom-left),
        // to tie what a screenshot shows to the texture that draws it
        public static object UiDump()
        {
            var list = new List<object>();
            var corners = new UnityEngine.Vector3[4];
            foreach (var img in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>())
            {
                if (img == null || !img.isActiveAndEnabled || img.sprite == null || img.color.a <= 0.02f) continue;
                if (img.canvasRenderer.GetInheritedAlpha() <= 0.02f || img.canvasRenderer.cull) continue;
                var canvas = img.canvas;
                var cam = canvas != null && canvas.renderMode != UnityEngine.RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                img.rectTransform.GetWorldCorners(corners);
                var a = UnityEngine.RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                var b = UnityEngine.RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                list.Add(new Dictionary<string, object> { { "sprite", img.sprite.name }, { "texture", img.sprite.texture != null ? img.sprite.texture.name : "" },
                    { "x", (int)a.x }, { "y", (int)a.y }, { "w", (int)(b.x - a.x) }, { "h", (int)(b.y - a.y) }, { "type", img.type.ToString() },
                    { "color", UnityEngine.ColorUtility.ToHtmlStringRGBA(img.color) },
                    { "path", img.transform.parent != null ? img.transform.parent.name + "/" + img.name : img.name } });
            }
            // RawImage too: collection cards and our own views draw textures without a sprite
            foreach (var raw in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.RawImage>())
            {
                if (raw == null || !raw.isActiveAndEnabled || raw.texture == null || raw.color.a <= 0.02f) continue;
                if (raw.canvasRenderer.GetInheritedAlpha() <= 0.02f || raw.canvasRenderer.cull) continue;
                var canvas = raw.canvas;
                var cam = canvas != null && canvas.renderMode != UnityEngine.RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                raw.rectTransform.GetWorldCorners(corners);
                var a = UnityEngine.RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                var b = UnityEngine.RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                list.Add(new Dictionary<string, object> { { "sprite", "" }, { "texture", raw.texture.name }, { "renderer", "RawImage" },
                    { "x", (int)a.x }, { "y", (int)a.y }, { "w", (int)(b.x - a.x) }, { "h", (int)(b.y - a.y) },
                    { "path", raw.transform.parent != null ? raw.transform.parent.name + "/" + raw.name : raw.name } });
            }
            foreach (var r in UnityEngine.Object.FindObjectsOfType<UnityEngine.SpriteRenderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.sprite == null) continue;
                list.Add(new Dictionary<string, object> { { "sprite", r.sprite.name }, { "texture", r.sprite.texture != null ? r.sprite.texture.name : "" },
                    { "renderer", "SpriteRenderer" }, { "path", r.transform.parent != null ? r.transform.parent.name + "/" + r.name : r.name } });
            }
            return new Dictionary<string, object> { { "screen", UnityEngine.Screen.width + "x" + UnityEngine.Screen.height }, { "items", list } };
        }

        // the hierarchy `up` levels above the first active object named `of`, down to `depth`: sibling order is the draw
        // order inside one canvas, and nested canvases / renderers with their own sorting are marked
        // every active canvas with what decides its draw order (which one covers which): render mode, camera and its
        // depth, sorting layer (name and value), order, override; plus the renderers drawn by cameras (BG planes)
        public static object CanvasDump()
        {
            var list = new List<object>();
            foreach (var c in UnityEngine.Object.FindObjectsOfType<UnityEngine.Canvas>())
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                var path = c.name; var t = c.transform.parent;
                for (int i = 0; i < 4 && t != null; i++, t = t.parent) path = t.name + "/" + path;
                list.Add(new Dictionary<string, object> { { "path", path }, { "mode", c.renderMode.ToString() },
                    { "camera", c.worldCamera != null ? c.worldCamera.name + " depth " + c.worldCamera.depth : "" },
                    { "layer", c.sortingLayerName + " (" + UnityEngine.SortingLayer.GetLayerValueFromID(c.sortingLayerID) + ")" },
                    { "order", c.sortingOrder }, { "override", c.overrideSorting }, { "root", c.isRootCanvas }, { "plane", c.planeDistance } });
            }
            return new Dictionary<string, object> { { "canvases", list } };
        }

        public static object Tree(string of, int up, int depth)
        {
            UnityEngine.Transform start = null;
            foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Transform>())
                if (t.name == of && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy) { start = t; break; }
            if (start == null) return new Dictionary<string, object> { { "error", "no active object named " + of } };
            var root = start;
            for (int i = 0; i < up && root.parent != null; i++) root = root.parent;
            return TreeNode(root, depth);
        }

        static object TreeNode(UnityEngine.Transform t, int depth)
        {
            var d = new Dictionary<string, object> { { "name", t.name } };
            if (!t.gameObject.activeSelf) d["off"] = true;
            var canvas = t.GetComponent<UnityEngine.Canvas>();
            if (canvas != null) d["canvas"] = (canvas.overrideSorting ? "override " : "") + canvas.sortingLayerName + " " + canvas.sortingOrder;
            var g = t.GetComponent<UnityEngine.UI.Graphic>();
            if (g != null)
            {
                d["graphic"] = g.GetType().Name + (g is UnityEngine.UI.Image && ((UnityEngine.UI.Image)g).sprite != null ? " " + ((UnityEngine.UI.Image)g).sprite.name : "");
                d["draw"] = (g.enabled ? "on" : "OFF") + " a " + g.color.a.ToString("0.00") + " inherited " + g.canvasRenderer.GetInheritedAlpha().ToString("0.00") + (g.canvasRenderer.cull ? " CULLED" : "");
            }
            var mask = t.GetComponent<UnityEngine.UI.Mask>();
            if (mask != null) d["mask"] = (mask.enabled ? "on" : "off") + (mask.showMaskGraphic ? " shown" : " hidden");
            var group = t.GetComponent<UnityEngine.CanvasGroup>();
            if (group != null) d["group"] = "alpha " + group.alpha.ToString("0.00");
            var r = t.GetComponent<UnityEngine.Renderer>();
            if (r != null) d["renderer"] = r.GetType().Name + " " + r.sortingLayerName + " " + r.sortingOrder;
            var rt = t as UnityEngine.RectTransform;
            if (rt != null) d["pos"] = string.Format("{0:0},{1:0} {2:0}x{3:0}", rt.localPosition.x, rt.localPosition.y, rt.rect.width, rt.rect.height);
            if (t.childCount > 0)
            {
                if (depth <= 0) d["children"] = t.childCount;
                else
                {
                    var kids = new List<object>();
                    for (int i = 0; i < t.childCount; i++) kids.Add(TreeNode(t.GetChild(i), depth - 1));
                    d["children"] = kids;
                }
            }
            return d;
        }

        // cost of the music-select layout: per call kind, count / total / max milliseconds plus frame-time stats while it runs
        public static class SelectPerf
        {
            sealed class Stat { public int N; public double Total, Max; }
            static readonly Dictionary<string, Stat> Stats = new Dictionary<string, Stat>();
            static readonly List<float> Frames = new List<float>();
            static int _lastFrame = -1;
            public static void Add(string kind, double ms)
            {
                lock (Stats)
                {
                    Stat s; if (!Stats.TryGetValue(kind, out s)) Stats[kind] = s = new Stat();
                    s.N++; s.Total += ms; if (ms > s.Max) s.Max = ms;
                    if (UnityEngine.Time.frameCount != _lastFrame)
                    {
                        _lastFrame = UnityEngine.Time.frameCount;
                        // "maintain" runs every select frame: keep the last minute only (unbounded, this grew all session)
                        if (Frames.Count >= 3600) Frames.RemoveRange(0, 600);
                        Frames.Add(UnityEngine.Time.unscaledDeltaTime * 1000f);
                    }
                }
            }
            public static object Dump(bool reset)
            {
                lock (Stats)
                {
                    var o = new Dictionary<string, object>();
                    foreach (var kv in Stats)
                        o[kv.Key] = new Dictionary<string, object> { { "calls", kv.Value.N }, { "totalMs", Math.Round(kv.Value.Total, 3) },
                            { "avgMs", kv.Value.N == 0 ? 0 : Math.Round(kv.Value.Total / kv.Value.N, 4) }, { "maxMs", Math.Round(kv.Value.Max, 3) } };
                    var f = new List<float>(Frames); f.Sort();
                    if (f.Count > 0) o["frameMs"] = new Dictionary<string, object> { { "frames", f.Count }, { "median", f[f.Count / 2] },
                        { "p95", f[(int)(f.Count * .95)] }, { "max", f[f.Count - 1] } };
                    if (reset) { Stats.Clear(); Frames.Clear(); }
                    return o;
                }
            }
        }

        // sprite / particle / mesh renderers under the menu backgrounds, with what decides whether and where they draw
        public static object RendererDump()
        {
            var list = new List<object>();
            foreach (var r in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Renderer>())
            {
                if (r == null || !r.gameObject.scene.IsValid()) continue;     // scene instances only, not prefab assets
                var path = r.name;
                string offAt = r.gameObject.activeSelf ? null : r.name;
                for (var p = r.transform.parent; p != null; p = p.parent) { path = p.name + "/" + path; if (!p.gameObject.activeSelf) offAt = p.name; }
                if (!path.Contains("UI_CMN_BG") && !path.Contains("UI_CMN_SubBG")) continue;
                if (offAt != null && !path.Contains("Bokeh") && !path.Contains("Particle System")) continue;
                var mat = r.sharedMaterial;
                var d = new Dictionary<string, object> {
                    { "path", path }, { "type", r.GetType().Name }, { "enabled", r.enabled }, { "visible", r.isVisible }, { "inactiveAt", offAt ?? "" },
                    { "layer", r.gameObject.layer }, { "sortingLayer", r.sortingLayerName }, { "sortingLayerValue", UnityEngine.SortingLayer.GetLayerValueFromID(r.sortingLayerID) },
                    { "order", r.sortingOrder }, { "queue", mat != null ? mat.renderQueue : -1 }, { "shader", mat != null && mat.shader != null ? mat.shader.name : "" },
                    { "center", r.bounds.center.ToString("F0") }, { "size", r.bounds.size.ToString("F0") } };
                var sr = r as UnityEngine.SpriteRenderer;
                if (sr != null) d["color"] = UnityEngine.ColorUtility.ToHtmlStringRGBA(sr.color);
                var ps = r.GetComponent<UnityEngine.ParticleSystem>();
                if (ps != null) { d["particles"] = ps.particleCount; d["playing"] = ps.isPlaying; }
                var cams = new List<string>();
                foreach (var cam in UnityEngine.Camera.allCameras)
                {
                    if ((cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                    var sp = cam.WorldToScreenPoint(r.bounds.center);
                    cams.Add(cam.name + (cam.orthographic ? "(ortho)" : "(persp)") + " depth" + cam.depth + " at " + sp.ToString("F0"));
                }
                d["cameras"] = cams;
                list.Add(d);
            }
            var camList = new List<object>();
            foreach (var cam in UnityEngine.Camera.allCameras)
                camList.Add(cam.name + " depth" + cam.depth + " mask" + cam.cullingMask + " pos" + cam.transform.position.ToString("F0") + " fwd" + cam.transform.forward.ToString("F2")
                    + (cam.orthographic ? " ortho" + cam.orthographicSize : " fov" + cam.fieldOfView) + " near" + cam.nearClipPlane + " far" + cam.farClipPlane + " rect" + cam.pixelRect);
            return new Dictionary<string, object> { { "renderers", list }, { "cameras", camList } };
        }

        // a chart is being played (set when the game adds its play process, cleared by the next screen)
        public static volatile bool InPlay;

        public static object ProcessLog(int since)
        {
            lock (Gate)
            {
                var list = new List<object>();
                for (int i = Math.Max(0, since); i < Procs.Count; i++) list.Add(Procs[i]);
                return new Dictionary<string, object> { { "count", Procs.Count }, { "processes", list }, { "testInput", InputHooked } };
            }
        }

        [HarmonyPatch(typeof(Manager.ProcessManager), "AddProcess")]
        static class AddProcessLog
        {
            static void Postfix(ProcessBase process)
            {
                if (process == null) return;
                var name = process.GetType().FullName;
                if (!name.EndsWith("FadeProcess")) InPlay = name == "Process.GameProcess";
                lock (Gate)
                {
                    Procs.Add(new Dictionary<string, object> { { "i", Procs.Count }, { "name", name }, { "t", Clock.ElapsedMilliseconds / 1000.0 } });
                    if (Procs.Count > 2000) Procs.RemoveAt(0);
                }
                Mod.Msg("[proc] + " + name);
            }
        }
    }
}
