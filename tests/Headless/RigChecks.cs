using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PartnerLive2D.Core;
using PartnerLive2D.Framework;
using Json = PartnerLive2D.Framework.Json;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void Near(float actual, float expected, string label)
        {
            if (float.IsNaN(actual) || Math.Abs(actual - expected) > 1e-4f)
                throw new Exception(label + ": expected " + expected + ", got " + actual);
        }

        static void Require(bool value, string label)
        {
            if (!value) throw new Exception(label);
        }

        static void RigChecks()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "rig_fixture", "test.model3.json");
            using (var avatar = CubismAvatar.Load(fixture))
            {
                CubismModel m = avatar.Model;
                Require(m is RigModel, "rig backend selection");
                Require(m.ParameterCount == 2 && m.PartCount == 2 && m.DrawableCount == 2, "rig counts");
                Near(m.CanvasWidth, 2, "canvas width"); Near(m.CanvasHeight, 2, "canvas height");
                Near(m.OriginX, .5f, "origin x"); Near(m.OriginY, .5f, "origin y"); Near(m.PixelsPerUnit, 10, "ppu");
                Require(m.GetParameterIndex("missing") == -1 && m.GetPartIndex(null) == -1 && m.GetDrawableIndex("missing") == -1, "id lookup");
                Near(m.GetParameterMinimum(1), -1, "min"); Near(m.GetParameterMaximum(0), 1, "max"); Near(m.GetParameterDefault(0), 0, "default");

                // Hand calculation: warp at A is (x + A*x*y/10, y + A*x/2).
                // Offsets=(2A,B); rotation about (5,5) uses angle=90B, translation=(2B,0).
                // Convert to ((x-5)/10,(5-y)/10). Case 2 includes y=12,17 outside warp rect;
                // case 3 blends both bindings and the rotation between 0 and 90 degrees.
                float[][] expected = {
                    new float[] {0,0, .5f,0, 1,-.5f},
                    new float[] {.52f,-.5f, .82f,-1, -.23f,-1.25f},
                    new float[] {.327888979f,-.264904852f, .963396734f,-.706846590f, 1.313028769f,-1.413953371f}
                };
                float[] a = { 0, 1, .5f }, b = { 0, 1, .5f }, opacity = { .4f, .1f, .225f };
                for (int setting = 0; setting < 3; setting++)
                {
                    m.SetParameterValue("A", a[setting]); m.SetParameterValue("B", b[setting]); m.Update();
                    for (int v = 0; v < 3; v++)
                    {
                        Near(m.VertexPositions[0][v].X, expected[setting][v * 2], "vertex x setting " + setting);
                        Near(m.VertexPositions[0][v].Y, expected[setting][v * 2 + 1], "vertex y setting " + setting);
                    }
                    Near(m.Opacities[0], opacity[setting], "deformer and ancestor-part opacity product");
                    Near(m.Opacities[1], .8f * a[setting], "mesh opacity interpolation");
                    Require(m.RenderOrders[0] == (setting == 0 ? 1 : 0) && m.RenderOrders[1] == (setting == 0 ? 0 : 1), "ranks and array-index ties");
                    byte changed = CubismCore.VertexPositionsDidChange | CubismCore.OpacityDidChange | CubismCore.DrawOrderDidChange | CubismCore.RenderOrderDidChange;
                    Require(m.DynamicFlags[0] == (changed | CubismCore.IsVisible), "dynamic flags");
                    Require(m.DynamicFlags[1] == (changed | (setting == 0 ? 0 : CubismCore.IsVisible)), "visibility from final opacity");
                }
                Require(m.ConstantFlags[0] == (CubismCore.IsDoubleSided | CubismCore.BlendAdditive | CubismCore.IsInvertedMask), "additive/inverted flags");
                Require(m.ConstantFlags[1] == (CubismCore.IsDoubleSided | CubismCore.BlendMultiplicative), "multiplicative flag");
                Require(m.MaskCounts[0] == 1 && m.Masks[0][0] == 1 && m.MaskCounts[1] == 0, "mask pointers");
                Require(m.VertexCounts[0] == 3 && m.IndexCounts[0] == 3 && m.Indices[0][2] == 2, "geometry pointers");
                Require(m.TextureIndices[0] == 0, "texture index");
                Near(m.VertexUvs[0][0].Y, 1, "UVs retained, no extra flip");
                Near(m.MultiplyColors[0].X, 1, "multiply red"); Near(m.MultiplyColors[0].W, 1, "multiply alpha");
                Near(m.ScreenColors[0].X, 0, "screen red"); Near(m.ScreenColors[0].W, 1, "screen alpha");
                m.SetParameterValue("A", 9); Near(m.GetParameterValue(0), 1, "clamp max");
                m.SetParameterValue("A", -9); Near(m.GetParameterValue(0), 0, "clamp min");
                m.AddParameterValue("A", .5f); m.MultiplyParameterValue("A", .5f); Near(m.GetParameterValue(0), .25f, "add/multiply string overloads");
                m.SaveParameters(); m.SetParameterValue(0, 1); m.LoadParameters(); Near(m.GetParameterValue(0), .25f, "save/load");
                m.SetParameterValue(0, 1, .5f); Near(m.GetParameterValue(0), .625f, "weighted set");
                m.SetPartOpacity(m.GetPartIndex("Root"), .4f); m.SetParameterValue("A", 0); m.SetParameterValue("B", 0); m.Update();
                Near(m.Opacities[0], .2f, "runtime ancestor opacity");
                m.SetPartOpacity(1, -5); m.Update(); Near(m.Opacities[0], 0, "part clamp");
                Require((m.DynamicFlags[0] & CubismCore.IsVisible) == 0, "hidden final opacity");
                for (int i = 0; i < 100; i++) m.Update();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) m.Update();
                Require(GC.GetAllocatedBytesForCurrentThread() == before, "RigModel.Update allocated managed memory");
            }
            RigEdgeChecks(fixture);
            RigCameraChecks();
            Console.WriteLine("RigChecks PASSED: v1 geometry/API, v2 camera identity/orbit/dolly/clamp/defaults/look and zero-allocation Update");
        }

        static void RigCameraChecks()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "rig_fixture_v2", "test.model3.json");
            string text = File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixture), "test.rig3.json"));
            RigCameraQaCases(text);
            using (var avatar = CubismAvatar.Load(fixture))
            {
                CubismModel m = avatar.Model;
                // Every DepthMesh vertex: rest (6,4) + offset (1,2) -> (7,6);
                // scale 2, translate (1,3) -> p=(15,15), after the deformer chain.
                // C=(5,5), F=100, PPU=10, origin=(5,5); neutral p -> (1,-1).
                // This holds for every z, even near the clamp; depths are never scaled by deformers.
                for (int v = 0; v < 6; v++)
                {
                    Near(m.VertexPositions[0][v].X, 1, "v2 neutral x");
                    Near(m.VertexPositions[0][v].Y, -1, "v2 neutral y");
                }
                m.SetParameterValue("ParamCamX", 1); m.Update();
                // z=50: D/(D-z)=2. tx=10, dx=10 -> proj.x=5+10=15;
                // rest proj.x=5+10*2=25; p'.x=15+15-25=5 -> X=0 (opposite +x).
                Near(m.VertexPositions[0][1].X, 0, "positive camera x moves near vertex left");
                Near(m.VertexPositions[0][1].Y, -1, "camera x leaves y unchanged");
                Near(m.VertexPositions[0][0].X, 1, "z=0 orbit unchanged");
                m.SetParameterValue("ParamCamX", 9); m.Update();
                Near(m.VertexPositions[0][1].X, 0, "camera uses clamped parameters");
                m.SetParameterValue("ParamCamX", 0); m.SetParameterValue("ParamCamY", 1); m.Update();
                Near(m.VertexPositions[0][1].X, 1, "camera y leaves x unchanged");
                Near(m.VertexPositions[0][1].Y, 0, "positive camera y moves near vertex up in canvas pixels");
                m.SetParameterValue("ParamCamY", 0); m.SetParameterValue("ParamCamZ", 1); m.Update();
                // w=1 -> D=80. z=0: p'=15+10*100/80-10=17.5 -> X=1.25.
                // z=50: p'=15+10*100/30-10*100/50=85/3 -> X=7/3.
                // z=79: D-z=1; p'=15+1000-1000/21 -> X=96.2380952381.
                // z=79.5: clamp .5 to 1; p'=15+1000-2000/41 -> X=96.1219512195.
                // z=80,90: clamp 0,-10 to 1; p'=965,915 -> X=96,91.
                float[] expected = { 1.25f, 7f / 3f, 96.2380952381f, 96.1219512195f, 96, 91 };
                for (int v = 0; v < expected.Length; v++)
                {
                    Near(m.VertexPositions[0][v].X, expected[v], "v2 dolly/clamp x vertex " + v);
                    Near(m.VertexPositions[0][v].Y, -expected[v], "v2 dolly/clamp y vertex " + v);
                }
                Require(m.VertexPositions[0][1].X > m.VertexPositions[0][0].X, "push-in magnifies near vertex more");
                Near(m.VertexPositions[1][0].X, 1.25f, "missing Depths means z=0");
                Near(m.VertexPositions[1][0].Y, -1.25f, "missing Depths y");
                Near(m.Opacities[0], .2f, "camera preserves opacity product");
                Require(m.RenderOrders[0] == 1 && m.RenderOrders[1] == 0, "camera preserves draw order");
                Require(m.MaskCounts[0] == 1 && m.Masks[0][0] == 1, "camera preserves masks");
                // Combined orbit+dolly: z=50, tx=10, ty=-5, D=80.
                // p'.x=15+10*1.25-20=7.5; p'.y=15+(-5+15*8/3)*1.25-20=38.75.
                m.SetParameterValue("ParamCamX", 1); m.SetParameterValue("ParamCamY", -.5f); m.Update();
                Near(m.VertexPositions[0][1].X, .25f, "combined camera x");
                Near(m.VertexPositions[0][1].Y, -3.375f, "combined camera y");
                for (int i = 0; i < 100; i++) m.Update();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) m.Update();
                Require(GC.GetAllocatedBytesForCurrentThread() == before, "camera Update allocated managed memory");
            }

            // Camera is optional in v2; v1 ignores the v2 additions, preserving all old geometry.
            var root = (Dictionary<string, object>)Json.Parse(text);
            root.Remove("Camera");
            using (var m = new RigModel(JsonSerializer.Serialize(root))) CheckNoCamera(m);
            root = (Dictionary<string, object>)Json.Parse(text);
            root["Version"] = 1.0;
            using (var m = new RigModel(JsonSerializer.Serialize(root))) CheckNoCamera(m);

            root = (Dictionary<string, object>)Json.Parse(text);
            var camera = (Dictionary<string, object>)root["Camera"];
            camera["ParamX"] = "missing"; camera.Remove("ParamY"); camera["ParamZ"] = "missing";
            using (var m = new RigModel(JsonSerializer.Serialize(root))) CheckNoCamera(m);
            root = (Dictionary<string, object>)Json.Parse(text);
            ((Dictionary<string, object>)Json.GetList(root, "Meshes")[0])["Depths"] = Json.Parse("[0]");
            bool rejected = false;
            try { using (var m = new RigModel(JsonSerializer.Serialize(root))) { } }
            catch (FormatException) { rejected = true; }
            Require(rejected, "depth count must match vertex count");

            using (var avatar = CubismAvatar.Load(fixture))
            {
                // Compare against the same drag smoother to verify factor 1 on each look axis.
                var drag = new CubismTargetPoint();
                drag.Set(.6f, -.4f); avatar.SetLookTarget(.6f, -.4f);
                for (int i = 0; i < 120; i++)
                {
                    drag.Update(1f / 60); avatar.Tick(1f / 60);
                    Near(avatar.Model.GetParameterValue(avatar.Model.GetParameterIndex("ParamCamX")), drag.X, "look camera x factor 1");
                    Near(avatar.Model.GetParameterValue(avatar.Model.GetParameterIndex("ParamCamY")), drag.Y, "look camera y factor 1");
                }
                Require(drag.X > .5f && drag.Y < -.3f, "look targets reached");
            }
            using (var avatar = CubismAvatar.Load(Path.Combine(AppContext.BaseDirectory, "rig_fixture", "test.model3.json")))
            {
                avatar.SetLookTarget(1, -1);
                for (int i = 0; i < 120; i++) avatar.Tick(1f / 60);
                Require(avatar.Model.ParameterCount == 2 && avatar.Model.GetParameterIndex("ParamCamX") == -1, "missing camera look ids remain absent");
                Near(avatar.Model.VertexPositions[0][1].X, .5f, "missing look ids preserve v1 geometry");
                Near(avatar.Model.VertexPositions[0][1].Y, 0, "missing look ids preserve v1 geometry y");
            }
        }

        static void RigCameraQaCases(string text)
        {
            // Independent QA cases retain the offset/deformer path: p=(15,15),
            // origin=(5,5), PPU=10. Expectations below are hand computed.
            var root = (Dictionary<string, object>)Json.Parse(text);
            root["Camera"] = Json.Parse("{\"Focal\":120,\"Center\":[3,-5],\"ParamX\":\"ParamCamX\",\"ParamY\":\"ParamCamY\",\"ParamZ\":\"ParamCamZ\",\"OrbitPx\":8,\"DollyPx\":40}");
            ((Dictionary<string, object>)Json.GetList(root, "Meshes")[0])["Depths"] = Json.Parse("[-20,-20,-20,-20,-20,-20]");
            using (var m = new RigModel(JsonSerializer.Serialize(root)))
            {
                Near(m.VertexPositions[0][0].X, 1, "QA negative depth neutral x");
                Near(m.VertexPositions[0][0].Y, -1, "QA negative depth neutral y");
                m.SetParameterValue("ParamCamX", .5f); m.SetParameterValue("ParamCamY", -.5f);
                m.SetParameterValue("ParamCamZ", .5f); m.Update();
                // z=-20, D=100, t=(4,-4), p-C=(12,20).
                // Current projection minus C=(64/5,96/5); rest=(72/7,120/7).
                // p'=(613/35,597/35); model=(219/175,-211/175).
                Near(m.VertexPositions[0][0].X, 219f / 175f, "QA negative depth combined x");
                Near(m.VertexPositions[0][0].Y, -211f / 175f, "QA negative depth combined y");
            }
            root["Camera"] = Json.Parse("{\"Focal\":60,\"Center\":[-5,10],\"ParamX\":\"ParamCamX\",\"ParamY\":\"ParamCamY\",\"ParamZ\":\"ParamCamZ\",\"OrbitPx\":12,\"DollyPx\":20}");
            ((Dictionary<string, object>)Json.GetList(root, "Meshes")[0])["Depths"] = Json.Parse("[30,30,30,30,30,30]");
            using (var m = new RigModel(JsonSerializer.Serialize(root)))
            {
                m.SetParameterValue("ParamCamX", -.5f); m.SetParameterValue("ParamCamY", .25f);
                m.SetParameterValue("ParamCamZ", -1); m.Update();
                // z=30, D=80 (pull-back), t=(-6,3), p-C=(20,5).
                // Current projection minus C=(267/10,93/20); rest=(40,10).
                // p'=(17/10,193/20); model=(-33/100,-93/200).
                Near(m.VertexPositions[0][0].X, -.33f, "QA pull-back combined x");
                Near(m.VertexPositions[0][0].Y, -.465f, "QA pull-back combined y");
            }
            Console.WriteLine("RigCamera QA PASSED: two independent hand-computed cases, negative depth and pull-back");
        }

        static void CheckNoCamera(CubismModel m)
        {
            m.SetParameterValue("ParamCamX", 1); m.SetParameterValue("ParamCamY", -1); m.SetParameterValue("ParamCamZ", 1); m.Update();
            for (int v = 0; v < 6; v++)
            {
                Near(m.VertexPositions[0][v].X, 1, "camera disabled/default parameter x");
                Near(m.VertexPositions[0][v].Y, -1, "camera disabled/default parameter y");
            }
        }

        static void RigEdgeChecks(string fixture)
        {
            string text = File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixture), "test.rig3.json"));
            var root = Json.Parse(text);
            var meshes = Json.GetList(root, "Meshes");
            var deformers = Json.GetList(root, "Deformers");
            // Single-key binding and missing optional fields must produce defaults at any parameter value.
            ((Dictionary<string, object>)deformers[0])["Bindings"] = Json.Parse("[{\"Parameter\":\"A\",\"Keys\":[0]}]");
            ((Dictionary<string, object>)deformers[0])["Keyforms"] = Json.Parse("[{}]");
            ((Dictionary<string, object>)deformers[1])["Bindings"] = Json.Parse("[]");
            ((Dictionary<string, object>)deformers[1]).Remove("Keyforms");
            ((Dictionary<string, object>)meshes[0]).Remove("Bindings");
            ((Dictionary<string, object>)meshes[0]).Remove("Keyforms");
            using (var m = new RigModel(JsonSerializer.Serialize(root)))
            {
                m.SetParameterValue("A", 1); m.SetParameterValue("B", -1); m.Update();
                Near(m.VertexPositions[0][2].X, 1, "rest-grid/default rotation/default offsets");
                Near(m.VertexPositions[0][2].Y, -.5f, "default geometry y");
                m.Dispose(); // idempotent disposal
            }
            // Exercise the negative-angle endpoint, including canvas-y-down clockwise convention.
            using (var m = new RigModel(text))
            {
                m.SetParameterValue("B", -1); m.Update();
                // v0=(5,4); relative=(0,-1); rotate -90 -> (-1,0); translate -2 -> (2,5).
                Near(m.VertexPositions[0][0].X, -.3f, "negative rotation endpoint x");
                Near(m.VertexPositions[0][0].Y, 0, "negative rotation endpoint y");
            }
            // Binding values outside their key interval clamp at the key endpoints.
            var keyed = Json.Parse(text);
            var keyedWarp = (Dictionary<string, object>)Json.GetList(keyed, "Deformers")[0];
            keyedWarp["Bindings"] = Json.Parse("[{\"Parameter\":\"A\",\"Keys\":[0.2,0.8]}]");
            using (var m = new RigModel(JsonSerializer.Serialize(keyed)))
            {
                Near(m.VertexPositions[0][0].X, 0, "below first binding key");
                m.SetParameterValue("A", 1); m.Update();
                // offset v0=(7,5), identity rotation, full warp -> (10.5,8.5).
                Near(m.VertexPositions[0][0].X, .55f, "above last binding key");
                Near(m.VertexPositions[0][0].Y, -.35f, "above last binding key y");
            }
            using (var m = new RigModel(text))
            {
                m.SetParameterValue("A", .475f); m.Update();
                Require(m.RenderOrders[0] == 0, "DrawOrder 10.5 rounds to even 10 and ties by index");
                m.SetParameterValue("A", .425f); m.Update();
                Require(m.RenderOrders[0] == 1, "DrawOrder 11.5 rounds to 12");
            }
            // Scale and Y translation are separate rotation fields.
            var rotate = (Dictionary<string, object>)deformers[1];
            rotate["Keyforms"] = Json.Parse("[{\"Scale\":2,\"Y\":3}]");
            using (var m = new RigModel(JsonSerializer.Serialize(root)))
            {
                Near(m.VertexPositions[0][1].X, 1, "rotation scale");
                Near(m.VertexPositions[0][1].Y, -.3f, "rotation Y translation");
            }
            // Representative load: 100 meshes x 300 vertices, four deformer levels.
            var mesh = (Dictionary<string, object>)meshes[0];
            mesh["Masks"] = Json.Parse("[]"); mesh["Parent"] = "Level4";
            var vertices = new List<object>(); var uvs = new List<object>(); var indices = new List<object>();
            for (int i = 0; i < 300; i++) { vertices.Add((double)(i % 20)); vertices.Add((double)(i / 20)); uvs.Add(0.0); uvs.Add(1.0); indices.Add((double)i); }
            mesh["Vertices"] = vertices; mesh["Uvs"] = uvs; mesh["Indices"] = indices;
            deformers.Add(Json.Parse("{\"Id\":\"Level3\",\"Parent\":\"Rotate\",\"Type\":\"Rotation\",\"Origin\":[0,0]}"));
            deformers.Add(Json.Parse("{\"Id\":\"Level4\",\"Parent\":\"Level3\",\"Type\":\"Rotation\",\"Origin\":[0,0]}"));
            meshes.Clear();
            for (int i = 0; i < 100; i++) { var copy = new Dictionary<string, object>(mesh); copy["Id"] = "Stress" + i; meshes.Add(copy); }
            using (var m = new RigModel(JsonSerializer.Serialize(root)))
            {
                for (int i = 0; i < 100; i++) m.Update();
                var watch = new Stopwatch();
                long before = GC.GetAllocatedBytesForCurrentThread();
                watch.Start();
                for (int i = 0; i < 300; i++) m.Update();
                watch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Require(allocated == 0, "stress Update allocation");
                Console.WriteLine("Rig stress: 100 meshes x 300 vertices x 4 deformers, {0:F3} ms/frame, {1} managed bytes", watch.Elapsed.TotalMilliseconds / 300, allocated);
            }
        }

        static void RigDump(string modelPath, string paramsPath)
        {
            using (var avatar = CubismAvatar.Load(modelPath))
            {
                CubismModel m = avatar.Model;
                var parameters = Json.ParseFile(paramsPath) as Dictionary<string, object>;
                if (parameters == null) throw new FormatException("params.json must be an object");
                foreach (var pair in parameters)
                {
                    if (!(pair.Value is double)) throw new FormatException("Parameter values must be numbers");
                    m.SetParameterValue(pair.Key, (float)(double)pair.Value);
                }
                m.Update();
                var meshes = new Dictionary<string, object>();
                for (int d = 0; d < m.DrawableCount; d++)
                {
                    var verts = new float[m.VertexCounts[d] * 2];
                    for (int v = 0; v < m.VertexCounts[d]; v++) { verts[2 * v] = m.VertexPositions[d][v].X; verts[2 * v + 1] = m.VertexPositions[d][v].Y; }
                    meshes.Add(m.DrawableIds[d], new { opacity = m.Opacities[d], order = m.RenderOrders[d], verts = verts });
                }
                Console.WriteLine(JsonSerializer.Serialize(new { meshes = meshes }));
            }
        }
    }
}
