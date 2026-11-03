using System;
using System.Collections.Generic;
using System.IO;
using PartnerLive2D.Framework;

namespace PartnerLive2D.Core
{
    // The select page played from a frame library (built by E:\cxref\ngo-motion-v2\work\sprites\v2\library.py): every frame
    // of every take of each character, and where playback may jump between frames because the jump looks like an ordinary
    // next frame. Pure data and decisions; no Unity objects. Version 2 of the sprite manifest (version 1: SpritePairManifest).
    public sealed class SpriteLibrary
    {
        public const int Fps = 20;                       // one picture per 3 refreshes of the 60 Hz screen
        public const byte Idle = 0, Lead = 1, Body = 2, Tail = 3;
        // Bridge: a way from an idle frame to the official rest art (only taken on purpose, into a RestStart action);
        // RestStart: an action that starts on the official rest art (retime.py --rest)
        public sealed class Take { public string Name, Action; public int First, Count, Key = -1, HoldEnd = -1; public bool BrokenBody, Bridge, RestStart; }
        public sealed class Character
        {
            public string Name, Pack;
            public int Frames, TextureWidth, TextureHeight;
            // two in-betweens of a frame (1/3 and 2/3 of the way to the next one, drawn on the refreshes between pictures):
            // their place in the pack after the frames, -1 = none; PackFrames = frames + in-betweens
            public int[] Between;
            public int PackFrames;
            // per idle frame: the first frame of its bridge to the official rest art and the bridge's length, else -1 / 0
            public int[] BridgeAt, BridgeLen;
            public float[] Rect;                         // on the 1024 pair canvas, y down
            public float PivotX, PivotY, HalfHeight;     // rest silhouette centre and half height: the motion feel's pivot
            public float AxisX, AxisY = -1, Reach;       // from the centre toward her face, and the silhouette's reach along it
            // how soft each part of her is (0 thick middle .. 1 hair ends, ribbons, hem, feet) on a cells x cells grid over
            // the pair canvas (library.py flex_map); none: softer with the distance from the centre
            public float[] Flex; public int FlexCells;
            public float FlexAt(float x, float y)
            {
                if (Flex == null || FlexCells < 2)
                {
                    float d = (float)Math.Sqrt((x - PivotX) * (x - PivotX) + (y - PivotY) * (y - PivotY)) / Math.Max(1, Reach);
                    return Math.Max(0, Math.Min(1, d * .9f - .1f));
                }
                float cell = 1024f / FlexCells, gx = x / cell - .5f, gy = y / cell - .5f;
                int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy); float fx = gx - ix, fy = gy - iy;
                Func<int, int, float> at = (i, j) => Flex[Math.Max(0, Math.Min(FlexCells - 1, j)) * FlexCells + Math.Max(0, Math.Min(FlexCells - 1, i))];
                return (at(ix, iy) * (1 - fx) + at(ix + 1, iy) * fx) * (1 - fy) + (at(ix, iy + 1) * (1 - fx) + at(ix + 1, iy + 1) * fx) * fy;
            }
            public Take[] Takes;
            public int[] TakeOf, Nearest;
            public byte[] Phase;
            public bool[] Bad, Safe;
            // jumps per frame (cheapest first): idle -> idle (cut), action tail -> idle (cut within TauEntry, else fade)
            public int[] IdleStart, IdleTo, ExitStart, ExitTo;
            public float[] IdleCost, ExitCost;
            // per take: the idle frames with a way into its rest lead (ascending), where they enter, at what cost
            public int[] EntryStart, EntryFrom, EntryTo;
            public float[] EntryCost;
            public int Next(int f) { return f + 1 < Frames && TakeOf[f + 1] == TakeOf[f] ? f + 1 : -1; }
            public int FindEntry(int take, int frame)
            {
                int s = EntryStart[take], n = EntryStart[take + 1] - s;
                int k = n > 0 ? Array.BinarySearch(EntryFrom, s, n, frame) : -1;
                return k >= 0 ? k : -1;
            }
        }
        public sealed class Action
        {
            public string Name; public int Character; public int[] Takes;
            public readonly Dictionary<string, PairFeel.Effect[]> Feel = new Dictionary<string, PairFeel.Effect[]>();   // cue -> effects
        }
        public struct Pick { public int Action; public double Weight; }
        public sealed class Moment { public string Name; public int Character; public Pick[] Picks; public double Chance, Delay, Cooldown, Wait; public int Priority; public bool Soft; }

        public readonly Character[] Characters = new Character[2];   // 0 Ame (drawn first, behind), 1 K
        public readonly List<Action> Actions = new List<Action>();
        public readonly Pick[][] Random = new Pick[2][];
        public readonly List<Moment> Moments = new List<Moment>();
        public string[] HitMap;
        public float Tau, TauEntry, TauSoft, SoftWeight, IdleJump, RandomMin, RandomMax, RandomWait, FirstRandomMin, FirstRandomMax;
        public int JumpPenalty, MaxCuts, Replan, BlendTicks;

        public static int CharacterIndex(string name) { return name == "Ame" ? 0 : name == "K" ? 1 : -1; }
        public int ActionIndex(string name) { for (int i = 0; i < Actions.Count; i++) if (Actions[i].Name == name) return i; return -1; }
        public bool HasMoment(string name) { foreach (var m in Moments) if (m.Name == name) return true; return false; }

        // who is under a point of the pair canvas (0..1024, y down) at rest, silhouettes widened: 1 K (in front), 0 Ame, -1 nobody
        public int HitTest(float x, float y)
        {
            if (HitMap == null || HitMap.Length == 0) return -1;
            int cells = HitMap.Length;
            int cx = (int)Math.Floor(x / 1024f * cells), cy = (int)Math.Floor(y / 1024f * cells);
            if (cx < 0 || cy < 0 || cy >= cells || cx >= HitMap[cy].Length) return -1;
            char c = HitMap[cy][cx];
            return c == 'K' ? 1 : c == 'A' ? 0 : -1;
        }

        static double Number(object v, string field)
        {
            if (!(v is double)) throw new FormatException("missing number: " + field);
            double d = (double)v;
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException("invalid number: " + field);
            return d;
        }
        static int Int(object v, string field)
        {
            double d = Number(v, field);
            if (d < -1 || d > int.MaxValue || d != Math.Floor(d)) throw new FormatException("invalid integer: " + field);
            return (int)d;
        }
        static string SafeName(string s, string field)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || s.Contains(".."))
                throw new FormatException("invalid file name: " + field);
            return s;
        }

        public static SpriteLibrary Load(string path)
        {
            string folder = System.IO.Path.GetDirectoryName(path);
            return Parse(File.ReadAllText(path), name => File.ReadAllBytes(System.IO.Path.Combine(folder, SafeName(name, "graph"))));
        }

        public static SpriteLibrary Parse(string text, Func<string, byte[]> readFile)
        {
            object root = Json.Parse(text);
            if (Json.GetInt(root, "version", -1) != 2) throw new FormatException("unsupported sprite library version");
            if (Json.GetInt(root, "fps", -1) != Fps) throw new FormatException("sprite library must be 20 pictures per second");
            var canvas = Json.GetList(root, "pairCanvas");
            if (canvas == null || canvas.Count != 2 || Int(canvas[0], "canvas") != 1024 || Int(canvas[1], "canvas") != 1024) throw new FormatException("invalid pair canvas");
            var lib = new SpriteLibrary();
            object t = Json.Get(root, "tuning");
            lib.Tau = (float)Number(Json.Get(t, "tau"), "tau"); lib.TauEntry = (float)Number(Json.Get(t, "tauEntry"), "tauEntry");
            lib.TauSoft = (float)Number(Json.Get(t, "tauSoft"), "tauSoft"); lib.SoftWeight = (float)Number(Json.Get(t, "softWeight"), "softWeight");
            lib.JumpPenalty = Int(Json.Get(t, "jumpPenalty"), "jumpPenalty"); lib.MaxCuts = Int(Json.Get(t, "maxCuts"), "maxCuts");
            lib.Replan = Math.Max(1, Int(Json.Get(t, "replan"), "replan")); lib.IdleJump = (float)Number(Json.Get(t, "idleJump"), "idleJump");
            lib.BlendTicks = Int(Json.Get(t, "blendTicks"), "blendTicks");
            lib.RandomMin = (float)Number(Json.Get(t, "randomMin"), "randomMin"); lib.RandomMax = (float)Number(Json.Get(t, "randomMax"), "randomMax");
            lib.RandomWait = (float)Number(Json.Get(t, "randomWait"), "randomWait");
            var first = Json.GetList(t, "firstRandom");
            if (first == null || first.Count != 2) throw new FormatException("firstRandom");
            lib.FirstRandomMin = (float)Number(first[0], "firstRandom"); lib.FirstRandomMax = (float)Number(first[1], "firstRandom");
            if (lib.MaxCuts < 0 || lib.MaxCuts > 4 || lib.BlendTicks < 1 || lib.BlendTicks > 10 || lib.RandomMax < lib.RandomMin) throw new FormatException("invalid tuning");

            var chars = Json.GetList(root, "characters");
            if (chars == null || chars.Count != 2) throw new FormatException("expected two characters");
            foreach (object node in chars)
            {
                var c = new Character { Name = Json.GetString(node, "name") };
                int ci = CharacterIndex(c.Name);
                if (ci < 0 || lib.Characters[ci] != null) throw new FormatException("invalid character");
                c.Pack = SafeName(Json.GetString(node, "pack"), "pack");
                c.Frames = Int(Json.Get(node, "frames"), "frames");
                var tex = Json.GetList(node, "texture"); var rect = Json.GetList(node, "rect");
                if (tex == null || tex.Count != 2 || rect == null || rect.Count != 4) throw new FormatException("texture/rect");
                c.TextureWidth = Int(tex[0], "texture"); c.TextureHeight = Int(tex[1], "texture");
                if (c.TextureWidth <= 0 || c.TextureHeight <= 0 || c.TextureWidth > 4096 || c.TextureHeight > 4096 || c.TextureWidth % 4 != 0 || c.TextureHeight % 4 != 0)
                    throw new FormatException("invalid texture size");
                c.Rect = new float[4];
                for (int i = 0; i < 4; i++) c.Rect[i] = (float)Number(rect[i], "rect");
                var pivot = Json.GetList(node, "pivot"); var extent = Json.GetList(node, "extent");
                c.PivotX = pivot != null && pivot.Count == 2 ? (float)Number(pivot[0], "pivot") : c.Rect[0] + c.Rect[2] / 2;
                c.PivotY = pivot != null && pivot.Count == 2 ? (float)Number(pivot[1], "pivot") : c.Rect[1] + c.Rect[3] / 2;
                c.HalfHeight = extent != null && extent.Count == 2 ? (float)Number(extent[1], "extent") / 2 : c.Rect[3] / 2;
                var axis = Json.GetList(node, "axis");
                if (axis != null && axis.Count == 2) { c.AxisX = (float)Number(axis[0], "axis"); c.AxisY = (float)Number(axis[1], "axis"); }
                c.Reach = Json.Get(node, "reach") is double ? (float)Number(Json.Get(node, "reach"), "reach") : c.HalfHeight;
                var flex = Json.Get(node, "flex");
                if (flex != null)
                {
                    int cells = Int(Json.Get(flex, "cells"), "flex cells"); var values = Json.GetList(flex, "values");
                    if (cells < 2 || cells > 128 || values == null || values.Count != cells * cells) throw new FormatException("flex");
                    c.FlexCells = cells; c.Flex = new float[values.Count];
                    for (int i = 0; i < values.Count; i++) c.Flex[i] = Math.Max(0, Math.Min(1, (float)Number(values[i], "flex")));
                }
                var takes = Json.GetList(node, "takes");
                if (takes == null || takes.Count == 0) throw new FormatException("no takes");
                c.Takes = new Take[takes.Count];
                int at = 0;
                for (int k = 0; k < takes.Count; k++)
                {
                    object tn = takes[k];
                    var take = new Take { Name = Json.GetString(tn, "name"), Action = Json.GetString(tn, "action"),
                        First = Int(Json.Get(tn, "first"), "first"), Count = Int(Json.Get(tn, "count"), "count"),
                        Bridge = Json.GetBool(tn, "bridge"), RestStart = Json.GetBool(tn, "restStart") };
                    if (take.Action != null) { take.Key = Int(Json.Get(tn, "key"), "key"); take.HoldEnd = Int(Json.Get(tn, "holdEnd"), "holdEnd"); }
                    if (take.First != at || take.Count <= 0) throw new FormatException("takes must cover the frames in order");
                    at += take.Count; c.Takes[k] = take;
                }
                if (at != c.Frames) throw new FormatException("take frame total mismatch");
                ReadGraph(c, readFile(SafeName(Json.GetString(node, "graph"), "graph")));
                c.PackFrames = c.Frames;
                var between = Json.GetString(node, "between");
                if (between != null)
                {
                    if (between.Length != c.Frames) throw new FormatException("between: one flag per frame");
                    c.Between = new int[c.Frames];
                    for (int f = 0; f < c.Frames; f++)
                    {
                        if (between[f] != '0' && between[f] != '1') throw new FormatException("between: 0 or 1");
                        c.Between[f] = -1;
                        if (between[f] == '1') { c.Between[f] = c.PackFrames; c.PackFrames += 2; }
                    }
                    for (int f = 0; f < c.Frames; f++) if (c.Next(f) < 0) c.Between[f] = -1;   // a take's last frame leads nowhere
                }
                var bridges = Json.GetList(node, "bridges");
                if (bridges != null && bridges.Count > 0)
                {
                    c.BridgeAt = new int[c.Frames]; c.BridgeLen = new int[c.Frames];
                    for (int f = 0; f < c.Frames; f++) c.BridgeAt[f] = -1;
                    foreach (object bn in bridges)
                    {
                        var b = bn as List<object>;
                        if (b == null || b.Count != 3) throw new FormatException("bridge");
                        int from = Int(b[0], "bridge"), bfirst = Int(b[1], "bridge"), count = Int(b[2], "bridge");
                        if (from < 0 || from >= c.Frames || bfirst < 0 || count < 1 || bfirst + count > c.Frames || !c.Safe[from]) throw new FormatException("bridge");
                        var bt = c.Takes[c.TakeOf[bfirst]];
                        if (!bt.Bridge || bt.First != bfirst || bt.Count != count) throw new FormatException("bridge take");
                        c.BridgeAt[from] = bfirst; c.BridgeLen[from] = count;
                    }
                }
                foreach (var take in c.Takes)
                    for (int f = take.First; f < take.First + take.Count; f++) if (c.Bad[f] && c.Phase[f] == Body) take.BrokenBody = true;
                lib.Characters[ci] = c;
            }
            var actions = Json.Get(root, "actions") as JsonObject;
            if (actions == null || actions.Count == 0) throw new FormatException("no actions");
            foreach (string name in actions.KeyOrder)
            {
                object an = actions[name];
                var a = new Action { Name = name, Character = CharacterIndex(Json.GetString(an, "character")) };
                var takes = Json.GetList(an, "takes");
                if (a.Character < 0 || takes == null || takes.Count == 0) throw new FormatException("invalid action " + name);
                var ch = lib.Characters[a.Character];
                a.Takes = new int[takes.Count];
                for (int i = 0; i < takes.Count; i++)
                {
                    int k = Array.FindIndex(ch.Takes, x => x.Name == takes[i] as string);
                    if (k < 0 || ch.Takes[k].Action != name) throw new FormatException("action " + name + ": unknown take");
                    a.Takes[i] = k;
                }
                var feel = Json.Get(an, "feel") as JsonObject;
                if (feel != null)
                    foreach (string cue in feel.KeyOrder)
                    {
                        var list = feel[cue] as List<object>;
                        if (list == null) throw new FormatException("action " + name + ": feel " + cue);
                        var effects = new PairFeel.Effect[list.Count];
                        for (int i = 0; i < list.Count; i++)
                        {
                            var pair = list[i] as List<object>;
                            if (pair == null || pair.Count != 2 || !(pair[0] is string)) throw new FormatException("action " + name + ": feel " + cue);
                            effects[i] = new PairFeel.Effect { Name = (string)pair[0], Strength = (float)Number(pair[1], "feel strength") };
                        }
                        a.Feel[cue] = effects;
                    }
                lib.Actions.Add(a);
            }
            var random = Json.Get(root, "random");
            for (int c = 0; c < 2; c++) lib.Random[c] = Picks(lib, Json.GetList(random, lib.Characters[c].Name), c, "random");
            var moments = Json.GetList(root, "moments");
            if (moments == null) throw new FormatException("no moments");
            foreach (object mn in moments)
            {
                var m = new Moment { Name = Json.GetString(mn, "moment"), Character = CharacterIndex(Json.GetString(mn, "character")),
                    Chance = Number(Json.Get(mn, "chance"), "chance"), Delay = Number(Json.Get(mn, "delay"), "delay"),
                    Cooldown = Number(Json.Get(mn, "cooldown"), "cooldown"), Wait = Number(Json.Get(mn, "wait"), "wait"),
                    Priority = Int(Json.Get(mn, "priority"), "priority"), Soft = Json.GetBool(mn, "soft") };
                if (string.IsNullOrEmpty(m.Name) || m.Character < 0 || m.Chance < 0 || m.Delay < 0 || m.Cooldown < 0 || m.Wait < 0 || m.Priority < 1)
                    throw new FormatException("invalid moment");
                m.Picks = Picks(lib, Json.GetList(mn, "picks"), m.Character, m.Name);
                lib.Moments.Add(m);
            }
            var hit = Json.GetList(root, "hitmap");
            if (hit != null) { lib.HitMap = new string[hit.Count]; for (int i = 0; i < hit.Count; i++) lib.HitMap[i] = hit[i] as string ?? ""; }
            return lib;
        }

        static Pick[] Picks(SpriteLibrary lib, List<object> list, int character, string what)
        {
            if (list == null || list.Count == 0) throw new FormatException("no picks: " + what);
            var picks = new Pick[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var pair = list[i] as List<object>;
                if (pair == null || pair.Count != 2) throw new FormatException("invalid pick: " + what);
                int a = lib.ActionIndex(pair[0] as string);
                if (a < 0 || lib.Actions[a].Character != character) throw new FormatException("unknown action in " + what);
                picks[i] = new Pick { Action = a, Weight = Number(pair[1], "weight") };
                if (picks[i].Weight <= 0) throw new FormatException("invalid weight: " + what);
            }
            return picks;
        }

        static void ReadGraph(Character c, byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data, false)))
            {
                if (r.ReadByte() != 'N' || r.ReadByte() != 'G' || r.ReadByte() != 'O' || r.ReadByte() != 'G') throw new FormatException("not a frame graph");
                int version = r.ReadInt32(), n = r.ReadInt32(), takes = r.ReadInt32();
                if (version != 1 || n != c.Frames || takes != c.Takes.Length) throw new FormatException("graph does not match the takes");
                c.Phase = new byte[n]; c.Bad = new bool[n]; c.Safe = new bool[n]; c.TakeOf = new int[n];
                var flags = r.ReadBytes(n);
                if (flags.Length != n) throw new EndOfStreamException();
                for (int i = 0; i < n; i++)
                {
                    c.Phase[i] = (byte)(flags[i] & 15); c.Bad[i] = (flags[i] & 16) != 0; c.Safe[i] = (flags[i] & 32) != 0;
                    if (c.Phase[i] > Tail) throw new FormatException("invalid phase");
                }
                for (int k = 0; k < c.Takes.Length; k++) for (int f = c.Takes[k].First; f < c.Takes[k].First + c.Takes[k].Count; f++) c.TakeOf[f] = k;
                c.Nearest = Ints(r, n, n);
                c.IdleStart = Starts(r, n + 1); c.IdleTo = Ints(r, c.IdleStart[n], n); c.IdleCost = Floats(r, c.IdleStart[n]);
                c.ExitStart = Starts(r, n + 1); c.ExitTo = Ints(r, c.ExitStart[n], n); c.ExitCost = Floats(r, c.ExitStart[n]);
                c.EntryStart = Starts(r, takes + 1);
                int m = c.EntryStart[takes];
                c.EntryFrom = Ints(r, m, n); c.EntryTo = Ints(r, m, n); c.EntryCost = Floats(r, m);
                if (r.BaseStream.Position != r.BaseStream.Length) throw new FormatException("trailing graph data");
                foreach (int j in c.IdleTo) if (!c.Safe[j]) throw new FormatException("idle jump to an unsafe frame");
                foreach (int j in c.ExitTo) if (!c.Safe[j]) throw new FormatException("exit to an unsafe frame");
                for (int k = 0; k < takes; k++)
                    for (int e = c.EntryStart[k]; e < c.EntryStart[k + 1]; e++)
                        if (c.TakeOf[c.EntryTo[e]] != k || (e > c.EntryStart[k] && c.EntryFrom[e] <= c.EntryFrom[e - 1])) throw new FormatException("invalid entry table");
            }
        }
        static int[] Starts(BinaryReader r, int count)
        {
            var a = new int[count];
            for (int i = 0; i < count; i++) { a[i] = r.ReadInt32(); if (a[i] < 0 || (i > 0 && a[i] < a[i - 1])) throw new FormatException("invalid table start"); }
            if (a[0] != 0) throw new FormatException("invalid table start");
            return a;
        }
        static int[] Ints(BinaryReader r, int count, int limit)
        {
            var a = new int[count];
            for (int i = 0; i < count; i++) { a[i] = r.ReadInt32(); if (a[i] < 0 || a[i] >= limit) throw new FormatException("frame index out of range"); }
            return a;
        }
        static float[] Floats(BinaryReader r, int count)
        {
            var a = new float[count];
            for (int i = 0; i < count; i++) { a[i] = r.ReadSingle(); if (float.IsNaN(a[i]) || a[i] < 0) throw new FormatException("invalid cost"); }
            return a;
        }
    }

    // Both characters' playback decisions, one picture at a time (port of v2/director.py, plus the select page's moments).
    // A state is cloned for every picture decided ahead (SpriteTimeline), so a new request can replay from a recent one.
    public sealed class SpriteDirector
    {
        public struct Shot { public int Frame, Under; public float Weight; }   // Under: the frame faded out under it (-1: none)
        sealed class Request
        {
            public int Take, Priority, Asked, Wait;
            public bool Soft;                       // when no way in is found in time, fade straight in
            public string Moment;
            public int[] Path; public int At, Entry; public bool EntryFade, Bridged;   // planned: frames to walk, then the way in
            public Request Clone() { return (Request)MemberwiseClone(); }
        }
        sealed class Player
        {
            public int Cur, ActTake = -1, ActPriority, SinceJump, BlendFrom = -1, BlendLeft, Last1 = -1, Last2 = -1;
            public string ActMoment;
            public Request Req;
            public Player Clone() { var p = (Player)MemberwiseClone(); if (Req != null) p.Req = Req.Clone(); return p; }
        }
        struct Delayed { public int Due, Moment, Action; }
        // breadth-first search scratch, per character (never part of a state)
        sealed class Scratch { public int[] Seen, Prev, Layer, NextLayer; public float[] Cost; public int Stamp; }

        readonly SpriteLibrary _lib;
        readonly Scratch[] _scratch;
        Player[] _players;
        List<Delayed> _pending;
        int[] _momentLast;
        ulong _rng;
        int _nextRandom, _turn;
        public int Tick { get; private set; }

        public SpriteDirector(SpriteLibrary lib, int seed)
        {
            _lib = lib;
            _players = new Player[2]; _pending = new List<Delayed>();
            _rng = (ulong)(uint)seed * 6364136223846793005UL + 1442695040888963407UL; if (_rng == 0) _rng = 1;
            _scratch = new Scratch[2];
            for (int c = 0; c < 2; c++)
            {
                int states = lib.Characters[c].Frames * (lib.MaxCuts + 1);
                _scratch[c] = new Scratch { Seen = new int[states], Prev = new int[states], Layer = new int[states], NextLayer = new int[states], Cost = new float[lib.Characters[c].Frames] };
                var ch = lib.Characters[c];
                var starts = new List<int>();
                foreach (var t in ch.Takes) if (t.Action == null && ch.Safe[t.First]) starts.Add(t.First);
                if (starts.Count == 0) for (int f = 0; f < ch.Frames; f++) if (ch.Safe[f]) { starts.Add(f); break; }
                if (starts.Count == 0) throw new FormatException(ch.Name + ": no idle frame to start from");
                _players[c] = new Player { Cur = starts[(int)(NextDouble() * starts.Count) % starts.Count] };
            }
            _momentLast = new int[lib.Moments.Count];
            for (int i = 0; i < _momentLast.Length; i++) _momentLast[i] = int.MinValue / 2;
            _nextRandom = Seconds(lib.FirstRandomMin + NextDouble() * (lib.FirstRandomMax - lib.FirstRandomMin));
            _turn = NextDouble() < .5 ? 0 : 1;
        }
        SpriteDirector(SpriteDirector o)
        {
            _lib = o._lib; _scratch = o._scratch; _rng = o._rng; _nextRandom = o._nextRandom; _turn = o._turn; Tick = o.Tick;
            _players = new[] { o._players[0].Clone(), o._players[1].Clone() };
            _pending = new List<Delayed>(o._pending);
            _momentLast = (int[])o._momentLast.Clone();
        }
        public SpriteDirector Clone() { return new SpriteDirector(this); }

        double NextDouble()
        {
            _rng ^= _rng >> 12; _rng ^= _rng << 25; _rng ^= _rng >> 27;
            return ((_rng * 2685821657736338717UL) >> 11) * (1.0 / (1UL << 53));
        }
        static int Seconds(double s) { return (int)Math.Round(s * SpriteLibrary.Fps); }

        // ---- state queries
        public int Frame(int c) { return _players[c].Cur; }
        public string ActionOf(int c) { var p = _players[c]; return p.ActTake < 0 ? null : _lib.Characters[c].Takes[p.ActTake].Name; }
        // a moment counts as playing while it waits for its way in, and in its action until the key pose has been held
        public bool Playing(string moment)
        {
            foreach (var d in _pending) if (_lib.Moments[d.Moment].Name == moment) return true;
            for (int c = 0; c < 2; c++)
            {
                var p = _players[c];
                if (p.Req != null && p.Req.Moment == moment) return true;
                if (p.ActTake >= 0 && p.ActMoment == moment)
                {
                    var take = _lib.Characters[c].Takes[p.ActTake];
                    if (p.Cur - take.First < take.HoldEnd) return true;
                }
            }
            return false;
        }

        // ---- requests
        int PickAction(SpriteLibrary.Pick[] picks)
        {
            double total = 0; foreach (var p in picks) total += p.Weight;
            double r = NextDouble() * total;
            foreach (var p in picks) { r -= p.Weight; if (r < 0) return p.Action; }
            return picks[picks.Length - 1].Action;
        }
        int PickTake(int c, int action)
        {
            var takes = _lib.Actions[action].Takes; var ch = _lib.Characters[c]; var p = _players[c];
            var good = new List<int>();
            foreach (int k in takes) if (!ch.Takes[k].BrokenBody) good.Add(k);
            if (good.Count == 0) good.AddRange(takes);
            var fresh = good.FindAll(k => k != p.Last1 && k != p.Last2);
            var pool = fresh.Count > 0 ? fresh : good;
            return pool[(int)(NextDouble() * pool.Count) % pool.Count];
        }
        bool Ask(int c, int action, string moment, int priority, double wait, bool soft, List<string> log)
        {
            var p = _players[c];
            if (p.ActTake >= 0 && priority <= p.ActPriority) { Note(log, c, "busy, skips " + moment); return false; }
            if (p.Req != null && priority < p.Req.Priority) { Note(log, c, "already waiting for " + p.Req.Moment + ", skips " + moment); return false; }
            int take = PickTake(c, action);
            p.Req = new Request { Take = take, Priority = priority, Asked = Tick, Wait = Math.Max(0, Seconds(wait)), Soft = soft, Moment = moment };
            Note(log, c, moment + " wants " + _lib.Characters[c].Takes[take].Name);
            return true;
        }
        // a select-page moment (enter, return, song, category, master, confirm, countdown, start, tap); who: -1 both, 0 Ame, 1 K
        public bool Moment(string name, int who, List<string> log = null)
        {
            bool any = false;
            for (int m = 0; m < _lib.Moments.Count; m++)
            {
                var mo = _lib.Moments[m];
                if (mo.Name != name || (who >= 0 && mo.Character != who)) continue;
                if (Tick < _momentLast[m] + Seconds(mo.Cooldown)) continue;
                if (mo.Chance < 1 && NextDouble() >= mo.Chance) continue;
                _momentLast[m] = Tick;
                int action = PickAction(mo.Picks), delay = Seconds(mo.Delay);
                if (delay > 0) { _pending.Add(new Delayed { Due = Tick + delay, Moment = m, Action = action }); any = true; }
                else any |= Ask(mo.Character, action, mo.Name, mo.Priority, mo.Wait, mo.Soft, log);
            }
            return any;
        }

        // ---- one picture
        public void Step(Shot[] shots, List<string> log = null)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var d = _pending[i];
                if (d.Due > Tick) continue;
                var mo = _lib.Moments[d.Moment];
                Ask(mo.Character, d.Action, mo.Name, mo.Priority, mo.Wait, mo.Soft, log);
                _pending.RemoveAt(i--);
            }
            bool busy = _pending.Count > 0;
            foreach (var p in _players) if (p.ActTake >= 0 || p.Req != null) busy = true;
            if (busy) _nextRandom = Math.Max(_nextRandom, Tick + Seconds(4));         // a few calm seconds after anything
            else if (Tick >= _nextRandom)
            {
                Ask(_turn, PickAction(_lib.Random[_turn]), "random", 0, _lib.RandomWait, false, log);
                _turn = 1 - _turn;
                _nextRandom = Tick + Seconds(_lib.RandomMin + NextDouble() * (_lib.RandomMax - _lib.RandomMin));
            }
            for (int c = 0; c < 2; c++)
            {
                TickPlayer(c, log);
                var p = _players[c];
                shots[c] = new Shot { Frame = p.Cur, Under = p.BlendLeft > 0 ? p.BlendFrom : -1,
                    Weight = p.BlendLeft > 0 ? 1f - p.BlendLeft / (float)(_lib.BlendTicks + 1) : 1f };
            }
            Tick++;
        }

        void Note(List<string> log, int c, string what)
        {
            if (log != null) log.Add(string.Format("{0:0.00} {1} {2}", Tick / (double)SpriteLibrary.Fps, _lib.Characters[c].Name, what));
        }
        void Go(int c, int j, bool fade)
        {
            var p = _players[c];
            if (fade) { p.BlendFrom = p.Cur; p.BlendLeft = _lib.BlendTicks; }
            p.Cur = j; p.SinceJump = 0;
        }
        void Enter(int c, int j, bool fade, List<string> log, string how)
        {
            var p = _players[c]; var r = p.Req;
            p.ActTake = r.Take; p.ActPriority = r.Priority; p.ActMoment = r.Moment; p.Req = null;
            p.Last2 = p.Last1; p.Last1 = r.Take;
            Note(log, c, string.Format("enters {0} after {1:0.00} s ({2})", _lib.Characters[c].Takes[r.Take].Name, (Tick - r.Asked) / (double)SpriteLibrary.Fps, how));
            Go(c, j, fade);
        }
        // straight into the requested take's rest lead, faded: the way in from here if there is one, else its first good lead frame
        void EnterFaded(int c, List<string> log)
        {
            var p = _players[c]; var ch = _lib.Characters[c]; var take = ch.Takes[p.Req.Take];
            int k = ch.FindEntry(p.Req.Take, p.Cur), j = k >= 0 ? ch.EntryTo[k] : -1;
            for (int f = take.First; j < 0 && f < take.First + take.Count; f++) if (!ch.Bad[f]) j = f;
            Enter(c, j, true, log, k >= 0 ? "fade, its way in from here" : "fade, no way in in time");
        }
        void Leave(int c, int j, bool fade, List<string> log)
        {
            var p = _players[c];
            Note(log, c, "back to idle" + (fade ? " (fade)" : ""));
            p.ActTake = -1; p.ActMoment = null; p.ActPriority = 0;
            Go(c, j, fade);
        }
        int PickJump(int c, int[] to, float[] cost, int s, int e, bool preferOther)
        {
            var ch = _lib.Characters[c]; int from = ch.TakeOf[_players[c].Cur];
            double total = 0;
            for (int k = s; k < e; k++) total += Math.Exp(-cost[k]) * (preferOther && ch.TakeOf[to[k]] != from ? 2 : 1);
            double r = NextDouble() * total;
            for (int k = s; k < e; k++) { r -= Math.Exp(-cost[k]) * (preferOther && ch.TakeOf[to[k]] != from ? 2 : 1); if (r < 0) return to[k]; }
            return to[e - 1];
        }

        void TickPlayer(int c, List<string> log)
        {
            var p = _players[c]; var ch = _lib.Characters[c];
            p.SinceJump++;
            if (p.BlendLeft > 0)
            {
                p.BlendLeft--;
                // the picture faded out keeps playing under the new one (a film dissolve), instead of a frozen double image
                // (owner 2026-11-02 evening: "切换时有重影")
                int under = ch.Next(p.BlendFrom);
                if (under >= 0 && !ch.Bad[under]) p.BlendFrom = under;
            }
            if (p.ActTake < 0) { IdleTick(c, log); return; }
            // a more important request waits for the current action at most its wait, then fades in over it
            if (p.Req != null && ch.Phase[p.Cur] != SpriteLibrary.Tail && Tick - p.Req.Asked >= p.Req.Wait)
            {
                if (p.Req.Soft) { EnterFaded(c, log); return; }
                Note(log, c, "no time for " + p.Req.Moment); p.Req = null;
            }
            ActionTick(c, log);
        }

        void IdleTick(int c, List<string> log)
        {
            var p = _players[c]; var ch = _lib.Characters[c]; int i = p.Cur;
            var r = p.Req;
            if (r != null)
            {
                int waited = Tick - r.Asked;
                if (r.Path == null && (waited % _lib.Replan == 0 || r.Soft))
                {
                    int left = r.Wait - waited;
                    Plan(c, r, Math.Max(0, left));
                    if (r.Path == null)
                    {
                        if (r.Soft) { EnterFaded(c, log); return; }                 // nothing in time: fade straight in
                        if (left <= 0)
                        {
                            Note(log, c, "no way in, skips " + r.Moment); p.Req = null;
                            if (r.Priority == 0) { _nextRandom = Tick + Seconds(2.5); _turn = c; }   // try her again soon
                        }
                    }
                }
                if (p.Req != null && r.Path != null)
                {
                    if (r.At < r.Path.Length)
                    {
                        int g = r.Path[r.At++];
                        if (g == ch.Next(i)) p.Cur = g; else Go(c, g, false);
                        return;
                    }
                    Enter(c, r.Entry, r.EntryFade, log, (r.EntryFade ? "fade" : r.Bridged ? "cut over a bridge" : "cut") + " after walking " + r.Path.Length + " pictures");
                    return;
                }
            }
            int nat = ch.Next(i);
            if (nat >= 0 && !ch.Safe[nat]) nat = -1;
            int s = ch.IdleStart[i], e = ch.IdleStart[i + 1];
            if (nat < 0 && e > s) { Go(c, PickJump(c, ch.IdleTo, ch.IdleCost, s, e, false), false); return; }
            if (nat < 0) { Go(c, ch.Nearest[i], true); return; }                       // should not happen: safe frames go on
            if (e > s && p.SinceJump > 2 * SpriteLibrary.Fps && NextDouble() < _lib.IdleJump)
            {
                Go(c, PickJump(c, ch.IdleTo, ch.IdleCost, s, e, true), false); return;
            }
            p.Cur = nat;
        }

        void ActionTick(int c, List<string> log)
        {
            var p = _players[c]; var ch = _lib.Characters[c]; int i = p.Cur, nat = ch.Next(i);
            if (ch.Phase[i] == SpriteLibrary.Tail || nat < 0)
            {
                int s = ch.ExitStart[i], e = ch.ExitStart[i + 1], hard = s;
                while (hard < e && ch.ExitCost[hard] <= _lib.TauEntry) hard++;              // cheapest first: cuts are a prefix
                if (hard > s) { Leave(c, PickJump(c, ch.ExitTo, ch.ExitCost, s, hard, false), false, log); return; }
                if (nat < 0 || ch.Bad[nat]) { Leave(c, e > s ? ch.ExitTo[s] : ch.Nearest[i], true, log); return; }
                p.Cur = nat; return;
            }
            if (nat >= 0 && ch.Bad[nat])
            {
                // never show a broken frame: on to the next good one of the take (a skip of one or two reads as speed)
                int j = nat;
                while (j >= 0 && ch.Bad[j]) j = ch.Next(j);
                if (j >= 0) { Go(c, j, j - nat > 2); return; }
                Leave(c, ch.Nearest[i], true, log); return;
            }
            p.Cur = nat;
        }

        // Walk through idle frames from the current one to a frame with a way into the take's rest lead, within maxTicks
        // pictures, at most MaxCuts cuts between takes. Score: pictures waited + JumpPenalty per cut + SoftWeight per
        // neighbour step the way in costs beyond a cut (a fade). Breadth first, so each state keeps its earliest arrival.
        // A take that starts on the official rest art (RestStart) can also be come into over a bridge (library.py
        // bridges.py, 2026-11-02 evening: the idle loops pass close to the rest art only every few seconds, so most ways in
        // were fades): walk to an idle frame with a bridge, play the bridge (a morph to the rest art), cut in. The bridge's
        // pictures count as time; a plain cut of the same length is preferred.
        void Plan(int c, Request r, int maxTicks)
        {
            var ch = _lib.Characters[c]; var sc = _scratch[c]; int cuts1 = _lib.MaxCuts + 1;
            r.Path = null;
            var take = ch.Takes[r.Take];
            bool bridged = take.RestStart && ch.BridgeAt != null;
            int es = ch.EntryStart[r.Take], ee = ch.EntryStart[r.Take + 1];
            if (es == ee && !bridged) return;
            if (++sc.Stamp == int.MaxValue) { Array.Clear(sc.Seen, 0, sc.Seen.Length); sc.Stamp = 1; }
            int stamp = sc.Stamp;
            int start = _players[c].Cur * cuts1;
            sc.Seen[start] = stamp; sc.Prev[start] = -1;
            int layerCount = 1; sc.Layer[0] = start;
            int best = -1, bestEntry = -1, bestBridge = -1; float bestScore = float.MaxValue;   // bestBridge -2: at a bridge's end
            for (int d = 0; d <= maxTicks && layerCount > 0 && d < bestScore; d++)
            {
                for (int q = 0; q < layerCount; q++)
                {
                    int s = sc.Layer[q], f = s / cuts1, cuts = s % cuts1;
                    int k = ch.FindEntry(r.Take, f);
                    // a random move is in no hurry: it only ever cuts in (it waits for that, or is skipped)
                    if (k >= 0 && (r.Priority > 0 || ch.EntryCost[k] <= _lib.TauEntry))
                    {
                        float score = d + _lib.JumpPenalty * cuts + _lib.SoftWeight * Math.Max(0f, ch.EntryCost[k] - _lib.TauEntry);
                        if (score < bestScore) { bestScore = score; best = s; bestEntry = k; bestBridge = -1; }
                    }
                    if (!bridged) continue;
                    if (ch.BridgeAt[f] >= 0)
                    {
                        // the bridge's pictures already move toward the action, so they cost like one picture of waiting -
                        // except for an urgent moment (start: 0.3 s), where the time to the action counts in full
                        float score = d + _lib.JumpPenalty * cuts + (r.Wait <= Seconds(.4) ? ch.BridgeLen[f] + 1 : 1);
                        if (score < bestScore) { bestScore = score; best = s; bestEntry = -1; bestBridge = f; }
                    }
                    else if (ch.Takes[ch.TakeOf[f]].Bridge && ch.Next(f) < 0)     // already on a bridge, at its end
                    {
                        float score = d + _lib.JumpPenalty * cuts;
                        if (score < bestScore) { bestScore = score; best = s; bestEntry = -1; bestBridge = -2; }
                    }
                }
                if (d == maxTicks) break;
                int nextCount = 0;
                for (int q = 0; q < layerCount; q++)
                {
                    int s = sc.Layer[q], f = s / cuts1, cuts = s % cuts1;
                    int n = ch.Next(f);
                    // on through idle, or along a bridge one is already on (a new request while walking one)
                    if (n >= 0 && (ch.Safe[n] || ch.Takes[ch.TakeOf[f]].Bridge)) Visit(sc, n * cuts1 + cuts, s, stamp, ref nextCount);
                    if (cuts < _lib.MaxCuts)
                        for (int j = ch.IdleStart[f]; j < ch.IdleStart[f + 1]; j++) Visit(sc, ch.IdleTo[j] * cuts1 + cuts + 1, s, stamp, ref nextCount);
                }
                var tmp = sc.Layer; sc.Layer = sc.NextLayer; sc.NextLayer = tmp; layerCount = nextCount;
            }
            if (best < 0) return;
            var path = new List<int>();
            for (int s = best; s != start; s = sc.Prev[s]) path.Add(s / cuts1);
            path.Reverse();
            if (bestBridge != -1)
            {
                if (bestBridge >= 0) for (int b = 0; b < ch.BridgeLen[bestBridge]; b++) path.Add(ch.BridgeAt[bestBridge] + b);
                // the bridge ends on the rest art, which is also the action's first picture: in from its second
                r.Entry = take.Count > 1 && !ch.Bad[take.First + 1] ? take.First + 1 : take.First; r.EntryFade = false; r.Bridged = true;
            }
            else { r.Entry = ch.EntryTo[bestEntry]; r.EntryFade = ch.EntryCost[bestEntry] > _lib.TauEntry; r.Bridged = false; }
            r.Path = path.ToArray(); r.At = 0;
        }
        static void Visit(Scratch sc, int state, int from, int stamp, ref int count)
        {
            if (sc.Seen[state] == stamp) return;
            sc.Seen[state] = stamp; sc.Prev[state] = from; sc.NextLayer[count++] = state;
        }
    }

    // The pictures decided ahead (1 s), so the frames can be read from disk before they are due. A moment replays the plan
    // from the picture after the next one: it shows 0.1 s later, while what is on screen and next stays as it was.
    public sealed class SpriteTimeline
    {
        public const int Ahead = 20, Keep = 2;
        sealed class Entry { public SpriteDirector After; public readonly SpriteDirector.Shot[] Shots = new SpriteDirector.Shot[2]; public List<string> Notes; }
        readonly List<Entry> _queue = new List<Entry>();
        readonly List<string> _scratch = new List<string>();   // notes of the picture being decided (a list only when there are some)
        readonly bool _notes;
        SpriteDirector _tail;
        public readonly SpriteLibrary Library;

        public SpriteTimeline(SpriteLibrary lib, int seed, bool notes = false)
        {
            Library = lib; _notes = notes;
            _tail = new SpriteDirector(lib, seed);
            Fill();
        }
        void Fill()
        {
            while (_queue.Count < Ahead)
            {
                var d = _tail.Clone(); var e = new Entry { After = d };
                _scratch.Clear();
                d.Step(e.Shots, _notes ? _scratch : null);
                if (_scratch.Count > 0) e.Notes = new List<string>(_scratch);
                _queue.Add(e); _tail = d;
            }
        }
        public int Count { get { return _queue.Count; } }
        public SpriteDirector.Shot Shot(int position, int character) { return _queue[position].Shots[character]; }
        public List<string> Notes(int position) { return _queue[position].Notes; }
        public SpriteDirector Now { get { return _queue[0].After; } }
        public void Advance() { _queue.RemoveAt(0); Fill(); }
        public bool Moment(string name, int who)
        {
            int at = Math.Min(Keep, _queue.Count) - 1;
            var d = _queue[at].After.Clone();
            var notes = _notes ? new List<string>() : null;
            bool fired = d.Moment(name, who, notes);
            if (notes != null && notes.Count > 0) { if (_queue[at].Notes == null) _queue[at].Notes = notes; else _queue[at].Notes.AddRange(notes); }
            _queue[at].After = d;
            _queue.RemoveRange(at + 1, _queue.Count - at - 1);
            _tail = d; Fill();
            return fired;
        }
        public bool Playing(string moment)
        {
            for (int k = 0; k < Keep && k < _queue.Count; k++) if (_queue[k].After.Playing(moment)) return true;
            return false;
        }
    }
}
