using System;
using System.IO;
using PartnerLive2D.Core;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void CheckSpritePack(string path)
        {
            var manifest = SpritePairManifest.Parse(File.ReadAllText(path));
            if (manifest.Clips.Count != 50) throw new Exception("expected 50 sprite clips");
            int drawings = 0;
            foreach (var clip in manifest.Clips)
            {
                var last = clip.Drawings[clip.Drawings.Length - 1];
                var bin = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), clip.Name + ".bin");
                if (new FileInfo(bin).Length != last.Offset + last.Length) throw new Exception("pack size mismatch: " + clip.Name);
                drawings += clip.Drawings.Length;
            }
            Console.WriteLine("Sprite manifest and pack lengths passed: " + manifest.Clips.Count + " clips, " + drawings + " drawings");
        }

        static void SpritePairChecks()
        {
            const string json = "{\"version\":1,\"pairCanvas\":[1024,1024],\"drawOrder\":[\"Ame\",\"K\"],\"crossfadeSeconds\":0.12," +
                "\"clips\":[{\"name\":\"1-K-15s\",\"character\":\"K\",\"group\":\"Idle\",\"tier\":null,\"fps\":24,\"totalFrames\":6," +
                "\"textureWidth\":4,\"textureHeight\":4,\"rect\":[-1,2,4,4],\"drawings\":[{\"offset\":0,\"length\":16,\"hold\":2},{\"offset\":16,\"length\":16,\"hold\":4}]}]}";
            var manifest = SpritePairManifest.Parse(json);
            var clip = manifest.Clips[0];
            if (clip.Name != "1-K-15s" || clip.Drawings[1].Offset != 16 || clip.Rect[0] != -1) throw new Exception("sprite manifest parsing");
            try { SpritePairManifest.Parse(json.Replace("\"hold\":4", "\"hold\":3")); throw new Exception("invalid sprite manifest accepted"); }
            catch (FormatException) { }
            var rng = new Random(42);
            for (int count = 2; count <= 3; count++)
            {
                int previous = -1;
                for (int i = 0; i < 1000; i++)
                {
                    int next = SpritePairMath.Pick(rng, count, previous);
                    if (next == previous || next < 0 || next >= count) throw new Exception("sprite repeat picker");
                    previous = next;
                }
            }
            if (SpritePairMath.DrawingAt(clip, 0, true) != 0 || SpritePairMath.DrawingAt(clip, 2.0 / 24, true) != 1 ||
                SpritePairMath.DrawingAt(clip, 6.0 / 24, true) != 0 || SpritePairMath.DrawingAt(clip, 8.0 / 24, false) != 1)
                throw new Exception("sprite timeline or loop");
            if (SpritePairMath.IncomingWeight(0, .12f) != 0 || SpritePairMath.IncomingWeight(.12f, .12f) != 1 ||
                Math.Abs(SpritePairMath.IncomingWeight(.06f, .12f) - .5f) > .00001f) throw new Exception("sprite crossfade");
            if (SpritePairMath.Tier(true, 969999, false) != "Clear" || SpritePairMath.Tier(true, 970000, false) != "Good" ||
                SpritePairMath.Tier(true, 999999, false) != "Good" || SpritePairMath.Tier(true, 1000000, false) != "Top" ||
                SpritePairMath.Tier(true, 990000, true) != "Top" || SpritePairMath.Tier(false, 999999, false) != "Fail")
                throw new Exception("sprite result tiers");
            // Exercise the actual pair controller: both characters start, delay and return.
            var pair = new SpritePairManifest { CrossfadeSeconds = .12f };
            foreach (string who in new[] { "Ame", "K" })
                foreach (string group in new[] { "Idle", "Enter" })
                    pair.Clips.Add(new SpritePairManifest.Clip { Name = who + group, Character = who, Group = group, Fps = 24, TotalFrames = 24,
                        Drawings = new[] { new SpritePairManifest.Drawing { Hold = 24 } } });
            var playback = new SpritePairPlayback(pair, "Idle", true, c => true, 7);
            playback.Tick(.01f);
            if (playback.Characters[0].Current.Group != "Idle" || playback.Characters[1].Current.Group != "Idle") throw new Exception("pair loop startup");
            if (!playback.Play("Enter", -1) || !playback.IsPlaying("Enter")) throw new Exception("pair enter trigger");
            if (playback.Characters[1].Delay < 0 || playback.Characters[1].Delay > .2f || playback.Incoming(0) != 0) throw new Exception("pair start delay/fade");
            double prior = playback.Characters[1].Time;
            float delay = playback.Characters[1].Delay;
            if (delay > .001f)
            {
                playback.Tick(delay * .5f);
                if (playback.Characters[1].Current.Group != "Idle" || playback.Characters[1].Time <= prior) throw new Exception("delayed character must keep playing outgoing loop");
            }
            playback.Tick(1.21f);
            if (playback.IsPlaying("Enter")) throw new Exception("pair return to loop");
            if (!playback.Play("Enter", 1) || playback.Characters[1].Pending == null || playback.Characters[1].Pending.Character != "K") throw new Exception("combined group index");
            if (playback.Play("Enter", 2)) throw new Exception("out of range combined group index");
            var unavailable = new SpritePairPlayback(pair, "Idle", true, c => c.Group == "Idle", 7);
            unavailable.Tick(.01f);
            if (unavailable.Play("Enter", -1) || unavailable.Characters[0].Current.Group != "Idle") throw new Exception("partial pair trigger");
            var allocationPlayback = new SpritePairPlayback(pair, "Idle", true, c => true, 5);
            for (int i = 0; i < 100; i++) allocationPlayback.Tick(.016f);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) allocationPlayback.Tick(.016f);
            if (GC.GetAllocatedBytesForCurrentThread() != allocatedBefore) throw new Exception("sprite frame state allocates");
            Console.WriteLine("Sprite pair data, picker, timeline, fades and tier checks passed");
        }
    }
}
