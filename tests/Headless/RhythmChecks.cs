using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PartnerLive2D.Core;

namespace PartnerLive2D.Headless
{
    // pair_rhythm (2026-11-13): tempo + short forms, later ways in, after-key delays, echo; and "off = the same stream".
    // Runs on the rhythm tables (library.py --no-pack + ngo_fx_install.py) when they are there; the old library
    // (pair_fx) is the reference for "off".
    internal static unsafe partial class Program
    {
        const string RhythmLibraryDir = @"E:\maimai-ngo-scratch\pair_opt\rhythm\library";

        static SpriteLibrary LoadRhythm(string dir)
        {
            string graphs = File.Exists(Path.Combine(dir, "Ame.graph")) ? dir : NoFxLibraryDir;
            return SpriteLibrary.Parse(File.ReadAllText(Path.Combine(dir, "library.json")), name => File.ReadAllBytes(Path.Combine(graphs, name)));
        }

        static void RhythmChecks()
        {
            string path = Path.Combine(RhythmLibraryDir, "library.json");
            if (!File.Exists(path)) { Console.WriteLine("Rhythm checks skipped: no " + path); return; }
            int checks = 0;
            Action<bool, string> ok = (cond, what) => { if (!cond) throw new Exception("rhythm: " + what); checks++; };
            var lib = LoadRhythm(RhythmLibraryDir);
            ok(lib.Rhythm != null && lib.Rhythm.Tempo && lib.Rhythm.Short && lib.Rhythm.LateEntry && lib.Rhythm.AfterKey && lib.Rhythm.EchoChance > 0, "tuning.rhythm read");
            var K = lib.Characters[1]; var A = lib.Characters[0];
            int shorts = K.Takes.Count(t => t.ShortA >= 0) + A.Takes.Count(t => t.ShortA >= 0);
            ok(shorts >= 10, "short forms read: " + shorts);
            foreach (var ch in lib.Characters)
                foreach (var t in ch.Takes)
                    if (t.ShortA >= 0) ok(t.ShortB > t.ShortA && t.ShortB < t.Count && (t.ShortA + 1 + t.Count - t.ShortB) >= 30, "short form length " + t.Name);
            ok(A.LateTo != null && K.LateTo != null, "late ways in read");
            RhythmBriskChecks(ok);
            var shots = new SpriteDirector.Shot[2];
            var log = new List<string>();
            Func<SpriteDirector, int, int> run = (d, n) => { for (int i = 0; i < n; i++) d.Step(shots, log); return d.Tick; };

            // ---- tempo and its hysteresis
            var tempo = new SpriteDirector(lib, 3);
            run(tempo, 20);
            tempo.Input(); run(tempo, 20); tempo.Input(); run(tempo, 20);
            ok(!tempo.Brisk, "two inputs: relaxed");
            tempo.Input(); ok(tempo.Brisk, "three inputs within 6 s: brisk at once");
            run(tempo, 80); tempo.Input(); run(tempo, 80); tempo.Input();       // 4 s apart: not 3 in 6 s, but never 5 s quiet
            run(tempo, 80); ok(tempo.Brisk, "inputs every 4 s keep the tempo brisk (hysteresis)");
            run(tempo, 15); ok(tempo.Brisk, "4.75 s after the last input: still brisk");
            run(tempo, 10); ok(!tempo.Brisk, "5.25 s after the last input: relaxed");
            var spread = new SpriteDirector(lib, 3);
            for (int i = 0; i < 3; i++) { spread.Input(); run(spread, 70); }     // 3 inputs over 7 s
            ok(!spread.Brisk, "three inputs over 7 s: relaxed");

            // ---- short form: an action started brisk takes one cut a -> b
            var wink = K.Takes.First(t => t.Action == "K_wink");
            ok(wink.ShortA >= 0, "K_wink has a short form");
            Func<bool, int[]> winkRun = brisk =>
            {
                var d = new SpriteDirector(lib, 5);
                run(d, 20);
                if (brisk) { d.Input(); d.Input(); d.Input(); }
                d.Moment("act.K_wink", -1, log);
                int start = -1, end = -1, jumpFrom = -1, prev = -1;
                for (int i = 0; i < 400 && end < 0; i++)
                {
                    d.Step(shots, log);
                    if (brisk && i % 40 == 0) d.Input();          // keep the tempo up
                    int f = shots[1].Frame; bool inWink = K.TakeOf[f] == Array.IndexOf(K.Takes, wink);
                    if (inWink && start < 0) start = i;
                    if (start >= 0 && !inWink) end = i;
                    if (inWink && jumpFrom < 0 && prev >= 0 && K.TakeOf[prev] == K.TakeOf[f] && f > prev + 3) jumpFrom = prev;
                    prev = f;
                }
                return new[] { start, end, jumpFrom };
            };
            var relaxed = winkRun(false); var brisky = winkRun(true);
            ok(relaxed[0] >= 0 && relaxed[1] > relaxed[0] && brisky[0] >= 0 && brisky[1] > brisky[0], "K_wink played both ways");
            ok(relaxed[2] < 0 || relaxed[2] != wink.First + wink.ShortA, "relaxed: no short-form cut");
            ok(brisky[2] == wink.First + wink.ShortA, "brisk: the short-form cut a -> b");
            ok((relaxed[1] - relaxed[0]) - (brisky[1] - brisky[0]) >= wink.ShortB - wink.ShortA - 3, "brisk K_wink is shorter by the jump");

            // ---- brisk: half the wait, priority 0 dropped
            var half = new SpriteDirector(lib, 9);
            run(half, 20); half.Input(); half.Input(); half.Input();
            half.Moment("act.K_excited", -1, log);
            ok(half.RequestOf(1) == "act.K_excited" && half.RequestWait(1) == 10, "brisk: a reaction waits half (10 of 20 pictures), was " + half.RequestWait(1));
            var calm = new SpriteDirector(lib, 9); run(calm, 20); calm.Moment("act.K_excited", -1, log);
            ok(calm.RequestWait(1) == 20, "relaxed: full wait");
            var skit = new SpriteDirector(lib, 9); skit.EnableSkits(true);
            run(skit, 20); skit.Input(); skit.Input(); skit.Input();
            skit.Moment("skit.photo", -1, log);
            run(skit, 2);
            ok(skit.RequestOf(0) == null && skit.ActionOf(0) == null, "brisk: a skit lead does not start");

            // ---- after-key: otherTap waits for the touched one's key picture + 0.25 s; fallback = the plain delay
            var ak = new SpriteDirector(lib, 11);
            run(ak, 20);
            ak.Moment("tap", 1, log); ak.Moment("otherTap", 0, log);
            int asked = -1;
            for (int i = 0; i < 200 && asked < 0; i++) { ak.Step(shots, log); if (ak.RequestOf(0) == "otherTap") asked = ak.RequestAsked(0); }
            ok(ak.KeyTickOf(1) >= 0 && asked == ak.KeyTickOf(1) + 5, "otherTap asked at K's key + 5 pictures: asked " + asked + ", key " + ak.KeyTickOf(1));
            var fb = new SpriteDirector(lib, 11);
            run(fb, 20);
            fb.Moment("act.K_excited", -1, log);
            for (int i = 0; i < 60 && fb.ActionOf(1) == null; i++) fb.Step(shots, log);
            ok(fb.ActionOf(1) != null, "K busy for the fallback check");
            int fired = fb.Tick;
            fb.Moment("tap", 1, log); fb.Moment("otherTap", 0, log);           // K busy (same priority): she does not react
            int fbAsked = -1;
            for (int i = 0; i < 60 && fbAsked < 0; i++) { fb.Step(shots, log); if (fb.RequestOf(0) == "otherTap") fbAsked = fb.RequestAsked(0); }
            ok(fbAsked == fired + 9, "fallback: the plain 0.45 s from the event, asked " + (fbAsked - fired));
            var ev = new SpriteDirector(lib, 2);
            run(ev, 20); int evAt = ev.Tick;
            ev.Moment("master", -1, log);
            int evAsked = -1;
            for (int i = 0; i < 40 && evAsked < 0; i++) { ev.Step(shots, log); if (ev.RequestOf(0) == "master") evAsked = ev.RequestAsked(0); }
            ok(evAsked == evAt + 3, "master Ame on the event + 0.15 s: " + (evAsked - evAt));

            // ---- echo: about 30 % of the random moves are answered 1.0-1.6 s after their key; an echo is never echoed
            var echo = new SpriteDirector(lib, 21);
            int randoms = 0, echoes = 0, badTiming = 0; string[] lastMoment = { null, null }; int[] lastAsk = { -1, -1 };
            for (int i = 0; i < 20 * 60 * 30; i++)
            {
                echo.Step(shots, null);
                for (int c = 0; c < 2; c++)
                {
                    string m = echo.MomentOf(c);
                    if (m != lastMoment[c] && m == "random") randoms++;
                    lastMoment[c] = m;
                    if (echo.RequestOf(c) == "echo" && echo.RequestAsked(c) != lastAsk[c])
                    {
                        lastAsk[c] = echo.RequestAsked(c); echoes++;
                        int since = echo.RequestAsked(c) - echo.KeyTickOf(1 - c);
                        if (since < 20 || since > 32) badTiming++;
                    }
                }
            }
            ok(randoms > 50, "random moves in 30 minutes: " + randoms);
            double share = echoes / (double)randoms;
            ok(share > .15 && share < .45, "echo share " + share.ToString("0.00", CultureInfo.InvariantCulture));
            ok(badTiming == 0, "echo 1.0-1.6 s after the key: " + badTiming + " off");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  echo: {0} random moves, {1} echoes ({2:0.0} %)", randoms, echoes, 100 * share));

            // ---- later way in: Ame_poke (16 still lead pictures) entered later than the graph's way in
            Func<bool, double> pokeEntry = late =>
            {
                var l2 = LoadRhythm(RhythmLibraryDir); if (!late) l2.Rhythm.LateEntry = false;
                var A2 = l2.Characters[0]; int poke = Array.FindIndex(A2.Takes, t => t.Action == "Ame_poke");
                var d = new SpriteDirector(l2, 31); var sum = 0.0; int n = 0; bool was = false;
                for (int i = 0; i < 20 * 60 * 8; i++)
                {
                    if (i % 300 == 150) d.Moment("poke", 0, null);
                    d.Step(shots, null);
                    bool now = A2.TakeOf[shots[0].Frame] == poke;
                    if (now && !was && shots[0].Under < 0) { sum += shots[0].Frame - A2.Takes[poke].First; n++; }
                    was = now;
                }
                return n == 0 ? -1 : sum / n;
            };
            double early = pokeEntry(false), later = pokeEntry(true);
            var pokeTake = A.Takes.First(t => t.Action == "Ame_poke");
            ok(early >= 0 && later > early + 5 && later < pokeTake.Key, string.Format(CultureInfo.InvariantCulture, "Ame_poke entered later: {0:0.0} -> {1:0.0}", early, later));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  later way in: Ame_poke's first picture {0:0.0} -> {1:0.0} (cut entries)", early, later));

            // ---- off = the same stream: the rhythm tables with rhythm off play exactly as the old library
            string oldPath = Path.Combine(FxLibraryDir, "library.json");
            if (File.Exists(oldPath))
            {
                var old = SpriteLibrary.Parse(File.ReadAllText(oldPath), name => File.ReadAllBytes(Path.Combine(NoFxLibraryDir, name)));
                var off = LoadRhythm(RhythmLibraryDir); off.Rhythm = null;
                foreach (int seed in new[] { 1, 7, 36 })
                {
                    var ta = new SpriteTimeline(old, seed, false, true); var tb = new SpriteTimeline(off, seed, false, true);
                    string[] script = { "enter", "song", "tap", "master", "return", "category", "otherTap", "poke", "countdown", "songHard", "bored" };
                    for (int i = 0; i < 20 * 60 * 4; i++)
                    {
                        if (i % 97 == 50) { string m = script[(i / 97) % script.Length]; int who = m == "tap" || m == "poke" ? (i / 97) % 2 : -1; ta.Moment(m, who); tb.Moment(m, who); }
                        if (i % 13 == 5) { ta.Input(); tb.Input(); }
                        ta.Advance(); tb.Advance();
                        for (int c = 0; c < 2; c++)
                        {
                            var x = ta.Shot(0, c); var y = tb.Shot(0, c);
                            if (x.Frame != y.Frame || x.Under != y.Under || x.Weight != y.Weight) throw new Exception("rhythm: off differs from the old library at picture " + i + " seed " + seed);
                        }
                    }
                    checks++;
                }
            }
            else Console.WriteLine("  (no " + oldPath + ": 'off = identical' not checked)");

            // ---- the timeline plays the rhythm tables cleanly: brisk song flipping and touches, no broken / unallowed picture
            var tl = new SpriteTimeline(lib, 44, false, true);
            int[] last = { -1, -1 }; int problems = 0, shortCuts = 0;
            for (int i = 0; i < 20 * 120; i++)
            {
                if (i >= 200 && i < 600 && i % 18 == 0) { tl.Input(); tl.Moment(i % 90 == 0 ? "songHard" : "song", -1); }
                if (i == 700) { tl.Moment("tap", 1); tl.Moment("otherTap", 0); }
                if (i == 900) { tl.Moment("tap", 0); tl.Moment("otherTap", 1); }
                tl.Advance();
                for (int c = 0; c < 2; c++)
                {
                    var ch = lib.Characters[c]; var s = tl.Shot(0, c);
                    if (ch.Bad[s.Frame]) problems++;
                    if (s.Under < 0 && last[c] >= 0 && s.Frame != last[c] && !Allowed(lib, ch, last[c], s.Frame)) problems++;
                    var tk = ch.Takes[ch.TakeOf[s.Frame]];
                    if (last[c] >= 0 && tk.ShortA >= 0 && last[c] == tk.First + tk.ShortA && s.Frame == tk.First + tk.ShortB) shortCuts++;
                    last[c] = s.Frame;
                }
            }
            ok(problems == 0, "brisk script: " + problems + " broken or unallowed pictures");
            ok(shortCuts > 0, "brisk script took short forms");
            Console.WriteLine("Rhythm checks passed: " + checks + " (short-form cuts in the brisk script: " + shortCuts + ")");
        }

        static void RhythmBriskChecks(Action<bool, string> ok)
        {
            // Parse the optional switches independently of whether the on-disk tables have been rebuilt yet.
            var root = (Dictionary<string, object>)PartnerLive2D.Framework.Json.Parse(File.ReadAllText(Path.Combine(RhythmLibraryDir, "library.json")));
            var tuning = (Dictionary<string, object>)root["tuning"];
            Func<string, SpriteLibrary> parse = rhythm =>
            {
                tuning["rhythm"] = PartnerLive2D.Framework.Json.Parse(rhythm);
                return SpriteLibrary.Parse(System.Text.Json.JsonSerializer.Serialize(root), name => File.ReadAllBytes(Path.Combine(RhythmLibraryDir, name)));
            };
            var absent = parse("{}");
            ok(!absent.Rhythm.Midway && absent.Rhythm.BriskMax == 0, "absent midway and briskMax are off");
            var enabled = parse("{\"midway\":true,\"briskMax\":4.5}");
            ok(enabled.Rhythm.Midway && enabled.Rhythm.BriskMax == 4.5, "midway and briskMax read");
            ok(parse("{\"midway\":false,\"briskMax\":-1}").Rhythm.BriskMax == -1, "nonpositive briskMax accepted");
            ok(parse("null").Rhythm == null, "rhythm off has no switches");

            // Start relaxed, then turn brisk at a known picture. The exact boundary must not arm a new jump.
            foreach (int mode in new[] { 0, 1, 2, 3, 4, 5 })
            {
                var lib = RhythmPickLibrary(0, true, new[] { "K_wink" }, new[] { 1.0 });
                lib.Rhythm.Midway = mode != 2; lib.Rhythm.Short = mode != 3;
                var ch = lib.Characters[1]; var wink = ch.Takes.First(t => t.Action == "K_wink");
                var d = new SpriteDirector(lib, 5); var shots = new SpriteDirector.Shot[2]; var notes = new List<string>();
                if (mode == 5) { lib.Rhythm.Calm = .05; d.Input(); }
                d.Moment("act.K_wink", -1, notes);
                int trigger = wink.First + wink.ShortA + (mode == 1 ? 1 : mode == 4 ? 0 : -2);
                bool entered = false, triggered = false, jumped = false, relaxedAtJump = false; int jumpCount = 0;
                for (int i = 0; i < 250; i++)
                {
                    int before = d.Frame(1);
                    if (d.ActionOf(1) == wink.Name && before >= trigger && !triggered && mode != 5)
                    { d.Input(); triggered = true; }
                    d.Step(shots, notes);
                    if (d.ActionOf(1) == wink.Name) entered = true;
                    if (before == wink.First + wink.ShortA && shots[1].Frame == wink.First + wink.ShortB)
                    { jumped = true; jumpCount++; relaxedAtJump = !d.Brisk; }
                    if (entered && d.ActionOf(1) != wink.Name) break;
                }
                string jump = "short form (midway) " + wink.ShortA + " -> " + wink.ShortB;
                ok(entered && (triggered || mode == 5), "midway fixture reached its trigger, mode " + mode);
                if (mode == 0)
                    ok(jumped && jumpCount == 1 && notes.Any(s => s.EndsWith(jump, StringComparison.Ordinal)), "midway before ShortA: timeline jumps once and log names it");
                else if (mode == 5)
                    ok(jumped && relaxedAtJump && notes.Any(s => s.Contains("short form " + wink.ShortA + " -> " + wink.ShortB)) && !notes.Any(s => s.Contains("(midway)")), "started brisk: keeps the jump after relaxing");
                else
                    ok(!jumped && !notes.Any(s => s.Contains("short form")), "no midway jump after ShortA / midway off / short off / at ShortA, mode " + mode);
            }

            string[] picks = { "K_heart", "K_wink", "K_excited" }; double[] weights = { 100, 2, 1 };
            var filtered = RhythmPickLibrary(4.5, true, picks, weights);
            var pool = RhythmPickLibrary(0, true, new[] { "K_wink", "K_excited" }, new[] { 2.0, 1.0 });
            var unlimited = RhythmPickLibrary(0, true, picks, weights);
            var allLong = RhythmPickLibrary(.01, true, picks, weights);
            var negative = RhythmPickLibrary(-1, true, picks, weights);
            var single = RhythmPickLibrary(.01, true, new[] { "K_heart" }, new[] { 1.0 });
            var noShort = RhythmPickLibrary(3.75, false, new[] { "K_wink", "K_touch" }, new[] { 100.0, 1.0 });
            var touchOnly = RhythmPickLibrary(0, false, new[] { "K_touch" }, new[] { 1.0 });
            var withShort = RhythmPickLibrary(3.75, true, new[] { "K_wink", "K_touch" }, new[] { 100.0, 1.0 });
            var multi = RhythmPickLibrary(4.5, true, new[] { "K_heart", "K_wink" }, new[] { 1.0, 1.0 });
            int heart = multi.ActionIndex("K_heart"), winkAction = multi.ActionIndex("K_wink");
            multi.Actions[winkAction].Takes = new[] { multi.Actions[heart].Takes[0], multi.Actions[winkAction].Takes[0] };
            var multiOnly = RhythmPickLibrary(0, true, new[] { "K_wink" }, new[] { 1.0 });
            multiOnly.Actions[winkAction].Takes = (int[])multi.Actions[winkAction].Takes.Clone();
            var equal = RhythmPickLibrary(0, true, new[] { "K_wink", "K_heart" }, new[] { 1.0, 1.0 });
            var wt = equal.Characters[1].Takes[equal.Actions[equal.ActionIndex("K_wink")].Takes[0]];
            equal.Rhythm.BriskMax = (wt.Count - (wt.ShortB - wt.ShortA)) / 20.0;
            var winkOnly = RhythmPickLibrary(0, true, new[] { "K_wink" }, new[] { 1.0 });
            bool sawWink = false, sawExcited = false, sawLong = false, shortChangesPick = false;
            foreach (int seed in Enumerable.Range(1, 24))
            {
                var want = RhythmPickLog(filtered, seed, true, ok);
                ok(want == RhythmPickLog(pool, seed, true, ok) && !want.Contains("heart"), "brisk weighted draw excludes long pick, seed " + seed);
                sawWink |= want.Contains("wink"); sawExcited |= want.Contains("excited");
                var all = RhythmPickLog(unlimited, seed, true, ok); sawLong |= all.Contains("heart");
                ok(RhythmPickLog(allLong, seed, true, ok) == all, "all long: original pool, seed " + seed);
                ok(RhythmPickLog(negative, seed, true, ok) == all, "nonpositive limit: original pool, seed " + seed);
                ok(RhythmPickLog(filtered, seed, false, ok) == RhythmPickLog(unlimited, seed, false, ok), "relaxed: identical pick, seed " + seed);
                ok(RhythmPickLog(single, seed, true, ok).Contains("heart"), "single long pick retained, seed " + seed);
                ok(RhythmPickLog(noShort, seed, true, ok) == RhythmPickLog(touchOnly, seed, true, ok), "short off: full length used, seed " + seed);
                shortChangesPick |= RhythmPickLog(withShort, seed, true, ok).Contains("wink");
                ok(RhythmPickLog(multi, seed, true, ok) == RhythmPickLog(multiOnly, seed, true, ok), "minimum over takes, seed " + seed);
                ok(RhythmPickLog(equal, seed, true, ok) == RhythmPickLog(winkOnly, seed, true, ok), "length equal to cap included, seed " + seed);
            }
            ok(sawWink && sawExcited && sawLong && shortChangesPick, "draw fixtures exercise both eligible weights, exclusion, and short lengths");
        }

        static SpriteLibrary RhythmPickLibrary(double max, bool shortOn, string[] picks, double[] weights)
        {
            var lib = LoadRhythm(RhythmLibraryDir);
            lib.Rhythm = new SpriteLibrary.RhythmTuning { Tempo = true, Inputs = 1, Short = shortOn, BriskMax = max };
            lib.FirstRandomMin = lib.FirstRandomMax = 10000;
            lib.Moments.Clear(); lib.Skits.Clear();
            lib.Moments.Add(new SpriteLibrary.Moment { Name = "rhythm.pick", Character = 1, Chance = 1, Priority = 2, Wait = 1, Soft = true,
                Picks = picks.Select((name, i) => new SpriteLibrary.Pick { Action = lib.ActionIndex(name), Weight = weights[i] }).ToArray() });
            return lib;
        }

        static string RhythmPickLog(SpriteLibrary lib, int seed, bool brisk, Action<bool, string> ok)
        {
            var d = new SpriteDirector(lib, seed); var notes = new List<string>();
            if (brisk) d.Input();
            // Count actual RNG state advances without adding a test-only hook to the director.
            var field = typeof(SpriteDirector).GetField("_rng", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            ulong expected = (ulong)field.GetValue(d);
            for (int i = 0; i < 2; i++) { expected ^= expected >> 12; expected ^= expected << 25; expected ^= expected >> 27; }
            d.Moment("rhythm.pick", -1, notes);
            ok((ulong)field.GetValue(d) == expected && notes.Count == 1 && notes[0].Contains("rhythm.pick wants "), "exactly one pick draw and one take draw, seed " + seed);
            return notes[0];
        }
    }
}
