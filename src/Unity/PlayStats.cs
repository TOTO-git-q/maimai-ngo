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
        static int _frames, _over25, _over50, _gc, _music, _difficulty;
        static float _worst, _load, _startedAt;

        public static void Poll()
        {
            if (!_on) return;
            float dt = Time.unscaledDeltaTime;
            if (++_frames <= LoadFrames) { if (dt > _load) _load = dt; return; }
            if (dt > 0.025f) { _over25++; if (dt > 0.05f) _over50++; }
            if (dt > _worst) _worst = dt;
        }

        [HarmonyPatch(typeof(Process.GameProcess), "OnStart")]
        static class Begin
        {
            static void Postfix()
            {
                _on = true; _frames = _over25 = _over50 = 0; _worst = _load = 0f;
                _gc = GC.CollectionCount(0);
                _startedAt = Time.realtimeSinceStartup;
                try { _music = Manager.GameManager.SelectMusicID[0]; _difficulty = Manager.GameManager.SelectDifficultyID[0]; }
                catch { _music = _difficulty = -1; }
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
            }
        }
    }
}
