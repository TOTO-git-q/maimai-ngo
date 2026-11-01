using System;
using System.Collections.Generic;
using PartnerLive2D.Core;

namespace PartnerLive2D.Framework
{
    internal enum ExpressionBlendType { Additive = 0, Multiply = 1, Overwrite = 2 }

    internal struct ExpressionParameter
    {
        public string ParameterId;
        public ExpressionBlendType BlendType;
        public float Value;
    }

    // CubismExpressionMotion (exp3.json)
    internal sealed class CubismExpressionMotion : ACubismMotion
    {
        public const float DefaultAdditiveValue = 0.0f;
        public const float DefaultMultiplyValue = 1.0f;
        const float DefaultFadeTime = 1.0f;

        readonly List<ExpressionParameter> _parameters = new List<ExpressionParameter>();

        public List<ExpressionParameter> Parameters { get { return _parameters; } }

        // An expression with no parameters; starting it fades the active expression back to neutral.
        public static CubismExpressionMotion CreateEmpty(float fadeSeconds)
        {
            var e = new CubismExpressionMotion();
            e.FadeInSeconds = fadeSeconds;
            e.FadeOutSeconds = fadeSeconds;
            return e;
        }

        public static CubismExpressionMotion Load(string path)
        {
            return Parse(Json.ParseFile(path));
        }

        public static CubismExpressionMotion Parse(object root)
        {
            var e = new CubismExpressionMotion();
            e.FadeInSeconds = Json.GetFloat(root, "FadeInTime", DefaultFadeTime);
            e.FadeOutSeconds = Json.GetFloat(root, "FadeOutTime", DefaultFadeTime);

            var list = Json.GetList(root, "Parameters");
            if (list != null)
            {
                foreach (object p in list)
                {
                    var item = new ExpressionParameter();
                    item.ParameterId = Json.GetString(p, "Id");
                    item.Value = Json.GetFloat(p, "Value");

                    string blend = Json.GetString(p, "Blend");
                    if (blend == null || blend == "Add") item.BlendType = ExpressionBlendType.Additive;
                    else if (blend == "Multiply") item.BlendType = ExpressionBlendType.Multiply;
                    else if (blend == "Overwrite") item.BlendType = ExpressionBlendType.Overwrite;
                    else item.BlendType = ExpressionBlendType.Additive; // unknown values recover as additive

                    e._parameters.Add(item);
                }
            }
            return e;
        }

        public override void DoUpdateParameters(CubismModel model, float userTimeSeconds, float weight, MotionQueueEntry entry)
        {
            for (int i = 0; i < _parameters.Count; ++i)
            {
                ExpressionParameter p = _parameters[i];
                int idx = model.GetParameterIndex(p.ParameterId);
                if (idx < 0) continue;
                switch (p.BlendType)
                {
                    case ExpressionBlendType.Additive: model.AddParameterValue(idx, p.Value, weight); break;
                    case ExpressionBlendType.Multiply: model.MultiplyParameterValue(idx, p.Value, weight); break;
                    case ExpressionBlendType.Overwrite: model.SetParameterValue(idx, p.Value, weight); break;
                }
            }
        }

        static float CalculateValue(float source, float destination, float fadeWeight)
        {
            return (source * (1.0f - fadeWeight)) + (destination * fadeWeight);
        }

        // Port of CubismExpressionMotion::CalculateExpressionParameters.
        // Note: like the C++ code (which copies the list element by value) the "previous" overwrite
        // value used for blending is the model's current parameter value, not the stored one.
        public void CalculateExpressionParameters(CubismModel model, float userTimeSeconds, MotionQueueEntry entry,
            List<ExpressionParameterValue> values, int expressionIndex, float fadeWeight)
        {
            if (entry == null || values == null) return;
            if (!entry.Available) return;

            UpdateFadeWeight(entry, userTimeSeconds);

            for (int i = 0; i < values.Count; ++i)
            {
                ExpressionParameterValue pv = values[i];
                if (pv.ParameterId == null) continue;

                int modelIndex = model.GetParameterIndex(pv.ParameterId);
                if (modelIndex < 0) continue;

                float currentParameterValue = model.GetParameterValue(modelIndex);
                float srcAdditive = pv.AdditiveValue;
                float srcMultiply = pv.MultiplyValue;
                float srcOverwrite = currentParameterValue; // value of the by-value copy in C++

                int parameterIndex = -1;
                for (int j = 0; j < _parameters.Count; ++j)
                {
                    if (pv.ParameterId != _parameters[j].ParameterId) continue;
                    parameterIndex = j;
                    break;
                }

                // parameters this expression does not reference fall back to their neutral values
                if (parameterIndex < 0)
                {
                    if (expressionIndex == 0)
                    {
                        pv.AdditiveValue = DefaultAdditiveValue;
                        pv.MultiplyValue = DefaultMultiplyValue;
                        pv.OverwriteValue = currentParameterValue;
                    }
                    else
                    {
                        pv.AdditiveValue = CalculateValue(srcAdditive, DefaultAdditiveValue, fadeWeight);
                        pv.MultiplyValue = CalculateValue(srcMultiply, DefaultMultiplyValue, fadeWeight);
                        pv.OverwriteValue = CalculateValue(srcOverwrite, currentParameterValue, fadeWeight);
                    }
                    continue;
                }

                float value = _parameters[parameterIndex].Value;
                float newAdditiveValue, newMultiplyValue, newSetValue;
                switch (_parameters[parameterIndex].BlendType)
                {
                    case ExpressionBlendType.Additive:
                        newAdditiveValue = value;
                        newMultiplyValue = DefaultMultiplyValue;
                        newSetValue = currentParameterValue;
                        break;
                    case ExpressionBlendType.Multiply:
                        newAdditiveValue = DefaultAdditiveValue;
                        newMultiplyValue = value;
                        newSetValue = currentParameterValue;
                        break;
                    case ExpressionBlendType.Overwrite:
                        newAdditiveValue = DefaultAdditiveValue;
                        newMultiplyValue = DefaultMultiplyValue;
                        newSetValue = value;
                        break;
                    default:
                        return;
                }

                if (expressionIndex == 0)
                {
                    pv.AdditiveValue = newAdditiveValue;
                    pv.MultiplyValue = newMultiplyValue;
                    pv.OverwriteValue = newSetValue;
                }
                else
                {
                    pv.AdditiveValue = (srcAdditive * (1.0f - fadeWeight)) + newAdditiveValue * fadeWeight;
                    pv.MultiplyValue = (srcMultiply * (1.0f - fadeWeight)) + newMultiplyValue * fadeWeight;
                    pv.OverwriteValue = (srcOverwrite * (1.0f - fadeWeight)) + newSetValue * fadeWeight;
                }
            }
        }
    }

    internal sealed class ExpressionParameterValue
    {
        public string ParameterId;
        public float AdditiveValue;
        public float MultiplyValue;
        public float OverwriteValue;
    }

    // CubismExpressionMotionManager (Cubism SDK 5 behaviour: expressions are blended through fade weights)
    internal sealed class ExpressionMotionManager : MotionQueueManager
    {
        readonly List<ExpressionParameterValue> _expressionParameterValues = new List<ExpressionParameterValue>();
        readonly List<float> _fadeWeights = new List<float>();

        public bool UpdateMotion(CubismModel model, float deltaTimeSeconds)
        {
            UserTimeSeconds += deltaTimeSeconds;
            bool updated = false;
            float expressionWeight = 0.0f;
            int expressionIndex = 0;

            while (_fadeWeights.Count < Motions.Count) _fadeWeights.Add(0.0f);

            for (int mi = 0; mi < Motions.Count;)
            {
                MotionQueueEntry entry = Motions[mi];
                if (entry == null) { Motions.RemoveAt(mi); continue; }
                var expressionMotion = entry.Motion as CubismExpressionMotion;
                if (expressionMotion == null) { Motions.RemoveAt(mi); continue; }

                if (entry.Available)
                {
                    // collect every parameter referenced by a running expression
                    var expressionParameters = expressionMotion.Parameters;
                    for (int i = 0; i < expressionParameters.Count; ++i)
                    {
                        string id = expressionParameters[i].ParameterId;
                        if (id == null) continue;
                        int modelIndex = model.GetParameterIndex(id);
                        if (modelIndex < 0) continue; // model lacks this parameter: skip silently

                        bool exists = false;
                        for (int j = 0; j < _expressionParameterValues.Count; ++j)
                        {
                            if (_expressionParameterValues[j].ParameterId == id) { exists = true; break; }
                        }
                        if (exists) continue;

                        var item = new ExpressionParameterValue();
                        item.ParameterId = id;
                        item.AdditiveValue = CubismExpressionMotion.DefaultAdditiveValue;
                        item.MultiplyValue = CubismExpressionMotion.DefaultMultiplyValue;
                        item.OverwriteValue = model.GetParameterValue(modelIndex);
                        _expressionParameterValues.Add(item);
                    }
                }

                expressionMotion.SetupMotionQueueEntry(entry, UserTimeSeconds);
                _fadeWeights[expressionIndex] = expressionMotion.UpdateFadeWeight(entry, UserTimeSeconds);
                expressionMotion.CalculateExpressionParameters(model, UserTimeSeconds, entry,
                    _expressionParameterValues, expressionIndex, _fadeWeights[expressionIndex]);

                expressionWeight += expressionMotion.FadeInSeconds == 0.0f
                    ? 1.0f
                    : CMath.GetEasingSine((UserTimeSeconds - entry.FadeInStartTime) / expressionMotion.FadeInSeconds);

                updated = true;

                if (entry.TriggeredFadeOut)
                    entry.StartFadeout(entry.FadeOutSeconds, UserTimeSeconds);

                ++mi;
                ++expressionIndex;
            }

            // once the newest expression is fully faded in, drop everything before it
            if (Motions.Count > 1)
            {
                float latestFadeWeight = _fadeWeights[_fadeWeights.Count - 1];
                if (latestFadeWeight >= 1.0f)
                {
                    for (int i = Motions.Count - 2; i >= 0; i--)
                    {
                        Motions.RemoveAt(i);
                        _fadeWeights.RemoveAt(i);
                    }
                }
            }

            if (expressionWeight > 1.0f) expressionWeight = 1.0f;

            // apply to the model
            for (int i = 0; i < _expressionParameterValues.Count; ++i)
            {
                ExpressionParameterValue pv = _expressionParameterValues[i];
                int idx = model.GetParameterIndex(pv.ParameterId);
                if (idx >= 0)
                    model.SetParameterValue(idx, (pv.OverwriteValue + pv.AdditiveValue) * pv.MultiplyValue, expressionWeight);
                pv.AdditiveValue = CubismExpressionMotion.DefaultAdditiveValue;
                pv.MultiplyValue = CubismExpressionMotion.DefaultMultiplyValue;
            }

            return updated;
        }
    }
}
