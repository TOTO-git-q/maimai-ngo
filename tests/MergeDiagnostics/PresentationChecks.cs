using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using PartnerLive2D.Unity;

namespace HarmonyLib
{
    class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type t, string method) { } }
    static class AccessTools
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static MethodInfo Method(Type type, string name, Type[] parameters) { return type.GetMethod(name, All, null, parameters, null); }
        public static FieldInfo Field(Type type, string name) { return type.GetField(name, All); }
    }
    class HarmonyPriority : Attribute { public HarmonyPriority(int p) { } }
    static class Priority { public const int Last = 0; }
}
namespace Process { class MusicSelectProcess { } class GameProcess { } }
namespace Manager { static class GameManager { public static int[] SelectMusicID = { 841 }, SelectDifficultyID = { 3 }; } }
namespace UnityEngine
{
    enum FullScreenMode { ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed }
    struct Resolution { public int refreshRate; }
    static class Screen
    {
        public static FullScreenMode fullScreenMode = FullScreenMode.Windowed;
        public static int width = 2160, height = 3840, Requests;
        public static Resolution currentResolution = new Resolution { refreshRate = 60 };
        public static void SetResolution(int w, int h, FullScreenMode mode, int hz) { Requests++; fullScreenMode = mode; }
    }
    static class QualitySettings { public static int vSyncCount = 1, maxQueuedFrames = 2; }
    static class Application { public static int targetFrameRate = -1; public static string streamingAssetsPath = "."; }
    static class Time { public static float realtimeSinceStartup, unscaledDeltaTime; public static int frameCount = 0; }
    static class SystemInfo { public static string graphicsDeviceName = "stub", graphicsDeviceVersion = "stub"; }
}
namespace PartnerLive2D.Unity
{
    static class Mod
    {
        public static readonly List<string> Lines = new List<string>();
        public static string AssetRoot = ".";
        public static void Warn(string s) { Lines.Add(s); }
        public static void Msg(string s) { Lines.Add(s); }
    }
}
partial class Checks
{
    static int checks;
    static void Check(bool b, string label) { checks++; if (!b) throw new Exception(label); }
    static void Select()
    {
        typeof(PresentationDiagnostics).GetNestedType("SelectionReady", BindingFlags.NonPublic)
            .GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
    }
    static void Main()
    {
        var warnings = new List<string>();
        var defaults = PresentationOptions.Parse(new Dictionary<string, string>(), warnings.Add);
        Check(!defaults.Diagnostics && !defaults.VSync.HasValue && !defaults.TargetFps.HasValue && !defaults.Queue.HasValue && defaults.ScreenMode == "keep", "default no-op");
        var bad = PresentationOptions.Parse(new Dictionary<string, string> {
            {"presentationDiagnostics","yes"}, {"presentationVSync","-1"}, {"presentationTargetFps","0"},
            {"presentationMaxQueuedFrames","0"}, {"screenMode","wrong"}}, warnings.Add);
        Check(warnings.Count == 5 && !bad.VSync.HasValue && !bad.TargetFps.HasValue && !bad.Queue.HasValue && bad.ScreenMode == "keep", "invalid settings fail safe");
        var good = PresentationOptions.Parse(new Dictionary<string, string> {
            {"presentationDiagnostics","true"}, {"presentationVSync","0"}, {"presentationTargetFps","-1"},
            {"presentationMaxQueuedFrames","1"}, {"screenMode","exclusive"}}, warnings.Add);
        Check(good.Diagnostics && good.VSync == 0 && good.TargetFps == -1 && good.Queue == 1, "valid boundary settings");
        var h = new PresentationHistogram();
        foreach (double ms in new double[] {15,16,17,17.5,20,25,33.333333,50,51}) h.Add(ms);
        foreach (int count in h.Counts) Check(count == 1, "histogram edge inclusivity");
        Check(h.Samples == 9 && h.WorstMs == 51, "histogram sample accounting");
        h.Clear(); Check(h.Samples == 0 && h.TotalMs == 0 && h.Counts[8] == 0, "reset");
        Check(Marshal.SizeOf(typeof(PresentationNative.Timing)) == 292, "DWM ABI size");
        Check(Marshal.OffsetOf(typeof(PresentationNative.Timing), "VBlank").ToInt32() == 28, "DWM vblank offset");
        Check(Marshal.OffsetOf(typeof(PresentationNative.Timing), "FramesDisplayed").ToInt32() == 180, "DWM displayed offset");
        Check(Marshal.OffsetOf(typeof(PresentationNative.Timing), "FramesDropped").ToInt32() == 212, "DWM dropped offset");
        PresentationDiagnostics.Initialize(new Dictionary<string, string>());
        Select(); PresentationDiagnostics.Begin(); PresentationDiagnostics.Frame(0.016f); PresentationDiagnostics.End(1);
        Check(Mod.Lines.Count == 0 && UnityEngine.Screen.Requests == 0 && UnityEngine.QualitySettings.maxQueuedFrames == 2, "disabled runtime no-op");
        PresentationDiagnostics.Initialize(new Dictionary<string, string> { {"presentationDiagnostics", "true"} });
        Select(); PresentationDiagnostics.Begin();
        for (int i = 0; i < 5; i++) PresentationDiagnostics.Frame(0.016f);
        PresentationDiagnostics.End(841);
        Check(UnityEngine.Screen.Requests == 0 && UnityEngine.QualitySettings.vSyncCount == 1 &&
            UnityEngine.Application.targetFrameRate == -1 && UnityEngine.QualitySettings.maxQueuedFrames == 2, "diagnostics-only example changes no presentation setting");
        Check(Mod.Lines.Exists(x => x.StartsWith("[play-present]")), "diagnostics-only produces the chart record");
        Mod.Lines.Clear();
        PresentationDiagnostics.Initialize(new Dictionary<string, string> {
            {"presentationDiagnostics","true"}, {"presentationVSync","0"}, {"presentationTargetFps","60"},
            {"presentationMaxQueuedFrames","1"}, {"screenMode","exclusive"}});
        Select(); Select();
        Check(UnityEngine.Screen.Requests == 1 && UnityEngine.Screen.fullScreenMode == UnityEngine.FullScreenMode.ExclusiveFullScreen, "request once at select");
        Check(UnityEngine.QualitySettings.vSyncCount == 0 && UnityEngine.Application.targetFrameRate == 60 && UnityEngine.QualitySettings.maxQueuedFrames == 1, "pacing controls");
        UnityEngine.Time.realtimeSinceStartup = 3;
        PresentationDiagnostics.Poll();
        Check(Mod.Lines.Exists(x => x.Contains("delayed observed=ExclusiveFullScreen")), "delayed mode readback");
        PresentationDiagnostics.Begin();
        for (int i=0;i<5;i++) PresentationDiagnostics.Frame(0.016f);
        PresentationDiagnostics.End(12072);
        var lines = Mod.Lines.FindAll(x => x.StartsWith("[play-present]"));
        Check(lines.Count == 1 && lines[0].Contains("unityUpdate[n=2,") && lines[0].Contains("qpcUpdate[n=2,"), "one chart line and same three-frame exclusion");
        Check(lines[0].Contains("dwmScope=desktop-not-cabinet"), "measurement scope explicit");
        PresentationDiagnostics.Begin(); PresentationDiagnostics.End(2);
        Check(Mod.Lines[Mod.Lines.Count-1].Contains("unityUpdate[n=0,"), "zero-frame chart resets and unavailable DWM");
        PlayStatsChecks();
        FontChecks();
        PresentationNative.Timing timing;
        int hr=PresentationNative.Snapshot(out timing);
        Console.WriteLine("Read-only native smoke: HRESULT=0x" + hr.ToString("X8") + ", size=" + timing.Size + ", refresh=" + timing.Refresh.Numerator + "/" + timing.Refresh.Denominator);
        Console.WriteLine("PASS " + checks + " checks; Unity/Harmony lifecycle stubbed, no game launched.");
    }

    static void PlayStart() { typeof(PlayStats).GetNestedType("Begin", BindingFlags.NonPublic).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); }
    static void PlayEnd() { typeof(PlayStats).GetNestedType("End", BindingFlags.NonPublic).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); }
    static void PlayStatsChecks()
    {
        Mod.Lines.Clear();
        PlayStats.VerboseFrames = false;
        PlayStart(); Check(PlayStats.InSong, "song guard starts");
        UnityEngine.Time.unscaledDeltaTime = 0.12f;
        for (int i = 0; i < 5; i++) PlayStats.Poll();
        PlayEnd(); Check(!PlayStats.InSong, "song guard ends");
        Check(!Mod.Lines.Exists(x => x.StartsWith("[late]") || x.StartsWith("[stall]")), "default disables per-frame log I/O");
        Check(Mod.Lines.Exists(x => x.StartsWith("[play]")) && Mod.Lines.Exists(x => x.StartsWith("[play-mod]")), "existing chart summaries retained");
        Check(Mod.Lines.Exists(x => x.StartsWith("[play-present]") && x.Contains("unityUpdate[n=2,")), "integrated PlayStats histogram excludes exactly three polls");
        Mod.Lines.Clear(); PlayStats.VerboseFrames = true;
        PlayStart(); for (int i = 0; i < 4; i++) PlayStats.Poll(); PlayEnd();
        Check(Mod.Lines.Exists(x => x.StartsWith("[late]")) && Mod.Lines.Exists(x => x.StartsWith("[stall]")), "lateFrameLog restores detailed logging");
        PlayStats.VerboseFrames = false;
        PresentationDiagnostics.Initialize(new Dictionary<string, string>());
        Mod.Lines.Clear(); PlayStart(); PlayStats.Poll(); PlayEnd();
        Check(!Mod.Lines.Exists(x => x.StartsWith("[play-present]")), "disabled diagnostics retain legacy summaries without native chart sampling");
    }
}
