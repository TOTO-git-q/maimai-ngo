using System;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // Animator sampling precedes LateUpdate; the canvas callback also follows UI rebuilds.
    [DefaultExecutionOrder(32000)]
    sealed class MusicSelectLateRunner : MonoBehaviour
    {
        public MusicSelectLayout Layout;
        void OnEnable() { Canvas.willRenderCanvases += Reapply; }
        void OnDisable() { Canvas.willRenderCanvases -= Reapply; }
        void LateUpdate() { Reapply(); }
        void Reapply()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { Layout?.ReapplyTransforms(); }
            catch (Exception e) { MusicSelectSession.Fail(e); }
            TestHarness.SelectPerf.Add("reapply", sw.Elapsed.TotalMilliseconds);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { Layout?.Maintain(); }
            catch (Exception e) { MusicSelectSession.Fail(e); }
            TestHarness.SelectPerf.Add("maintain", (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }
    }
}
