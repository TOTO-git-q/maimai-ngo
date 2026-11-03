using System;
using System.Collections.Generic;

namespace PartnerLive2D.Core
{
    // Floating, breathing, follow-through: a spring layer over each character's frames on the select page and the result
    // screen (owner 2026-11-02: "加点漂浮 形变 位移这种增加动感的效果"; "浮动有点僵硬"; then, evening: the whole picture
    // swaying, squashing and shaking "像贴纸").
    // So she is no longer one picture moved, squashed and shaken as a whole. Every point of her has a softness
    // (SpriteLibrary.Character.FlexAt, from library.py: 0 in the thick middle of her body, 1 in hair ends, ribbons, the hem,
    // the feet):
    //   - her core floats a little (three layered waves of different periods, never a metronome) and takes the reactions'
    //     kicks on under-damped springs;
    //   - the soft parts follow the core late (0.45 s behind its float, on looser springs behind its kicks: inertia hands
    //     the core's movement on, strength pulls back) and sway in a slow wave running along her body - the follow-through
    //     Live2D / Spine physics give hair and cloth;
    //   - breathing stretches only her core, not her outline;
    //   - no squash and stretch, no scale pulses, no whole-body shaking, no hit-stop: the old effect names now turn into a
    //     lift, a settle, a lean or a shiver of the soft parts.
    // Units: pair-canvas pixels (1024 square, y down), radians. Pure math; the backend turns a Pose into mesh vertices.
    public sealed class PairFeel
    {
        // the core's move and turn (around the pivot), where the soft parts lag to (move and turn, at full softness), the
        // sway's size and phase, the breath (how far the core is stretched)
        public struct Pose { public float Dx, Dy, Rot, LagX, LagY, LagRot, WaveA, WavePh, Breath; }
        public struct Effect { public string Name; public float Strength; }

        sealed class Spring
        {
            public float X, V;
            public readonly float K, C, Omega;
            public Spring(float k, float c) { K = k; C = c; Omega = (float)Math.Sqrt(k); }
            public float Accel { get { return -K * X - C * V; } }
            public void Step(float dt) { V += Accel * dt; X += V * dt; }
            public void Kick(float peak) { V += peak * Omega; }             // about `peak` at the first swing
        }
        // the soft parts on their own springs behind the core: a move and a turn
        sealed class Soft
        {
            public float X, Y, R, Vx, Vy, Vr;
            public const float K = 36, C = 3.4f, KR = 30, CR = 3.0f, Inertia = .9f;   // ~1 Hz, damping ratio ~0.28
            public void Step(float dt, float ax, float ay, float ar)
            {
                Vx += (-K * X - C * Vx + ax) * dt; Vy += (-K * Y - C * Vy + ay) * dt; Vr += (-KR * R - CR * Vr + ar) * dt;
                X += Vx * dt; Y += Vy * dt; R += Vr * dt;
            }
        }
        sealed class Wave { public float A, W, P; }
        sealed class Body
        {
            // the core: up / down 2.4 Hz, sideways 2.3 Hz, turn 1.2 Hz, damping ratios ~0.4 (a visible overshoot)
            public readonly Spring Y = new Spring(220, 12), X = new Spring(200, 11), Rot = new Spring(60, 6.5f);
            public readonly Soft Soft = new Soft();
            public Wave[] Fy, Fx;
            public float Tau, Bank, TiltAmp, TiltW, TiltP, BreathAmp, BreathPeriod, SwayAmp, SwayW, SwayPh, Mod;
            public float Burst;                               // extra sway of the soft parts (a touch, a shiver, a gust), dies away
            public float NudgeAt = -1, NudgeX, NudgeTurn;    // jostled by a touch on the other one, a moment later
        }

        readonly Body[] _b = { new Body(), new Body() };
        readonly Random _rng;
        readonly Wave[] _apart = new Wave[2];                 // the two drift apart and together a little (K +, Ame -)
        float _t, _gust;
        public bool Holding { get { return false; } }         // no hit-stop any more: nothing freezes

        public PairFeel(int seed, SpriteLibrary.Character ame, SpriteLibrary.Character k)
        {
            _rng = new Random(seed);
            // K floats on her side: more bob and drift; Ame lies back: slower, smaller. Periods differ between the two.
            // The core moves less than in the afternoon (21 / 14 px): what reads as alive now is the soft parts following.
            Init(_b[0], 8f, new[] { 5.3f, 3.3f, 2.1f }, 3.5f, new[] { 7.9f, 4.9f, 3.1f }, .022f, 3.2f, 3.1f);
            Init(_b[1], 11f, new[] { 4.6f, 2.9f, 1.7f }, 5f, new[] { 6.7f, 4.1f, 2.5f }, .018f, 4.2f, 2.7f);
            _apart[0] = new Wave { A = 5, W = 2 * (float)Math.PI / 7.3f, P = (float)(_rng.NextDouble() * 2 * Math.PI) };
            _apart[1] = new Wave { A = 2.5f, W = 2 * (float)Math.PI / 11.9f, P = (float)(_rng.NextDouble() * 2 * Math.PI) };
            _gust = 6 + (float)_rng.NextDouble() * 4;
        }
        void Init(Body b, float ay, float[] py, float ax, float[] px, float breath, float sway, float swayPeriod)
        {
            float[] wy = { .62f, .26f, .12f }, wx = { .6f, .3f, .1f };
            b.Fy = new Wave[3]; b.Fx = new Wave[3];
            for (int i = 0; i < 3; i++)
            {
                b.Fy[i] = new Wave { A = ay * wy[i], W = 2 * (float)Math.PI / py[i], P = (float)(_rng.NextDouble() * 2 * Math.PI) };
                b.Fx[i] = new Wave { A = ax * wx[i], W = 2 * (float)Math.PI / px[i], P = (float)(_rng.NextDouble() * 2 * Math.PI) };
            }
            b.Tau = .45f;
            b.Bank = .002f;                                               // lean into the drift (rad per px/s)
            b.TiltAmp = .4f * (float)Math.PI / 180; b.TiltW = 2 * (float)Math.PI / 9.7f; b.TiltP = (float)(_rng.NextDouble() * 6.3);
            b.BreathAmp = breath; b.BreathPeriod = 3.6f + (float)_rng.NextDouble() * .4f;
            b.SwayAmp = sway; b.SwayW = 2 * (float)Math.PI / swayPeriod; b.SwayPh = (float)(_rng.NextDouble() * 6.3);
            b.Mod = (float)(_rng.NextDouble() * 6.3);
        }

        // the float: three waves, gently swelling and calming over ~23 s
        static float Sum(Wave[] ws, float t) { float s = 0; foreach (var w in ws) s += w.A * (float)Math.Sin(w.W * t + w.P); return s; }
        static float Slope(Wave[] ws, float t) { float s = 0; foreach (var w in ws) s += w.A * w.W * (float)Math.Cos(w.W * t + w.P); return s; }
        float Swell(Body b, float t) { return .8f + .2f * (float)Math.Sin(2 * Math.PI * t / 23 + b.Mod); }
        // breathing: in over 40 % of the cycle, out over 60 %
        static float Breath(float t, float period)
        {
            float u = t / period; u -= (float)Math.Floor(u);
            return u < .4f ? (float)Math.Sin(Math.PI / 2 * u / .4f) : (float)Math.Cos(Math.PI / 2 * (u - .4f) / .6f);
        }

        public void Update(float dt)
        {
            if (dt <= 0) return;
            dt = Math.Min(dt, .1f);
            _t += dt;
            int steps = Math.Max(1, (int)Math.Ceiling(dt / (1f / 240)));      // stiff springs: small steps
            float h = dt / steps;
            for (int c = 0; c < 2; c++)
            {
                var b = _b[c];
                for (int i = 0; i < steps; i++)
                {
                    // the soft parts hang back on the core's movement (inertia), and are pulled back to it (strength)
                    float ax = b.X.Accel, ay = b.Y.Accel, ar = b.Rot.Accel;
                    b.Soft.Step(h, -Soft.Inertia * ax, -Soft.Inertia * ay, -Soft.Inertia * ar);
                    b.Y.Step(h); b.X.Step(h); b.Rot.Step(h);
                }
                b.SwayPh += b.SwayW * dt;
                b.Burst *= (float)Math.Exp(-dt / .7f);
                if (b.NudgeAt > 0 && (b.NudgeAt -= dt) <= 0) { Move(b, b.NudgeX, -3); Turn(b, b.NudgeTurn); }
            }
            // a gust now and then: the soft parts of both are blown to one side and sway harder, Ame a moment after K
            if ((_gust -= dt) <= 0)
            {
                _gust = 8 + (float)_rng.NextDouble() * 4;
                float side = _rng.NextDouble() < .5 ? -1 : 1;
                for (int c = 0; c < 2; c++) { var b = _b[c]; b.Soft.Vx += side * (c == 1 ? 34 : 26); b.Burst += c == 1 ? 2.5f : 2f; }
            }
        }

        public Pose Get(int c)
        {
            var b = _b[c]; float t = _t, swell = Swell(b, t);
            var p = new Pose();
            float fx = Sum(b.Fx, t) * swell, fy = Sum(b.Fy, t) * swell, apart = Sum(_apart, t) * (c == 1 ? 1 : -.7f);
            p.Dx = fx + .5f * apart + b.X.X;
            p.Dy = fy + apart + b.Y.X;
            float tilt = b.TiltAmp * (float)Math.Sin(b.TiltW * t + b.TiltP);
            p.Rot = b.Bank * Slope(b.Fx, t) * swell + tilt + b.Rot.X;
            // the soft parts: where the float had the core Tau ago (they follow late), plus their springs
            p.LagX = (Sum(b.Fx, t - b.Tau) - Sum(b.Fx, t)) * swell + b.Soft.X;
            p.LagY = (Sum(b.Fy, t - b.Tau) - Sum(b.Fy, t)) * swell + b.Soft.Y;
            p.LagRot = b.TiltAmp * (float)Math.Sin(b.TiltW * (t - b.Tau) + b.TiltP) - tilt + b.Soft.R;
            p.WaveA = b.SwayAmp * swell + b.Burst;
            p.WavePh = b.SwayPh;
            p.Breath = b.BreathAmp * (Breath(t, b.BreathPeriod) - .5f);
            p.Dx = Clamp(p.Dx, -45, 45); p.Dy = Clamp(p.Dy, -45, 45); p.Rot = Clamp(p.Rot, -.07f, .07f);
            p.LagX = Clamp(p.LagX, -35, 35); p.LagY = Clamp(p.LagY, -35, 35); p.LagRot = Clamp(p.LagRot, -.08f, .08f);
            p.WaveA = Clamp(p.WaveA, 0, 12);
            return p;
        }
        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

        // one cue of an action (start / key / end): its effects, in order
        public void Cue(int c, IList<Effect> effects)
        {
            if (effects == null) return;
            foreach (var e in effects) Apply(c, e.Name, e.Strength);
        }
        // a kick to the core; the soft parts keep their speed for a moment, so they hang back (inertia)
        void Move(Body b, float dx, float dy)
        {
            float vx = dx * b.X.Omega, vy = dy * b.Y.Omega;
            b.X.V += vx; b.Y.V += vy;
            b.Soft.Vx -= Soft.Inertia * vx; b.Soft.Vy -= Soft.Inertia * vy;
        }
        void Turn(Body b, float peak)
        {
            float w = peak * b.Rot.Omega; b.Rot.V += w; b.Soft.Vr -= Soft.Inertia * w;
        }
        // the action table's effect names (library.py FEEL), as soft moves of the core with the soft parts following:
        // lift and settle (hop / pop / punch / jolt up, sink / nod down), lean (sway / shake), a shiver of the soft parts
        // (tremble). Nothing squashes, scales, shakes as a whole or stops.
        public void Apply(int c, string name, float s)
        {
            var b = _b[c];
            float side = _rng.NextDouble() < .5 ? -1 : 1;
            switch (name)
            {
                case "hop": Move(b, 0, -10 * s); Turn(b, side * .006f * s); break;
                case "pop": Move(b, 0, -6 * s); break;
                case "punch": Move(b, 0, -4 * s); b.Burst += 1.5f * s; break;
                case "jolt": Move(b, 0, -7 * s); b.Burst += 2.5f * s; break;
                case "sink": Move(b, 0, 7 * s); break;
                case "nod": Move(b, 0, 5 * s); break;
                case "sway": Turn(b, side * .022f * s); break;
                case "shake": Turn(b, side * .012f * s); b.Burst += 2.5f * s; break;
                case "tremble": b.Burst += 3f * s; break;
            }
        }

        // a finger on character c at pair-canvas x: her core gives way from the finger and turns the same way, her soft
        // parts swing after it; the other one, lying against her, is jostled the same way a moment later, less
        public void Touch(int c, float x, float pivotX, float strength = 1)
        {
            var b = _b[c];
            float away = x < pivotX ? 1 : -1;
            Move(b, away * 9 * strength, -3 * strength); Turn(b, away * .02f * strength); b.Burst += 2 * strength;
            var o = _b[1 - c]; o.NudgeAt = .07f; o.NudgeX = away * 4 * strength; o.NudgeTurn = away * .01f * strength;
        }

        // where a point of the character's picture (pair canvas, y down) of the given softness is drawn under the pose
        public static void Deform(Pose p, float pivotX, float pivotY, float axisX, float axisY, float flex, float x, float y, out float ox, out float oy)
        {
            float lx = x - pivotX, ly = y - pivotY;
            ly *= 1 + p.Breath * (1 - flex);                      // the breath stretches her core, not her outline
            float cos = (float)Math.Cos(p.Rot), sin = (float)Math.Sin(p.Rot);
            ox = pivotX + lx * cos - ly * sin + p.Dx;
            oy = pivotY + lx * sin + ly * cos + p.Dy;
            float lag = (float)Math.Pow(flex, 1.5);                  // the soft parts: behind the core's move and turn
            ox += lag * (p.LagX - p.LagRot * ly);
            oy += lag * (p.LagY + p.LagRot * lx);
            // and swaying across her body axis in a wave that runs along it (a wavelength of ~380 px)
            float along = lx * axisX + ly * axisY;
            float sway = flex * flex * p.WaveA * (float)Math.Sin(p.WavePh + along * (2 * Math.PI / 380));
            ox -= sway * axisY; oy += sway * axisX;
        }
    }

    // Which feel cue a newly shown picture starts: an action's first picture (start), its key pose (key), the first picture
    // after it (end). Fed every picture the stage shows, so cues follow what is on screen, not the plan ahead.
    public sealed class PairCues
    {
        readonly SpriteLibrary _lib;
        readonly PairFeel _feel;
        readonly int[] _take = { -1, -1 };
        readonly bool[] _key = new bool[2];
        public PairCues(SpriteLibrary lib, PairFeel feel) { _lib = lib; _feel = feel; }
        public void Shown(int c, int frame, List<string> log = null)
        {
            var ch = _lib.Characters[c]; int k = ch.TakeOf[frame]; var take = ch.Takes[k];
            if (k != _take[c])
            {
                if (_take[c] >= 0 && ch.Takes[_take[c]].Action != null) Fire(c, ch.Takes[_take[c]].Action, "end", log);
                if (take.Action != null) Fire(c, take.Action, "start", log);
                _take[c] = k; _key[c] = false;
            }
            if (take.Action != null && !_key[c] && frame - take.First >= take.Key) { _key[c] = true; Fire(c, take.Action, "key", log); }
        }
        void Fire(int c, string action, string cue, List<string> log)
        {
            int a = _lib.ActionIndex(action);
            PairFeel.Effect[] effects;
            if (a < 0 || !_lib.Actions[a].Feel.TryGetValue(cue, out effects)) return;
            _feel.Cue(c, effects);
            if (log != null) log.Add(_lib.Characters[c].Name + " feel " + action + " " + cue);
        }
    }
}
