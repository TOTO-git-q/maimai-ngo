using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PartnerLive2D.Unity
{
    // Fixed storage on hot paths. Snapshot/percentile allocation happens only on an explicit API request.
    static class RuntimePerf
    {
        public enum Part { ThemeScan, ThemeReapply, ThemeImage, ThemeRaw, ThemeLoad, FontWarm, LibraryStart, LibraryDispose }
        static readonly long[] Calls = new long[8], Total = new long[8], Max = new long[8];
        static readonly float[] Frames = new float[600];
        static int _count, _next;
        public static void Poll()
        {
            Frames[_next] = UnityEngine.Time.unscaledDeltaTime * 1000f;
            _next = (_next + 1) % Frames.Length; if (_count < Frames.Length) _count++;
        }
        public static long Now() => Stopwatch.GetTimestamp();
        public static void End(Part part, long start)
        {
            int i = (int)part; long ticks = Now() - start;
            Calls[i]++; Total[i] += ticks; if (ticks > Max[i]) Max[i] = ticks;
        }
        public static object Snapshot(bool reset)
        {
            var parts = new Dictionary<string, object>(); double ms = 1000.0 / Stopwatch.Frequency;
            for (int i = 0; i < Calls.Length; i++)
                parts[((Part)i).ToString()] = new Dictionary<string, object> { { "calls", Calls[i] },
                    { "totalMs", Total[i] * ms }, { "maxMs", Max[i] * ms }, { "avgMs", Calls[i] > 0 ? Total[i] * ms / Calls[i] : 0 } };
            var result = new Dictionary<string, object> { { "quality", RuntimeQuality.Policy.Low ? "low" : "high" },
                { "reason", RuntimeQuality.Policy.Reason }, { "managedBytes", GC.GetTotalMemory(false) },
                { "parts", parts }, { "font", NgoPixelFont.Info() } };
            if (_count > 0)
            {
                var sorted = new float[_count]; Array.Copy(Frames, sorted, _count); Array.Sort(sorted);
                result["frameMs"] = new Dictionary<string, object> { { "samples", _count }, { "median", sorted[_count / 2] },
                    { "p95", sorted[(int)(_count * .95)] }, { "p99", sorted[(int)(_count * .99)] }, { "max", sorted[_count - 1] } };
            }
            result["gc0"] = GC.CollectionCount(0);
            if (reset) { Array.Clear(Calls, 0, Calls.Length); Array.Clear(Total, 0, Total.Length); Array.Clear(Max, 0, Max.Length); _count = _next = 0; }
            return result;
        }
    }
}
