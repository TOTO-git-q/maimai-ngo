using System;
using System.Diagnostics;
using HarmonyLib;

namespace PartnerLive2D.Unity
{
    // The game cleans memory (MAI2System.SystemConfig.unloadUnusedAndGC: Resources.UnloadUnusedAssets + a full
    // GC.Collect) in every fade (FadeProcess / BlackoutFadeProcess) and once more in GameProcess.OnStart, the moment the
    // song starts. A full collection stops the game for as long as it takes to scan the whole heap, which grows over a
    // long session: the run at song start reached 1-1.8 s. The fade into the track-start card has just cleaned, so a
    // second run within MinGap seconds is skipped: the cleanup stays under the fade/loading screens, where a pause is
    // not seen. Every run is logged with its time and the heap left after it (a few lines per track, also in real play,
    // so a growing heap shows up in tools\ngo_play_report.py); skips only with frameLog.
    [HarmonyPatch(typeof(MAI2System.SystemConfig), "unloadUnusedAndGC")]
    static class NgoCleanupTiming
    {
        public static float MinGap = 20f;            // settings.ini cleanupMinGap (seconds); 0 = the game's own behaviour
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static double _lastRun = -1000;
        static long _started;

        static bool Prefix()
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (MinGap > 0 && now - _lastRun < MinGap)
            {
                if (TestHarness.FrameLog) Mod.Msg(string.Format("[clean] skipped ({0:0.0} s after the last one)", now - _lastRun));
                return false;
            }
            _started = Stopwatch.GetTimestamp();
            return true;
        }

        static void Postfix(bool __runOriginal)
        {
            if (!__runOriginal) return;
            _lastRun = Clock.Elapsed.TotalSeconds;
            Mod.Msg(string.Format("[clean] unload+GC {0:0} ms, heap {1:0} MB", (Stopwatch.GetTimestamp() - _started) * 1000.0 / Stopwatch.Frequency, GC.GetTotalMemory(false) / 1048576.0));
        }
    }
}
