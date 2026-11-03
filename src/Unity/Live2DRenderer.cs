using System;
using System.Collections.Generic;
using System.IO;
using PartnerLive2D.Core;
using PartnerLive2D.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace PartnerLive2D.Unity
{
    // Draws a Cubism model into an offscreen RenderTexture with nothing but the game's
    // built-in shaders (the game ships no Live2D shaders and we cannot compile new ones):
    //
    //   pass 1  "Sprites/Default"  -> premultiplied colour AND correct alpha for normal drawables
    //   pass 2  black quad, UI/Default with ColorMask RGB -> wipes colour, keeps alpha
    //   pass 3  "UI/Default"  ColorMask RGB -> straight-alpha blending over black = premultiplied
    //           colour; clipping masks via stencil (mask meshes write ref 1 with alpha clip,
    //           masked drawable tests Equal / NotEqual), additive & multiply via legacy
    //           particle shaders (they already use ColorMask RGB).
    //
    // The result is a premultiplied-alpha texture, shown with a premultiplied blend shader.
    public sealed unsafe class Live2DRenderer : IDisposable
    {
        readonly CubismAvatar _avatar;
        readonly CubismModel _m;
        readonly Texture2D[] _textures;
        readonly RenderTexture _rt;
        readonly CommandBuffer _cb = new CommandBuffer { name = "PartnerLive2D" };
        readonly Matrix4x4 _proj;

        readonly Mesh[] _meshes;        // colour = (multiply rgb, opacity)
        readonly Mesh[] _maskMeshes;    // same geometry, opaque white; only for drawables used as masks
        readonly Vector3[][] _verts;
        readonly Color[][] _colors;
        readonly int[] _order;          // drawable indices sorted by render order
        readonly int[] _orderKeys;
        readonly Mesh _quad;

        readonly Material[] _alphaMat, _normalMat, _maskWriteMat, _maskedEqMat, _maskedNeMat, _addMat, _mulMat;
        readonly Material _blackMat;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        public RenderTexture Texture => _rt;
        public float Aspect { get; private set; }   // width / height of the drawn area

        public Live2DRenderer(CubismAvatar avatar, int renderSize)
        {
            _avatar = avatar;
            _m = avatar.Model;
            int n = _m.DrawableCount;

            _textures = new Texture2D[avatar.TexturePaths.Length];
            for (int t = 0; t < _textures.Length; t++)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                tex.LoadImage(File.ReadAllBytes(avatar.TexturePaths[t]), true); // immutable source atlas; release CPU copy
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 4;
                _textures[t] = Own(tex);
            }

            // geometry
            _meshes = new Mesh[n];
            _maskMeshes = new Mesh[n];
            _verts = new Vector3[n][];
            _colors = new Color[n][];
            var usedAsMask = new bool[n];
            var maskCounts = _m.MaskCounts;
            var masks = _m.Masks;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < maskCounts[i]; k++)
                    usedAsMask[masks[i][k]] = true;

            var vcount = _m.VertexCounts;
            var uvs = _m.VertexUvs;
            var icount = _m.IndexCounts;
            var idx = _m.Indices;
            for (int i = 0; i < n; i++)
            {
                int vc = vcount[i];
                _verts[i] = new Vector3[vc];
                _colors[i] = new Color[vc];
                var uv = new Vector2[vc];
                for (int v = 0; v < vc; v++) uv[v] = new Vector2(uvs[i][v].X, uvs[i][v].Y);
                var tris = new int[icount[i]];
                for (int t = 0; t < tris.Length; t++) tris[t] = idx[i][t];

                _meshes[i] = Own(NewMesh(_m.DrawableIds[i], uv, tris));
                if (usedAsMask[i])
                {
                    _maskMeshes[i] = Own(NewMesh(_m.DrawableIds[i] + "_mask", uv, tris));
                    var white = new Color[vc];
                    for (int v = 0; v < vc; v++) white[v] = Color.white;
                    _maskMeshes[i].vertices = new Vector3[vc];
                    _maskMeshes[i].colors = white;
                }
            }
            _order = new int[n];
            _orderKeys = new int[n];

            // fit the projection to the model at rest, with headroom for motions and physics
            UploadAll(true);
            var b = ComputeBounds();
            // headroom for motions and physics; rig3 v2 models with a camera also need room for push-in
            bool hasCam = Array.IndexOf(_m.ParameterIds, "ParamCamZ") >= 0;
            float padX = b.width * (hasCam ? 0.16f : 0.12f), padY = b.height * (hasCam ? 0.12f : 0.06f);
            b = new Rect(b.xMin - padX, b.yMin - padY, b.width + 2 * padX, b.height + 2 * padY);
            Aspect = b.width / b.height;
            _proj = Matrix4x4.Ortho(b.xMin, b.xMax, b.yMin, b.yMax, -1f, 1f);

            int w, h;
            if (Aspect >= 1f) { w = renderSize; h = Mathf.Max(16, Mathf.RoundToInt(renderSize / Aspect)); }
            else { h = renderSize; w = Mathf.Max(16, Mathf.RoundToInt(renderSize * Aspect)); }
            _rt = Own(new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "PartnerLive2D_RT", antiAliasing = 1, useMipMap = false });
            _rt.Create();

            _quad = Own(new Mesh());
            _quad.vertices = new[] { new Vector3(b.xMin, b.yMin), new Vector3(b.xMin, b.yMax), new Vector3(b.xMax, b.yMax), new Vector3(b.xMax, b.yMin) };
            _quad.colors = new[] { Color.black, Color.black, Color.black, Color.black };
            _quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };

            // materials
            var ui = FindShader("UI/Default");
            var sprite = FindShader("Sprites/Default");
            var add = FindShader("Legacy Shaders/Particles/Additive");
            var mul = FindShader("Legacy Shaders/Particles/Multiply");
            int tc = _textures.Length;
            _alphaMat = new Material[tc]; _normalMat = new Material[tc]; _maskWriteMat = new Material[tc];
            _maskedEqMat = new Material[tc]; _maskedNeMat = new Material[tc]; _addMat = new Material[tc]; _mulMat = new Material[tc];
            for (int t = 0; t < tc; t++)
            {
                _alphaMat[t] = Mat(sprite, _textures[t]);
                _alphaMat[t].SetColor("_RendererColor", Color.white);
                _alphaMat[t].SetVector("_Flip", Vector4.one);
                _normalMat[t] = UiMat(ui, _textures[t], CompareFunction.Always, StencilOp.Keep, 14);
                _maskWriteMat[t] = UiMat(ui, _textures[t], CompareFunction.Always, StencilOp.Replace, 0);
                _maskWriteMat[t].EnableKeyword("UNITY_UI_ALPHACLIP");
                _maskedEqMat[t] = UiMat(ui, _textures[t], CompareFunction.Equal, StencilOp.Keep, 14);
                _maskedNeMat[t] = UiMat(ui, _textures[t], CompareFunction.NotEqual, StencilOp.Keep, 14);
                _addMat[t] = Mat(add ?? ui, _textures[t]);
                _mulMat[t] = Mat(mul ?? ui, _textures[t]);
            }
            _blackMat = UiMat(ui, null, CompareFunction.Always, StencilOp.Keep, 14);
        }

        static Shader FindShader(string name)
        {
            var s = Shader.Find(name);
            if (s == null) Mod.Warn("shader not found: " + name);
            return s;
        }

        T Own<T>(T o) where T : UnityEngine.Object { _owned.Add(o); return o; }

        Mesh NewMesh(string name, Vector2[] uv, int[] tris)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.vertices = new Vector3[uv.Length];
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            return mesh;
        }

        Material Mat(Shader s, Texture tex)
        {
            var m = Own(new Material(s));
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        Material UiMat(Shader s, Texture tex, CompareFunction comp, StencilOp op, int colorMask)
        {
            var m = Mat(s, tex);
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Stencil", 1);
            m.SetFloat("_StencilComp", (float)comp);
            m.SetFloat("_StencilOp", (float)op);
            m.SetFloat("_StencilReadMask", 255);
            m.SetFloat("_StencilWriteMask", 255);
            m.SetFloat("_ColorMask", colorMask);
            return m;
        }

        Rect ComputeBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var dyn = _m.DynamicFlags;
            for (int i = 0; i < _m.DrawableCount; i++)
            {
                if ((dyn[i] & CubismCore.IsVisible) == 0) continue;
                foreach (var p in _verts[i])
                {
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                }
            }
            if (minX > maxX) return new Rect(-1, -1, 2, 2);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        // copy changed vertex/colour data from the core into the meshes
        void UploadAll(bool force)
        {
            var dyn = _m.DynamicFlags;
            var pos = _m.VertexPositions;
            var op = _m.Opacities;
            var mul = _m.MultiplyColors;
            for (int i = 0; i < _m.DrawableCount; i++)
            {
                byte f = dyn[i];
                if (force || (f & CubismCore.VertexPositionsDidChange) != 0)
                {
                    var v = _verts[i];
                    var p = pos[i];
                    for (int k = 0; k < v.Length; k++) { v[k].x = p[k].X; v[k].y = p[k].Y; }
                    _meshes[i].vertices = v;
                    if (_maskMeshes[i] != null) _maskMeshes[i].vertices = v;
                }
                if (force || (f & (CubismCore.OpacityDidChange | CubismCore.BlendColorDidChange)) != 0)
                {
                    var c = new Color(mul[i].X, mul[i].Y, mul[i].Z, op[i]);
                    var cs = _colors[i];
                    for (int k = 0; k < cs.Length; k++) cs[k] = c;
                    _meshes[i].colors = cs;
                }
            }
            var ro = _m.RenderOrders;
            for (int i = 0; i < _order.Length; i++) { _orderKeys[i] = ro[i]; _order[i] = i; }
            Array.Sort(_orderKeys, _order);
        }

        // call once per frame after the avatar ticked
        public void Render()
        {
            UploadAll(false);
            var cf = _m.ConstantFlags;
            var dyn = _m.DynamicFlags;
            var op = _m.Opacities;
            var tex = _m.TextureIndices;
            var maskCounts = _m.MaskCounts;
            var masks = _m.Masks;

            _cb.Clear();
            _cb.SetRenderTarget(_rt);
            _cb.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
            _cb.SetViewProjectionMatrices(Matrix4x4.identity, _proj);

            // pass 1: alpha (and provisional colour)
            foreach (int i in _order)
            {
                if ((dyn[i] & CubismCore.IsVisible) == 0 || op[i] <= 0.001f) continue;
                if ((cf[i] & (CubismCore.BlendAdditive | CubismCore.BlendMultiplicative)) != 0) continue;
                _cb.DrawMesh(_meshes[i], Matrix4x4.identity, _alphaMat[tex[i]]);
            }

            // pass 2: wipe colour, keep alpha
            _cb.DrawMesh(_quad, Matrix4x4.identity, _blackMat);

            // pass 3: colour with masks and blend modes
            foreach (int i in _order)
            {
                if ((dyn[i] & CubismCore.IsVisible) == 0 || op[i] <= 0.001f) continue;
                int t = tex[i];
                byte flags = cf[i];
                if ((flags & CubismCore.BlendAdditive) != 0) { _cb.DrawMesh(_meshes[i], Matrix4x4.identity, _addMat[t]); continue; }
                if ((flags & CubismCore.BlendMultiplicative) != 0) { _cb.DrawMesh(_meshes[i], Matrix4x4.identity, _mulMat[t]); continue; }

                int mc = maskCounts[i];
                if (mc == 0) { _cb.DrawMesh(_meshes[i], Matrix4x4.identity, _normalMat[t]); continue; }

                _cb.ClearRenderTarget(true, false, Color.clear);   // depth + stencil only
                for (int k = 0; k < mc; k++)
                {
                    int j = masks[i][k];
                    if (_maskMeshes[j] != null) _cb.DrawMesh(_maskMeshes[j], Matrix4x4.identity, _maskWriteMat[tex[j]]);
                }
                bool inverted = (flags & CubismCore.IsInvertedMask) != 0;
                _cb.DrawMesh(_meshes[i], Matrix4x4.identity, inverted ? _maskedNeMat[t] : _maskedEqMat[t]);
            }

            Graphics.ExecuteCommandBuffer(_cb);
        }

        public void Dispose()
        {
            _cb.Release();
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
