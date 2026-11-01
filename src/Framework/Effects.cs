using System;
using System.Collections.Generic;
using PartnerLive2D.Core;

namespace PartnerLive2D.Framework
{
    // CubismEyeBlink
    internal sealed class CubismEyeBlink
    {
        enum EyeState { First = 0, Interval, Closing, Closed, Opening }

        // the eye parameter is 0 when closed
        const bool CloseIfZero = true;

        EyeState _blinkingState = EyeState.First;
        float _nextBlinkingTime;
        float _stateStartTimeSeconds;
        float _blinkingIntervalSeconds = 4.0f;
        float _closingSeconds = 0.1f;
        float _closedSeconds = 0.05f;
        float _openingSeconds = 0.15f;
        float _userTimeSeconds;
        readonly List<string> _parameterIds;
        readonly Random _random;

        public CubismEyeBlink(List<string> parameterIds, Random random)
        {
            _parameterIds = parameterIds;
            _random = random;
        }

        float DetermineNextBlinkingTiming()
        {
            float r = (float)_random.NextDouble();
            return _userTimeSeconds + (r * (2.0f * _blinkingIntervalSeconds - 1.0f));
        }

        public void UpdateParameters(CubismModel model, float deltaTimeSeconds)
        {
            _userTimeSeconds += deltaTimeSeconds;
            float parameterValue;
            float t;

            switch (_blinkingState)
            {
                case EyeState.Closing:
                    t = (_userTimeSeconds - _stateStartTimeSeconds) / _closingSeconds;
                    if (t >= 1.0f)
                    {
                        t = 1.0f;
                        _blinkingState = EyeState.Closed;
                        _stateStartTimeSeconds = _userTimeSeconds;
                    }
                    parameterValue = 1.0f - t;
                    break;
                case EyeState.Closed:
                    t = (_userTimeSeconds - _stateStartTimeSeconds) / _closedSeconds;
                    if (t >= 1.0f)
                    {
                        _blinkingState = EyeState.Opening;
                        _stateStartTimeSeconds = _userTimeSeconds;
                    }
                    parameterValue = 0.0f;
                    break;
                case EyeState.Opening:
                    t = (_userTimeSeconds - _stateStartTimeSeconds) / _openingSeconds;
                    if (t >= 1.0f)
                    {
                        t = 1.0f;
                        _blinkingState = EyeState.Interval;
                        _nextBlinkingTime = DetermineNextBlinkingTiming();
                    }
                    parameterValue = t;
                    break;
                case EyeState.Interval:
                    if (_nextBlinkingTime < _userTimeSeconds)
                    {
                        _blinkingState = EyeState.Closing;
                        _stateStartTimeSeconds = _userTimeSeconds;
                    }
                    parameterValue = 1.0f;
                    break;
                case EyeState.First:
                default:
                    _blinkingState = EyeState.Interval;
                    _nextBlinkingTime = DetermineNextBlinkingTiming();
                    parameterValue = 1.0f;
                    break;
            }

#pragma warning disable 162 // CloseIfZero is a compile-time switch kept for parity with the SDK
            if (!CloseIfZero) parameterValue = -parameterValue;
#pragma warning restore 162

            for (int i = 0; i < _parameterIds.Count; ++i)
                model.SetParameterValue(model.GetParameterIndex(_parameterIds[i]), parameterValue);
        }
    }

    // CubismBreath
    internal sealed class CubismBreath
    {
        public struct BreathParameterData
        {
            public string ParameterId;
            public float Offset, Peak, Cycle, Weight;
            public BreathParameterData(string id, float offset, float peak, float cycle, float weight)
            {
                ParameterId = id; Offset = offset; Peak = peak; Cycle = cycle; Weight = weight;
            }
        }

        readonly List<BreathParameterData> _parameters = new List<BreathParameterData>();
        float _currentTime;

        public void SetParameters(IEnumerable<BreathParameterData> parameters)
        {
            _parameters.Clear();
            _parameters.AddRange(parameters);
        }

        public void UpdateParameters(CubismModel model, float deltaTimeSeconds)
        {
            _currentTime += deltaTimeSeconds;
            float t = _currentTime * 2.0f * CMath.Pi;

            for (int i = 0; i < _parameters.Count; ++i)
            {
                BreathParameterData d = _parameters[i];
                model.AddParameterValue(model.GetParameterIndex(d.ParameterId),
                    d.Offset + (d.Peak * CMath.Sin(t / d.Cycle)), d.Weight);
            }
        }
    }

    // CubismLook (drag -> parameter factors)
    internal sealed class CubismLook
    {
        public struct LookParameterData
        {
            public string ParameterId;
            public float FactorX, FactorY, FactorXY;
            public LookParameterData(string id, float fx, float fy, float fxy)
            {
                ParameterId = id; FactorX = fx; FactorY = fy; FactorXY = fxy;
            }
        }

        readonly List<LookParameterData> _parameters = new List<LookParameterData>();

        public void SetParameters(IEnumerable<LookParameterData> parameters)
        {
            _parameters.Clear();
            _parameters.AddRange(parameters);
        }

        public void UpdateParameters(CubismModel model, float dragX, float dragY)
        {
            float dragXY = dragX * dragY;
            for (int i = 0; i < _parameters.Count; ++i)
            {
                LookParameterData d = _parameters[i];
                model.AddParameterValue(model.GetParameterIndex(d.ParameterId),
                    d.FactorX * dragX + d.FactorY * dragY + d.FactorXY * dragXY);
            }
        }
    }

    // CubismTargetPoint (smoothed face direction)
    internal sealed class CubismTargetPoint
    {
        const int FrameRate = 30;
        const float Epsilon = 0.01f;

        float _faceTargetX, _faceTargetY;
        float _faceX, _faceY;
        float _faceVX, _faceVY;
        float _lastTimeSeconds;
        float _userTimeSeconds;

        public float X { get { return _faceX; } }
        public float Y { get { return _faceY; } }

        public void Set(float x, float y) { _faceTargetX = x; _faceTargetY = y; }

        public void Update(float deltaTimeSeconds)
        {
            _userTimeSeconds += deltaTimeSeconds;

            const float FaceParamMaxV = 40.0f / 10.0f;
            const float MaxV = FaceParamMaxV * 1.0f / FrameRate;

            if (_lastTimeSeconds == 0.0f)
            {
                _lastTimeSeconds = _userTimeSeconds;
                return;
            }

            float deltaTimeWeight = (_userTimeSeconds - _lastTimeSeconds) * FrameRate;
            _lastTimeSeconds = _userTimeSeconds;

            const float TimeToMaxSpeed = 0.15f;
            const float FrameToMaxSpeed = TimeToMaxSpeed * FrameRate;
            float MaxA = deltaTimeWeight * MaxV / FrameToMaxSpeed;

            float dx = _faceTargetX - _faceX;
            float dy = _faceTargetY - _faceY;

            if (CMath.Abs(dx) <= Epsilon && CMath.Abs(dy) <= Epsilon) return;

            float d = CMath.Sqrt((dx * dx) + (dy * dy));

            float vx = MaxV * dx / d;
            float vy = MaxV * dy / d;

            float ax = vx - _faceVX;
            float ay = vy - _faceVY;

            float a = CMath.Sqrt((ax * ax) + (ay * ay));

            if (a < -MaxA || a > MaxA)
            {
                ax *= MaxA / a;
                ay *= MaxA / a;
            }

            _faceVX += ax;
            _faceVY += ay;

            {
                float maxV = 0.5f * (CMath.Sqrt((MaxA * MaxA) + 16.0f * MaxA * d - 8.0f * MaxA * d) - MaxA);
                float curV = CMath.Sqrt((_faceVX * _faceVX) + (_faceVY * _faceVY));

                if (curV > maxV)
                {
                    _faceVX *= maxV / curV;
                    _faceVY *= maxV / curV;
                }
            }

            _faceX += _faceVX;
            _faceY += _faceVY;
        }
    }

    // CubismPose (pose3.json)
    internal sealed class CubismPose
    {
        const float Epsilon = 0.001f;
        const float DefaultFadeInSeconds = 0.5f;

        sealed class PartData
        {
            public string PartId;
            public int ParameterIndex;
            public int PartIndex;
            // C++ would create a "not exist" parameter for ids the model lacks; its value is kept here
            public float MissingParameterValue;
            public List<PartData> Link = new List<PartData>();

            public void Initialize(CubismModel model)
            {
                ParameterIndex = model.GetParameterIndex(PartId);
                PartIndex = model.GetPartIndex(PartId);
                SetParam(model, 1f);
            }

            public float GetParam(CubismModel model)
            {
                return ParameterIndex >= 0 ? model.GetParameterValue(ParameterIndex) : MissingParameterValue;
            }

            public void SetParam(CubismModel model, float v)
            {
                if (ParameterIndex >= 0) model.SetParameterValue(ParameterIndex, v);
                else MissingParameterValue = v;
            }
        }

        readonly List<PartData> _partGroups = new List<PartData>();
        readonly List<int> _partGroupCounts = new List<int>();
        float _fadeTimeSeconds = DefaultFadeInSeconds;
        CubismModel _lastModel;

        public static CubismPose Parse(object root)
        {
            var pose = new CubismPose();

            if (!Json.IsNull(root, "FadeInTime"))
            {
                pose._fadeTimeSeconds = Json.GetFloat(root, "FadeInTime", DefaultFadeInSeconds);
                if (pose._fadeTimeSeconds < 0.0f) pose._fadeTimeSeconds = DefaultFadeInSeconds;
            }

            var groups = Json.GetList(root, "Groups");
            if (groups != null)
            {
                foreach (object g in groups)
                {
                    var idList = g as List<object>;
                    if (idList == null) continue;
                    int groupCount = 0;
                    foreach (object partInfo in idList)
                    {
                        var pd = new PartData();
                        pd.PartId = Json.GetString(partInfo, "Id");
                        var links = Json.GetList(partInfo, "Link");
                        if (links != null)
                        {
                            foreach (object l in links)
                            {
                                var lp = new PartData();
                                lp.PartId = l as string;
                                pd.Link.Add(lp);
                            }
                        }
                        pose._partGroups.Add(pd);
                        ++groupCount;
                    }
                    pose._partGroupCounts.Add(groupCount);
                }
            }
            return pose;
        }

        void Reset(CubismModel model)
        {
            int beginIndex = 0;
            for (int i = 0; i < _partGroupCounts.Count; ++i)
            {
                int groupCount = _partGroupCounts[i];
                for (int j = beginIndex; j < beginIndex + groupCount; ++j)
                {
                    _partGroups[j].Initialize(model);

                    int partsIndex = _partGroups[j].PartIndex;
                    if (partsIndex < 0) continue;

                    model.SetPartOpacity(partsIndex, (j == beginIndex ? 1.0f : 0.0f));
                    _partGroups[j].SetParam(model, (j == beginIndex ? 1.0f : 0.0f));

                    for (int k = 0; k < _partGroups[j].Link.Count; ++k)
                        _partGroups[j].Link[k].Initialize(model);
                }
                beginIndex += groupCount;
            }
        }

        void CopyPartOpacities(CubismModel model)
        {
            for (int groupIndex = 0; groupIndex < _partGroups.Count; ++groupIndex)
            {
                PartData partData = _partGroups[groupIndex];
                if (partData.Link.Count == 0) continue;
                if (partData.PartIndex < 0) continue;

                float opacity = model.GetPartOpacity(partData.PartIndex);

                for (int linkIndex = 0; linkIndex < partData.Link.Count; ++linkIndex)
                {
                    PartData linkPart = partData.Link[linkIndex];
                    if (linkPart.PartIndex < 0) continue;
                    model.SetPartOpacity(linkPart.PartIndex, opacity);
                }
            }
        }

        void DoFade(CubismModel model, float deltaTimeSeconds, int beginIndex, int partGroupCount)
        {
            int visiblePartIndex = -1;
            float newOpacity = 1.0f;

            const float Phi = 0.5f;
            const float BackOpacityThreshold = 0.15f;

            // find the part that is currently visible
            for (int i = beginIndex; i < beginIndex + partGroupCount; ++i)
            {
                int partIndex = _partGroups[i].PartIndex;
                if (partIndex < 0) continue;

                if (_partGroups[i].GetParam(model) > Epsilon)
                {
                    if (visiblePartIndex >= 0) break;

                    visiblePartIndex = i;

                    if (_fadeTimeSeconds == 0.0f)
                    {
                        newOpacity = 1.0f;
                        continue;
                    }

                    newOpacity = model.GetPartOpacity(partIndex);
                    newOpacity += (deltaTimeSeconds / _fadeTimeSeconds);
                    if (newOpacity > 1.0f) newOpacity = 1.0f;
                }
            }

            if (visiblePartIndex < 0)
            {
                visiblePartIndex = 0;
                newOpacity = 1.0f;
            }

            for (int i = beginIndex; i < beginIndex + partGroupCount; ++i)
            {
                int partsIndex = _partGroups[i].PartIndex;
                if (partsIndex < 0) continue;

                if (visiblePartIndex == i)
                {
                    model.SetPartOpacity(partsIndex, newOpacity);
                }
                else
                {
                    float opacity = model.GetPartOpacity(partsIndex);
                    float a1;

                    if (newOpacity < Phi)
                        a1 = newOpacity * (Phi - 1) / Phi + 1.0f;       // line through (0,1),(phi,phi)
                    else
                        a1 = (1 - newOpacity) * Phi / (1.0f - Phi);     // line through (1,0),(phi,phi)

                    // limit how much of the background shows through
                    float backOpacity = (1.0f - a1) * (1.0f - newOpacity);
                    if (backOpacity > BackOpacityThreshold)
                        a1 = 1.0f - BackOpacityThreshold / (1.0f - newOpacity);

                    if (opacity > a1) opacity = a1;

                    model.SetPartOpacity(partsIndex, opacity);
                }
            }
        }

        public void UpdateParameters(CubismModel model, float deltaTimeSeconds)
        {
            if (model != _lastModel) Reset(model);
            _lastModel = model;

            if (deltaTimeSeconds < 0.0f) deltaTimeSeconds = 0.0f;

            int beginIndex = 0;
            for (int i = 0; i < _partGroupCounts.Count; i++)
            {
                int partGroupCount = _partGroupCounts[i];
                DoFade(model, deltaTimeSeconds, beginIndex, partGroupCount);
                beginIndex += partGroupCount;
            }

            CopyPartOpacities(model);
        }
    }
}
