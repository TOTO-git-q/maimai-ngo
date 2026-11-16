using System;
using System.Collections.Generic;
using PartnerLive2D.Framework;

namespace PartnerLive2D.Core
{
    // Small pixel-art emotes next to the pair (art director 2026-11-13, tasks/pair_fx_task.md): a face on the select page is
    // ~60 px, so a sparkle / sweat drop / anger mark pops beside her at the action's beat instead. The table lives in
    // library.json "fx" (written by tools/ngo_fx_install.py); no "fx" object = no layer. Pure data and timing, no Unity.
    public sealed class FxTable
    {
        public const byte Pop = 0, Rise = 1, Loop = 2;
        public const byte AtStart = 0, AtKey = 1, AtSeconds = 2;
        public sealed class Sprite { public string Name; public int W, H, Frames, Y; }
        public sealed class Entry
        {
            public int Sprite, Anchor, Character;          // Character: whose float it follows (the anchor's owner)
            public byte At, Style; public double Seconds;   // Seconds: for AtSeconds
            public int Ticks, Hold, Loops, Count, Stagger, Peak = 3;  // Ticks: extra delay after the cue
            public float SpreadX, SpreadY, Scale;
        }
        public string Atlas;
        public int AtlasWidth, AtlasHeight, Texel = 4;
        public float Tick = 1f / 12, TopLimit = 100;
        public readonly float[] ArtPixel = { 1.7f, 1.7f };   // canvas px per art pixel, per character (0 Ame, 1 K)
        public Sprite[] Sprites;
        public string[] AnchorNames; public float[] AnchorX, AnchorY; public int[] AnchorCharacter;
        public Entry[][] ByAction;                            // per library action index; null = none
        public Entry Touch;                                   // the sparkle at a touch point (Anchor -1)
        public readonly List<string> Warnings = new List<string>();

        public int SpriteIndex(string name) { for (int i = 0; i < Sprites.Length; i++) if (Sprites[i].Name == name) return i; return -1; }
        public int AnchorIndex(string name) { for (int i = 0; i < AnchorNames.Length; i++) if (AnchorNames[i] == name) return i; return -1; }

        static float Num(object v, string what)
        {
            if (!(v is double)) throw new FormatException("fx: missing number " + what);
            double d = (double)v;
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException("fx: invalid number " + what);
            return (float)d;
        }
        static int Int(object v, string what, int lo, int hi)
        {
            float f = Num(v, what);
            if (f != Math.Floor(f) || f < lo || f > hi) throw new FormatException("fx: invalid integer " + what);
            return (int)f;
        }
        static int OptInt(object node, string key, int def, int lo, int hi) { var v = Json.Get(node, key); return v == null ? def : Int(v, key, lo, hi); }
        static float OptNum(object node, string key, float def) { var v = Json.Get(node, key); return v == null ? def : Num(v, key); }

        // Structural errors throw (the caller drops the layer); unknown action / sprite / anchor names only skip that entry
        // and leave one warning each (the art director edits the table by hand).
        public static FxTable Parse(object node, SpriteLibrary lib)
        {
            if (Json.GetInt(node, "version", -1) != 1) throw new FormatException("fx: unsupported version");
            var t = new FxTable { Atlas = Json.GetString(node, "atlas") };
            if (string.IsNullOrEmpty(t.Atlas) || t.Atlas.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || t.Atlas.Contains("..")) throw new FormatException("fx: atlas");
            var size = Json.GetList(node, "atlasSize");
            if (size == null || size.Count != 2) throw new FormatException("fx: atlasSize");
            t.AtlasWidth = Int(size[0], "atlasSize", 1, 8192); t.AtlasHeight = Int(size[1], "atlasSize", 1, 8192);
            t.Texel = OptInt(node, "texel", 4, 1, 64);
            t.Tick = OptNum(node, "tick", 1f / 12); if (t.Tick < .01f || t.Tick > 1) throw new FormatException("fx: tick");
            t.TopLimit = OptNum(node, "topLimit", 100);
            var art = Json.Get(node, "artPixel");
            if (art is double) t.ArtPixel[0] = t.ArtPixel[1] = Num(art, "artPixel");
            else if (art != null) { t.ArtPixel[0] = OptNum(art, "Ame", 1.7f); t.ArtPixel[1] = OptNum(art, "K", 1.7f); }
            if (t.ArtPixel[0] <= 0 || t.ArtPixel[1] <= 0) throw new FormatException("fx: artPixel");

            var sprites = Json.Get(node, "sprites") as JsonObject;
            if (sprites == null || sprites.Count == 0) throw new FormatException("fx: no sprites");
            var list = new List<Sprite>();
            foreach (string name in sprites.KeyOrder)
            {
                object s = sprites[name];
                var sp = new Sprite { Name = name, W = Int(Json.Get(s, "w"), "w", 1, 4096), H = Int(Json.Get(s, "h"), "h", 1, 4096),
                    Frames = Int(Json.Get(s, "frames"), "frames", 1, 64), Y = Int(Json.Get(s, "y"), "y", 0, 8192) };
                if (sp.Frames * sp.W * t.Texel > t.AtlasWidth || (sp.Y + sp.H) * t.Texel > t.AtlasHeight) throw new FormatException("fx: sprite " + name + " outside the atlas");
                if (sp.Frames < 4) throw new FormatException("fx: sprite " + name + " needs 4 frames");
                list.Add(sp);
            }
            t.Sprites = list.ToArray();

            var anchors = Json.Get(node, "anchors") as JsonObject;
            if (anchors == null) throw new FormatException("fx: no anchors");
            int n = anchors.Count;
            t.AnchorNames = new string[n]; t.AnchorX = new float[n]; t.AnchorY = new float[n]; t.AnchorCharacter = new int[n];
            int k = 0;
            foreach (string name in anchors.KeyOrder)
            {
                var xy = anchors[name] as List<object>;
                if (xy == null || xy.Count != 2) throw new FormatException("fx: anchor " + name);
                int dot = name.IndexOf('.');
                int c = SpriteLibrary.CharacterIndex(dot > 0 ? name.Substring(0, dot) : name);
                if (c < 0) throw new FormatException("fx: anchor " + name + " names no character");
                t.AnchorNames[k] = name; t.AnchorX[k] = Num(xy[0], "anchor"); t.AnchorY[k] = Num(xy[1], "anchor"); t.AnchorCharacter[k] = c; k++;
            }

            t.ByAction = new Entry[lib.Actions.Count][];
            var actions = Json.Get(node, "actions") as JsonObject;
            if (actions != null)
                foreach (string name in actions.KeyOrder)
                {
                    int a = lib.ActionIndex(name);
                    if (a < 0) { t.Warnings.Add("fx: unknown action '" + name + "', skipped"); continue; }
                    var entries = actions[name] as List<object>;
                    if (entries == null) throw new FormatException("fx: action " + name);
                    var got = new List<Entry>();
                    foreach (object en in entries)
                    {
                        var e = ReadEntry(t, en, name, false);
                        if (e != null) got.Add(e);
                    }
                    if (got.Count > 0) t.ByAction[a] = got.ToArray();
                }
            var touch = Json.Get(node, "touch");
            if (touch != null) t.Touch = ReadEntry(t, touch, "touch", true);
            return t;
        }

        static Entry ReadEntry(FxTable t, object en, string owner, bool touch)
        {
            string sprite = Json.GetString(en, "sprite");
            int s = sprite == null ? -1 : t.SpriteIndex(sprite);
            if (s < 0) { t.Warnings.Add("fx: " + owner + ": unknown sprite '" + sprite + "', skipped"); return null; }
            var e = new Entry { Sprite = s, Anchor = -1, Character = -1 };
            if (!touch)
            {
                string anchor = Json.GetString(en, "anchor");
                e.Anchor = anchor == null ? -1 : t.AnchorIndex(anchor);
                if (e.Anchor < 0) { t.Warnings.Add("fx: " + owner + ": unknown anchor '" + anchor + "', skipped"); return null; }
                e.Character = t.AnchorCharacter[e.Anchor];
                object at = Json.Get(en, "at");
                if (at is double) { e.At = AtSeconds; e.Seconds = Num(at, "at"); if (e.Seconds < 0) throw new FormatException("fx: at"); }
                else if (at == null || (string)at == "start") e.At = AtStart;
                else if ((string)at == "key") e.At = AtKey;
                else throw new FormatException("fx: " + owner + ": at '" + at + "'");
            }
            string style = Json.GetString(en, "style") ?? "pop";
            e.Style = style == "pop" ? Pop : style == "rise" ? Rise : style == "loop" ? Loop : (byte)255;
            if (e.Style == 255) throw new FormatException("fx: " + owner + ": style '" + style + "'");
            e.Ticks = OptInt(en, "ticks", 0, 0, 1000);
            e.Hold = OptInt(en, "hold", e.Style == Rise ? 7 : 5, 0, 1000);
            e.Loops = OptInt(en, "loops", 2, 1, 100);
            e.Peak = OptInt(en, "peak", 3, -1, 3);   // pop / rise: the frame held (the later frames play out before the blink); -1 = the four frames keep cycling
            e.Count = OptInt(en, "count", 1, 1, PairFx.PerCharacter);
            e.Stagger = OptInt(en, "stagger", 2, 0, 1000);
            e.Scale = OptNum(en, "scale", 1); if (e.Scale <= 0 || e.Scale > 10) throw new FormatException("fx: scale");
            var spread = Json.GetList(en, "spread");
            if (spread != null) { if (spread.Count != 2) throw new FormatException("fx: spread"); e.SpreadX = Num(spread[0], "spread"); e.SpreadY = Num(spread[1], "spread"); }
            return e;
        }

        // ticks a style lives (the blink included)
        public static int Length(byte style, int hold, int loops) { return style == Loop ? 8 * loops + 3 : 7 + hold; }
        static readonly int[] Sway = { 0, 1, 1, 0, -1, -1 };   // +-1 art pixel, period 6 ticks
        // the stepped animation: frame, size (of the art), shown, offset in art pixels (y down), at a tick of its life
        public static void State(byte style, int hold, int loops, int tick, out int frame, out float size, out bool shown, out int offX, out int offY)
        { State(style, hold, loops, 3, tick, out frame, out size, out shown, out offX, out offY); }
        public static void State(byte style, int hold, int loops, int peak, int tick, out int frame, out float size, out bool shown, out int offX, out int offY)
        {
            frame = 3; size = 1; shown = true; offX = 0; offY = 0;
            if (style == Loop)
            {
                int n = 8 * loops;
                if (tick < n) frame = (tick / 2) % 4;
                else shown = tick - n == 1;
                return;
            }
            if (tick == 0) { frame = 0; size = .6f; }
            else if (tick == 1) { frame = 1; size = 1.15f; }
            else if (tick == 2) frame = 2;
            else if (tick >= 4 + hold) shown = tick - (4 + hold) == 1;
            if (peak >= 0 && peak < 3 && tick >= 2 && tick < 3 + hold) frame = tick == 2 && peak > 2 ? 2 : peak;
            else if (peak < 0 && tick >= 3 && tick < 4 + hold) frame = ((tick - 3) / 2) % 4;
            if (style == Rise && tick >= 2) { offY = -2 * (tick - 1); offX = Sway[(tick - 2) % 6]; }
        }
    }

    // The emotes alive now: at most 4 per character, a character's new emote replaces her running one (all entries of one
    // action start together and stay together); each runs on its own 1/12 s ticks. Fixed arrays, no garbage per frame.
    public sealed class PairFx
    {
        public const int PerCharacter = 4, Slots = 2 * PerCharacter;
        public struct Draw { public int Sprite, Frame, Character, Tick; public float X, Y, W, H, Size; public bool Shown; }
        struct Live
        {
            public bool On, Follow, IsTouch; public byte Style;
            public int Sprite, Hold, Loops, Peak, Serial, Order;
            public float T, X, Y, Art;
        }
        readonly FxTable _t;
        readonly Live[] _live = new Live[Slots];
        readonly int[] _serial = new int[2];
        int _order;
        public List<string> Log;                    // spawns and replacements (headless / tests); null in game
        public FxTable Table { get { return _t; } }
        public PairFx(FxTable table) { _t = table; }

        public bool Any { get { for (int i = 0; i < Slots; i++) if (_live[i].On) return true; return false; } }
        public int Alive(int c) { int n = 0; for (int i = c * PerCharacter; i < (c + 1) * PerCharacter; i++) if (_live[i].On) n++; return n; }

        // an action's first picture is shown (PairCues "start"); take = its take (key -1: no key cue -> 40 % of its length)
        // shownAt: pictures into the take of the first one shown (a later way in, pair_rhythm): "at" seconds count from the
        // take's own first picture, so an emote placed on a beat stays on it however the action was entered
        public void ActionStart(int c, int action, SpriteLibrary.Take take, int shownAt = 0)
        {
            var entries = action >= 0 && action < _t.ByAction.Length ? _t.ByAction[action] : null;
            if (entries == null) return;
            _serial[c]++;
            foreach (var e in entries)
            {
                float delay = e.Ticks * _t.Tick;
                if (e.At == FxTable.AtStart) Spawn(e, delay);
                else if (e.At == FxTable.AtSeconds) Spawn(e, Math.Max(0f, (float)e.Seconds - shownAt / (float)SpriteLibrary.Fps) + delay);
                else if (take.Key < 0) Spawn(e, .4f * take.Count / SpriteLibrary.Fps + delay);
            }
        }
        // its key pose is shown (the same picture PairCues fires "key" on)
        public void ActionKey(int c, int action, SpriteLibrary.Take take)
        {
            var entries = action >= 0 && action < _t.ByAction.Length ? _t.ByAction[action] : null;
            if (entries == null || take.Key < 0) return;
            foreach (var e in entries) if (e.At == FxTable.AtKey) Spawn(e, e.Ticks * _t.Tick);
        }
        // a touch that hit character c, at a pair-canvas point: one sparkle there (it stays where the finger was)
        public void Touch(int c, float x, float y)
        {
            var e = _t.Touch;
            if (e == null || c < 0 || c > 1) return;
            for (int i = c * PerCharacter; i < (c + 1) * PerCharacter; i++) if (_live[i].On && _live[i].IsTouch) _live[i].On = false;
            int slot = Slot(c);
            _live[slot] = new Live { On = true, Follow = false, IsTouch = true, Style = e.Style, Sprite = e.Sprite, Hold = e.Hold, Loops = e.Loops, Peak = e.Peak,
                Serial = -1, Order = ++_order, T = 0, X = x, Y = y, Art = _t.ArtPixel[c] * e.Scale };
            if (Log != null) Log.Add(string.Format("fx touch {0} {1} at ({2:0}, {3:0})", c == 0 ? "Ame" : "K", _t.Sprites[e.Sprite].Name, x, y));
        }

        void Spawn(FxTable.Entry e, float delay)
        {
            int c = e.Character, serial = _serial[c];
            // the character's running emote (an earlier action's) goes at once
            for (int i = c * PerCharacter; i < (c + 1) * PerCharacter; i++)
                if (_live[i].On && !_live[i].IsTouch && _live[i].Serial != serial)
                {
                    _live[i].On = false;
                    if (Log != null) Log.Add("fx replace " + (c == 0 ? "Ame" : "K") + " " + _t.Sprites[_live[i].Sprite].Name);
                }
            float art = _t.ArtPixel[c] * e.Scale;
            for (int k = 0; k < e.Count; k++)
            {
                int slot = Slot(c);
                _live[slot] = new Live { On = true, Follow = true, Style = e.Style, Sprite = e.Sprite, Hold = e.Hold, Loops = e.Loops, Peak = e.Peak, Serial = serial,
                    Order = ++_order, T = -(delay + k * e.Stagger * _t.Tick), X = _t.AnchorX[e.Anchor] + k * e.SpreadX * art,
                    Y = _t.AnchorY[e.Anchor] + k * e.SpreadY * art, Art = art };
            }
            if (Log != null) Log.Add(string.Format("fx spawn {0} {1} @{2} x{3} in {4:0.000} s", c == 0 ? "Ame" : "K", _t.Sprites[e.Sprite].Name, _t.AnchorNames[e.Anchor], e.Count, delay));
        }
        // a free slot of the character, else her oldest one (the cap: 4 alive per character)
        int Slot(int c)
        {
            int oldest = -1;
            for (int i = c * PerCharacter; i < (c + 1) * PerCharacter; i++)
            {
                if (!_live[i].On) return i;
                if (oldest < 0 || _live[i].Order < _live[oldest].Order) oldest = i;
            }
            return oldest;
        }

        public void Update(float dt)
        {
            for (int i = 0; i < Slots; i++)
            {
                if (!_live[i].On) continue;
                _live[i].T += dt;
                if (_live[i].T >= 0 && (int)(_live[i].T / _t.Tick) >= FxTable.Length(_live[i].Style, _live[i].Hold, _live[i].Loops)) _live[i].On = false;
            }
        }
        public void Clear() { for (int i = 0; i < Slots; i++) _live[i].On = false; }

        // slot i as drawn now: centre (pair canvas, y down, snapped to whole art pixels, top kept at TopLimit or below),
        // size in canvas px; dx / dy: each character's core float (PairFeel pose Dx, Dy). False: nothing in this slot yet.
        public bool Get(int i, float dxAme, float dyAme, float dxK, float dyK, out Draw d)
        {
            d = default(Draw);
            var l = _live[i];
            if (!l.On || l.T < 0) return false;
            int c = i / PerCharacter, tick = (int)(l.T / _t.Tick), frame, ox, oy; float size; bool shown;
            FxTable.State(l.Style, l.Hold, l.Loops, l.Peak, tick, out frame, out size, out shown, out ox, out oy);
            var sp = _t.Sprites[l.Sprite];
            float x = l.X + ox * l.Art, y = l.Y + oy * l.Art;
            if (l.Follow) { x += c == 0 ? dxAme : dxK; y += c == 0 ? dyAme : dyK; }
            x = (float)Math.Round(x / l.Art) * l.Art; y = (float)Math.Round(y / l.Art) * l.Art;
            float w = sp.W * l.Art * size, h = sp.H * l.Art * size;
            if (y - h / 2 < _t.TopLimit) y = _t.TopLimit + h / 2;
            d = new Draw { Sprite = l.Sprite, Frame = frame, Character = c, Tick = tick, X = x, Y = y, W = w, H = h, Size = size * l.Art / _t.ArtPixel[c], Shown = shown };
            return true;
        }
    }
}
