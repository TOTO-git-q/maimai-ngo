using System;
using System.Collections.Generic;
using System.IO;
using PartnerLive2D.Core;

namespace PartnerLive2D.Framework
{
    // CubismMotionQueueEntry
    internal sealed class MotionQueueEntry
    {
        public ACubismMotion Motion;
        public bool Available = true;
        public bool Finished;
        public bool Started;
        public float StartTime = -1f;
        public float FadeInStartTime = 0f;
        public float EndTime = -1f;
        public float StateTime;
        public float StateWeight;
        public float FadeOutSeconds;
        public bool TriggeredFadeOut;

        public void SetFadeout(float fadeOutSeconds)
        {
            FadeOutSeconds = fadeOutSeconds;
            TriggeredFadeOut = true;
        }

        public void StartFadeout(float fadeOutSeconds, float userTimeSeconds)
        {
            float newEndTimeSeconds = userTimeSeconds + fadeOutSeconds;
            TriggeredFadeOut = true;
            if (EndTime < 0.0f || newEndTimeSeconds < EndTime)
                EndTime = newEndTimeSeconds;
        }

        public void SetState(float timeSeconds, float weight)
        {
            StateTime = timeSeconds;
            StateWeight = weight;
        }
    }

    // ACubismMotion
    internal abstract class ACubismMotion
    {
        public float FadeInSeconds = -1f;
        public float FadeOutSeconds = -1f;
        public float Weight = 1f;
        public float OffsetSeconds = 0f;
        public bool IsLoop = false;
        public bool IsLoopFadeIn = true;
        protected bool PreviousLoopState;

        protected ACubismMotion() { PreviousLoopState = IsLoop; }

        public virtual float GetDuration() { return -1f; }
        public virtual float GetLoopDuration() { return -1f; }

        public abstract void DoUpdateParameters(CubismModel model, float userTimeSeconds, float fadeWeight, MotionQueueEntry entry);

        public void UpdateParameters(CubismModel model, MotionQueueEntry entry, float userTimeSeconds)
        {
            if (!entry.Available || entry.Finished) return;

            SetupMotionQueueEntry(entry, userTimeSeconds);

            float fadeWeight = UpdateFadeWeight(entry, userTimeSeconds);

            DoUpdateParameters(model, userTimeSeconds, fadeWeight, entry);

            // set finished flag once past the end time
            if (entry.EndTime > 0.0f && entry.EndTime < userTimeSeconds)
                entry.Finished = true;
        }

        public void SetupMotionQueueEntry(MotionQueueEntry entry, float userTimeSeconds)
        {
            if (!entry.Available || entry.Finished) return;
            if (entry.Started) return;

            entry.Started = true;
            entry.StartTime = userTimeSeconds - OffsetSeconds;
            entry.FadeInStartTime = userTimeSeconds;

            if (entry.EndTime < 0)
            {
                // an end time may have been requested before the motion started
                AdjustEndTime(entry);
            }
        }

        public float UpdateFadeWeight(MotionQueueEntry entry, float userTimeSeconds)
        {
            float fadeWeight = Weight;

            float fadeIn = FadeInSeconds == 0.0f
                ? 1.0f
                : CMath.GetEasingSine((userTimeSeconds - entry.FadeInStartTime) / FadeInSeconds);

            float fadeOut = (FadeOutSeconds == 0.0f || entry.EndTime < 0.0f)
                ? 1.0f
                : CMath.GetEasingSine((entry.EndTime - userTimeSeconds) / FadeOutSeconds);

            fadeWeight = fadeWeight * fadeIn * fadeOut;
            entry.SetState(userTimeSeconds, fadeWeight);
            return fadeWeight;
        }

        protected void AdjustEndTime(MotionQueueEntry entry)
        {
            float duration = GetDuration();
            // duration == -1 means "loops forever"
            float endTime = (duration <= 0) ? -1 : entry.StartTime + duration;
            entry.EndTime = endTime;
        }
    }

    // CubismMotionQueueManager (without user-data events)
    internal class MotionQueueManager
    {
        protected readonly List<MotionQueueEntry> Motions = new List<MotionQueueEntry>();
        protected float UserTimeSeconds;

        public MotionQueueEntry StartMotion(ACubismMotion motion)
        {
            if (motion == null) return null;

            // flag every running entry to fade out
            for (int i = 0; i < Motions.Count; i++)
            {
                var e = Motions[i];
                if (e == null) continue;
                e.SetFadeout(e.Motion.FadeOutSeconds);
            }

            var entry = new MotionQueueEntry();
            entry.Motion = motion;
            Motions.Add(entry);
            return entry;
        }

        public bool DoUpdateMotion(CubismModel model, float userTimeSeconds)
        {
            bool updated = false;

            for (int i = 0; i < Motions.Count;)
            {
                var entry = Motions[i];
                if (entry == null || entry.Motion == null)
                {
                    Motions.RemoveAt(i);
                    continue;
                }

                entry.Motion.UpdateParameters(model, entry, userTimeSeconds);
                updated = true;

                if (entry.Finished)
                {
                    Motions.RemoveAt(i);
                }
                else
                {
                    if (entry.TriggeredFadeOut)
                        entry.StartFadeout(entry.FadeOutSeconds, userTimeSeconds);
                    i++;
                }
            }
            return updated;
        }

        public bool IsFinished()
        {
            for (int i = 0; i < Motions.Count;)
            {
                var entry = Motions[i];
                if (entry == null || entry.Motion == null)
                {
                    Motions.RemoveAt(i);
                    continue;
                }
                if (!entry.Finished) return false;
                i++;
            }
            return true;
        }

        public void StopAllMotions()
        {
            Motions.Clear();
        }
    }

    // CubismMotionManager: single main motion channel with priorities
    internal sealed class MotionManager : MotionQueueManager
    {
        public int CurrentPriority { get; private set; }
        public int ReservePriority { get; set; }

        public MotionQueueEntry StartMotionPriority(ACubismMotion motion, int priority)
        {
            if (priority == ReservePriority)
                ReservePriority = 0; // reservation consumed
            CurrentPriority = priority;
            return StartMotion(motion);
        }

        public bool UpdateMotion(CubismModel model, float deltaTimeSeconds)
        {
            UserTimeSeconds += deltaTimeSeconds;
            bool updated = DoUpdateMotion(model, UserTimeSeconds);
            if (IsFinished())
                CurrentPriority = 0;
            return updated;
        }

        public bool ReserveMotion(int priority)
        {
            if (priority <= ReservePriority || priority <= CurrentPriority)
                return false;
            ReservePriority = priority;
            return true;
        }
    }

    // CubismMotion (motion3.json)
    internal sealed class CubismMotion : ACubismMotion
    {
        enum CurveTarget { Model = 0, Parameter = 1, PartOpacity = 2 }
        enum SegmentType { Linear = 0, Bezier = 1, Stepped = 2, InverseStepped = 3 }

        struct Point { public float Time, Value; }

        struct Segment
        {
            public int BasePointIndex;
            public SegmentType Type;
            public bool UseCardano; // bezier evaluation: true = Cardano, false = restricted (De Casteljau)
        }

        sealed class Curve
        {
            public CurveTarget Type;
            public string Id;
            public int BaseSegmentIndex;
            public int SegmentCount;
            public float FadeInTime = -1f;
            public float FadeOutTime = -1f;
        }

        // C++ UseOldBeziersCurveMotion
        const bool UseOldBeziersCurveMotion = false;
        const float FltMax = float.MaxValue;
        const int MaxTargetSize = 64;

        // Cubism 4.x style (V2) loop behaviour is the framework default.
        public bool Loop { get; private set; }   // Meta.Loop as stored in the file (not applied, see CubismAvatar notes)
        public float Duration;
        public float Fps = 30f;
        float _modelOpacity = 1f;

        readonly List<Curve> _curves = new List<Curve>();
        readonly List<Segment> _segments = new List<Segment>();
        readonly List<Point> _points = new List<Point>();
        Point[] _pointArray;

        List<string> _eyeBlinkParameterIds = new List<string>();
        List<string> _lipSyncParameterIds = new List<string>();

        // called when a model "Opacity" curve is evaluated
        public Action<float> OnModelOpacity;

        public float ModelOpacity { get { return _modelOpacity; } }

        public static CubismMotion Load(string path)
        {
            return Parse(Json.ParseFile(path));
        }

        public static CubismMotion Parse(object root)
        {
            object meta = Json.Get(root, "Meta");
            var m = new CubismMotion();
            m.Duration = Json.GetFloat(meta, "Duration");
            m.Loop = Json.GetBool(meta, "Loop");
            m.Fps = Json.GetFloat(meta, "Fps", 30f);
            if (m.Fps <= 0f) m.Fps = 30f;
            bool areBeziersRestricted = Json.GetBool(meta, "AreBeziersRestricted");

            if (!Json.IsNull(meta, "FadeInTime"))
            {
                float f = Json.GetFloat(meta, "FadeInTime");
                m.FadeInSeconds = f < 0.0f ? 1.0f : f;
            }
            else m.FadeInSeconds = 1.0f;

            if (!Json.IsNull(meta, "FadeOutTime"))
            {
                float f = Json.GetFloat(meta, "FadeOutTime");
                m.FadeOutSeconds = f < 0.0f ? 1.0f : f;
            }
            else m.FadeOutSeconds = 1.0f;

            var curves = Json.GetList(root, "Curves") ?? new List<object>();
            for (int ci = 0; ci < curves.Count; ci++)
            {
                object cj = curves[ci];
                var curve = new Curve();
                string target = Json.GetString(cj, "Target");
                if (target == "Model") curve.Type = CurveTarget.Model;
                else if (target == "Parameter") curve.Type = CurveTarget.Parameter;
                else if (target == "PartOpacity") curve.Type = CurveTarget.PartOpacity;
                else throw new InvalidDataException("motion3.json: unknown curve target '" + target + "'");

                curve.Id = Json.GetString(cj, "Id");
                curve.BaseSegmentIndex = m._segments.Count;
                curve.FadeInTime = Json.IsNull(cj, "FadeInTime") ? -1f : Json.GetFloat(cj, "FadeInTime");
                curve.FadeOutTime = Json.IsNull(cj, "FadeOutTime") ? -1f : Json.GetFloat(cj, "FadeOutTime");

                var raw = Json.GetList(cj, "Segments") ?? new List<object>();
                var seg = new float[raw.Count];
                for (int k = 0; k < seg.Length; k++) seg[k] = Json.ToFloat(raw[k]);

                int pos = 0;
                while (pos < seg.Length)
                {
                    var s = new Segment();
                    if (pos == 0)
                    {
                        if (seg.Length < 2) break;
                        s.BasePointIndex = m._points.Count;
                        m._points.Add(new Point { Time = seg[0], Value = seg[1] });
                        pos += 2;
                    }
                    else
                    {
                        s.BasePointIndex = m._points.Count - 1;
                    }

                    if (pos >= seg.Length) break;
                    int type = (int)seg[pos];
                    switch (type)
                    {
                        case 0: // linear
                        case 2: // stepped
                        case 3: // inverse stepped
                            if (pos + 2 >= seg.Length) throw new InvalidDataException("motion3.json: truncated segment");
                            s.Type = (SegmentType)type;
                            m._points.Add(new Point { Time = seg[pos + 1], Value = seg[pos + 2] });
                            pos += 3;
                            break;
                        case 1: // bezier
                            if (pos + 6 >= seg.Length) throw new InvalidDataException("motion3.json: truncated bezier segment");
                            s.Type = SegmentType.Bezier;
                            s.UseCardano = !(areBeziersRestricted || UseOldBeziersCurveMotion);
                            m._points.Add(new Point { Time = seg[pos + 1], Value = seg[pos + 2] });
                            m._points.Add(new Point { Time = seg[pos + 3], Value = seg[pos + 4] });
                            m._points.Add(new Point { Time = seg[pos + 5], Value = seg[pos + 6] });
                            pos += 7;
                            break;
                        default:
                            throw new InvalidDataException("motion3.json: unknown segment type " + type);
                    }
                    m._segments.Add(s);
                    curve.SegmentCount++;
                }
                m._curves.Add(curve);
            }
            m._pointArray = m._points.ToArray();
            return m;
        }

        public override float GetDuration() { return IsLoop ? -1.0f : Duration; }
        public override float GetLoopDuration() { return Duration; }

        public void SetEffectIds(List<string> eyeBlinkParameterIds, List<string> lipSyncParameterIds)
        {
            _eyeBlinkParameterIds = eyeBlinkParameterIds ?? new List<string>();
            _lipSyncParameterIds = lipSyncParameterIds ?? new List<string>();
        }

        // ---- curve evaluation ------------------------------------------------------

        static Point Lerp(Point a, Point b, float t)
        {
            Point r;
            r.Time = a.Time + ((b.Time - a.Time) * t);
            r.Value = a.Value + ((b.Value - a.Value) * t);
            return r;
        }

        static float LinearEvaluate(Point[] p, int b, float time)
        {
            float t = (time - p[b].Time) / (p[b + 1].Time - p[b].Time);
            if (t < 0.0f) t = 0.0f;
            return p[b].Value + ((p[b + 1].Value - p[b].Value) * t);
        }

        static float BezierEvaluate(Point[] p, int b, float time)
        {
            float t = (time - p[b].Time) / (p[b + 3].Time - p[b].Time);
            if (t < 0.0f) t = 0.0f;
            return BezierAt(p, b, t);
        }

        static float BezierAt(Point[] p, int b, float t)
        {
            Point p01 = Lerp(p[b], p[b + 1], t);
            Point p12 = Lerp(p[b + 1], p[b + 2], t);
            Point p23 = Lerp(p[b + 2], p[b + 3], t);
            Point p012 = Lerp(p01, p12, t);
            Point p123 = Lerp(p12, p23, t);
            return Lerp(p012, p123, t).Value;
        }

        static float BezierEvaluateCardano(Point[] p, int b, float time)
        {
            float x = time;
            float x1 = p[b].Time;
            float x2 = p[b + 3].Time;
            float cx1 = p[b + 1].Time;
            float cx2 = p[b + 2].Time;

            float a = x2 - 3.0f * cx2 + 3.0f * cx1 - x1;
            float bb = 3.0f * cx2 - 6.0f * cx1 + 3.0f * x1;
            float c = 3.0f * cx1 - 3.0f * x1;
            float d = x1 - x;

            float t = CMath.CardanoAlgorithmForBezier(a, bb, c, d);
            return BezierAt(p, b, t);
        }

        float EvaluateSegment(int segmentIndex, int basePointIndex, float time)
        {
            Segment s = _segments[segmentIndex];
            switch (s.Type)
            {
                case SegmentType.Linear: return LinearEvaluate(_pointArray, basePointIndex, time);
                case SegmentType.Bezier:
                    return s.UseCardano
                        ? BezierEvaluateCardano(_pointArray, basePointIndex, time)
                        : BezierEvaluate(_pointArray, basePointIndex, time);
                case SegmentType.Stepped: return _pointArray[basePointIndex].Value;
                default: return _pointArray[basePointIndex + 1].Value; // inverse stepped
            }
        }

        float CorrectEndPoint(int segmentIndex, int beginIndex, int endIndex, float time, float endTime)
        {
            var pts = new Point[2];
            pts[0] = _pointArray[endIndex];
            pts[1] = _pointArray[beginIndex];
            pts[1].Time = endTime;

            switch (_segments[segmentIndex].Type)
            {
                case SegmentType.Stepped: return pts[0].Value;
                case SegmentType.InverseStepped: return pts[1].Value;
                default: return LinearEvaluate(pts, 0, time);
            }
        }

        float EvaluateCurve(int index, float time, bool isCorrection, float endTime)
        {
            Curve curve = _curves[index];

            int target = -1;
            int totalSegmentCount = curve.BaseSegmentIndex + curve.SegmentCount;
            int pointPosition = 0;
            for (int i = curve.BaseSegmentIndex; i < totalSegmentCount; ++i)
            {
                // first point of the next segment
                pointPosition = _segments[i].BasePointIndex + (_segments[i].Type == SegmentType.Bezier ? 3 : 1);

                if (_pointArray[pointPosition].Time > time)
                {
                    target = i;
                    break;
                }
            }

            if (target == -1)
            {
                if (isCorrection && time < endTime)
                {
                    // blend from the last point back to the first point of the loop
                    return CorrectEndPoint(totalSegmentCount - 1, _segments[curve.BaseSegmentIndex].BasePointIndex,
                        pointPosition, time, endTime);
                }
                return _pointArray[pointPosition].Value;
            }

            return EvaluateSegment(target, _segments[target].BasePointIndex, time);
        }

        // ---- update ----------------------------------------------------------------

        public override void DoUpdateParameters(CubismModel model, float userTimeSeconds, float fadeWeight, MotionQueueEntry entry)
        {
            // Cubism SDK 5 "MotionBehavior_V2"
            if (PreviousLoopState != IsLoop)
            {
                AdjustEndTime(entry);
                PreviousLoopState = IsLoop;
            }

            float timeOffsetSeconds = userTimeSeconds - entry.StartTime;
            if (timeOffsetSeconds < 0.0f) timeOffsetSeconds = 0.0f;

            float lipSyncValue = FltMax;
            float eyeBlinkValue = FltMax;

            ulong lipSyncFlags = 0UL;
            ulong eyeBlinkFlags = 0UL;

            float tmpFadeIn = (FadeInSeconds <= 0.0f)
                ? 1.0f
                : CMath.GetEasingSine((userTimeSeconds - entry.FadeInStartTime) / FadeInSeconds);

            float tmpFadeOut = (FadeOutSeconds <= 0.0f || entry.EndTime < 0.0f)
                ? 1.0f
                : CMath.GetEasingSine((entry.EndTime - userTimeSeconds) / FadeOutSeconds);

            float value;

            // 'Repeat' time as necessary.
            float time = timeOffsetSeconds;
            float duration = Duration;
            bool isCorrection = IsLoop;

            if (IsLoop)
            {
                duration += 1.0f / Fps;
                while (time > duration) time -= duration;
            }

            // model curves
            for (int c = 0; c < _curves.Count; c++)
            {
                if (_curves[c].Type != CurveTarget.Model) continue;
                value = EvaluateCurve(c, time, isCorrection, duration);

                string id = _curves[c].Id;
                if (id == "EyeBlink") eyeBlinkValue = value;
                else if (id == "LipSync") lipSyncValue = value;
                else if (id == "Opacity")
                {
                    _modelOpacity = value;
                    if (OnModelOpacity != null) OnModelOpacity(_modelOpacity);
                }
            }

            // parameter curves
            for (int c = 0; c < _curves.Count; c++)
            {
                Curve curve = _curves[c];
                if (curve.Type != CurveTarget.Parameter) continue;

                int parameterIndex = model.GetParameterIndex(curve.Id);
                if (parameterIndex == -1) continue;

                float sourceValue = model.GetParameterValue(parameterIndex);

                value = EvaluateCurve(c, time, isCorrection, duration);

                if (eyeBlinkValue != FltMax)
                {
                    for (int i = 0; i < _eyeBlinkParameterIds.Count && i < MaxTargetSize; ++i)
                    {
                        if (_eyeBlinkParameterIds[i] == curve.Id)
                        {
                            value *= eyeBlinkValue;
                            eyeBlinkFlags |= 1UL << i;
                            break;
                        }
                    }
                }

                if (lipSyncValue != FltMax)
                {
                    for (int i = 0; i < _lipSyncParameterIds.Count && i < MaxTargetSize; ++i)
                    {
                        if (_lipSyncParameterIds[i] == curve.Id)
                        {
                            value += lipSyncValue;
                            lipSyncFlags |= 1UL << i;
                            break;
                        }
                    }
                }

                // parameter repeat is off by default in the SDK (SDK-side override), so values are clamped by SetParameterValue

                float v;
                if (curve.FadeInTime < 0.0f && curve.FadeOutTime < 0.0f)
                {
                    v = sourceValue + (value - sourceValue) * fadeWeight;
                }
                else
                {
                    float fin;
                    float fout;

                    if (curve.FadeInTime < 0.0f) fin = tmpFadeIn;
                    else
                        fin = curve.FadeInTime == 0.0f
                            ? 1.0f
                            : CMath.GetEasingSine((userTimeSeconds - entry.FadeInStartTime) / curve.FadeInTime);

                    if (curve.FadeOutTime < 0.0f) fout = tmpFadeOut;
                    else
                        fout = (curve.FadeOutTime == 0.0f || entry.EndTime < 0.0f)
                            ? 1.0f
                            : CMath.GetEasingSine((entry.EndTime - userTimeSeconds) / curve.FadeOutTime);

                    float paramWeight = Weight * fin * fout;
                    v = sourceValue + (value - sourceValue) * paramWeight;
                }

                model.SetParameterValue(parameterIndex, v);
            }

            // eye blink / lip sync parameters not overridden by the motion's own curves
            if (eyeBlinkValue != FltMax)
            {
                for (int i = 0; i < _eyeBlinkParameterIds.Count && i < MaxTargetSize; ++i)
                {
                    int pi = model.GetParameterIndex(_eyeBlinkParameterIds[i]);
                    if (pi < 0) continue;
                    float sourceValue = model.GetParameterValue(pi);
                    if (((eyeBlinkFlags >> i) & 0x01UL) != 0UL) continue;
                    model.SetParameterValue(pi, sourceValue + (eyeBlinkValue - sourceValue) * fadeWeight);
                }
            }

            if (lipSyncValue != FltMax)
            {
                for (int i = 0; i < _lipSyncParameterIds.Count && i < MaxTargetSize; ++i)
                {
                    int pi = model.GetParameterIndex(_lipSyncParameterIds[i]);
                    if (pi < 0) continue;
                    float sourceValue = model.GetParameterValue(pi);
                    if (((lipSyncFlags >> i) & 0x01UL) != 0UL) continue;
                    model.SetParameterValue(pi, sourceValue + (lipSyncValue - sourceValue) * fadeWeight);
                }
            }

            // part opacity curves (the SDK writes them through the parameter of the same id)
            for (int c = 0; c < _curves.Count; c++)
            {
                if (_curves[c].Type != CurveTarget.PartOpacity) continue;
                int parameterIndex = model.GetParameterIndex(_curves[c].Id);
                if (parameterIndex == -1) continue;
                value = EvaluateCurve(c, time, isCorrection, duration);
                model.SetParameterValue(parameterIndex, value);
            }

            if (timeOffsetSeconds >= duration)
            {
                if (IsLoop)
                {
                    entry.StartTime = userTimeSeconds - time; // back to the first frame
                    if (IsLoopFadeIn)
                        entry.FadeInStartTime = userTimeSeconds - time;
                }
                else
                {
                    entry.Finished = true;
                }
            }
        }
    }
}
