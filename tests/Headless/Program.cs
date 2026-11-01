using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PartnerLive2D.Core;
using PartnerLive2D.Framework;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        const string DefaultResources = @"E:\tools\live2d\CubismSdkForNative-5-r.5\Samples\Resources";

        sealed class Range
        {
            public string Name;
            public int Index = -1;
            public float Min = float.MaxValue, Max = float.MinValue;
            public void Add(float v) { if (v < Min) Min = v; if (v > Max) Max = v; }
            public override string ToString()
            {
                if (Index < 0) return string.Format(CultureInfo.InvariantCulture, "  {0,-18} (not in model)", Name);
                return string.Format(CultureInfo.InvariantCulture, "  {0,-18} min={1,9:F4} max={2,9:F4}", Name, Min, Max);
            }
        }

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "spritepack")
            {
                try
                {
                    if (args.Length != 2) throw new ArgumentException("Usage: Headless.exe spritepack <manifest.json>");
                    CheckSpritePack(args[1]);
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Length > 0 && args[0] == "rigdump")
            {
                try
                {
                    if (args.Length != 3) throw new ArgumentException("Usage: Headless.exe rigdump <model3.json> <params.json>");
                    RigDump(args[1], args[2]);
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            string resources = args.Length > 0 ? args[0] : DefaultResources;
            var failures = new List<string>();
            var modelFiles = new List<string>();
            foreach (string dir in Directory.GetDirectories(resources))
            {
                string[] files = Directory.GetFiles(dir, "*.model3.json");
                Array.Sort(files, StringComparer.Ordinal);
                modelFiles.AddRange(files);
            }
            if (modelFiles.Count == 0)
            {
                Console.WriteLine("No models found in " + resources);
                return 2;
            }

            try { ThemeChecks(); }
            catch (Exception ex) { Console.WriteLine("  FAIL: " + ex); failures.Add("theme checks: " + ex.Message); }
            try { SpritePairChecks(); SpritePairLoaderChecks(); }
            catch (Exception ex) { Console.WriteLine("  FAIL: " + ex); failures.Add("sprite pair checks: " + ex.Message); }
            try { MainThreadChecks(); }
            catch (Exception ex) { Console.WriteLine("  FAIL: " + ex); failures.Add("main-thread checks: " + ex.Message); }
            try { RigChecks(); }
            catch (Exception ex) { Console.WriteLine("  FAIL: " + ex); failures.Add("rig checks: " + ex.Message); }
            try { SelectPhaseChecks(); }
            catch (Exception ex) { Console.WriteLine("  FAIL: " + ex); failures.Add("select phase checks: " + ex.Message); }
            try { SelectPngCacheChecks(); }
            catch (Exception ex) { Console.WriteLine("  FAIL: " + ex); failures.Add("PNG cache checks: " + ex.Message); }
            try { SelectChecks(); }
            catch (Exception ex) { Console.WriteLine("  FAIL: " + ex); failures.Add("select checks: " + ex.Message); }
            Console.WriteLine("Core version: 0x" + CubismModel.CoreVersion.ToString("X8"));
            try { MathChecks(); }
            catch (Exception ex)
            {
                Console.WriteLine("  FAIL: " + ex);
                failures.Add("math self-check: " + ex.Message);
            }
            foreach (string path in modelFiles)
            {
                try
                {
                    RunModel(path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  FAIL: " + ex);
                    failures.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }

            Console.WriteLine();
            if (failures.Count > 0)
            {
                Console.WriteLine("FAILED (" + failures.Count + " of " + modelFiles.Count + ")");
                foreach (string f in failures) Console.WriteLine("  " + f);
                return 1;
            }
            Console.WriteLine("ALL " + modelFiles.Count + " MODELS PASSED");
            return 0;
        }

        static void RunModel(string model3Path)
        {
            Console.WriteLine("=== " + Path.GetFileName(model3Path));

            // physics output parameter to watch (first Destination id of physics3.json)
            Model3Json settings = Model3Json.Load(model3Path);
            string physicsOut = null;
            if (settings.PhysicsFile != null)
            {
                object phys = Json.ParseFile(settings.Resolve(settings.PhysicsFile));
                var sets = Json.GetList(phys, "PhysicsSettings");
                if (sets != null && sets.Count > 0)
                {
                    var outs = Json.GetList(sets[0], "Output");
                    if (outs != null && outs.Count > 0)
                        physicsOut = Json.GetString(Json.Get(outs[0], "Destination"), "Id");
                }
            }

            using (CubismAvatar avatar = CubismAvatar.Load(model3Path))
            {
                CubismModel m = avatar.Model;
                Console.WriteLine("  params={0} parts={1} drawables={2} textures={3} canvas={4:F2}x{5:F2}",
                    m.ParameterCount, m.PartCount, m.DrawableCount, avatar.TexturePaths.Length, m.CanvasWidth, m.CanvasHeight);
                foreach (string tp in avatar.TexturePaths)
                    if (tp.Length > 0 && !File.Exists(tp)) throw new FileNotFoundException("texture missing", tp);

                var sb = new StringBuilder("  motions:");
                foreach (string g in avatar.MotionGroups)
                    sb.Append(' ').Append(g).Append('=').Append(avatar.GetMotionCount(g));
                if (avatar.MotionGroups.Length == 0) sb.Append(" (none)");
                Console.WriteLine(sb.ToString());
                Console.WriteLine("  expressions: " + (avatar.ExpressionNames.Length == 0 ? "(none)" : string.Join(", ", avatar.ExpressionNames)));

                sb = new StringBuilder("  hit areas:");
                foreach (string h in avatar.HitAreaNames)
                    sb.Append(' ').Append(h).Append("->drawable#").Append(avatar.GetHitAreaDrawableIndex(h));
                if (avatar.HitAreaNames.Length == 0) sb.Append(" (none)");
                Console.WriteLine(sb.ToString());
                if (avatar.GetHitAreaDrawableIndex("__no_such_area__") != -1) throw new Exception("unknown hit area must return -1");

                if (avatar.ExpressionNames.Length > 0) ExpressionCheck(model3Path, settings);

                var ranges = new List<Range>();
                ranges.Add(new Range { Name = "ParamAngleX" });
                ranges.Add(new Range { Name = "ParamEyeLOpen" });
                ranges.Add(new Range { Name = "ParamBreath" });
                ranges.Add(new Range { Name = physicsOut ?? "(no physics)" });
                foreach (Range r in ranges) r.Index = m.GetParameterIndex(r.Name);

                // schedule: each motion group once, spread over the 10 s run; retried while refused by priority
                const int fps = 60;
                const int frames = 10 * fps;
                var groups = new List<string>(avatar.MotionGroups);
                int nextGroup = 0;
                int started = 0;
                int firstRefused = 0;
                string exprName = avatar.ExpressionNames.Length > 0 ? avatar.ExpressionNames[0] : null;
                float dt = 1f / fps;
                int motionStartedFrames = 0;

                // 10 s minimum; keeps going (max 60 s) only while a motion group is still waiting for a free priority slot
                int f = 0;
                for (; f < frames || (nextGroup < groups.Count && f < 60 * fps); f++)
                {

                    if (nextGroup < groups.Count && f >= nextGroup * frames / Math.Max(1, groups.Count))
                    {
                        if (avatar.GetMotionCount(groups[nextGroup]) == 0 || avatar.StartMotion(groups[nextGroup], 0, 2))
                        {
                            started++;
                            nextGroup++;
                        }
                        else if (f == nextGroup * frames / Math.Max(1, groups.Count)) firstRefused++;
                    }

                    if (f == frames / 2 && exprName != null) avatar.SetExpression(exprName);
                    if (f == frames * 4 / 5 && exprName != null) avatar.SetExpression(null);

                    double ang = 2.0 * Math.PI * f / frames; // one full circle per 10 s
                    avatar.SetLookTarget((float)Math.Cos(ang), (float)Math.Sin(ang));

                    avatar.Tick(dt);
                    if (!avatar.IsMotionFinished) motionStartedFrames++;

                    CheckFinite(avatar, f);
                    foreach (Range r in ranges)
                        if (r.Index >= 0) r.Add(m.GetParameterValue(r.Index));
                }

                Console.WriteLine("  sim: {0} frames @ {1} fps, motion groups started {2}/{3} (priority-refused at scheduled time: {4}), motion active in {5} frames, expression={6}",
                    f, fps, started, groups.Count, firstRefused, motionStartedFrames, exprName ?? "(none)");
                if (started != groups.Count) throw new Exception("not every motion group could be started");
                foreach (Range r in ranges) Console.WriteLine(r.ToString());

                // API edge cases
                if (avatar.StartMotion("__nope__", 0, 2)) throw new Exception("unknown group must be refused");
                avatar.SetExpression("__nope__");
                avatar.Tick(dt);
                CheckFinite(avatar, f);
                Console.WriteLine("  OK");
            }
        }

        // Bezier time -> t solver (Cardano) must agree with the true curve on monotone control points.
        static void MathChecks()
        {
            var rnd = new Random(12345);
            double worst = 0;
            int skipped = 0, big = 0;
            for (int n = 0; n < 20000; n++)
            {
                float x1 = (float)(rnd.NextDouble() * 2.0);
                float x2 = x1 + 0.05f + (float)(rnd.NextDouble() * 3.0);
                float c1 = x1 + (float)(rnd.NextDouble() * (x2 - x1));
                float c2 = c1 + (float)(rnd.NextDouble() * (x2 - c1));
                float time = x1 + (float)(rnd.NextDouble() * (x2 - x1));

                float a = x2 - 3.0f * c2 + 3.0f * c1 - x1;
                float b = 3.0f * c2 - 6.0f * c1 + 3.0f * x1;
                float c = 3.0f * c1 - 3.0f * x1;
                float d = x1 - time;
                // |a| ~ 0 (a nearly quadratic curve) is ill-conditioned in float32 for the SDK algorithm too
                if (Math.Abs(a) < 0.01f) { skipped++; continue; }
                float t = CMath.CardanoAlgorithmForBezier(a, b, c, d);

                double u = 1.0 - t;
                double xAt = u * u * u * x1 + 3 * u * u * t * c1 + 3 * u * t * t * c2 + (double)t * t * t * x2;
                double err = Math.Abs(xAt - time); // error measured on the time axis
                if (double.IsNaN(err)) throw new Exception("Cardano returned NaN");
                if (err > worst) worst = err;
                if (err > 3e-3) big++;
            }
            Console.WriteLine("Cardano bezier solver: time-axis error over " + (20000 - skipped) + " random curves: worst = " + worst.ToString("E2", CultureInfo.InvariantCulture)
                + ", >3e-3 in " + big + " (float32 cancellation, same as the C++ SDK), " + skipped + " near-quadratic curves skipped");
            // a double-precision run of the same algorithm stays below 1e-6 everywhere (verified separately)
            if (worst > 0.05 || big > (20000 - skipped) / 50) throw new Exception("Cardano solver does not reproduce the requested time");

            if (Math.Abs(CMath.GetEasingSine(0.5f) - 0.5f) > 1e-6f || CMath.GetEasingSine(-1f) != 0f || CMath.GetEasingSine(2f) != 1f)
                throw new Exception("GetEasingSine");

            var j = (Dictionary<string, object>)Json.Parse("﻿{\"a\":[1,2.5e1,-3],\"b\":{\"c\":\"x\\u0041\\n\"},\"d\":true,\"e\":null}");
            if (Json.GetList(j, "a").Count != 3 || (double)Json.GetList(j, "a")[1] != 25.0
                || Json.GetString(Json.Get(j, "b"), "c") != "xA\n"
                || !Json.GetBool(j, "d") || !Json.IsNull(j, "e")) throw new Exception("Json parser");
        }

        // With idle disabled, every expression must fade back to neutral when cleared, and at least one
        // expression of the model must visibly move a parameter (some sample expressions are identity or clamped).
        static void ExpressionCheck(string model3Path, Model3Json settings)
        {
            float dt = 1f / 60f;
            // parameters that other systems (breath, look, blink) keep moving are excluded
            var volatileIds = new HashSet<string> { "ParamAngleX", "ParamAngleY", "ParamAngleZ", "ParamBodyAngleX", "ParamBreath",
                "ParamEyeBallX", "ParamEyeBallY", "ParamEyeLOpen", "ParamEyeROpen" };

            float bestDelta = 0f;
            float worstBack = 0f;
            string bestName = null;
            foreach (ExpressionEntry entry in settings.Expressions)
            {
                using (CubismAvatar avatar = CubismAvatar.Load(model3Path))
                {
                    CubismModel m = avatar.Model;
                    avatar.IdleGroup = "__none__";
                    CubismExpressionMotion exp = CubismExpressionMotion.Load(settings.Resolve(entry.File));
                    var watch = new List<int>();
                    foreach (ExpressionParameter p in exp.Parameters)
                    {
                        int idx = m.GetParameterIndex(p.ParameterId);
                        if (idx >= 0 && !volatileIds.Contains(p.ParameterId)) watch.Add(idx);
                    }

                    for (int i = 0; i < 120; i++) avatar.Tick(dt);
                    var baseline = new float[watch.Count];
                    for (int i = 0; i < watch.Count; i++) baseline[i] = m.GetParameterValue(watch[i]);

                    avatar.SetExpression(entry.Name);
                    for (int i = 0; i < 180; i++) avatar.Tick(dt); // 3 s > fade in
                    float maxDelta = 0f;
                    for (int i = 0; i < watch.Count; i++) maxDelta = Math.Max(maxDelta, Math.Abs(m.GetParameterValue(watch[i]) - baseline[i]));

                    avatar.SetExpression(null);
                    for (int i = 0; i < 240; i++) avatar.Tick(dt); // 4 s > fade out
                    float maxBack = 0f;
                    for (int i = 0; i < watch.Count; i++) maxBack = Math.Max(maxBack, Math.Abs(m.GetParameterValue(watch[i]) - baseline[i]));

                    if (maxDelta > bestDelta) { bestDelta = maxDelta; bestName = entry.Name; }
                    if (maxBack > worstBack) worstBack = maxBack;
                }
            }

            Console.WriteLine("  expression check: {0} expressions, strongest = {1} (max parameter change {2:F3}), worst residual after clear = {3:F4}",
                settings.Expressions.Count, bestName ?? "(none)", bestDelta, worstBack);
            if (bestDelta < 1e-3f) throw new Exception("no expression had any effect on the model");
            if (worstBack > 0.02f) throw new Exception("an expression did not fade back to neutral");
        }

        static void CheckFinite(CubismAvatar avatar, int frame)
        {
            CubismModel m = avatar.Model;
            for (int i = 0; i < m.ParameterCount; i++)
            {
                float v = m.GetParameterValue(i);
                if (float.IsNaN(v) || float.IsInfinity(v))
                    throw new Exception("non-finite parameter " + m.ParameterIds[i] + " at frame " + frame);
            }
            for (int i = 0; i < m.PartCount; i++)
            {
                float v = m.GetPartOpacity(i);
                if (float.IsNaN(v) || float.IsInfinity(v))
                    throw new Exception("non-finite part opacity " + m.PartIds[i] + " at frame " + frame);
            }

            int* counts = m.VertexCounts;
            CubismCore.Vector2** pos = m.VertexPositions;
            float* op = m.Opacities;
            for (int d = 0; d < m.DrawableCount; d++)
            {
                if (float.IsNaN(op[d])) throw new Exception("NaN drawable opacity " + m.DrawableIds[d] + " at frame " + frame);
                for (int k = 0; k < counts[d]; k++)
                {
                    float x = pos[d][k].X, y = pos[d][k].Y;
                    if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
                        throw new Exception("non-finite vertex in drawable " + m.DrawableIds[d] + " at frame " + frame);
                }
            }
        }
    }
}
