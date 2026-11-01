using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PartnerLive2D.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace PartnerLive2D.Unity
{
    // Constructed and pumped on Unity's thread; manifest and pack I/O stay on workers.
    internal sealed class SpritePairBackend : IDisposable
    {
        readonly object _gate = new object();
        readonly string _manifestPath, _loop;
        readonly bool _select;
        readonly Thread _initThread;
        readonly CommandBuffer _commands = new CommandBuffer { name = "Partner sprites" };
        readonly Dictionary<SpritePairManifest.Clip, SpritePairLoader.LoadedClip> _clips = new Dictionary<SpritePairManifest.Clip, SpritePairLoader.LoadedClip>();
        readonly Dictionary<SpritePairManifest.Clip, Mesh> _meshes = new Dictionary<SpritePairManifest.Clip, Mesh>();
        readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        readonly Material _material;
        readonly RenderTexture _pair;
        SpritePairManifest _manifest;
        SpritePairLoader _loader;
        SpritePairPlayback _playback;
        Exception _error;
        bool _closed, _initialized;
        internal RenderTexture Texture { get { return _pair; } }
        internal float Aspect { get { return 1f; } }
        internal SpritePairPlayback Playback { get { return _playback; } }
        internal SpritePairLoader Loader { get { return _loader; } }
        internal SpritePairManifest Manifest { get { return _manifest; } }

        internal SpritePairBackend(string path, string loop, bool select, int size)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC7)) throw new NotSupportedException("BC7 unsupported");
            // Sprites/Default ships with the game (the Live2D renderer uses it too): it premultiplies the straight-alpha
            // packs and blends One/OneMinusSrcAlpha, so the RT stays premultiplied for the RawImage.
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported) throw new NotSupportedException("sprite compositing shader unavailable");
            _manifestPath = path; _loop = loop; _select = select;
            _material = new Material(shader);
            _material.SetColor("_RendererColor", Color.white); _material.SetVector("_Flip", Vector4.one);
            _pair = MakeTarget(size, "PartnerSpritePair");
            _initThread = new Thread(Load) { IsBackground = true, Name = "Partner sprite manifest" };
            _initThread.Start();
        }
        static RenderTexture MakeTarget(int size, string name)
        {
            var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32) { name = name, antiAliasing = 1, useMipMap = false };
            rt.Create(); return rt;
        }
        static Mesh Quad(float x, float y, float w, float h)
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(x, y), new Vector3(x, y+h), new Vector3(x+w, y+h), new Vector3(x+w, y) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return mesh;
        }
        void Load()
        {
            SpritePairLoader loader = null;
            try
            {
                var manifest = SpritePairManifest.Parse(File.ReadAllText(_manifestPath));
                loader = new SpritePairLoader(manifest, Path.GetDirectoryName(_manifestPath), _loop, _select);
                lock (_gate)
                {
                    if (_closed) { loader.Dispose(); return; }
                    _manifest = manifest; _loader = loader;
                }
            }
            catch (Exception e) { if (loader != null) loader.Dispose(); lock (_gate) if (!_closed) _error = e; }
        }
        bool Ready(SpritePairManifest.Clip c)
        {
            SpritePairLoader.LoadedClip clip;
            if (!_clips.TryGetValue(c, out clip)) return false;
            return c.Group == _loop ? clip.Textures[0] != null : clip.Complete;
        }
        internal void Tick(float dt)
        {
            lock (_gate) { if (_error != null) throw new InvalidOperationException("sprite initialization failed", _error); }
            if (_loader == null) return;
            if (_loader.Error != null) throw new InvalidOperationException("sprite pack read failed", _loader.Error);
            if (!_initialized)
            {
                foreach (var clip in _loader.Clips)
                {
                    _clips.Add(clip.Data, clip);
                    var r = clip.Data.Rect;
                    _meshes.Add(clip.Data, Quad(r[0], 1024-r[1]-r[3], r[2], r[3]));
                }
                _playback = new SpritePairPlayback(_manifest, _loop, _select, Ready, Environment.TickCount);
                _initialized = true;
            }
            _loader.Pump(3);
            _playback.Tick(dt);
        }
        void Draw(Mesh mesh, Texture texture, float weight)
        {
            if (texture == null || weight <= 0) return;
            _properties.Clear();
            _properties.SetTexture("_MainTex", texture);
            _properties.SetColor("_Color", new Color(1, 1, 1, weight));
            _commands.DrawMesh(mesh, Matrix4x4.identity, _material, 0, 0, _properties);
        }
        void Drawing(SpritePairManifest.Clip clip, int frame, float weight)
        {
            if (clip == null) return;
            var loaded = _clips[clip];
            var texture = loaded.Textures[frame] ?? loaded.Textures[0];
            Draw(_meshes[clip], texture, weight);
        }
        internal void Render()
        {
            _commands.Clear();
            _commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1024, 0, 1024, -1, 1));
            _commands.SetRenderTarget(_pair); _commands.ClearRenderTarget(false, true, Color.clear);
            for (int c = 0; _playback != null && c < 2; c++)
            {
                var state = _playback.Characters[c];
                if (state.Current == null) continue;
                // Crossfade: the outgoing drawing stays fully opaque underneath and the incoming one fades in over it,
                // so the character never turns translucent and Ame never shows through K.
                float incoming = _playback.Incoming(c);
                Drawing(state.Outgoing, state.OutgoingDrawing, 1);
                int currentDrawing = state.Outgoing != null ? 0 : SpritePairMath.DrawingAt(state.Current, state.Time, state.Current.Group == _loop);
                Drawing(state.Current, currentDrawing, incoming);
            }
            Graphics.ExecuteCommandBuffer(_commands);
        }
        public void Dispose()
        {
            lock (_gate) { if (_closed) return; _closed = true; }
            _initThread.Join();
            if (_loader != null) _loader.Dispose();
            foreach (var mesh in _meshes.Values) UnityEngine.Object.Destroy(mesh);
            _commands.Release(); UnityEngine.Object.Destroy(_pair); UnityEngine.Object.Destroy(_material);
        }
    }
}
