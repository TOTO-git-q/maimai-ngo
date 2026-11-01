using System;
using System.Collections.Generic;
using System.IO;
using PartnerLive2D.Core;

namespace PartnerLive2D.Framework
{
    // CubismPhysics (physics3.json), Cubism SDK 5 evaluation with fixed-step simulation and output interpolation.
    internal sealed class CubismPhysics
    {
        // physics constants
        const float AirResistance = 5.0f;
        const float MaximumWeight = 100.0f;
        const float MovementThreshold = 0.001f;
        const float MaxDeltaTime = 5.0f;

        enum Source { X = 0, Y = 1, Angle = 2 }

        struct Normalization { public float Minimum, Maximum, Default; }

        sealed class Input
        {
            public string SourceId;
            public int SourceParameterIndex = -1;
            public float Weight;
            public bool Reflect;
            public Source Type;
        }

        sealed class Output
        {
            public string DestinationId;
            public int DestinationParameterIndex = -1;
            public int VertexIndex;
            public Vec2 TranslationScale;
            public float AngleScale;
            public float Weight;
            public Source Type;
            public bool Reflect;
            public float ValueBelowMinimum;
            public float ValueExceededMaximum;
        }

        sealed class Particle
        {
            public Vec2 InitialPosition;
            public float Mobility, Delay, Acceleration, Radius;
            public Vec2 Position, LastPosition, LastGravity, Force, Velocity;
        }

        sealed class SubRig
        {
            public int InputCount, OutputCount, ParticleCount;
            public int BaseInputIndex, BaseOutputIndex, BaseParticleIndex;
            public Normalization NormalizationPosition, NormalizationAngle;
        }

        // options (gravity / wind), defaults like the C++ constructor
        public Vec2 Gravity = new Vec2(0f, -1f);
        public Vec2 Wind = new Vec2(0f, 0f);

        readonly List<SubRig> _settings = new List<SubRig>();
        readonly List<Input> _inputs = new List<Input>();
        readonly List<Output> _outputs = new List<Output>();
        readonly List<Particle> _particles = new List<Particle>();
        float _fps;

        float _currentRemainTime;
        float[] _parameterCaches = new float[0];
        float[] _parameterInputCaches = new float[0];
        float[][] _currentRigOutputs;
        float[][] _previousRigOutputs;

        public static CubismPhysics Parse(object root)
        {
            var p = new CubismPhysics();

            object meta = Json.Get(root, "Meta");
            p._fps = Json.GetFloat(meta, "Fps", 0.0f);

            var settings = Json.GetList(root, "PhysicsSettings");
            if (settings == null) throw new InvalidDataException("physics3.json has no PhysicsSettings");

            p._currentRigOutputs = new float[settings.Count][];
            p._previousRigOutputs = new float[settings.Count][];

            for (int i = 0; i < settings.Count; i++)
            {
                object sj = settings[i];
                var rig = new SubRig();

                object normPos = Json.Path(sj, "Normalization", "Position");
                object normAng = Json.Path(sj, "Normalization", "Angle");
                rig.NormalizationPosition.Minimum = Json.GetFloat(normPos, "Minimum");
                rig.NormalizationPosition.Maximum = Json.GetFloat(normPos, "Maximum");
                rig.NormalizationPosition.Default = Json.GetFloat(normPos, "Default");
                rig.NormalizationAngle.Minimum = Json.GetFloat(normAng, "Minimum");
                rig.NormalizationAngle.Maximum = Json.GetFloat(normAng, "Maximum");
                rig.NormalizationAngle.Default = Json.GetFloat(normAng, "Default");

                var ins = Json.GetList(sj, "Input") ?? new List<object>();
                rig.InputCount = ins.Count;
                rig.BaseInputIndex = p._inputs.Count;
                foreach (object ij in ins)
                {
                    var inp = new Input();
                    inp.Weight = Json.GetFloat(ij, "Weight");
                    inp.Reflect = Json.GetBool(ij, "Reflect");
                    inp.Type = ParseSource(Json.GetString(ij, "Type"));
                    inp.SourceId = Json.GetString(Json.Get(ij, "Source"), "Id");
                    p._inputs.Add(inp);
                }

                var outs = Json.GetList(sj, "Output") ?? new List<object>();
                rig.OutputCount = outs.Count;
                rig.BaseOutputIndex = p._outputs.Count;
                p._currentRigOutputs[i] = new float[outs.Count];
                p._previousRigOutputs[i] = new float[outs.Count];
                foreach (object oj in outs)
                {
                    var o = new Output();
                    o.VertexIndex = Json.GetInt(oj, "VertexIndex");
                    o.AngleScale = Json.GetFloat(oj, "Scale");
                    // The C++ port never fills TranslationScale (it stays zero, so X/Y outputs would be silent).
                    // Use the same "Scale" value so that X/Y outputs work as authored.
                    o.TranslationScale = new Vec2(o.AngleScale, o.AngleScale);
                    o.Weight = Json.GetFloat(oj, "Weight");
                    o.DestinationId = Json.GetString(Json.Get(oj, "Destination"), "Id");
                    o.Type = ParseSource(Json.GetString(oj, "Type"));
                    o.Reflect = Json.GetBool(oj, "Reflect");
                    p._outputs.Add(o);
                }

                var verts = Json.GetList(sj, "Vertices") ?? new List<object>();
                rig.ParticleCount = verts.Count;
                rig.BaseParticleIndex = p._particles.Count;
                foreach (object vj in verts)
                {
                    var pt = new Particle();
                    pt.Mobility = Json.GetFloat(vj, "Mobility");
                    pt.Delay = Json.GetFloat(vj, "Delay");
                    pt.Acceleration = Json.GetFloat(vj, "Acceleration");
                    pt.Radius = Json.GetFloat(vj, "Radius");
                    object pos = Json.Get(vj, "Position");
                    pt.Position = new Vec2(Json.GetFloat(pos, "X"), Json.GetFloat(pos, "Y"));
                    p._particles.Add(pt);
                }

                p._settings.Add(rig);
            }

            p.Initialize();
            return p;
        }

        static Source ParseSource(string s)
        {
            if (s == "Y") return Source.Y;
            if (s == "Angle") return Source.Angle;
            return Source.X;
        }

        // ---- helpers (anonymous namespace functions of the C++ file) ----------------

        static float GetRangeValue(float min, float max)
        {
            float maxValue = CMath.Max(min, max);
            float minValue = CMath.Min(min, max);
            return CMath.Abs(maxValue - minValue);
        }

        static int Sign(float value)
        {
            if (value > 0.0f) return 1;
            if (value < 0.0f) return -1;
            return 0;
        }

        static float GetDefaultValue(float min, float max)
        {
            float minValue = CMath.Min(min, max);
            return minValue + (GetRangeValue(min, max) / 2.0f);
        }

        static float NormalizeParameterValue(float value, float parameterMinimum, float parameterMaximum,
            float parameterDefault, float normalizedMinimum, float normalizedMaximum, float normalizedDefault,
            bool isInverted)
        {
            float result = 0.0f;

            float maxValue = CMath.Max(parameterMaximum, parameterMinimum);
            if (maxValue < value) value = maxValue;

            float minValue = CMath.Min(parameterMaximum, parameterMinimum);
            if (minValue > value) value = minValue;

            float minNormValue = CMath.Min(normalizedMinimum, normalizedMaximum);
            float maxNormValue = CMath.Max(normalizedMinimum, normalizedMaximum);
            float middleNormValue = normalizedDefault;

            float middleValue = GetDefaultValue(minValue, maxValue);
            float paramValue = value - middleValue;

            switch (Sign(paramValue))
            {
                case 1:
                    {
                        float nLength = maxNormValue - middleNormValue;
                        float pLength = maxValue - middleValue;
                        if (pLength != 0.0f)
                        {
                            result = paramValue * (nLength / pLength);
                            result += middleNormValue;
                        }
                        break;
                    }
                case -1:
                    {
                        float nLength = minNormValue - middleNormValue;
                        float pLength = minValue - middleValue;
                        if (pLength != 0.0f)
                        {
                            result = paramValue * (nLength / pLength);
                            result += middleNormValue;
                        }
                        break;
                    }
                case 0:
                    result = middleNormValue;
                    break;
            }

            return isInverted ? result : (result * -1.0f);
        }

        static void ApplyInput(Input input, SubRig setting, float value, float pMin, float pMax, float pDef,
            ref Vec2 totalTranslation, ref float totalAngle, float weight)
        {
            switch (input.Type)
            {
                case Source.X:
                    totalTranslation.X += NormalizeParameterValue(value, pMin, pMax, pDef,
                        setting.NormalizationPosition.Minimum, setting.NormalizationPosition.Maximum,
                        setting.NormalizationPosition.Default, input.Reflect) * weight;
                    break;
                case Source.Y:
                    totalTranslation.Y += NormalizeParameterValue(value, pMin, pMax, pDef,
                        setting.NormalizationPosition.Minimum, setting.NormalizationPosition.Maximum,
                        setting.NormalizationPosition.Default, input.Reflect) * weight;
                    break;
                default:
                    totalAngle += NormalizeParameterValue(value, pMin, pMax, pDef,
                        setting.NormalizationAngle.Minimum, setting.NormalizationAngle.Maximum,
                        setting.NormalizationAngle.Default, input.Reflect) * weight;
                    break;
            }
        }

        // outputs: translation of the particle relative to its parent
        float GetOutputValue(Output o, Vec2 translation, int baseParticle, int particleIndex, Vec2 parentGravity)
        {
            float outputValue;
            switch (o.Type)
            {
                case Source.X:
                    outputValue = translation.X;
                    if (o.Reflect) outputValue *= -1.0f;
                    return outputValue;
                case Source.Y:
                    outputValue = translation.Y;
                    if (o.Reflect) outputValue *= -1.0f;
                    return outputValue;
                default:
                    if (particleIndex >= 2)
                    {
                        parentGravity = _particles[baseParticle + particleIndex - 1].Position
                                        - _particles[baseParticle + particleIndex - 2].Position;
                    }
                    else
                    {
                        parentGravity = parentGravity * -1.0f;
                    }
                    outputValue = CMath.DirectionToRadian(parentGravity, translation);
                    if (o.Reflect) outputValue *= -1.0f;
                    return outputValue;
            }
        }

        static float GetOutputScale(Output o)
        {
            switch (o.Type)
            {
                case Source.X: return o.TranslationScale.X;
                case Source.Y: return o.TranslationScale.Y;
                default: return o.AngleScale;
            }
        }

        void UpdateParticles(int baseIndex, int strandCount, Vec2 totalTranslation, float totalAngle,
            Vec2 windDirection, float thresholdValue, float deltaTimeSeconds, float airResistance)
        {
            _particles[baseIndex].Position = totalTranslation;

            float totalRadian = CMath.DegreesToRadian(totalAngle);
            Vec2 currentGravity = CMath.RadianToDirection(totalRadian);
            currentGravity.Normalize();

            for (int i = 1; i < strandCount; ++i)
            {
                Particle cur = _particles[baseIndex + i];
                Particle prev = _particles[baseIndex + i - 1];

                cur.Force = (currentGravity * cur.Acceleration) + windDirection;
                cur.LastPosition = cur.Position;

                float delay = cur.Delay * deltaTimeSeconds * 30.0f;

                Vec2 direction;
                direction.X = cur.Position.X - prev.Position.X;
                direction.Y = cur.Position.Y - prev.Position.Y;

                float radian = CMath.DirectionToRadian(cur.LastGravity, currentGravity) / airResistance;

                // faithful to the SDK: direction.Y uses the already rotated direction.X
                direction.X = ((CMath.Cos(radian) * direction.X) - (direction.Y * CMath.Sin(radian)));
                direction.Y = ((CMath.Sin(radian) * direction.X) + (direction.Y * CMath.Cos(radian)));

                cur.Position = prev.Position + direction;

                Vec2 velocity;
                velocity.X = cur.Velocity.X * delay;
                velocity.Y = cur.Velocity.Y * delay;
                Vec2 force = cur.Force * delay * delay;

                cur.Position = cur.Position + velocity + force;

                Vec2 newDirection = cur.Position - prev.Position;
                newDirection.Normalize();

                cur.Position = prev.Position + (newDirection * cur.Radius);

                if (CMath.Abs(cur.Position.X) < thresholdValue)
                    cur.Position.X = 0.0f;

                if (delay != 0.0f)
                {
                    cur.Velocity.X = cur.Position.X - cur.LastPosition.X;
                    cur.Velocity.Y = cur.Position.Y - cur.LastPosition.Y;
                    cur.Velocity = cur.Velocity / delay;
                    cur.Velocity = cur.Velocity * cur.Mobility;
                }

                cur.Force = new Vec2(0.0f, 0.0f);
                cur.LastGravity = currentGravity;
            }
        }

        void UpdateParticlesForStabilization(int baseIndex, int strandCount, Vec2 totalTranslation, float totalAngle,
            Vec2 windDirection, float thresholdValue)
        {
            _particles[baseIndex].Position = totalTranslation;

            float totalRadian = CMath.DegreesToRadian(totalAngle);
            Vec2 currentGravity = CMath.RadianToDirection(totalRadian);
            currentGravity.Normalize();

            for (int i = 1; i < strandCount; ++i)
            {
                Particle cur = _particles[baseIndex + i];
                Particle prev = _particles[baseIndex + i - 1];

                cur.Force = (currentGravity * cur.Acceleration) + windDirection;
                cur.LastPosition = cur.Position;
                cur.Velocity = new Vec2(0.0f, 0.0f);

                Vec2 force = cur.Force;
                force.Normalize();
                force = force * cur.Radius;
                cur.Position = prev.Position + force;

                if (CMath.Abs(cur.Position.X) < thresholdValue)
                    cur.Position.X = 0.0f;

                cur.Force = new Vec2(0.0f, 0.0f);
                cur.LastGravity = currentGravity;
            }
        }

        // returns the new parameter value
        static float UpdateOutputParameterValue(float parameterValue, float min, float max, float translation, Output output)
        {
            float outputScale = GetOutputScale(output);
            float value = translation * outputScale;

            if (value < min)
            {
                if (value < output.ValueBelowMinimum) output.ValueBelowMinimum = value;
                value = min;
            }
            else if (value > max)
            {
                if (value > output.ValueExceededMaximum) output.ValueExceededMaximum = value;
                value = max;
            }

            float weight = (output.Weight / MaximumWeight);
            if (weight >= 1.0f) return value;
            return (parameterValue * (1.0f - weight)) + (value * weight);
        }

        // ---- initialisation --------------------------------------------------------

        void Initialize()
        {
            for (int s = 0; s < _settings.Count; ++s)
            {
                SubRig setting = _settings[s];
                int b = setting.BaseParticleIndex;

                Particle first = _particles[b];
                first.InitialPosition = new Vec2(0.0f, 0.0f);
                first.LastPosition = first.InitialPosition;
                first.LastGravity = new Vec2(0.0f, 1.0f);
                first.Velocity = new Vec2(0.0f, 0.0f);
                first.Force = new Vec2(0.0f, 0.0f);

                for (int i = 1; i < setting.ParticleCount; ++i)
                {
                    Particle pt = _particles[b + i];
                    Vec2 radius = new Vec2(0.0f, pt.Radius);
                    pt.InitialPosition = _particles[b + i - 1].InitialPosition + radius;
                    pt.Position = pt.InitialPosition;
                    pt.LastPosition = pt.InitialPosition;
                    pt.LastGravity = new Vec2(0.0f, 1.0f);
                    pt.Velocity = new Vec2(0.0f, 0.0f);
                    pt.Force = new Vec2(0.0f, 0.0f);
                }
            }
        }

        public void Reset()
        {
            Gravity = new Vec2(0f, -1f);
            Wind = new Vec2(0f, 0f);
            Initialize();
        }

        void EnsureCaches(CubismModel model, bool copyInputCache)
        {
            int count = model.ParameterCount;
            if (_parameterCaches.Length < count) _parameterCaches = new float[count];
            if (_parameterInputCaches.Length < count)
            {
                _parameterInputCaches = new float[count];
                if (copyInputCache)
                    for (int j = 0; j < count; ++j) _parameterInputCaches[j] = model.GetParameterValue(j);
            }
        }

        // Settles the pendulums to the current parameter values without simulating motion.
        public void Stabilization(CubismModel model)
        {
            int count = model.ParameterCount;
            if (_parameterCaches.Length < count) _parameterCaches = new float[count];
            if (_parameterInputCaches.Length < count) _parameterInputCaches = new float[count];
            for (int j = 0; j < count; ++j)
            {
                _parameterCaches[j] = model.GetParameterValue(j);
                _parameterInputCaches[j] = model.GetParameterValue(j);
            }

            for (int settingIndex = 0; settingIndex < _settings.Count; ++settingIndex)
            {
                float totalAngle = 0.0f;
                Vec2 totalTranslation = new Vec2(0f, 0f);
                SubRig setting = _settings[settingIndex];

                for (int i = 0; i < setting.InputCount; ++i)
                {
                    Input inp = _inputs[setting.BaseInputIndex + i];
                    float weight = inp.Weight / MaximumWeight;
                    if (inp.SourceParameterIndex == -1)
                        inp.SourceParameterIndex = model.GetParameterIndex(inp.SourceId);
                    if (inp.SourceParameterIndex < 0) continue;

                    int pi = inp.SourceParameterIndex;
                    ApplyInput(inp, setting, model.GetParameterValue(pi), model.GetParameterMinimum(pi),
                        model.GetParameterMaximum(pi), model.GetParameterDefault(pi),
                        ref totalTranslation, ref totalAngle, weight);
                    _parameterCaches[pi] = model.GetParameterValue(pi);
                }

                float radAngle = CMath.DegreesToRadian(-totalAngle);
                totalTranslation.X = (totalTranslation.X * CMath.Cos(radAngle) - totalTranslation.Y * CMath.Sin(radAngle));
                totalTranslation.Y = (totalTranslation.X * CMath.Sin(radAngle) + totalTranslation.Y * CMath.Cos(radAngle));

                UpdateParticlesForStabilization(setting.BaseParticleIndex, setting.ParticleCount, totalTranslation,
                    totalAngle, Wind, MovementThreshold * setting.NormalizationPosition.Maximum);

                for (int i = 0; i < setting.OutputCount; ++i)
                {
                    Output o = _outputs[setting.BaseOutputIndex + i];
                    int particleIndex = o.VertexIndex;

                    if (o.DestinationParameterIndex == -1)
                        o.DestinationParameterIndex = model.GetParameterIndex(o.DestinationId);

                    if (particleIndex < 1 || particleIndex >= setting.ParticleCount) continue;
                    if (o.DestinationParameterIndex < 0) continue;

                    Particle pt = _particles[setting.BaseParticleIndex + particleIndex];
                    Particle parent = _particles[setting.BaseParticleIndex + particleIndex - 1];
                    Vec2 translation = new Vec2(pt.Position.X - parent.Position.X, pt.Position.Y - parent.Position.Y);

                    float outputValue = GetOutputValue(o, translation, setting.BaseParticleIndex, particleIndex, Gravity);
                    _currentRigOutputs[settingIndex][i] = outputValue;
                    _previousRigOutputs[settingIndex][i] = outputValue;

                    int di = o.DestinationParameterIndex;
                    float nv = UpdateOutputParameterValue(model.GetParameterValue(di), model.GetParameterMinimum(di),
                        model.GetParameterMaximum(di), outputValue, o);
                    model.SetParameterValue(di, nv);
                    _parameterCaches[di] = model.GetParameterValue(di);
                }
            }
        }

        // Pendulum evaluation. The pendulum is stepped at the rig fps (or the render rate when the rig has no fps);
        // the output to the model is interpolated between the previous and the latest pendulum result.
        public void Evaluate(CubismModel model, float deltaTimeSeconds)
        {
            if (0.0f >= deltaTimeSeconds) return;

            _currentRemainTime += deltaTimeSeconds;
            if (_currentRemainTime > MaxDeltaTime)
                _currentRemainTime = 0.0f;

            int paramCount = model.ParameterCount;
            EnsureCaches(model, true);

            float physicsDeltaTime;
            if (_fps > 0.0f) physicsDeltaTime = 1.0f / _fps;
            else physicsDeltaTime = deltaTimeSeconds;

            while (_currentRemainTime >= physicsDeltaTime)
            {
                // _previousRigOutputs <- _currentRigOutputs
                for (int settingIndex = 0; settingIndex < _settings.Count; ++settingIndex)
                {
                    SubRig cs = _settings[settingIndex];
                    for (int i = 0; i < cs.OutputCount; ++i)
                        _previousRigOutputs[settingIndex][i] = _currentRigOutputs[settingIndex][i];
                }

                // input at the pendulum step timing: linear blend of the input cache and the live parameters
                float inputWeight = physicsDeltaTime / _currentRemainTime;
                for (int j = 0; j < paramCount; ++j)
                {
                    _parameterCaches[j] = _parameterInputCaches[j] * (1.0f - inputWeight) + model.GetParameterValue(j) * inputWeight;
                    _parameterInputCaches[j] = _parameterCaches[j];
                }

                for (int settingIndex = 0; settingIndex < _settings.Count; ++settingIndex)
                {
                    float totalAngle = 0.0f;
                    Vec2 totalTranslation = new Vec2(0f, 0f);
                    SubRig setting = _settings[settingIndex];

                    for (int i = 0; i < setting.InputCount; ++i)
                    {
                        Input inp = _inputs[setting.BaseInputIndex + i];
                        float weight = inp.Weight / MaximumWeight;
                        if (inp.SourceParameterIndex == -1)
                            inp.SourceParameterIndex = model.GetParameterIndex(inp.SourceId);
                        if (inp.SourceParameterIndex < 0) continue; // model lacks the parameter: ignore this input

                        int pi = inp.SourceParameterIndex;
                        ApplyInput(inp, setting, _parameterCaches[pi], model.GetParameterMinimum(pi),
                            model.GetParameterMaximum(pi), model.GetParameterDefault(pi),
                            ref totalTranslation, ref totalAngle, weight);
                    }

                    float radAngle = CMath.DegreesToRadian(-totalAngle);
                    // faithful to the SDK: Y uses the already rotated X
                    totalTranslation.X = (totalTranslation.X * CMath.Cos(radAngle) - totalTranslation.Y * CMath.Sin(radAngle));
                    totalTranslation.Y = (totalTranslation.X * CMath.Sin(radAngle) + totalTranslation.Y * CMath.Cos(radAngle));

                    UpdateParticles(setting.BaseParticleIndex, setting.ParticleCount, totalTranslation, totalAngle, Wind,
                        MovementThreshold * setting.NormalizationPosition.Maximum, physicsDeltaTime, AirResistance);

                    for (int i = 0; i < setting.OutputCount; ++i)
                    {
                        Output o = _outputs[setting.BaseOutputIndex + i];
                        int particleIndex = o.VertexIndex;

                        if (o.DestinationParameterIndex == -1)
                            o.DestinationParameterIndex = model.GetParameterIndex(o.DestinationId);

                        if (particleIndex < 1 || particleIndex >= setting.ParticleCount) continue;
                        if (o.DestinationParameterIndex < 0) continue;

                        Particle pt = _particles[setting.BaseParticleIndex + particleIndex];
                        Particle parent = _particles[setting.BaseParticleIndex + particleIndex - 1];
                        Vec2 translation = new Vec2(pt.Position.X - parent.Position.X, pt.Position.Y - parent.Position.Y);

                        float outputValue = GetOutputValue(o, translation, setting.BaseParticleIndex, particleIndex, Gravity);
                        _currentRigOutputs[settingIndex][i] = outputValue;

                        int di = o.DestinationParameterIndex;
                        _parameterCaches[di] = UpdateOutputParameterValue(_parameterCaches[di], model.GetParameterMinimum(di),
                            model.GetParameterMaximum(di), outputValue, o);
                    }
                }

                _currentRemainTime -= physicsDeltaTime;
            }

            float alpha = _currentRemainTime / physicsDeltaTime;
            Interpolate(model, alpha);
        }

        void Interpolate(CubismModel model, float weight)
        {
            for (int settingIndex = 0; settingIndex < _settings.Count; ++settingIndex)
            {
                SubRig setting = _settings[settingIndex];
                for (int i = 0; i < setting.OutputCount; ++i)
                {
                    Output o = _outputs[setting.BaseOutputIndex + i];
                    int di = o.DestinationParameterIndex;
                    if (di < 0) continue;

                    float nv = UpdateOutputParameterValue(model.GetParameterValue(di), model.GetParameterMinimum(di),
                        model.GetParameterMaximum(di),
                        _previousRigOutputs[settingIndex][i] * (1 - weight) + _currentRigOutputs[settingIndex][i] * weight, o);
                    model.SetParameterValue(di, nv);
                }
            }
        }
    }
}
