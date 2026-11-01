using System;
using System.Collections.Generic;

namespace PartnerLive2D.Core
{
    // Shared parameter/part semantics over backend-owned unmanaged storage.
    public abstract unsafe class CubismModel : IDisposable
    {
        public int ParameterCount { get; protected set; }
        public int PartCount { get; protected set; }
        public int DrawableCount { get; protected set; }

        public string[] ParameterIds { get; protected set; }
        public string[] PartIds { get; protected set; }
        public string[] DrawableIds { get; protected set; }

        protected float* _paramValues, _paramMin, _paramMax, _paramDefault;
        protected float* _partOpacities;
        float[] _savedParams;

        readonly Dictionary<string, int> _paramIndex = new Dictionary<string, int>();
        readonly Dictionary<string, int> _partIndex = new Dictionary<string, int>();
        readonly Dictionary<string, int> _drawableIndex = new Dictionary<string, int>();

        // canvas in model units: size and origin are in pixels in the core, converted here
        public float CanvasWidth { get; protected set; }
        public float CanvasHeight { get; protected set; }
        public float PixelsPerUnit { get; protected set; }
        public float OriginX { get; protected set; }
        public float OriginY { get; protected set; }

        public static uint CoreVersion => CubismCore.csmGetVersion();

        // ---- parameters -------------------------------------------------------

        public int GetParameterIndex(string id)
        {
            int i;
            return id != null && _paramIndex.TryGetValue(id, out i) ? i : -1;
        }

        public float GetParameterValue(int i) => _paramValues[i];
        public float GetParameterMinimum(int i) => _paramMin[i];
        public float GetParameterMaximum(int i) => _paramMax[i];
        public float GetParameterDefault(int i) => _paramDefault[i];

        // weight 1 = overwrite; otherwise blend towards value (Cubism SetParameterValue semantics)
        public void SetParameterValue(int i, float value, float weight = 1f)
        {
            if (i < 0) return;
            value = Clamp(value, _paramMin[i], _paramMax[i]);
            _paramValues[i] = weight >= 1f ? value : _paramValues[i] * (1f - weight) + value * weight;
        }

        public void AddParameterValue(int i, float value, float weight = 1f)
        {
            if (i < 0) return;
            SetParameterValue(i, _paramValues[i] + value * weight);
        }

        public void MultiplyParameterValue(int i, float value, float weight = 1f)
        {
            if (i < 0) return;
            SetParameterValue(i, _paramValues[i] * (1f + (value - 1f) * weight));
        }

        public void SetParameterValue(string id, float value, float weight = 1f) => SetParameterValue(GetParameterIndex(id), value, weight);
        public void AddParameterValue(string id, float value, float weight = 1f) => AddParameterValue(GetParameterIndex(id), value, weight);

        public void SaveParameters()
        {
            for (int i = 0; i < ParameterCount; i++) _savedParams[i] = _paramValues[i];
        }

        public void LoadParameters()
        {
            for (int i = 0; i < ParameterCount; i++) _paramValues[i] = _savedParams[i];
        }

        // ---- parts ------------------------------------------------------------

        public int GetPartIndex(string id)
        {
            int i;
            return id != null && _partIndex.TryGetValue(id, out i) ? i : -1;
        }

        public float GetPartOpacity(int i) => _partOpacities[i];

        public void SetPartOpacity(int i, float value)
        {
            if (i < 0) return;
            _partOpacities[i] = Clamp(value, 0f, 1f);
        }

        // ---- drawables (valid after Update) -----------------------------------

        public int GetDrawableIndex(string id)
        {
            int i;
            return id != null && _drawableIndex.TryGetValue(id, out i) ? i : -1;
        }

        protected void InitializeIds()
        {
            for (int i = 0; i < ParameterCount; i++) _paramIndex.Add(ParameterIds[i], i);
            for (int i = 0; i < PartCount; i++) _partIndex.Add(PartIds[i], i);
            for (int i = 0; i < DrawableCount; i++) _drawableIndex.Add(DrawableIds[i], i);
            _savedParams = new float[ParameterCount];
        }

        public void MultiplyParameterValue(string id, float value, float weight = 1f) => MultiplyParameterValue(GetParameterIndex(id), value, weight);
        public abstract byte* ConstantFlags { get; }
        public abstract byte* DynamicFlags { get; }
        public abstract int* TextureIndices { get; }
        public abstract int* RenderOrders { get; }
        public abstract float* Opacities { get; }
        public abstract int* MaskCounts { get; }
        public abstract int** Masks { get; }
        public abstract int* VertexCounts { get; }
        public abstract CubismCore.Vector2** VertexPositions { get; }
        public abstract CubismCore.Vector2** VertexUvs { get; }
        public abstract int* IndexCounts { get; }
        public abstract ushort** Indices { get; }
        public abstract CubismCore.Vector4* MultiplyColors { get; }
        public abstract CubismCore.Vector4* ScreenColors { get; }

        public abstract void Update();
        protected static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public abstract void Dispose();
    }
}
