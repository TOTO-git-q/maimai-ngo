using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PartnerLive2D.Core;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        // Headless.exe rhythmlat <library dir> <out.json> [firings] [seed] [--rhythm=off] [--late=off]
        // pair_rhythm: how fast each moment answers, from the pictures the game would show. Every moment is fired <firings>
        // times at random points of the idle loop (both idle 1.5-6 s, random moves running, no skits); per character entry:
        // trigger -> the action's first picture (the wait for a way in) -> its first moving picture (body; the lead before it
        // is still) -> its key picture. Touch moments fire their side reaction as PartnerView does.
        static void RhythmLatency(string[] args)
        {
            var pos = args.Where(a => !a.StartsWith("--")).ToList();
            if (pos.Count < 3) throw new ArgumentException("Usage: Headless.exe rhythmlat <library dir> <out.json> [firings] [seed] [--rhythm=off] [--late=off]");
            var lib = LoadRhythm(pos[1]);
            if (args.Contains("--rhythm=off")) lib.Rhythm = null;
            if (args.Contains("--late=off") && lib.Rhythm != null) lib.Rhythm.LateEntry = false;
            if (args.Contains("--anchor=pose") && lib.Rhythm != null) lib.Rhythm.AfterPose = true;
            int firings = pos.Count > 3 ? int.Parse(pos[3], CultureInfo.InvariantCulture) : 200;
            int seed = pos.Count > 4 ? int.Parse(pos[4], CultureInfo.InvariantCulture) : 5;
            var plan = new List<string[]>
            {
                new[] { "enter" }, new[] { "return" }, new[] { "song" }, new[] { "songHard" }, new[] { "category" }, new[] { "master" },
                new[] { "confirm" }, new[] { "countdown" }, new[] { "start" }, new[] { "bored:Ame" }, new[] { "bored:K" },
                new[] { "tap:K", "otherTap:Ame" }, new[] { "tap:Ame", "otherTap:K" }, new[] { "poke:K", "otherPoke:Ame" }, new[] { "poke:Ame", "otherPoke:K" },
                new[] { "resultTop" }, new[] { "resultGood" }, new[] { "resultClear" }, new[] { "resultFail" }, new[] { "roomEnter" }, new[] { "roomPresent" },
            };
            var rng = new Random(seed);
            var rows = new SortedDictionary<string, List<double[]>>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var group in plan)
            {
                if (!group.All(g => lib.HasMoment(g.Split(':')[0]))) continue;
                var tl = new SpriteTimeline(lib, seed + group[0].Sum(ch => (int)ch) % 1000, false, false);   // stable per moment (string hashes are randomized per process)
                // which actions each entry of these moments may play, per character: (row name, character, actions)
                var watch = new List<Tuple<string, int, HashSet<string>>>();
                foreach (var g in group)
                {
                    string name = g.Split(':')[0]; int who = g.Contains(':') ? SpriteLibrary.CharacterIndex(g.Split(':')[1]) : -1;
                    foreach (var m in lib.Moments)
                        if (m.Name == name && (who < 0 || m.Character == who) && !watch.Any(w => w.Item1 == name + " " + lib.Characters[m.Character].Name))
                            watch.Add(Tuple.Create(name + " " + lib.Characters[m.Character].Name, m.Character, new HashSet<string>(m.Picks.Select(p => lib.Actions[p.Action].Name))));
                }
                int fired = 0, idleFor = 0, wantIdle = 30 + rng.Next(90), guard = 0;
                while (fired < firings && guard++ < firings * 2000)
                {
                    tl.Advance();
                    var now = tl.Now;
                    bool idle = now.ActionOf(0) == null && now.ActionOf(1) == null && now.RequestOf(0) == null && now.RequestOf(1) == null;
                    idleFor = idle ? idleFor + 1 : 0;
                    if (idleFor < wantIdle) continue;
                    // fire, then follow the shown pictures up to 8 s
                    foreach (var g in group)
                    {
                        string name = g.Split(':')[0]; int who = g.Contains(':') ? SpriteLibrary.CharacterIndex(g.Split(':')[1]) : -1;
                        tl.Moment(name, who);
                    }
                    var res = watch.Select(w => new double[] { -1, -1, -1, -1 }).ToArray();
                    var takeSeen = new int[watch.Count]; for (int i = 0; i < watch.Count; i++) takeSeen[i] = -1;
                    for (int t = 1; t <= 160; t++)
                    {
                        tl.Advance();
                        for (int i = 0; i < watch.Count; i++)
                        {
                            int c = watch[i].Item2; var ch = lib.Characters[c]; var s = tl.Shot(0, c);
                            int k = ch.TakeOf[s.Frame]; var take = ch.Takes[k];
                            if (res[i][0] < 0)
                            {
                                if (take.Action == null || !watch[i].Item3.Contains(take.Action)) continue;
                                res[i][0] = t; takeSeen[i] = k;
                            }
                            if (k != takeSeen[i]) continue;
                            if (res[i][1] < 0 && ch.Phase[s.Frame] == SpriteLibrary.Body) res[i][1] = t;
                            if (res[i][2] < 0 && s.Frame - take.First >= take.Key) res[i][2] = t;
                            if (res[i][3] < 0 && s.Frame - take.First >= (take.PoseAt >= 0 ? take.PoseAt : take.Key)) res[i][3] = t;
                        }
                    }
                    for (int i = 0; i < watch.Count; i++)
                    {
                        if (res[i][0] < 0) continue;
                        if (!rows.ContainsKey(watch[i].Item1)) rows[watch[i].Item1] = new List<double[]>();
                        rows[watch[i].Item1].Add(new[] { res[i][0] / 20, (res[i][1] < 0 ? res[i][0] : res[i][1]) / 20, res[i][2] / 20, res[i][3] / 20 });
                    }
                    fired++; idleFor = 0; wantIdle = 30 + rng.Next(90);
                }
            }
            Func<List<double>, double, double> q = (xs, p) => { var a = xs.OrderBy(x => x).ToList(); return a.Count == 0 ? double.NaN : a[(int)Math.Min(a.Count - 1, Math.Floor(p * a.Count))]; };
            var json = new StringBuilder("{");
            Console.WriteLine(string.Format("{0,-22} {1,4}  {2,-15} {3,-15} {4,-15} {5,-9} {6}", "moment", "n", "first pic med/p90", "first move", "key med/p90", "lead", "pose med"));
            bool firstRow = true;
            foreach (var kv in rows)
            {
                var w = kv.Value.Select(r => r[0]).ToList(); var mv = kv.Value.Select(r => r[1]).ToList();
                var key = kv.Value.Where(r => r[2] >= 0).Select(r => r[2]).ToList(); var pose = kv.Value.Where(r => r[3] >= 0).Select(r => r[3]).ToList(); var lead = kv.Value.Select(r => r[1] - r[0]).ToList();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-22} {1,4}  {2,5:0.00} / {3,5:0.00}   {4,5:0.00} / {5,5:0.00}   {6,5:0.00} / {7,5:0.00}   {8,5:0.00}     {9,5:0.00}",
                    kv.Key, kv.Value.Count, q(w, .5), q(w, .9), q(mv, .5), q(mv, .9), q(key, .5), q(key, .9), q(lead, .5), q(pose, .5)));
                json.Append(firstRow ? "" : ",").Append(string.Format(CultureInfo.InvariantCulture,
                    "\"{0}\":{{\"n\":{1},\"first\":[{2:0.###},{3:0.###}],\"move\":[{4:0.###},{5:0.###}],\"key\":[{6:0.###},{7:0.###}],\"lead\":{8:0.###},\"pose\":{9:0.###}}}",
                    kv.Key, kv.Value.Count, q(w, .5), q(w, .9), q(mv, .5), q(mv, .9), q(key, .5), q(key, .9), q(lead, .5), q(pose, .5)));
                firstRow = false;
            }
            json.Append("}");
            File.WriteAllText(pos[2], json.ToString());
        }
    }
}
