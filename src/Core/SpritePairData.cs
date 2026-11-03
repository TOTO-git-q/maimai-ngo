using System;
using System.Collections.Generic;
using PartnerLive2D.Framework;

namespace PartnerLive2D.Core
{
    // Pure data/timing logic for the sprite backend. No Unity objects or frame allocations.
    public sealed class SpritePairManifest
    {
        public sealed class Drawing { public long Offset; public int Length, Hold; }
        public sealed class Clip
        {
            public string Name, Character, Group, Tier;
            public int Fps, TotalFrames, Width, Height;
            public float[] Rect;
            public Drawing[] Drawings;
        }
        public readonly List<Clip> Clips = new List<Clip>();
        public float CrossfadeSeconds;

        static double Number(object value, string field)
        {
            if (!(value is double)) throw new FormatException("missing number: " + field);
            double v = (double)value;
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException("invalid number: " + field);
            return v;
        }
        static int Int(object value, string field)
        {
            double n = Number(value, field);
            if (n < 0 || n > int.MaxValue || n != Math.Floor(n)) throw new FormatException("invalid integer: " + field);
            return (int)n;
        }
        static string Required(object node, string field)
        {
            string v = Json.GetString(node, field);
            if (string.IsNullOrEmpty(v)) throw new FormatException("missing string: " + field);
            return v;
        }
        public static SpritePairManifest Parse(string text)
        {
            object root = Json.Parse(text);
            if (Json.GetInt(root, "version", -1) != 1) throw new FormatException("unsupported sprite manifest version");
            var canvas = Json.GetList(root, "pairCanvas");
            var order = Json.GetList(root, "drawOrder");
            if (canvas == null || canvas.Count != 2 || Int(canvas[0], "canvas width") != 1024 || Int(canvas[1], "canvas height") != 1024 ||
                order == null || order.Count != 2 || !Equals(order[0], "Ame") || !Equals(order[1], "K")) throw new FormatException("invalid pair composition");
            var result = new SpritePairManifest();
            result.CrossfadeSeconds = (float)Number(Json.Get(root, "crossfadeSeconds"), "crossfadeSeconds");
            if (result.CrossfadeSeconds <= 0f || result.CrossfadeSeconds > 10f) throw new FormatException("invalid crossfade");
            var clips = Json.GetList(root, "clips");
            if (clips == null || clips.Count == 0) throw new FormatException("no sprite clips");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (object node in clips)
            {
                var clip = new Clip { Name = Required(node, "name"), Character = Required(node, "character"), Group = Required(node, "group"),
                    Tier = Json.GetString(node, "tier"), Fps = Int(Json.Get(node, "fps"), "fps"), TotalFrames = Int(Json.Get(node, "totalFrames"), "totalFrames"),
                    Width = Int(Json.Get(node, "textureWidth"), "textureWidth"), Height = Int(Json.Get(node, "textureHeight"), "textureHeight") };
                if (!names.Add(clip.Name) || clip.Name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || clip.Name.Contains("..") ||
                    (clip.Character != "K" && clip.Character != "Ame") || clip.Fps != 24 || clip.TotalFrames <= 0 ||
                    clip.Width <= 0 || clip.Height <= 0 || clip.Width > 8192 || clip.Height > 8192 || clip.Width % 4 != 0 || clip.Height % 4 != 0)
                    throw new FormatException("invalid sprite clip: " + clip.Name);
                var rect = Json.GetList(node, "rect");
                if (rect == null || rect.Count != 4) throw new FormatException("invalid rect");
                clip.Rect = new float[4];
                for (int i = 0; i < 4; i++) clip.Rect[i] = (float)Number(rect[i], "rect");
                if (clip.Rect[2] <= 0 || clip.Rect[3] <= 0) throw new FormatException("invalid rect size");
                var drawings = Json.GetList(node, "drawings");
                if (drawings == null || drawings.Count == 0) throw new FormatException("no drawings: " + clip.Name);
                clip.Drawings = new Drawing[drawings.Count];
                long offset = 0, frames = 0;
                for (int i = 0; i < drawings.Count; i++)
                {
                    object d = drawings[i];
                    double rawOffset = Number(Json.Get(d, "offset"), "offset");
                    int length = Int(Json.Get(d, "length"), "length"), hold = Int(Json.Get(d, "hold"), "hold");
                    if (rawOffset != offset || length != clip.Width * clip.Height || hold == 0) throw new FormatException("invalid drawing: " + clip.Name);
                    clip.Drawings[i] = new Drawing { Offset = offset, Length = length, Hold = hold };
                    offset += length; frames += hold;
                }
                if (frames != clip.TotalFrames) throw new FormatException("hold total mismatch: " + clip.Name);
                result.Clips.Add(clip);
            }
            return result;
        }
    }

    public static class SpritePairMath
    {
        public static int Pick(Random random, int count, int previous)
        {
            if (random == null || count <= 0) throw new ArgumentException("picker requires a random source and clips");
            if (count == 1) return 0;
            if (previous < 0 || previous >= count) return random.Next(count);
            int pick = random.Next(count - 1);
            return pick >= previous ? pick + 1 : pick;
        }
        public static int DrawingAt(SpritePairManifest.Clip clip, double seconds, bool loop)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException("seconds");
            double frame = Math.Floor(seconds * clip.Fps);
            if (loop) frame %= clip.TotalFrames;
            else if (frame >= clip.TotalFrames) frame = clip.TotalFrames - 1;
            int target = (int)frame;
            for (int i = 0; i < clip.Drawings.Length; i++)
            {
                if (target < clip.Drawings[i].Hold) return i;
                target -= clip.Drawings[i].Hold;
            }
            throw new InvalidOperationException("invalid drawing timeline");
        }
        // DrawingAt plus the drawing that follows and its fade-in weight over the last blendSeconds of the hold
        // (a loop wraps to drawing 0; a one-shot's last drawing has none: next = -1, weight 0). The clips are on
        // threes, so this softens the 8-per-second cadence without moving any drawing's own timing.
        public static int DrawingBlend(SpritePairManifest.Clip clip, double seconds, bool loop, float blendSeconds, out int next, out float weight)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException("seconds");
            double frame = seconds * clip.Fps;
            if (loop) frame %= clip.TotalFrames;
            else if (frame > clip.TotalFrames - 1) frame = clip.TotalFrames - 1;
            int end = 0;
            for (int i = 0; i < clip.Drawings.Length; i++)
            {
                end += clip.Drawings[i].Hold;
                if (frame >= end) continue;
                next = i + 1 < clip.Drawings.Length ? i + 1 : loop && i > 0 ? 0 : -1;
                double left = (end - frame) / clip.Fps;
                weight = next < 0 || blendSeconds <= 0 || left >= blendSeconds ? 0 : (float)(1 - left / blendSeconds);
                return i;
            }
            throw new InvalidOperationException("invalid drawing timeline");
        }
        public static float IncomingWeight(float elapsed, float duration)
        {
            if (duration <= 0) throw new ArgumentOutOfRangeException("duration");
            return elapsed <= 0 ? 0 : elapsed >= duration ? 1 : elapsed / duration;
        }
        // Achievement is the game's integer representation: 100 percent = 1,000,000.
        public static string Tier(bool cleared, uint achievement, bool allPerfect)
        {
            if (!cleared) return "Fail";
            if (allPerfect || achievement >= 1000000) return "Top";
            return achievement >= 970000 ? "Good" : "Clear";
        }
    }
}
