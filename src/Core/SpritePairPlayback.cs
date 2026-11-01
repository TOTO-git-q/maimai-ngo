using System;
using System.Collections.Generic;

namespace PartnerLive2D.Core
{
    // Page-local playback state; all group tables are built before the frame loop.
    public sealed class SpritePairPlayback
    {
        public sealed class CharacterState
        {
            public SpritePairManifest.Clip Current, Outgoing, Pending;
            public int OutgoingDrawing;
            public double Time;
            public float Fade, Delay, RandomIn;
            internal int[] Previous;
        }
        sealed class Group
        {
            public string Name;
            public readonly List<SpritePairManifest.Clip>[] Clips = { new List<SpritePairManifest.Clip>(), new List<SpritePairManifest.Clip>() };
        }
        readonly List<Group> _groups = new List<Group>();
        readonly Func<SpritePairManifest.Clip, bool> _ready;
        readonly Random _random;
        readonly bool _select;
        readonly int _loop;
        readonly float _fade;
        public readonly CharacterState[] Characters = { new CharacterState(), new CharacterState() }; // Ame, K

        public SpritePairPlayback(SpritePairManifest manifest, string loop, bool select, Func<SpritePairManifest.Clip, bool> ready, int seed)
        {
            _ready = ready; _random = new Random(seed); _select = select; _fade = manifest.CrossfadeSeconds;
            foreach (var clip in manifest.Clips)
            {
                int g = Find(clip.Group);
                if (g < 0) { g = _groups.Count; _groups.Add(new Group { Name = clip.Group }); }
                _groups[g].Clips[clip.Character == "Ame" ? 0 : 1].Add(clip);
            }
            _loop = Find(loop);
            if (_loop < 0 || _groups[_loop].Clips[0].Count == 0 || _groups[_loop].Clips[1].Count == 0)
                throw new FormatException("missing loop pair");
            foreach (var s in Characters)
            {
                s.Previous = new int[_groups.Count];
                for (int i = 0; i < s.Previous.Length; i++) s.Previous[i] = -1;
                s.RandomIn = NextRandom();
            }
        }
        int Find(string name) { for (int i = 0; i < _groups.Count; i++) if (_groups[i].Name == name) return i; return -1; }
        float NextRandom() { return 8f + (float)_random.NextDouble() * 7f; }
        public int Count(string group) { int g = Find(group); return g < 0 ? 0 : _groups[g].Clips[0].Count + _groups[g].Clips[1].Count; }
        public bool CanPlay(string group)
        {
            int g = Find(group);
            if (g < 0) return false;
            for (int c = 0; c < 2; c++)
            {
                var list = _groups[g].Clips[c];
                if (list.Count == 0) return false;
                for (int i = 0; i < list.Count; i++) if (!_ready(list[i])) return false;
            }
            return true;
        }
        bool Start(int c, int group, int index, float delay)
        {
            var s = Characters[c]; var clips = _groups[group].Clips[c];
            if (clips.Count == 0) return false;
            int chosen = index < 0 ? SpritePairMath.Pick(_random, clips.Count, s.Previous[group]) : index;
            if (chosen < 0 || chosen >= clips.Count || !_ready(clips[chosen])) return false;
            s.Previous[group] = chosen;
            if (delay > 0 && s.Current != null)
            {
                s.Pending = clips[chosen]; s.Delay = delay;
                return true;
            }
            Switch(s, clips[chosen]);
            return true;
        }
        void Switch(CharacterState s, SpritePairManifest.Clip clip)
        {
            s.Pending = null;
            if (s.Current != null)
            {
                s.Outgoing = s.Current;
                s.OutgoingDrawing = SpritePairMath.DrawingAt(s.Current, s.Time, s.Current.Group == _groups[_loop].Name);
            }
            s.Current = clip; s.Time = 0; s.Fade = 0; s.Delay = 0;
        }
        // Nonnegative indexes address the combined Ame-then-K group; the other character picks independently.
        public bool Play(string group, int index)
        {
            int g = Find(group);
            if (g < 0 || !CanPlay(group)) return false;
            int ameCount = _groups[g].Clips[0].Count, kCount = _groups[g].Clips[1].Count;
            if (index >= ameCount + kCount) return false;
            int ameIndex = index >= 0 && index < ameCount ? index : -1;
            int kIndex = index >= ameCount ? index - ameCount : -1;
            Start(0, g, ameIndex, 0); Start(1, g, kIndex, (float)_random.NextDouble() * .2f);
            return true;
        }
        public void Tick(float dt)
        {
            if (dt < 0) return;
            for (int c = 0; c < 2; c++)
            {
                var s = Characters[c];
                if (s.Current == null) { Start(c, _loop, 0, 0); continue; }
                float advance = dt;
                if (s.Pending != null)
                {
                    float before = Math.Min(advance, s.Delay);
                    s.Time += before; s.Fade += before; s.Delay -= before; advance -= before;
                    if (s.Delay <= 0) Switch(s, s.Pending);
                    else continue;
                }
                s.Time += advance; s.Fade += advance;
                if (s.Fade >= _fade) s.Outgoing = null;
                if (s.Current.Group != _groups[_loop].Name && s.Time >= (double)s.Current.TotalFrames / s.Current.Fps)
                    Start(c, _loop, -1, 0);
                if (_select)
                {
                    s.RandomIn -= advance;
                    if (s.RandomIn <= 0)
                    {
                        s.RandomIn = NextRandom();
                        int g = Find("Random");
                        if (g >= 0 && s.Current.Group == _groups[_loop].Name) Start(c, g, -1, 0);
                    }
                }
            }
        }
        public float Incoming(int c) { var s = Characters[c]; return s.Outgoing == null ? 1 : SpritePairMath.IncomingWeight(s.Fade, _fade); }
        public bool IsPlaying(string group)
        {
            for (int c = 0; c < 2; c++) if ((Characters[c].Current != null && Characters[c].Current.Group == group) || (Characters[c].Pending != null && Characters[c].Pending.Group == group)) return true;
            return false;
        }
    }
}
