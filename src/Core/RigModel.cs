using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PartnerLive2D.Framework;

namespace PartnerLive2D.Core
{
    // Managed rig evaluator with stable unmanaged buffers matching the Cubism drawable API.
    public sealed unsafe class RigModel : CubismModel
    {
        sealed class Binding
        {
            public int Parameter, Stride;
            public float[] Keys;
        }

        sealed class Forms
        {
            public Binding[] Bindings;
            public float[][] Values;
            public float[] Result, Weights;

            public void Evaluate(float* parameters)
            {
                Array.Clear(Result, 0, Result.Length);
                Array.Clear(Weights, 0, Weights.Length);
                Weights[0] = 1;
                int count = 1;
                for (int b = 0; b < Bindings.Length; b++)
                {
                    Binding binding = Bindings[b];
                    float[] keys = binding.Keys;
                    float value = parameters[binding.Parameter];
                    int lo = 0;
                    float t = 0;
                    if (keys.Length > 1)
                    {
                        if (value >= keys[keys.Length - 1]) { lo = keys.Length - 2; t = 1; }
                        else if (value > keys[0])
                        {
                            while (value >= keys[lo + 1]) lo++;
                            t = (value - keys[lo]) / (keys[lo + 1] - keys[lo]);
                        }
                    }
                    // Expand from high indices down so lower-dimensional weights are not overwritten.
                    for (int i = count - 1; i >= 0; i--)
                    {
                        float weight = Weights[i];
                        Weights[i] = 0;
                        Weights[i + lo * binding.Stride] = weight * (1 - t);
                        if (keys.Length > 1) Weights[i + (lo + 1) * binding.Stride] = weight * t;
                    }
                    count *= keys.Length;
                }
                for (int k = 0; k < Values.Length; k++)
                {
                    float weight = Weights[k];
                    if (weight == 0) continue;
                    float[] form = Values[k];
                    for (int f = 0; f < Result.Length; f++) Result[f] += form[f] * weight;
                }
            }
        }

        sealed class Deformer
        {
            public bool Warp;
            public int Parent, Rows, Cols;
            public float X, Y, Width, Height;
            public Forms Forms;
            public float Cos, Sin, Opacity;

            public void Apply(ref float x, ref float y)
            {
                float[] f = Forms.Result;
                if (!Warp)
                {
                    float dx = x - X, dy = y - Y;
                    x = X + f[1] + f[3] * (Cos * dx - Sin * dy);
                    y = Y + f[2] + f[3] * (Sin * dx + Cos * dy);
                    return;
                }
                float u = (x - X) / Width * Cols, v = (y - Y) / Height * Rows;
                int c = (int)Math.Max(0, Math.Min(Cols - 1, Math.Floor(u)));
                int r = (int)Math.Max(0, Math.Min(Rows - 1, Math.Floor(v)));
                float fu = u - c, fv = v - r;
                int a = 2 * (r * (Cols + 1) + c), d = a + 2 * (Cols + 1);
                x = (1 - fu) * (1 - fv) * f[a] + fu * (1 - fv) * f[a + 2]
                    + (1 - fu) * fv * f[d] + fu * fv * f[d + 2];
                y = (1 - fu) * (1 - fv) * f[a + 1] + fu * (1 - fv) * f[a + 3]
                    + (1 - fu) * fv * f[d + 1] + fu * fv * f[d + 3];
            }
        }

        sealed class Mesh
        {
            public float[] Vertices, Depths;
            public int[] Deformers, Parts;
            public Forms Forms;
            public int Order;
        }

        sealed class Camera
        {
            public int ParamX, ParamY, ParamZ;
            public double Focal, CenterX, CenterY, OrbitPx, DollyPx;
        }

        readonly List<IntPtr> _allocations = new List<IntPtr>();
        Deformer[] _deformers;
        Mesh[] _meshes;
        Camera _camera;
        int[] _sort;
        bool _disposed;
        float _originXPixels, _originYPixels;
        byte* _constantFlags, _dynamicFlags;
        int* _textures, _orders, _maskCounts, _vertexCounts, _indexCounts;
        float* _opacities;
        int** _masks;
        CubismCore.Vector2** _positions, _uvs;
        ushort** _indices;
        CubismCore.Vector4* _multiply, _screen;

        public override byte* ConstantFlags { get { return _constantFlags; } }
        public override byte* DynamicFlags { get { return _dynamicFlags; } }
        public override int* TextureIndices { get { return _textures; } }
        public override int* RenderOrders { get { return _orders; } }
        public override float* Opacities { get { return _opacities; } }
        public override int* MaskCounts { get { return _maskCounts; } }
        public override int** Masks { get { return _masks; } }
        public override int* VertexCounts { get { return _vertexCounts; } }
        public override CubismCore.Vector2** VertexPositions { get { return _positions; } }
        public override CubismCore.Vector2** VertexUvs { get { return _uvs; } }
        public override int* IndexCounts { get { return _indexCounts; } }
        public override ushort** Indices { get { return _indices; } }
        public override CubismCore.Vector4* MultiplyColors { get { return _multiply; } }
        public override CubismCore.Vector4* ScreenColors { get { return _screen; } }

        public RigModel(string json)
        {
            try { Load(Json.Parse(json)); Update(); }
            catch { Dispose(); throw; }
        }

        void* Allocate(int count, int size)
        {
            int bytes = checked(count * size);
            if (bytes == 0) return null;
            IntPtr memory = Marshal.AllocHGlobal(bytes);
            _allocations.Add(memory);
            byte* p = (byte*)memory;
            for (int i = 0; i < bytes; i++) p[i] = 0;
            return p;
        }

        static List<object> List(object node, string field)
        {
            return Json.GetList(node, field) ?? new List<object>();
        }

        static float[] Numbers(object node, string field)
        {
            var list = List(node, field);
            float[] result = new float[list.Count];
            for (int i = 0; i < result.Length; i++)
            {
                if (!(list[i] is double)) throw new FormatException(field + " must contain numbers");
                result[i] = (float)(double)list[i];
                if (float.IsNaN(result[i]) || float.IsInfinity(result[i])) throw new FormatException(field + " must be finite");
            }
            return result;
        }

        static string[] Ids(List<object> nodes)
        {
            string[] ids = new string[nodes.Count];
            var seen = new HashSet<string>();
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = Json.GetString(nodes[i], "Id");
                if (string.IsNullOrEmpty(ids[i]) || !seen.Add(ids[i])) throw new FormatException("Missing or duplicate Id");
            }
            return ids;
        }

        static int Reference(object node, string field, string[] ids)
        {
            string id = Json.GetString(node, field);
            if (id == null) return -1;
            int index = Array.IndexOf(ids, id);
            if (index < 0) throw new FormatException("Unknown " + field + ": " + id);
            return index;
        }

        static int[] Chain(int start, int[] parents)
        {
            var chain = new List<int>();
            while (start >= 0)
            {
                if (chain.Contains(start)) throw new FormatException("Parent cycle");
                chain.Add(start);
                start = parents[start];
            }
            return chain.ToArray();
        }

        Forms ReadForms(object node, float[] defaults, string geometry)
        {
            var bindings = List(node, "Bindings");
            var result = new Forms { Bindings = new Binding[bindings.Count], Result = new float[defaults.Length] };
            int count = 1;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = new Binding { Parameter = GetParameterIndex(Json.GetString(bindings[i], "Parameter")),
                    Keys = Numbers(bindings[i], "Keys"), Stride = count };
                if (b.Parameter < 0 || b.Keys.Length == 0) throw new FormatException("Invalid binding");
                for (int k = 1; k < b.Keys.Length; k++)
                    if (b.Keys[k] <= b.Keys[k - 1]) throw new FormatException("Keys must be strictly increasing");
                result.Bindings[i] = b;
                count = checked(count * b.Keys.Length);
            }
            var forms = List(node, "Keyforms");
            if (forms.Count != count && !(bindings.Count == 0 && forms.Count == 0))
                throw new FormatException("Keyform count does not match binding grid");
            result.Values = new float[count][];
            result.Weights = new float[count];
            for (int k = 0; k < count; k++)
            {
                float[] values = (float[])defaults.Clone();
                object form = forms.Count == 0 ? null : forms[k];
                if (geometry != null)
                {
                    if (Json.Get(form, geometry) != null)
                    {
                        float[] coords = Numbers(form, geometry);
                        if (coords.Length != defaults.Length - (geometry == "Offsets" ? 2 : 1))
                            throw new FormatException("Wrong " + geometry + " length");
                        Array.Copy(coords, values, coords.Length);
                    }
                    int opacity = defaults.Length - (geometry == "Offsets" ? 2 : 1);
                    values[opacity] = Json.GetFloat(form, "Opacity", defaults[opacity]);
                    if (geometry == "Offsets") values[opacity + 1] = Json.GetFloat(form, "DrawOrder", defaults[opacity + 1]);
                }
                else
                {
                    string[] fields = { "Angle", "X", "Y", "Scale", "Opacity" };
                    for (int i = 0; i < fields.Length; i++) values[i] = Json.GetFloat(form, fields[i], defaults[i]);
                }
                result.Values[k] = values;
            }
            return result;
        }

        void Load(object root)
        {
            int version = Json.GetInt(root, "Version");
            if (version != 1 && version != 2) throw new FormatException("Unsupported rig version");
            object canvas = Json.Get(root, "Canvas");
            PixelsPerUnit = Json.GetFloat(canvas, "PixelsPerUnit");
            if (PixelsPerUnit <= 0) throw new FormatException("PixelsPerUnit must be positive");
            CanvasWidth = Json.GetFloat(canvas, "Width") / PixelsPerUnit;
            CanvasHeight = Json.GetFloat(canvas, "Height") / PixelsPerUnit;
            _originXPixels = Json.GetFloat(canvas, "OriginX");
            _originYPixels = Json.GetFloat(canvas, "OriginY");
            OriginX = _originXPixels / PixelsPerUnit;
            OriginY = _originYPixels / PixelsPerUnit;
            var parameters = List(root, "Parameters");
            var parts = List(root, "Parts");
            var meshes = List(root, "Meshes");
            var deformers = List(root, "Deformers");
            ParameterCount = parameters.Count; PartCount = parts.Count; DrawableCount = meshes.Count;
            ParameterIds = Ids(parameters); PartIds = Ids(parts); DrawableIds = Ids(meshes);
            InitializeIds();
            _paramValues = (float*)Allocate(ParameterCount, sizeof(float));
            _paramMin = (float*)Allocate(ParameterCount, sizeof(float));
            _paramMax = (float*)Allocate(ParameterCount, sizeof(float));
            _paramDefault = (float*)Allocate(ParameterCount, sizeof(float));
            _partOpacities = (float*)Allocate(PartCount, sizeof(float));
            for (int i = 0; i < ParameterCount; i++)
            {
                _paramMin[i] = Json.GetFloat(parameters[i], "Min");
                _paramMax[i] = Json.GetFloat(parameters[i], "Max");
                _paramDefault[i] = Json.GetFloat(parameters[i], "Default");
                if (_paramMin[i] > _paramMax[i] || _paramDefault[i] < _paramMin[i] || _paramDefault[i] > _paramMax[i])
                    throw new FormatException("Invalid parameter range/default");
                _paramValues[i] = _paramDefault[i];
            }
            object camera = version == 2 ? Json.Get(root, "Camera") : null;
            if (camera != null)
            {
                float[] center = Numbers(camera, "Center");
                if (center.Length != 2) throw new FormatException("Invalid camera Center");
                _camera = new Camera {
                    Focal = Json.GetFloat(camera, "Focal"), CenterX = center[0], CenterY = center[1],
                    OrbitPx = Json.GetFloat(camera, "OrbitPx"), DollyPx = Json.GetFloat(camera, "DollyPx"),
                    ParamX = GetParameterIndex(Json.GetString(camera, "ParamX")),
                    ParamY = GetParameterIndex(Json.GetString(camera, "ParamY")),
                    ParamZ = GetParameterIndex(Json.GetString(camera, "ParamZ"))
                };
            }
            int[] partParents = new int[PartCount];
            for (int i = 0; i < PartCount; i++)
            {
                _partOpacities[i] = Json.GetFloat(parts[i], "Opacity", 1);
                partParents[i] = Reference(parts[i], "Parent", PartIds);
            }
            for (int i = 0; i < PartCount; i++) Chain(i, partParents); // validate even unused parts
            string[] deformerIds = Ids(deformers);
            int[] parents = new int[deformers.Count];
            _deformers = new Deformer[deformers.Count];
            for (int i = 0; i < deformers.Count; i++)
            {
                object node = deformers[i];
                string type = Json.GetString(node, "Type");
                if (type != "Warp" && type != "Rotation") throw new FormatException("Unknown deformer Type");
                var d = new Deformer { Warp = type == "Warp", Parent = Reference(node, "Parent", deformerIds) };
                if (d.Parent >= i) throw new FormatException("Deformer parents must precede children");
                parents[i] = d.Parent;
                if (d.Warp)
                {
                    float[] rect = Numbers(node, "Rect");
                    d.Rows = Json.GetInt(node, "Rows"); d.Cols = Json.GetInt(node, "Cols");
                    if (rect.Length != 4 || rect[2] <= 0 || rect[3] <= 0 || d.Rows <= 0 || d.Cols <= 0)
                        throw new FormatException("Invalid warp grid");
                    d.X = rect[0]; d.Y = rect[1]; d.Width = rect[2]; d.Height = rect[3];
                    float[] rest = new float[checked(2 * (d.Rows + 1) * (d.Cols + 1) + 1)];
                    for (int r = 0; r <= d.Rows; r++) for (int c = 0; c <= d.Cols; c++)
                    {
                        int p = 2 * (r * (d.Cols + 1) + c);
                        rest[p] = d.X + c * d.Width / d.Cols; rest[p + 1] = d.Y + r * d.Height / d.Rows;
                    }
                    rest[rest.Length - 1] = 1;
                    d.Forms = ReadForms(node, rest, "Points");
                }
                else
                {
                    float[] origin = Numbers(node, "Origin");
                    if (origin.Length != 2) throw new FormatException("Invalid rotation Origin");
                    d.X = origin[0]; d.Y = origin[1];
                    d.Forms = ReadForms(node, new float[] { 0, 0, 0, 1, 1 }, null);
                }
                _deformers[i] = d;
            }
            _constantFlags = (byte*)Allocate(DrawableCount, 1); _dynamicFlags = (byte*)Allocate(DrawableCount, 1);
            _textures = (int*)Allocate(DrawableCount, sizeof(int)); _orders = (int*)Allocate(DrawableCount, sizeof(int));
            _opacities = (float*)Allocate(DrawableCount, sizeof(float));
            _maskCounts = (int*)Allocate(DrawableCount, sizeof(int)); _masks = (int**)Allocate(DrawableCount, IntPtr.Size);
            _vertexCounts = (int*)Allocate(DrawableCount, sizeof(int)); _indexCounts = (int*)Allocate(DrawableCount, sizeof(int));
            _positions = (CubismCore.Vector2**)Allocate(DrawableCount, IntPtr.Size);
            _uvs = (CubismCore.Vector2**)Allocate(DrawableCount, IntPtr.Size);
            _indices = (ushort**)Allocate(DrawableCount, IntPtr.Size);
            _multiply = (CubismCore.Vector4*)Allocate(DrawableCount, sizeof(CubismCore.Vector4));
            _screen = (CubismCore.Vector4*)Allocate(DrawableCount, sizeof(CubismCore.Vector4));
            _meshes = new Mesh[DrawableCount]; _sort = new int[DrawableCount];
            for (int i = 0; i < DrawableCount; i++)
            {
                object node = meshes[i];
                var m = new Mesh { Vertices = Numbers(node, "Vertices"),
                    Deformers = Chain(Reference(node, "Parent", deformerIds), parents),
                    Parts = Chain(Reference(node, "Part", PartIds), partParents) };
                int count = m.Vertices.Length / 2;
                float[] uvs = Numbers(node, "Uvs"), indices = Numbers(node, "Indices");
                if (m.Vertices.Length % 2 != 0 || count >= 65536 || uvs.Length != m.Vertices.Length || indices.Length % 3 != 0)
                    throw new FormatException("Invalid mesh geometry");
                if (version == 2 && Json.Get(node, "Depths") != null)
                {
                    m.Depths = Numbers(node, "Depths");
                    if (m.Depths.Length != count) throw new FormatException("Depths must have one value per vertex");
                }
                _vertexCounts[i] = count; _indexCounts[i] = indices.Length;
                _positions[i] = (CubismCore.Vector2*)Allocate(count, sizeof(CubismCore.Vector2));
                _uvs[i] = (CubismCore.Vector2*)Allocate(count, sizeof(CubismCore.Vector2));
                _indices[i] = (ushort*)Allocate(indices.Length, sizeof(ushort));
                for (int v = 0; v < count; v++) { _uvs[i][v].X = uvs[2 * v]; _uvs[i][v].Y = uvs[2 * v + 1]; }
                for (int k = 0; k < indices.Length; k++)
                {
                    if (indices[k] < 0 || indices[k] >= count || indices[k] != Math.Floor(indices[k])) throw new FormatException("Invalid triangle index");
                    _indices[i][k] = (ushort)indices[k];
                }
                var masks = List(node, "Masks");
                _maskCounts[i] = masks.Count; _masks[i] = (int*)Allocate(masks.Count, sizeof(int));
                for (int k = 0; k < masks.Count; k++)
                {
                    int mask = Array.IndexOf(DrawableIds, masks[k] as string);
                    if (mask < 0) throw new FormatException("Unknown mask");
                    _masks[i][k] = mask;
                }
                _textures[i] = Json.GetInt(node, "Texture");
                _constantFlags[i] = CubismCore.IsDoubleSided;
                string blend = Json.GetString(node, "Blend", "Normal");
                if (blend == "Additive") _constantFlags[i] |= CubismCore.BlendAdditive;
                else if (blend == "Multiplicative") _constantFlags[i] |= CubismCore.BlendMultiplicative;
                else if (blend != "Normal") throw new FormatException("Unknown Blend");
                if (Json.GetBool(node, "InvertMask")) _constantFlags[i] |= CubismCore.IsInvertedMask;
                _multiply[i] = new CubismCore.Vector4 { X = 1, Y = 1, Z = 1, W = 1 };
                _screen[i] = new CubismCore.Vector4 { W = 1 };
                float[] defaults = new float[m.Vertices.Length + 2];
                defaults[defaults.Length - 2] = 1; defaults[defaults.Length - 1] = Json.GetFloat(node, "DrawOrder");
                m.Forms = ReadForms(node, defaults, "Offsets");
                _meshes[i] = m;
            }
        }

        public override void Update()
        {
            if (_disposed) throw new ObjectDisposedException("RigModel");
            double tx = 0, ty = 0, distance = 0, cameraScale = 0;
            bool project = false;
            if (_camera != null)
            {
                double x = _camera.ParamX < 0 ? 0 : _paramValues[_camera.ParamX];
                double y = _camera.ParamY < 0 ? 0 : _paramValues[_camera.ParamY];
                double w = _camera.ParamZ < 0 ? 0 : _paramValues[_camera.ParamZ];
                // Preserve the rest pose exactly, including vertices at the denominator clamp.
                project = x != 0 || y != 0 || w != 0;
                tx = x * _camera.OrbitPx; ty = y * _camera.OrbitPx;
                distance = _camera.Focal - w * _camera.DollyPx;
                if (project) cameraScale = _camera.Focal / distance;
            }
            for (int i = 0; i < _deformers.Length; i++)
            {
                Deformer d = _deformers[i];
                d.Forms.Evaluate(_paramValues);
                float[] f = d.Forms.Result;
                d.Opacity = f[f.Length - 1];
                if (!d.Warp) { double radians = f[0] * Math.PI / 180; d.Cos = (float)Math.Cos(radians); d.Sin = (float)Math.Sin(radians); }
            }
            for (int i = 0; i < DrawableCount; i++)
            {
                Mesh m = _meshes[i]; m.Forms.Evaluate(_paramValues);
                float[] f = m.Forms.Result;
                float opacity = f[f.Length - 2];
                for (int d = 0; d < m.Deformers.Length; d++) opacity *= _deformers[m.Deformers[d]].Opacity;
                for (int p = 0; p < m.Parts.Length; p++) opacity *= _partOpacities[m.Parts[p]];
                _opacities[i] = opacity;
                _dynamicFlags[i] = (byte)(CubismCore.VertexPositionsDidChange | CubismCore.OpacityDidChange
                    | CubismCore.DrawOrderDidChange | CubismCore.RenderOrderDidChange | (opacity > 0 ? CubismCore.IsVisible : 0));
                m.Order = (int)Math.Round(f[f.Length - 1], MidpointRounding.ToEven);
                for (int v = 0; v < _vertexCounts[i]; v++)
                {
                    float x = m.Vertices[2 * v] + f[2 * v], y = m.Vertices[2 * v + 1] + f[2 * v + 1];
                    for (int d = 0; d < m.Deformers.Length; d++) _deformers[m.Deformers[d]].Apply(ref x, ref y);
                    if (project)
                    {
                        double z = m.Depths == null ? 0 : m.Depths[v];
                        double ratio = distance / Math.Max(1, distance - z);
                        double restRatio = _camera.Focal / Math.Max(1, _camera.Focal - z);
                        double dx = x - _camera.CenterX, dy = y - _camera.CenterY;
                        // p + proj(p; t,D) - proj(p; 0,Focal); depth stays in canvas pixels.
                        x = (float)(x + (tx + (dx - tx) * ratio) * cameraScale - dx * restRatio);
                        y = (float)(y + (ty + (dy - ty) * ratio) * cameraScale - dy * restRatio);
                    }
                    _positions[i][v].X = (x - _originXPixels) / PixelsPerUnit;
                    _positions[i][v].Y = (_originYPixels - y) / PixelsPerUnit;
                }
                // Stable insertion sort avoids comparer/delegate allocations and preserves array-index ties.
                int j = i;
                while (j > 0 && _meshes[_sort[j - 1]].Order > m.Order) { _sort[j] = _sort[j - 1]; j--; }
                _sort[j] = i;
            }
            for (int rank = 0; rank < DrawableCount; rank++) _orders[_sort[rank]] = rank;
        }

        public override void Dispose()
        {
            if (_disposed) return;
            for (int i = 0; i < _allocations.Count; i++) Marshal.FreeHGlobal(_allocations[i]);
            _allocations.Clear();
            _disposed = true;
        }
    }
}
