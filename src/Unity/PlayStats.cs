using System;
using HarmonyLib;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // One "[play]" line per chart in the normal log, so a long real session can be checked afterwards (tools\ngo_play_report.py)
    // without the test-only frameLog, whose per-minute object census causes hitches of its own: how many frames were slower
    // than 25 ms (a visible hitch at 60 Hz) and 50 ms, the worst one, the stall while the chart loads, and whether a
    // garbage collection ran (the game switches GC off for the song). Costs one comparison per frame.
    static class PlayStats
    {
        const int LoadFrames = 3;   // the first frames after GameProcess.OnStart carry the chart/movie load

        static bool _on;
        public static bool InSong => _on;
        // Per-frame console/file output can itself stall a weak PC. Summaries remain on.
        public static bool VerboseFrames;
        static int _frames, _over25, _over50, _gc, _music, _difficulty;
        static float _worst, _load, _startedAt;

        // The mod's own time while a chart plays (owner 2026-11-02 night: "确实会掉帧……可能是游玩的过程中mod占用资源导致的"):
        // every part of the mod that can run during a song adds its time here, and a second line per song ("[play-mod]")
        // says what the mod cost, next to the frames that came late by even one refresh (over 17.5 / 20 ms)
        public enum Part { Update, Tint, Cards, Transition, Font, Glyph }
        static readonly int[] Calls = new int[6];
        static readonly long[] FrameParts = new long[6];
        static long _modTicks, _frameTicks, _frameMax;
        static int _modHeavy, _over17, _over20, _lateLines;
        public static long Now() { return System.Diagnostics.Stopwatch.GetTimestamp(); }
        public static void Spent(Part part, long since)
        {
            if (!_on) return;
            long d = System.Diagnostics.Stopwatch.GetTimestamp() - since;
            _frameTicks += d; FrameParts[(int)part] += d; Calls[(int)part]++;
        }

        public static void Poll()
        {
            if (!_on) return;
            // the previous frame's account of the mod's time; this frame's dt is how long that frame took
            float dt = Time.unscaledDeltaTime;
            PresentationDiagnostics.Frame(dt);
            double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (VerboseFrames && _frames >= LoadFrames && dt > 0.02f && _lateLines < 200)
            {
                _lateLines++;
                Mod.Msg(string.Format("[late] {0:0.0} ms at {1:0.0} s into music {2}: the mod {3:0.00} ms (update {4:0.00}, tint {5:0.00}, cards {6:0.00}, transition {7:0.00}, font {8:0.00}, glyphs {9:0.00})",
                    dt * 1000f, Time.realtimeSinceStartup - _startedAt, _music, _frameTicks * ms, FrameParts[0] * ms, FrameParts[1] * ms, FrameParts[2] * ms,
                    FrameParts[3] * ms, FrameParts[4] * ms, FrameParts[5] * ms));
            }
            _modTicks += _frameTicks;
            if (_frameTicks > _frameMax) _frameMax = _frameTicks;
            if (_frameTicks * ms > 1.0) _modHeavy++;
            _frameTicks = 0; Array.Clear(FrameParts, 0, FrameParts.Length);
            if (++_frames <= LoadFrames) { if (dt > _load) _load = dt; return; }
            if (dt > 0.0175f) { _over17++; if (dt > 0.02f) _over20++; }
            if (dt > 0.025f) { _over25++; if (dt > 0.05f) _over50++; }
            // a visible freeze: when it happened (wall clock, to match other logs) and where in the song
            if (VerboseFrames && dt > 0.1f)
                Mod.Msg(string.Format("[stall] {0:0} ms at {1:0.0} s into music {2} ({3:HH:mm:ss.fff})",
                    dt * 1000f, Time.realtimeSinceStartup - _startedAt, _music, DateTime.Now));
            if (dt > _worst) _worst = dt;
        }

        [HarmonyPatch(typeof(Process.GameProcess), "OnStart")]
        static class Begin
        {
            static void Postfix()
            {
                PresentationDiagnostics.Begin();
                _on = true; _frames = _over25 = _over50 = 0; _worst = _load = 0f;
                _modTicks = _frameTicks = _frameMax = 0; _modHeavy = _over17 = _over20 = _lateLines = 0;
                Array.Clear(Calls, 0, Calls.Length); Array.Clear(FrameParts, 0, FrameParts.Length);
                _gc = GC.CollectionCount(0);
                _startedAt = Time.realtimeSinceStartup;
                try { _music = Manager.GameManager.SelectMusicID[0]; _difficulty = Manager.GameManager.SelectDifficultyID[0]; }
                catch { _music = _difficulty = -1; }
                // Record presentation settings; these timings do not establish the cause of late frames.
                Mod.Msg(string.Format("[play-start] music {0}: {1}, {2}x{3}, display {4} Hz, vsync {5}, target fps {6}", _music, Screen.fullScreenMode,
                    Screen.width, Screen.height, Screen.currentResolution.refreshRate, QualitySettings.vSyncCount, Application.targetFrameRate));
            }
        }

        [HarmonyPatch(typeof(Process.GameProcess), "OnRelease")]
        static class End
        {
            // before the game turns GC back on and collects
            static void Prefix()
            {
                if (!_on) return;
                _on = false;
                float seconds = Time.realtimeSinceStartup - _startedAt;
                int counted = Math.Max(0, _frames - LoadFrames);
                Mod.Msg(string.Format("[play] music {0} diff {1}: {2:0.0} s, {3} frames ({4:0.0} fps), {5} over 25 ms, {6} over 50 ms, worst {7:0.0} ms, load {8:0} ms, gc {9}",
                    _music, _difficulty, seconds, counted, seconds > 0 ? counted / seconds : 0f, _over25, _over50, _worst * 1000f, _load * 1000f, GC.CollectionCount(0) - _gc));
                double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Mod.Msg(string.Format("[play-mod] music {0}: {1} frames over 17.5 ms, {2} over 20 ms; the mod {3:0.0} ms in all, {4:0.0} us a frame, worst frame {5:0.00} ms, " +
                    "{6} frames over 1 ms; calls update {7}, tint {8}, cards {9}, transition {10}, font {11}, glyphs added {12}",
                    _music, _over17, _over20, _modTicks * ms, counted > 0 ? _modTicks * ms * 1000.0 / counted : 0, _frameMax * ms, _modHeavy,
                    Calls[0], Calls[1], Calls[2], Calls[3], Calls[4], Calls[5]));
                PresentationDiagnostics.End(_music);
            }
        }
    }
}
