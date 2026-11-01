using System;

namespace PartnerLive2D.Unity
{
    // Shared by the Unity adapter and headless regressions. Baselines are immutable.
    public sealed class MusicSelectNodeState
    {
        readonly float[] _baseline;
        readonly Action<float[]> _write;
        readonly float[] _desired = new float[6];
        readonly float[] _restore = new float[6];
        bool _applied;
        public MusicSelectNodeState(float[] baseline, Action<float[]> write)
        {
            _baseline = (float[])baseline.Clone(); _write = write;
        }
        public void Apply(float[] values, bool explicitNode = false)
        {
            if (!explicitNode && values[0] == 1 && values[1] == 0 && values[2] == 0) { Restore(); return; }
            _applied = true;
            _desired[0] = _baseline[0] + values[1]; _desired[1] = _baseline[1] + values[2]; _desired[2] = _baseline[2];
            _desired[3] = _baseline[3] * values[0]; _desired[4] = _baseline[4] * values[0]; _desired[5] = _baseline[5];
            Reapply();
        }
        public void Reapply() { if (_applied) _write(_desired); }
        public void Restore()
        {
            if (!_applied) return;
            Array.Copy(_baseline, _restore, 6); _write(_restore); _applied = false;
        }
    }
    public sealed class MusicSelectHiddenState
    {
        readonly float _alpha;
        readonly bool _raycasts;
        readonly Action<float, bool> _write;
        bool _hidden;
        public MusicSelectHiddenState(float alpha, bool raycasts, Action<float, bool> write)
        { _alpha = alpha; _raycasts = raycasts; _write = write; }
        public void Apply(bool hidden)
        {
            if (!hidden) { Restore(); return; }
            _hidden = true; _write(0, false);
        }
        public void Restore()
        {
            if (!_hidden) return;
            _write(_alpha, _raycasts); _hidden = false;
        }
    }
}
