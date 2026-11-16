using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PartnerLive2D.Core;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        // Headless.exe spritelib <library.json> <timeline.json> [seconds] [seed] [moment@seconds[:K|Ame]] ...
        // Plays the select page from the library the way the game does (60 Hz frames, a picture every 3 of them, the motion
        // feel's springs and hit-stops), with the given moments ("tap" also puts a finger on her). Checks every picture (no
        // broken frame, every cut an allowed one, the start moment ends in time) and writes the shown pictures and the
        // per-frame poses as a timeline for v2/render.py.
        static void SpriteLibraryRun(string[] args)
        {
            if (args.Length < 3) throw new ArgumentException("Usage: Headless.exe spritelib <library.json> <timeline.json> [seconds] [seed] [moment@s[:who]] ...");
            var lib = SpriteLibrary.Load(args[1]);
            double seconds = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 60;
            int seed = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 7;
            var events = new List<KeyValuePair<double, string>>();
            for (int i = 5; i < args.Length; i++)
            {
                // pair_rhythm: --rhythm=off plays as partner.ini rhythm = off; "input@s" is a select-page input (the tempo)
                if (args[i] == "--rhythm=off") { lib.Rhythm = null; continue; }
                if (args[i] == "--anchor=pose") { if (lib.Rhythm != null) lib.Rhythm.AfterPose = true; continue; }
                int at = args[i].IndexOf('@');
                if (at < 0) throw new ArgumentException("event must be moment@seconds: " + args[i]);
                string rest = args[i].Substring(at + 1); int colon = rest.IndexOf(':');
                double s = double.Parse(colon < 0 ? rest : rest.Substring(0, colon), CultureInfo.InvariantCulture);
                events.Add(new KeyValuePair<double, string>(s, args[i].Substring(0, at) + (colon < 0 ? "" : ":" + rest.Substring(colon + 1))));
            }
            var tl = new SpriteTimeline(lib, seed, true, true);          // the select page: skits on
            var feel = new PairFeel(seed, lib.Characters[0], lib.Characters[1]);
            var cues = new PairCues(lib, feel);
            foreach (var w in lib.FxWarnings) Console.WriteLine("fx warning: " + w);
            // the emote layer as the backend runs it (pair_fx 2026-11-13): its state every 60 Hz frame goes into "fx60"
            var fx = lib.Fx != null ? new PairFx(lib.Fx) : null;
            cues.Fx = fx;
            var fxFrames = new StringBuilder("[");
            var brisk = new StringBuilder("[");   // pair_rhythm: the tempo per picture (1 brisk)
            int frames = (int)Math.Round(seconds * 60);
            var ticks = new StringBuilder("["); var poses = new StringBuilder("["); var log = new List<string>(); if (fx != null) fx.Log = log;
            var prev = new[] { -1, -1 }; int fades = 0, cuts = 0, problems = 0, shown = 0, startAt = -1, startEnd = -1, holds = 0;
            int fired = 0; double clock = 0;
            Action show = () =>
            {
                double t = shown / 20.0;
                if (tl.Notes(0) != null && shown > 0) log.AddRange(tl.Notes(0));
                for (int c = 0; c < 2; c++) cues.Shown(c, tl.Shot(0, c).Frame, log);
                ticks.Append(shown == 0 ? "{" : ",{");
                brisk.Append(shown == 0 ? "" : ",").Append(tl.Brisk ? '1' : '0');
                for (int c = 0; c < 2; c++)
                {
                    var ch = lib.Characters[c]; var shot = tl.Shot(0, c);
                    if (ch.Bad[shot.Frame]) { problems++; log.Add(string.Format("{0:0.00} PROBLEM {1} shows broken frame {2}", t, ch.Name, Describe(ch, shot.Frame))); }
                    int p = prev[c];
                    if (shot.Under >= 0) { if (shot.Weight < .3f) fades++; }
                    else if (p >= 0 && shot.Frame != p && !Allowed(lib, ch, p, shot.Frame))
                    { problems++; log.Add(string.Format("{0:0.00} PROBLEM {1} cut {2} -> {3}", t, ch.Name, Describe(ch, p), Describe(ch, shot.Frame))); }
                    else if (p >= 0 && shot.Frame != ch.Next(p) && shot.Frame != p) cuts++;
                    prev[c] = shot.Frame;
                    ticks.Append(c == 0 ? "\"Ame\":[" : ",\"K\":[");
                    if (shot.Under >= 0) ticks.Append(Layer(ch, shot.Under, 1f)).Append(',');
                    ticks.Append(Layer(ch, shot.Frame, shot.Weight)).Append(']');
                }
                ticks.Append('}');
                shown++;
            };
            show();
            for (int f = 0; f < frames; f++)
            {
                double now = f / 60.0;
                for (; fired < events.Count; fired++)
                {
                    events.Sort((a, b) => a.Key.CompareTo(b.Key));
                    if (events[fired].Key > now) break;
                    string name = events[fired].Value; int who = -1;
                    int colon = name.IndexOf(':');
                    if (colon >= 0) { who = SpriteLibrary.CharacterIndex(name.Substring(colon + 1)); name = name.Substring(0, colon); }
                    if (name == "input") { tl.Input(); log.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00} INPUT", now)); continue; }
                    if ((name == "tap" || name == "poke") && who >= 0)
                    {
                        feel.Touch(who, lib.Characters[who].PivotX - 60, lib.Characters[who].PivotX);
                        if (fx != null) fx.Touch(who, lib.Characters[who].PivotX - 60, lib.Characters[who].PivotY);   // where the finger is
                    }
                    bool ok = tl.Moment(name, who);
                    // as PartnerView.TouchPair: the other one notices when the touched one reacts
                    if (ok && who >= 0 && (name == "tap" || name == "poke")) tl.Moment(name == "tap" ? "otherTap" : "otherPoke", 1 - who);
                    log.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00} EVENT {1}{2} {3}", now, name, who < 0 ? "" : ":" + lib.Characters[who].Name, ok ? "" : "(nothing)"));
                    if (name == "start") startAt = f;
                }
                feel.Update(1 / 60f);
                if (fx != null) fx.Update(1 / 60f);
                if (feel.Holding) holds++;
                else
                {
                    clock = Math.Min(clock + 1 / 60.0, .1);
                    while (clock >= .05 - .004) { tl.Advance(); clock -= .05; show(); }
                }
                if (startAt >= 0 && startEnd < 0 && f > startAt && !tl.Playing("start")) startEnd = f;
                // r: which refresh of the picture, as SpriteLibraryBackend picks the in-betweens (0 picture, 1 and 2 in-betweens)
                poses.Append(f == 0 ? "{" : ",{").Append("\"t\":").Append(shown - 1).Append(",\"r\":").Append(clock < .0125 ? 0 : clock < .02917 ? 1 : 2);
                for (int c = 0; c < 2; c++)
                {
                    var q = feel.Get(c);
                    // the soft-body pose (2026-11-02 evening): core move / turn, the soft parts' lag, sway, breath
                    poses.Append(string.Format(CultureInfo.InvariantCulture, ",\"{9}\":[{0:0.###},{1:0.###},{2:0.#####},{3:0.###},{4:0.###},{5:0.#####},{6:0.###},{7:0.####},{8:0.#####}]",
                        q.Dx, q.Dy, q.Rot, q.LagX, q.LagY, q.LagRot, q.WaveA, q.WavePh, q.Breath, lib.Characters[c].Name));
                }
                poses.Append('}');
                // emotes: [sprite, character, tick, x, y, frame, size, shown] per slot alive (pair canvas, y down)
                fxFrames.Append(f == 0 ? "[" : ",[");
                if (fx != null)
                {
                    var qa = feel.Get(0); var qk = feel.Get(1); bool first = true; PairFx.Draw dr;
                    for (int s = 0; s < PairFx.Slots; s++)
                        if (fx.Get(s, qa.Dx, qa.Dy, qk.Dx, qk.Dy, out dr))
                        {
                            fxFrames.Append(first ? "" : ",").Append(string.Format(CultureInfo.InvariantCulture, "[\"{0}\",\"{1}\",{2},{3:0.##},{4:0.##},{5},{6:0.####},{7}]",
                                lib.Fx.Sprites[dr.Sprite].Name, lib.Characters[dr.Character].Name, dr.Tick, dr.X, dr.Y, dr.Frame, dr.Size, dr.Shown ? 1 : 0));
                            first = false;
                        }
                }
                fxFrames.Append(']');
            }
            fxFrames.Append(']');
            ticks.Append(']'); poses.Append(']');
            var json = new StringBuilder("{\"fps\":20,\"feel\":3,\"ticks\":").Append(ticks).Append(",\"frames60\":").Append(poses).Append(",\"pivots\":{");
            for (int c = 0; c < 2; c++)
                json.Append(string.Format(CultureInfo.InvariantCulture, "{0}\"{1}\":[{2},{3},{4},{5},{6}]", c == 0 ? "" : ",", lib.Characters[c].Name,
                    lib.Characters[c].PivotX, lib.Characters[c].PivotY, lib.Characters[c].AxisX, lib.Characters[c].AxisY, lib.Characters[c].Reach));
            json.Append("},\"flex\":{");                     // each one's softness on a grid over the pair canvas, for render.py
            for (int c = 0; c < 2; c++)
            {
                var ch = lib.Characters[c]; int cells = 32;
                json.Append(c == 0 ? "" : ",").Append('"').Append(ch.Name).Append("\":{\"cells\":").Append(cells).Append(",\"values\":[");
                for (int j = 0; j < cells; j++)
                    for (int i = 0; i < cells; i++)
                        json.Append(j + i == 0 ? "" : ",").Append(ch.FlexAt((i + .5f) * 1024f / cells, (j + .5f) * 1024f / cells).ToString("0.###", CultureInfo.InvariantCulture));
                json.Append("]}");
            }
            json.Append("},\"fx60\":").Append(fxFrames).Append(",\"brisk\":").Append(brisk).Append(']');
            if (lib.Fx != null)
            {
                // what a renderer needs to draw them: atlas, texels per art pixel, sprite rows, each character's art pixel
                json.Append(string.Format(CultureInfo.InvariantCulture, ",\"fxTable\":{{\"atlas\":\"{0}\",\"texel\":{1},\"artPixel\":{{\"Ame\":{2},\"K\":{3}}},\"sprites\":{{",
                    lib.Fx.Atlas, lib.Fx.Texel, lib.Fx.ArtPixel[0], lib.Fx.ArtPixel[1]));
                for (int s = 0; s < lib.Fx.Sprites.Length; s++)
                    json.Append(s == 0 ? "" : ",").Append(string.Format(CultureInfo.InvariantCulture, "\"{0}\":[{1},{2},{3}]", lib.Fx.Sprites[s].Name, lib.Fx.Sprites[s].W, lib.Fx.Sprites[s].H, lib.Fx.Sprites[s].Y));
                json.Append("}}");
            }
            json.Append(",\"log\":[");
            for (int i = 0; i < log.Count; i++) json.Append(i == 0 ? "" : ",").Append('"').Append(log[i].Replace("\\", "/").Replace("\"", "'")).Append('"');
            json.Append("]}");
            File.WriteAllText(args[2], json.ToString());
            foreach (var line in log) if (!line.Contains(" idle jump")) Console.WriteLine(line);
            Console.WriteLine(string.Format("{0} pictures in {1} frames, {2} cuts between takes, {3} fades, {4} hit-stop frames, {5} problems{6}", shown, frames, cuts, fades, holds, problems,
                startAt < 0 ? "" : string.Format(CultureInfo.InvariantCulture, ", start moment held the page {0:0.00} s", ((startEnd < 0 ? frames : startEnd) - startAt) / 60.0)));
            // garbage per picture as the game runs it (notes on): the select page had GC stalls from per-frame garbage before
            var bench = new SpriteTimeline(lib, seed, true);
            for (int i = 0; i < 400; i++) bench.Advance();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2400; i++) { bench.Advance(); if (i % 100 == 50) bench.Moment(i % 200 == 50 ? "song" : "tap", i % 400 == 50 ? 0 : 1); }
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            Console.WriteLine(string.Format("allocation: {0} bytes per picture over 2 minutes with a moment every 5 s", (a1 - a0) / 2400));
            if (problems > 0) throw new Exception("sprite library playback problems: " + problems);
            if (startAt >= 0 && (startEnd < 0 || startEnd - startAt > 4 * 60)) throw new Exception("start moment does not end within 4 s");
        }

        static string Describe(SpriteLibrary.Character ch, int f) { var t = ch.Takes[ch.TakeOf[f]]; return t.Name + "#" + (f - t.First); }
        static string Layer(SpriteLibrary.Character ch, int f, float w)
        {
            var t = ch.Takes[ch.TakeOf[f]];
            return string.Format(CultureInfo.InvariantCulture, "[\"{0}\",{1},{2:0.###}]", t.Name, f - t.First, w);
        }
        // a cut (no fade) is the next frame, an idle jump, a way out of an action's tail or into its lead within TauEntry,
        // or a skip of at most two broken frames inside a take
        static bool Allowed(SpriteLibrary lib, SpriteLibrary.Character ch, int from, int to)
        {
            if (to == ch.Next(from)) return true;
            if (ch.TakeOf[to] == ch.TakeOf[from] && to > from && to - from <= 3) { for (int f = from + 1; f < to; f++) if (!ch.Bad[f]) return false; return true; }
            for (int k = ch.IdleStart[from]; k < ch.IdleStart[from + 1]; k++) if (ch.IdleTo[k] == to && ch.IdleCost[k] <= lib.Tau) return true;
            for (int k = ch.ExitStart[from]; k < ch.ExitStart[from + 1]; k++) if (ch.ExitTo[k] == to && ch.ExitCost[k] <= lib.TauEntry) return true;
            // a bridge (2026-11-02 evening): from its idle frame onto it, and from its end (the rest art) into an action that
            // starts on the rest art
            if (ch.BridgeAt != null && ch.BridgeAt[from] == to) return true;
            var tt = ch.Takes[ch.TakeOf[to]];
            if (ch.Takes[ch.TakeOf[from]].Bridge && ch.Next(from) < 0 && tt.RestStart && (to == tt.First || to == tt.First + 1 || (tt.LateRest > 1 && to == tt.First + tt.LateRest))) return true;
            // pair_rhythm: a short form's cut (exporter: within Tau), a later way in (exporter: within TauEntry)
            if (ch.TakeOf[to] == ch.TakeOf[from] && tt.ShortA >= 0 && from == tt.First + tt.ShortA && to == tt.First + tt.ShortB) return true;
            int e = ch.FindEntry(ch.TakeOf[to], from);
            if (e >= 0 && ch.LateTo != null && ch.LateTo[e] == to) return true;
            return e >= 0 && ch.EntryTo[e] == to && ch.EntryCost[e] <= lib.TauEntry;
        }
    }
}
