using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    static class PresentationDiagnostics
    {
        static PresentationOptions _options = new PresentationOptions();
        static readonly PresentationHistogram UnityIntervals = new PresentationHistogram(), QpcIntervals = new PresentationHistogram();
        static bool _chart, _screenRequested;
        static int _polls, _startHr;
        static long _previousQpc, _startQpc;
        static float _verifyAt = -1;
        static string _startWindow;
        static PresentationNative.Timing _start;

        public static void Initialize(Dictionary<string, string> settings)
        {
            _options = PresentationOptions.Parse(settings, Mod.Warn);
            if (_options.Diagnostics) Mod.Msg("[present-config] diagnostics on; DWM scope=desktop, not cabinet; histogram upper bounds ms=15,16,17,17.5,20,25,33.333333,50,+inf");
        }

        static void ApplyPacing(string stage)
        {
            if (!_options.VSync.HasValue && !_options.TargetFps.HasValue && !_options.Queue.HasValue) return;
            if (_options.VSync.HasValue) QualitySettings.vSyncCount = _options.VSync.Value;
            if (_options.TargetFps.HasValue) Application.targetFrameRate = _options.TargetFps.Value;
            if (_options.Queue.HasValue) QualitySettings.maxQueuedFrames = _options.Queue.Value;
            Mod.Msg(string.Format("[present-settings] {0}: vsync={1},targetFps={2},maxQueuedFrames={3}", stage,
                QualitySettings.vSyncCount, Application.targetFrameRate, QualitySettings.maxQueuedFrames));
        }

        // The first music select is a real lifecycle boundary after AquaMai's boot setup and 2-second style task.
        // Request once, outside a song. Never keep fighting the window manager every frame.
        [HarmonyPatch(typeof(Process.MusicSelectProcess), "OnStart")]
        static class SelectionReady
        {
            [HarmonyPriority(Priority.Last)]
            static void Postfix()
            {
                ApplyPacing("select");
                if (_screenRequested || _options.ScreenMode == "keep") return;
                _screenRequested = true;
                var mode = _options.ScreenMode == "exclusive" ? FullScreenMode.ExclusiveFullScreen :
                    _options.ScreenMode == "borderless" ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                Mod.Msg(string.Format("[present-screen] select: before={0}; request={1},size={2}x{3},hz={4}; {5}",
                    Screen.fullScreenMode, mode, Screen.width, Screen.height, Screen.currentResolution.refreshRate, PresentationNative.WindowIdentity()));
                Screen.SetResolution(Screen.width, Screen.height, mode, Screen.currentResolution.refreshRate);
                _verifyAt = Time.realtimeSinceStartup + 2f;
            }
        }

        public static void Poll()
        {
            if (_verifyAt < 0 || _chart || Time.realtimeSinceStartup < _verifyAt) return;
            _verifyAt = -1;
            Mod.Msg("[present-screen] delayed observed=" + Screen.fullScreenMode + "; " + PresentationNative.WindowIdentity());
        }

        public static void Begin()
        {
            _chart = true;
            ApplyPacing("chart-start");
            if (!_options.Diagnostics) return;
            UnityIntervals.Clear(); QpcIntervals.Clear(); _polls = 0;
            PresentationNative.Timing ignored;
            PresentationNative.Snapshot(out ignored); // prime: several DWM counters require a second call
            _startWindow = PresentationNative.WindowIdentity();
            _startHr = unchecked((int)0x80004005);
            _startQpc = _previousQpc = Stopwatch.GetTimestamp();
        }

        public static void Frame(float dt)
        {
            if (!_chart || !_options.Diagnostics) return;
            long now = Stopwatch.GetTimestamp();
            if (++_polls == 3)
            {
                _startHr = PresentationNative.Snapshot(out _start);
                _startQpc = now;
            }
            if (_polls > 3)
            {
                UnityIntervals.Add(dt * 1000.0);
                QpcIntervals.Add((now - _previousQpc) * 1000.0 / Stopwatch.Frequency);
            }
            _previousQpc = now;
        }

        public static void End(int music)
        {
            _chart = false;
            if (!_options.Diagnostics) return;
            PresentationNative.Timing end;
            int endHr = PresentationNative.Snapshot(out end);
            long endQpc = Stopwatch.GetTimestamp();
            string dwm = "unavailable";
            if (_polls >= 3 && _startHr == 0 && endHr == 0)
            {
                // Raw endpoints preserve deprecated counter semantics. Never call their difference cabinet drops.
                dwm = string.Format(CultureInfo.InvariantCulture,
                    "refresh={0}/{1}->{2}/{3},compose={4}/{5}->{6}/{7},periodQpc={8}->{9},vblankQpc={10}->{11},refreshCount={12}->{13},displayedRaw={14}->{15},droppedRaw={16}->{17},missedRaw={18}->{19},lateRaw={20}->{21}",
                    _start.Refresh.Numerator, _start.Refresh.Denominator, end.Refresh.Numerator, end.Refresh.Denominator,
                    _start.Compose.Numerator, _start.Compose.Denominator, end.Compose.Numerator, end.Compose.Denominator,
                    _start.RefreshPeriod, end.RefreshPeriod, _start.VBlank, end.VBlank, _start.RefreshCount, end.RefreshCount,
                    _start.FramesDisplayed, end.FramesDisplayed, _start.FramesDropped, end.FramesDropped,
                    _start.FramesMissed, end.FramesMissed, _start.FramesLate, end.FramesLate);
            }
            Mod.Msg(string.Format(CultureInfo.InvariantCulture,
                "[play-present] music={0}; unityUpdate[{1}]; qpcUpdate[{2}]; sampleQpc={3}->{4},freq={5}; dwmScope=desktop-not-cabinet,hr={6:X8}/{7:X8},size={8}; {9}; start[{10}]; end[{11}]; mode={12},vsync={13},targetFps={14},queue={15},gpu={16},api={17}",
                music, UnityIntervals.Summary(), QpcIntervals.Summary(), _startQpc, endQpc, Stopwatch.Frequency,
                _startHr, endHr, System.Runtime.InteropServices.Marshal.SizeOf(typeof(PresentationNative.Timing)), dwm,
                _startWindow, PresentationNative.WindowIdentity(), Screen.fullScreenMode, QualitySettings.vSyncCount,
                Application.targetFrameRate, QualitySettings.maxQueuedFrames, SystemInfo.graphicsDeviceName, SystemInfo.graphicsDeviceVersion));
        }
    }
}
