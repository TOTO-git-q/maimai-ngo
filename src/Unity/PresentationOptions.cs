using System;
using System.Collections.Generic;
using System.Globalization;

namespace PartnerLive2D.Unity
{
    // Missing keys leave the game alone. Read once at boot; restart between controlled trials.
    sealed class PresentationOptions
    {
        public bool Diagnostics;
        public int? VSync, TargetFps, Queue;
        public string ScreenMode = "keep";

        public static PresentationOptions Parse(Dictionary<string, string> settings, Action<string> warn)
        {
            var o = new PresentationOptions();
            string value;
            if (settings.TryGetValue("presentationDiagnostics", out value))
            {
                bool enabled;
                if (bool.TryParse(value, out enabled)) o.Diagnostics = enabled;
                else warn("presentationDiagnostics must be true or false; left off");
            }
            o.VSync = Number(settings, "presentationVSync", 0, 4, false, warn);
            o.TargetFps = Number(settings, "presentationTargetFps", 1, 240, true, warn);
            o.Queue = Number(settings, "presentationMaxQueuedFrames", 1, 4, false, warn);
            if (settings.TryGetValue("screenMode", out value))
            {
                value = value.ToLowerInvariant();
                if (value == "keep" || value == "exclusive" || value == "borderless" || value == "windowed") o.ScreenMode = value;
                else warn("screenMode must be keep, exclusive, borderless or windowed; keeping the game mode");
            }
            return o;
        }

        static int? Number(Dictionary<string, string> settings, string key, int min, int max, bool minusOne, Action<string> warn)
        {
            string value; int n;
            if (!settings.TryGetValue(key, out value) || value == "keep") return null;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) &&
                ((n >= min && n <= max) || (minusOne && n == -1))) return n;
            warn(key + " invalid; keeping the game value");
            return null;
        }
    }

    // Fixed bins, no per-frame allocation. These are update intervals, never GPU render durations.
    sealed class PresentationHistogram
    {
        static readonly double[] Limits = { 15, 16, 17, 17.5, 20, 25, 33.333333, 50 };
        public readonly int[] Counts = new int[9];
        public int Samples;
        public double TotalMs, WorstMs;
        public void Clear() { Array.Clear(Counts, 0, Counts.Length); Samples = 0; TotalMs = WorstMs = 0; }
        public void Add(double ms)
        {
            int bin = 0;
            while (bin < Limits.Length && ms > Limits[bin]) bin++;
            Counts[bin]++; Samples++; TotalMs += ms;
            if (ms > WorstMs) WorstMs = ms;
        }
        public string Summary()
        {
            return string.Format(CultureInfo.InvariantCulture, "n={0},bins={1},meanMs={2:0.000},maxMs={3:0.000}",
                Samples, string.Join(",", Counts), Samples == 0 ? 0 : TotalMs / Samples, WorstMs);
        }
    }
}
