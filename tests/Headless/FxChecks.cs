using System;
using System.Collections.Generic;
using System.IO;
using PartnerLive2D.Core;
using PartnerLive2D.Framework;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        const string FxLibraryDir = @"E:\maimai-ngo-scratch\pair_opt\library_fx";
        const string NoFxLibraryDir = @"E:\maimai-ngo-scratch\pair_opt\library_sparse";

        // pair_fx (2026-11-13): the emote layer's styles, replacement, cap and table loader; skits in the library
        static void FxChecks()
        {
            int checks = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception(why); checks++; };
            var lib = new SpriteLibrary();
            foreach (var n in new[] { "K_wink", "K_excited", "K_heart", "K_pout", "Ame_glare", "Ame_photo", "K_quiet" })
                lib.Actions.Add(new SpriteLibrary.Action { Name = n, Character = SpriteLibrary.CharacterIndex(n.Substring(0, n.IndexOf('_'))), Takes = new int[0] });
            string json = @"{""version"":1,""atlas"":""fx.png"",""atlasSize"":[1248,2512],""texel"":4,""tick"":0.0833333,""topLimit"":100,
                ""artPixel"":{""Ame"":1.5,""K"":1.7},
                ""sprites"":{""sparkle"":{""w"":70,""h"":71,""frames"":4,""y"":362},""anger"":{""w"":78,""h"":61,""frames"":4,""y"":0},""tear"":{""w"":54,""h"":81,""frames"":4,""y"":547}},
                ""anchors"":{""K.a"":[860,330],""K.b"":[590,480],""Ame.a"":[415,185],""Ame.phone"":[500,125],""Ame.top"":[415,20]},
                ""actions"":{
                  ""K_wink"":[{""sprite"":""sparkle"",""anchor"":""K.a"",""at"":""key""},{""sprite"":""sparkle"",""anchor"":""K.b"",""at"":""key"",""ticks"":2,""scale"":1.2}],
                  ""K_excited"":[{""sprite"":""sparkle"",""anchor"":""K.a"",""at"":""key"",""count"":3,""stagger"":2,""spread"":[-60,35],""scale"":1.3}],
                  ""K_heart"":[{""sprite"":""sparkle"",""anchor"":""K.a"",""at"":""start"",""style"":""rise""}],
                  ""K_pout"":[{""sprite"":""anger"",""anchor"":""K.a"",""at"":""start"",""style"":""loop"",""loops"":2}],
                  ""Ame_glare"":[{""sprite"":""tear"",""anchor"":""Ame.top"",""at"":""start""}],
                  ""Ame_photo"":[{""sprite"":""nosuch"",""anchor"":""Ame.phone"",""at"":""key""}],
                  ""K_nosuch"":[{""sprite"":""sparkle"",""anchor"":""K.a""}]},
                ""touch"":{""sprite"":""sparkle"",""style"":""pop"",""hold"":1,""scale"":1.0}}";
            var table = FxTable.Parse(Json.Parse(json), lib);
            check(table.Sprites.Length == 3 && table.AnchorNames.Length == 5 && table.Touch != null, "fx table: sprites, anchors, touch");
            check(table.Warnings.Count == 2 && table.ByAction[lib.ActionIndex("Ame_photo")] == null && table.ByAction[lib.ActionIndex("K_quiet")] == null,
                "fx table: unknown sprite / action skipped with one warning each");
            check(table.ByAction[lib.ActionIndex("K_wink")].Length == 2 && table.ByAction[lib.ActionIndex("K_wink")][1].Ticks == 2, "fx table: entries and key+ticks");
            check(table.ByAction[lib.ActionIndex("K_heart")][0].Hold == 7 && table.ByAction[lib.ActionIndex("K_wink")][0].Hold == 5, "fx table: default holds (pop 5, rise 7)");
            bool threw = false;
            try { FxTable.Parse(Json.Parse(json.Replace("\"style\":\"loop\"", "\"style\":\"wobble\"")), lib); } catch (FormatException) { threw = true; }
            check(threw, "fx table: an unknown style is a format error");

            // styles: frame / size / shown sequences
            int f, ox, oy; float size; bool shown;
            var pop = new List<string>();
            for (int t = 0; t < FxTable.Length(FxTable.Pop, 5, 2); t++) { FxTable.State(FxTable.Pop, 5, 2, t, out f, out size, out shown, out ox, out oy); pop.Add(shown ? f + "@" + size : "-"); }
            check(string.Join(" ", pop) == "0@0.6 1@1.15 2@1 3@1 3@1 3@1 3@1 3@1 3@1 - 3@1 -", "pop sequence: " + string.Join(" ", pop));
            check(FxTable.Length(FxTable.Pop, 5, 2) == 12, "pop lasts 12 ticks (1 s)");
            var rise = new List<string>();
            for (int t = 0; t < 10; t++) { FxTable.State(FxTable.Rise, 7, 2, t, out f, out size, out shown, out ox, out oy); rise.Add(ox + "," + oy); }
            check(string.Join(" ", rise) == "0,0 0,0 0,-2 1,-4 1,-6 0,-8 -1,-10 -1,-12 0,-14 1,-16", "rise path: " + string.Join(" ", rise));
            var loop = new List<string>();
            for (int t = 0; t < FxTable.Length(FxTable.Loop, 5, 2); t++) { FxTable.State(FxTable.Loop, 5, 2, t, out f, out size, out shown, out ox, out oy); loop.Add(shown ? f.ToString() : "-"); }
            check(string.Join("", loop) == "0011223300112233-3-", "loop sequence: " + string.Join("", loop));

            // timing: key entries, key + 2 ticks, stagger; the float; snapping; top limit
            var fx = new PairFx(table) { Log = new List<string>() };
            var kTake = new SpriteLibrary.Take { Name = "k", Action = "K_wink", First = 0, Count = 60, Key = 20 };
            fx.ActionStart(1, lib.ActionIndex("K_wink"), kTake);
            check(fx.Alive(1) == 0, "key entries wait for the key cue");
            fx.ActionKey(1, lib.ActionIndex("K_wink"), kTake);
            check(fx.Alive(1) == 2, "key cue spawns both entries");
            PairFx.Draw d;
            check(fx.Get(4, 0, 0, 3.3f, -2.2f, out d) && d.Frame == 0 && Math.Abs(d.Size - .6f) < 1e-4, "first entry shows frame 0 at 60 %");
            check(Math.Abs(d.X / 1.7 - Math.Round(d.X / 1.7)) < 1e-3 && Math.Abs(d.X - 860 - 3.3f) <= .86f, "position follows the float, snapped to art pixels: " + d.X);
            check(!fx.Get(5, 0, 0, 0, 0, out d), "key + 2 ticks not shown yet");
            fx.Update(table.Tick * 2.01f);
            check(fx.Get(5, 0, 0, 0, 0, out d) && d.Frame == 0 && Math.Abs(d.W - 70 * 1.7f * 1.2f * .6f) < .01f, "second entry starts 2 ticks later, scale 1.2");
            check(fx.Get(4, 0, 0, 0, 0, out d) && d.Frame == 2, "first entry at tick 2 shows frame 2");
            fx.Update(table.Tick * 11);
            check(fx.Alive(1) == 1, "first entry gone after 12 ticks, second still there");
            fx.Update(table.Tick * 3);
            check(!fx.Any, "all gone");

            // replacement: a new action's emote takes the old one away at once; the cap: 4 per character
            var start = new SpriteLibrary.Take { Name = "s", Action = "K_heart", First = 0, Count = 60, Key = -1 };
            fx.ActionStart(1, lib.ActionIndex("K_heart"), start);
            check(fx.Alive(1) == 1, "rise at start");
            fx.Update(table.Tick);
            var ex = new SpriteLibrary.Take { Name = "e", Action = "K_excited", First = 0, Count = 60, Key = 10 };
            fx.ActionStart(1, lib.ActionIndex("K_excited"), ex);
            fx.ActionKey(1, lib.ActionIndex("K_excited"), ex);
            check(fx.Alive(1) == 3, "replacement: 3 sparkles, the heart's emote gone: " + fx.Alive(1));
            check(fx.Get(4, 0, 0, 0, 0, out d) && !fx.Get(5, 0, 0, 0, 0, out d), "staggered copies wait");
            fx.Update(table.Tick * 2.01f);
            check(fx.Get(5, 0, 0, 0, 0, out d) && Math.Abs(d.X - (860 - 60 * 1.7 * 1.3)) < 1.2 * 2.21, "copy 2 shifted by spread art pixels: " + d.X);
            fx.Touch(1, 700, 400);
            check(fx.Alive(1) == 4, "touch sparkle joins (4 alive)");
            fx.Touch(1, 710, 410);
            check(fx.Alive(1) == 4, "a new touch replaces the old touch sparkle");
            fx.ActionStart(1, lib.ActionIndex("K_pout"), start);
            check(fx.Alive(1) == 2, "a new action emote replaces the action emotes, the touch sparkle stays: " + fx.Alive(1));
            var many = new PairFx(table);
            for (int i = 0; i < 6; i++) { many.ActionStart(1, lib.ActionIndex("K_excited"), ex); many.ActionKey(1, lib.ActionIndex("K_excited"), ex); many.Touch(1, 600, 500); }
            check(many.Alive(1) <= PairFx.PerCharacter && many.Alive(0) == 0, "cap: never more than 4 per character");
            // top limit: a tall sprite near the top is moved down so its top sits at y = 100
            var top = new PairFx(table);
            top.ActionStart(0, lib.ActionIndex("Ame_glare"), start);
            top.Update(table.Tick * 3);
            check(top.Get(0, 0, 0, 0, 0, out d) && Math.Abs(d.Y - d.H / 2 - 100) < 1e-3, "top edge kept at y = 100: " + (d.Y - d.H / 2));
            check(Math.Abs(d.W - 54 * 1.5f) < 1e-3, "Ame's art pixel 1.5");

            // no allocation per frame in steady state
            var steady = new PairFx(table);
            steady.ActionStart(1, lib.ActionIndex("K_pout"), start);
            for (int i = 0; i < 10; i++) { steady.Update(1 / 60f); steady.Get(4, 0, 0, 0, 0, out d); }
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) { steady.Update(1 / 60f); for (int s = 0; s < PairFx.Slots; s++) steady.Get(s, 1, 1, 2, 2, out d); }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            check(bytes == 0, "fx update / draw allocates nothing per frame; actual=" + bytes);

            // the installed table and skits, when the scratch libraries exist (ngo_fx_install.py output)
            string withFx = Path.Combine(FxLibraryDir, "library.json"), without = Path.Combine(NoFxLibraryDir, "library.json");
            if (File.Exists(without))
            {
                var plain = SpriteLibrary.Load(without);
                check(plain.Fx == null && plain.FxWarnings.Count == 0 && plain.Skits.Count == 0, "no fx object = no layer, no skits");
            }
            if (File.Exists(withFx))
            {
                // library_fx holds library.json + fx.png only; the graphs stay with the packs in library_sparse
                var real = SpriteLibrary.Parse(File.ReadAllText(withFx), name => File.ReadAllBytes(
                    File.Exists(Path.Combine(FxLibraryDir, name)) ? Path.Combine(FxLibraryDir, name) : Path.Combine(NoFxLibraryDir, name)));
                check(real.Fx != null && real.FxWarnings.Count == 0, "installed fx table loads cleanly: " + string.Join("; ", real.FxWarnings));
                check(real.Skits.Count == 6, "six skits");
                int listed = 0; foreach (var e in real.Fx.ByAction) if (e != null) listed++;
                check(listed == 21, "21 actions with emotes: " + listed);
                var photo = real.Skits.Find(s => s.Name == "skit.photo");
                check(photo != null && real.Moments[photo.Lead].Character == 0 && real.Moments[photo.Second].Character == 1 && Math.Abs(real.Moments[photo.Second].Delay - .8) < 1e-9 && photo.Weight == 3, "skit photo: Ame leads, K 0.8 s later, weight 3");
                // skits only when switched on, never the same twice in a row, both idle 6 s, 35-60 s apart
                var on = new SpriteTimeline(real, 11, true, true); var notes = new List<string>();
                for (int i = 0; i < 20 * 600; i++) { on.Advance(); var n = on.Notes(0); if (n != null) foreach (var line in n) if (line.Contains("skit.") && line.Contains(" wants ")) notes.Add(line); }
                check(notes.Count >= 6, "skits run on their own over 10 minutes: " + notes.Count);
                string last = null; double lastAt = -100; bool repeat = false, tooSoon = false;
                foreach (var line in notes)
                {
                    int sp = line.IndexOf(' '); double at = double.Parse(line.Substring(0, sp), System.Globalization.CultureInfo.InvariantCulture);
                    string name = line.Substring(line.IndexOf("skit."), line.IndexOf(" wants ") - line.IndexOf("skit."));
                    bool lead = real.Skits.Exists(s => s.Name == name && line.Contains(" " + real.Characters[real.Moments[s.Lead].Character].Name + " "));
                    if (!lead) continue;
                    if (name == last) repeat = true;
                    if (at - lastAt < DirectorSkitMin - .1) tooSoon = true;
                    last = name; lastAt = at;
                }
                check(!repeat && !tooSoon, "skits: no repeat in a row, at least 35 s apart");
                var off = new SpriteTimeline(real, 11, true, false); int offSkits = 0;
                for (int i = 0; i < 20 * 300; i++) { off.Advance(); var n = off.Notes(0); if (n != null) foreach (var line in n) if (line.Contains("skit.")) offSkits++; }
                check(offSkits == 0, "no skits where they are off (result, room)");
            }
            Console.WriteLine("Fx checks passed: " + checks + (File.Exists(withFx) ? " (incl. " + FxLibraryDir + ")" : " (no installed library found; synthetic only)"));
        }
        const double DirectorSkitMin = SpriteDirector.SkitMin;
    }
}
