using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PartnerLive2D.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace PartnerLive2D.Unity
{
    // The select page from the frame library (SpriteLibrary, partner.ini "library"): the director decides a second ahead
    // (SpriteTimeline), a reader thread reads those frames from the two packs, and the main thread uploads them into a small
    // pool of reused BC7 textures, so only a few dozen frames per character are ever in video memory. One picture per
    // 3 refreshes of the 60 Hz screen, and while a take plays on to its next picture the two refreshes between show its
    // in-betweens (owner 2026-11-02: "动作稍微还是有点卡顿不够平滑"); a picture whose frames have not arrived yet is held,
    // never skipped, a missing in-between just leaves the picture up.
    internal sealed class SpriteLibraryBackend : IDisposable
    {
        readonly int Slots;               // textures per character: the second ahead (fades included), in-betweens, recent ones
        const int Buffers = 10;             // frames read ahead of the upload, per character
        readonly int BetweenAhead;        // pictures ahead whose in-betweens are read (half a second)
        const float Picture = 1f / SpriteLibrary.Fps;
        readonly double UploadBudgetMs;
        readonly int PlanAhead;

        sealed class Pool
        {
            public SpriteLibrary.Character Data;
            public string PackPath;
            public long FrameBytes;
            public Texture2D[] Textures;
            public int[] SlotFrame, SlotUse;
            public readonly Dictionary<int, int> Resident = new Dictionary<int, int>();
            public readonly HashSet<int> Needed = new HashSet<int>();
            public readonly Stack<byte[]> Free = new Stack<byte[]>();   // under _gate
            public Mesh Quad;                // a grid, bent and moved every frame by the motion feel (PairFeel)
            public Vector2[] Base;           // grid points on the pair canvas (y down), undeformed
            public float[] Flex;             // how soft she is at each grid point (PairFeel: the soft parts follow late)
            public Vector3[] Points;         // the same, deformed, in the target's y-up space
        }
        readonly int GridColumns, GridRows;   // fine enough for hair ends and ribbons to bend on their own
        struct Read { public int Character, Frame; public byte[] Buffer; }

        readonly object _gate = new object();
        readonly string _path;
        readonly Thread _init;
        Thread _reader;
        SpriteLibrary _lib;
        SpriteTimeline _timeline;
        readonly Pool[] _pools = { new Pool(), new Pool() };
        readonly List<Read> _wanted = new List<Read>();     // main -> reader: missing frames, most urgent first
        readonly List<Read> _done = new List<Read>();       // reader -> main: read, waiting for upload
        readonly HashSet<long> _inFlight = new HashSet<long>();
        readonly HashSet<long> _listed = new HashSet<long>();
        readonly List<Read> _batch = new List<Read>();
        readonly System.Diagnostics.Stopwatch _budget = new System.Diagnostics.Stopwatch();
        Exception _error;
        bool _closed, _started, _ready;
        float _clock;
        int _use;
        readonly CommandBuffer _commands = new CommandBuffer { name = "Partner library" };
        readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
        readonly Material _material;
        readonly RenderTexture _pair;
        internal int Stalls, Uploads, Pictures, BetweenShown, BetweenMissed;
        internal Action<string> Log;
        internal RenderTexture Texture { get { return _pair; } }
        internal SpriteLibrary Library { get { return _lib; } }
        internal bool Ready { get { return _ready; } }

        internal SpriteLibraryBackend(string path, int size)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC7)) throw new NotSupportedException("BC7 unsupported");
            var shader = Shader.Find("Sprites/Default");   // as SpritePairBackend: premultiplies straight alpha, RT stays premultiplied
            if (shader == null || !shader.isSupported) throw new NotSupportedException("sprite compositing shader unavailable");
            var quality = RuntimeQuality.Policy;
            Slots = quality.Slots; BetweenAhead = quality.BetweenAhead; PlanAhead = quality.PlanAhead;
            GridColumns = GridRows = quality.Grid; UploadBudgetMs = quality.UploadBudgetMs;
            foreach (var p in _pools) { p.Textures = new Texture2D[Slots]; p.SlotFrame = new int[Slots]; p.SlotUse = new int[Slots]; }
            _path = path;
            _material = new Material(shader);
            _material.SetColor("_RendererColor", Color.white); _material.SetVector("_Flip", Vector4.one);
            _pair = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32) { name = "PartnerLibraryPair", antiAliasing = 1, useMipMap = false };
            _pair.Create();
            _init = new Thread(Load) { IsBackground = true, Name = "Partner library" };
            _init.Start();
        }

        void Load()
        {
            try
            {
                var lib = SpriteLibrary.Load(_path);
                string folder = Path.GetDirectoryName(_path);
                for (int c = 0; c < 2; c++)
                {
                    var ch = lib.Characters[c];
                    string pack = Path.Combine(folder, ch.Pack);
                    long frame = (long)ch.TextureWidth * ch.TextureHeight;
                    if (new FileInfo(pack).Length != frame * ch.PackFrames) throw new InvalidDataException("pack size does not match the library: " + ch.Pack);
                    _pools[c].Data = ch; _pools[c].PackPath = pack; _pools[c].FrameBytes = frame;
                    for (int b = 0; b < Buffers; b++)
                    {
                        var buffer = new byte[frame];
                        lock (_gate) { if (_closed) return; _pools[c].Free.Push(buffer); }
                    }
                }
                var timeline = new SpriteTimeline(lib, Environment.TickCount, true);
                lock (_gate) { if (_closed) return; _lib = lib; _timeline = timeline; }
            }
            catch (Exception e) { lock (_gate) if (!_closed) _error = e; }
        }

        // main thread, once the library and read buffers are in: quads and the reader
        void Start()
        {
            long started = RuntimePerf.Now();
            _started = true;
            _feel = new PairFeel(Environment.TickCount, _lib.Characters[0], _lib.Characters[1]);
            _cues = new PairCues(_lib, _feel);
            for (int c = 0; c < 2; c++)
            {
                var p = _pools[c]; var r = p.Data.Rect;
                for (int i = 0; i < Slots; i++) p.SlotFrame[i] = -1;
                // the picture as a grid (texture rows are stored bottom-up: v = 0 is the picture's bottom edge)
                int n = (GridColumns + 1) * (GridRows + 1);
                p.Base = new Vector2[n]; p.Points = new Vector3[n]; p.Flex = new float[n];
                var uv = new Vector2[n]; var colors = new Color[n]; var tris = new int[GridColumns * GridRows * 6];
                for (int j = 0, k = 0; j <= GridRows; j++)
                    for (int i = 0; i <= GridColumns; i++, k++)
                    {
                        p.Base[k] = new Vector2(r[0] + r[2] * i / GridColumns, r[1] + r[3] * j / GridRows);
                        p.Flex[k] = p.Data.FlexAt(p.Base[k].x, p.Base[k].y);
                        p.Points[k] = new Vector3(p.Base[k].x, 1024 - p.Base[k].y);
                        uv[k] = new Vector2((float)i / GridColumns, 1 - (float)j / GridRows); colors[k] = Color.white;
                    }
                for (int j = 0, t = 0; j < GridRows; j++)
                    for (int i = 0; i < GridColumns; i++)
                    {
                        int a = j * (GridColumns + 1) + i, below = a + GridColumns + 1;
                        tris[t++] = a; tris[t++] = below; tris[t++] = below + 1; tris[t++] = a; tris[t++] = below + 1; tris[t++] = a + 1;
                    }
                var mesh = new Mesh(); mesh.MarkDynamic();
                mesh.vertices = p.Points; mesh.uv = uv; mesh.colors = colors; mesh.triangles = tris;
                mesh.bounds = new Bounds(new Vector3(512, 512, 0), new Vector3(4096, 4096, 10));
                p.Quad = mesh;
            }
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Partner library reader" };
            _reader.Start();
            RuntimePerf.End(RuntimePerf.Part.LibraryStart, started);
        }

        static long Key(int c, int f) { return ((long)c << 32) | (uint)f; }

        void ReadLoop()
        {
            var streams = new FrameBlockReader[2];
            try
            {
                for (int c = 0; c < 2; c++) streams[c] = new FrameBlockReader(
                    new FileStream(_pools[c].PackPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess),
                    checked((int)_pools[c].FrameBytes));
                while (true)
                {
                    Read job = default(Read);
                    lock (_gate)
                    {
                        while (true)
                        {
                            if (_closed) return;
                            int k = 0;
                            while (k < _wanted.Count && _pools[_wanted[k].Character].Free.Count == 0) k++;
                            if (k < _wanted.Count) { job = _wanted[k]; _wanted.RemoveAt(k); job.Buffer = _pools[job.Character].Free.Pop(); _inFlight.Add(Key(job.Character, job.Frame)); break; }
                            System.Threading.Monitor.Wait(_gate);
                        }
                    }
                    var s = streams[job.Character];
                    s.Read(job.Frame, job.Buffer);
                    lock (_gate)
                    {
                        _readCalls[job.Character] = s.Reads; _readBytes[job.Character] = s.Bytes;
                        _readHits[job.Character] = s.Hits; _readSeeks[job.Character] = s.Seeks;
                        _readMax[job.Character] = s.MaxReadMs;
                        if (_closed) return;
                        _done.Add(job);
                    }
                }
            }
            catch (Exception e) { lock (_gate) if (!_closed) _error = e; }
            finally { foreach (var s in streams) if (s != null) s.Dispose(); }
        }

        bool Resident(int position)
        {
            for (int c = 0; c < 2; c++)
            {
                var shot = _timeline.Shot(position, c); var p = _pools[c];
                if (!p.Resident.ContainsKey(shot.Frame) || (shot.Under >= 0 && !p.Resident.ContainsKey(shot.Under))) return false;
            }
            return true;
        }

        // main-thread cost per frame (Tick + Render), the last 600 frames, and the slowest moment replan
        readonly System.Diagnostics.Stopwatch _cpu = new System.Diagnostics.Stopwatch();
        readonly float[] _cpuMs = new float[600];
        int _cpuCount, _cpuNext;
        float _tickMs, _momentMaxMs;

        internal void Tick(float dt)
        {
            _cpu.Restart();
            try { TickInner(dt); } finally { _tickMs = (float)_cpu.Elapsed.TotalMilliseconds; }
        }
        void TickInner(float dt)
        {
            lock (_gate) { if (_error != null) throw new InvalidOperationException("sprite library failed", _error); }
            if (_timeline == null) return;
            if (!_started)
            {
                Start();
                foreach (var m in _early) _timeline.Moment(m.Key, m.Value);   // moments asked for while the library loaded
                _early.Clear();
            }
            Upload();
            _feel.Update(dt);
            if (!_ready)
            {
                if (Resident(0)) { _ready = true; _clock = 0; Touch(0); Record(); Cue(); }
            }
            else if (!_feel.Holding)                                         // a hit-stop holds the pictures too
            {
                _clock = Math.Min(_clock + dt, 2 * Picture);                 // after a hitch catch up one picture at most
                while (_clock >= Picture - .004f)
                {
                    if (_timeline.Count < 2 || !Resident(1)) { Stalls++; _clock = Math.Min(_clock, Picture); break; }
                    _timeline.Advance(); _clock -= Picture; Pictures++;
                    Touch(0); Record(); Cue();
                    var notes = _timeline.Notes(0);
                    if (notes != null && Log != null) foreach (var n in notes) if (!n.Contains(" idle jump")) Log(n);
                }
            }
            Want();
        }

        PairFeel _feel;
        PairCues _cues;
        void Cue() { for (int c = 0; c < 2; c++) _cues.Shown(c, _timeline.Shot(0, c).Frame); }
        // a finger on one of them (pair canvas x, < 0: somewhere on her): she is squashed at once, before her reaction starts
        internal void Finger(int who, float x)
        {
            if (_feel == null || who < 0) return;
            float pivot = _lib.Characters[who].PivotX;
            _feel.Touch(who, x >= 0 ? x : pivot + (UnityEngine.Random.value < .5f ? -40 : 40), pivot);
        }

        void Touch(int position)
        {
            _use++;
            for (int c = 0; c < 2; c++)
            {
                var shot = _timeline.Shot(position, c); var p = _pools[c]; int slot;
                if (p.Resident.TryGetValue(shot.Frame, out slot)) p.SlotUse[slot] = _use;
                if (shot.Under >= 0 && p.Resident.TryGetValue(shot.Under, out slot)) p.SlotUse[slot] = _use;
            }
        }

        // every frame of the plan that is not in a texture yet, in the order it will be shown
        void Want()
        {
            for (int c = 0; c < 2; c++) _pools[c].Needed.Clear();
            lock (_gate)
            {
                _wanted.Clear(); _listed.Clear();
                for (int k = 0; k < Math.Min(PlanAhead, _timeline.Count); k++)
                    for (int c = 0; c < 2; c++)
                    {
                        var shot = _timeline.Shot(k, c);
                        Need(c, shot.Frame);
                        if (shot.Under >= 0) Need(c, shot.Under);
                        if (k < BetweenAhead && k + 1 < _timeline.Count)
                        {
                            var following = _timeline.Shot(k + 1, c);
                            int b = Between(c, shot.Frame, following.Frame);
                            if (b >= 0) { Need(c, b); Need(c, b + 1); }
                            if (shot.Under >= 0 && following.Under >= 0)
                            {
                                int bu = Between(c, shot.Under, following.Under);
                                if (bu >= 0) { Need(c, bu); Need(c, bu + 1); }
                            }
                        }
                    }
                if (_wanted.Count > 0) System.Threading.Monitor.PulseAll(_gate);
            }
        }
        // frame f's in-betweens when its take simply plays on to the next picture (also the new frame of a fade): the
        // first one's place in the pack, else -1
        int Between(int c, int f, int next)
        {
            var ch = _pools[c].Data;
            return BetweenAhead > 0 && ch.Between != null && next == ch.Next(f) ? ch.Between[f] : -1;
        }
        void Need(int c, int f)
        {
            var p = _pools[c]; long key = Key(c, f);
            p.Needed.Add(f);
            if (p.Resident.ContainsKey(f) || _inFlight.Contains(key) || !_listed.Add(key)) return;
            _wanted.Add(new Read { Character = c, Frame = f });
        }

        readonly long[] _readCalls = new long[2], _readBytes = new long[2], _readHits = new long[2], _readSeeks = new long[2];
        readonly double[] _readMax = new double[2];
        double _uploadMaxMs, _uploadTotalMs;
        int _textureCreates, _staleReads;
        void Upload()
        {
            _budget.Restart();
            lock (_gate) { _batch.AddRange(_done); _done.Clear(); }
            int used = 0;
            for (; used < _batch.Count; used++)
            {
                if (used > 0 && _budget.Elapsed.TotalMilliseconds >= UploadBudgetMs) break;
                var job = _batch[used]; var p = _pools[job.Character];
                if (!p.Needed.Contains(job.Frame)) _staleReads++;
                if (p.Needed.Contains(job.Frame) && !p.Resident.ContainsKey(job.Frame))
                {
                    int slot = Victim(p);
                    if (p.SlotFrame[slot] >= 0) p.Resident.Remove(p.SlotFrame[slot]);
                    if (p.Textures[slot] == null)
                    {
                        p.Textures[slot] = new Texture2D(p.Data.TextureWidth, p.Data.TextureHeight, TextureFormat.BC7, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PartnerLibrary" };
                        _textureCreates++;
                    }
                    p.Textures[slot].LoadRawTextureData(job.Buffer);
                    p.Textures[slot].Apply(false, false);
                    p.SlotFrame[slot] = job.Frame; p.SlotUse[slot] = _use; p.Resident[job.Frame] = slot;
                    Uploads++;
                }
                lock (_gate) { p.Free.Push(job.Buffer); _inFlight.Remove(Key(job.Character, job.Frame)); System.Threading.Monitor.PulseAll(_gate); }
            }
            if (used > 0) _batch.RemoveRange(0, used);
            double elapsed = _budget.Elapsed.TotalMilliseconds;
            _uploadTotalMs += elapsed; _uploadMaxMs = Math.Max(_uploadMaxMs, elapsed);
        }
        // an empty slot, else the least recently shown one the plan does not need
        int Victim(Pool p)
        {
            int best = -1, bestUse = int.MaxValue, any = 0, anyUse = int.MaxValue;
            for (int i = 0; i < Slots; i++)
            {
                if (p.SlotFrame[i] < 0) return i;
                if (p.SlotUse[i] < anyUse) { anyUse = p.SlotUse[i]; any = i; }
                if (!p.Needed.Contains(p.SlotFrame[i]) && p.SlotUse[i] < bestUse) { bestUse = p.SlotUse[i]; best = i; }
            }
            return best >= 0 ? best : any;
        }

        // this frame's motion feel on the character's grid
        void Deform(int c)
        {
            var p = _pools[c]; var ch = p.Data; var pose = _feel.Get(c);
            for (int k = 0; k < p.Base.Length; k++)
            {
                float x, y;
                PairFeel.Deform(pose, ch.PivotX, ch.PivotY, ch.AxisX, ch.AxisY, p.Flex[k], p.Base[k].x, p.Base[k].y, out x, out y);
                p.Points[k] = new Vector3(x, 1024 - y, 0);
            }
            p.Quad.vertices = p.Points;
        }

        // When a fade ends or a cut lands, the old picture stays behind the new one for 6 more refreshes and fades out there:
        // the new picture is opaque over it, so nothing turns see-through, and whatever of the old one lies outside the new
        // outline (a strand of hair, a sleeve) goes gradually instead of vanishing in one refresh (measured 2026-11-02: the
        // step at a fade's end was 5-8 times the steps around it; fading the old picture out during the fade instead let
        // the background show through and flickered).
        const int LingerRefreshes = 6;
        readonly SpriteDirector.Shot[] _lastShot = new SpriteDirector.Shot[2];
        readonly bool[] _hadShot = new bool[2];
        readonly int[] _linger = { -1, -1 }, _lingerLeft = new int[2];
        void Linger(int c, SpriteDirector.Shot shot)
        {
            var p = _pools[c]; var last = _lastShot[c];
            if (_hadShot[c] && (shot.Frame != last.Frame || shot.Under != last.Under))
            {
                int old = -1;
                if (last.Under >= 0 && shot.Under < 0) old = last.Under;                                    // a fade ended
                else if (last.Under < 0 && shot.Under < 0 && shot.Frame != p.Data.Next(last.Frame)) old = last.Frame;   // a cut
                if (old >= 0 && old != shot.Frame) { _linger[c] = old; _lingerLeft[c] = LingerRefreshes; }
            }
            _lastShot[c] = shot; _hadShot[c] = true;
            int slot;
            if (_lingerLeft[c] > 0 && p.Resident.TryGetValue(_linger[c], out slot))
            {
                p.SlotUse[slot] = _use;
                Draw(c, _linger[c], _lingerLeft[c] / (float)(LingerRefreshes + 1));
                _lingerLeft[c]--;
            }
            else _lingerLeft[c] = 0;
        }

        void Draw(int c, int frame, float weight)
        {
            var p = _pools[c]; int slot;
            if (weight <= 0 || !p.Resident.TryGetValue(frame, out slot)) return;
            _props.Clear();
            _props.SetTexture("_MainTex", p.Textures[slot]);
            _props.SetColor("_Color", new Color(1, 1, 1, weight));
            _commands.DrawMesh(p.Quad, Matrix4x4.identity, _material, 0, 0, _props);
        }

        internal void Render()
        {
            _cpu.Restart();
            try { RenderInner(); }
            finally
            {
                _cpuMs[_cpuNext] = _tickMs + (float)_cpu.Elapsed.TotalMilliseconds;
                _cpuNext = (_cpuNext + 1) % _cpuMs.Length; if (_cpuCount < _cpuMs.Length) _cpuCount++;
                _tickMs = 0;
            }
        }
        void RenderInner()
        {
            _commands.Clear();
            _commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1024, 0, 1024, -1, 1));
            _commands.SetRenderTarget(_pair); _commands.ClearRenderTarget(false, true, Color.clear);
            // which refresh of the picture this is: the first shows the picture, the next two are 1/3 and 2/3 of the way to
            // the next one - the take's in-betweens, else a dissolve (a cut, the weight of a fade), so nothing moves on
            // only every third refresh (owner 2026-11-02: "动作还是不够平滑"; measured: fades and cuts stepped)
            int r = _clock < Picture * .25f ? 0 : _clock < Picture * .5833f ? 1 : 2;
            float a = r / 3f;
            if (_ready)
                for (int c = 0; c < 2; c++)   // Ame first, K over her; during a fade the old frame stays opaque under the new one
                {
                    Deform(c);
                    var p = _pools[c]; var shot = _timeline.Shot(0, c);
                    bool more = r > 0 && _timeline.Count > 1;
                    var next = more ? _timeline.Shot(1, c) : shot;
                    int frame = shot.Frame, slot;
                    if (more)
                    {
                        int b = Between(c, shot.Frame, next.Frame);
                        if (b >= 0 && p.Resident.TryGetValue(b + r - 1, out slot)) { frame = b + r - 1; p.SlotUse[slot] = _use; BetweenShown++; }
                        else if (b >= 0) BetweenMissed++;
                    }
                    Linger(c, shot);
                    if (shot.Under >= 0)
                    {
                        float to = next.Under >= 0 ? next.Weight : 1;   // the fade goes on (its old picture plays on), or ends
                        int under = shot.Under;                         // the old picture plays on smoothly too
                        if (more && next.Under >= 0)
                        {
                            int bu = Between(c, shot.Under, next.Under);
                            if (bu >= 0 && p.Resident.TryGetValue(bu + r - 1, out slot)) { under = bu + r - 1; p.SlotUse[slot] = _use; }
                        }
                        Draw(c, under, 1);
                        Draw(c, frame, shot.Weight + (to - shot.Weight) * (more ? a : 0));
                    }
                    else
                    {
                        Draw(c, frame, 1);
                        // a cut (or a fade beginning with the next picture): already dissolving toward it
                        if (more && frame == shot.Frame && next.Frame != shot.Frame && p.Resident.TryGetValue(next.Frame, out slot))
                        {
                            p.SlotUse[slot] = _use;
                            Draw(c, next.Frame, (next.Under >= 0 ? next.Weight : 1) * a);
                        }
                    }
                }
            Graphics.ExecuteCommandBuffer(_commands);
        }

        // ---- the select page's moments
        readonly List<KeyValuePair<string, int>> _early = new List<KeyValuePair<string, int>>();
        internal bool CanPlay(string moment) { return _ready && _lib.HasMoment(moment); }
        internal bool Moment(string moment, int who)
        {
            if (_timeline == null || !_started) { _early.Add(new KeyValuePair<string, int>(moment, who)); return true; }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            bool fired = _timeline.Moment(moment, who);
            _momentMaxMs = Math.Max(_momentMaxMs, (float)((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
            return fired;
        }
        internal bool Playing(string moment)
        {
            foreach (var m in _early) if (m.Key == moment) return true;
            return _timeline != null && _timeline.Playing(moment);
        }
        internal int HitTest(float x, float y) { return _lib == null ? -1 : _lib.HitTest(x, y); }
        internal string CharacterName(int who) { return _lib == null || who < 0 || who > 1 ? "nobody" : _lib.Characters[who].Name; }
        // what was shown, the last 3 minutes (tests: GET views/<id>/timeline, rendered with v2/render.py), and when;
        // a ring of plain values, so recording makes no garbage
        const int HistoryLength = 3600;
        readonly SpriteDirector.Shot[] _historyShots = new SpriteDirector.Shot[HistoryLength * 2];
        readonly float[] _historyAt = new float[HistoryLength];
        int _historyCount, _historyNext;
        void Record()
        {
            _historyShots[_historyNext * 2] = _timeline.Shot(0, 0); _historyShots[_historyNext * 2 + 1] = _timeline.Shot(0, 1);
            _historyAt[_historyNext] = Time.realtimeSinceStartup;
            _historyNext = (_historyNext + 1) % HistoryLength;
            if (_historyCount < HistoryLength) _historyCount++;
        }
        int HistoryIndex(int k) { return (_historyNext - _historyCount + k + HistoryLength) % HistoryLength; }
        internal Dictionary<string, object> Timeline()
        {
            var ticks = new List<object>(); var at = new List<object>();
            for (int k = 0; k < _historyCount; k++)
            {
                int h = HistoryIndex(k);
                var tick = new Dictionary<string, object>();
                for (int c = 0; c < 2; c++)
                {
                    var ch = _lib.Characters[c]; var s = _historyShots[h * 2 + c]; var layers = new List<object>();
                    if (s.Under >= 0) layers.Add(Layer(ch, s.Under, 1f));
                    layers.Add(Layer(ch, s.Frame, s.Under >= 0 ? s.Weight : 1f));
                    tick[ch.Name] = layers;
                }
                ticks.Add(tick); at.Add((double)_historyAt[h]);
            }
            return new Dictionary<string, object> { { "fps", SpriteLibrary.Fps }, { "ticks", ticks }, { "shownAt", at } };
        }
        static List<object> Layer(SpriteLibrary.Character ch, int f, float w)
        {
            var t = ch.Takes[ch.TakeOf[f]];
            return new List<object> { t.Name, f - t.First, (double)w };
        }
        internal Dictionary<string, object> Info()
        {
            // cadence: a picture should stay exactly 3 refreshes (50 ms) at 60 Hz
            int even = 0, uneven = 0; float prev = -1, longest = 0;
            for (int k = 0; k < _historyCount; k++)
            {
                float t = _historyAt[HistoryIndex(k)];
                if (prev >= 0) { float gap = t - prev; if (Math.Abs(gap - Picture) < .008f) even++; else uneven++; if (gap > longest) longest = gap; }
                prev = t;
            }
            var d = new Dictionary<string, object> { { "ready", _ready }, { "pictures", Pictures }, { "stalls", Stalls }, { "uploads", Uploads },
                { "even50ms", even }, { "uneven", uneven }, { "longestGapMs", Math.Round(longest * 1000) },
                { "betweenShown", BetweenShown }, { "betweenMissed", BetweenMissed } };
            d["quality"] = RuntimeQuality.Policy.Low ? "low" : "high";
            d["slotsPerCharacter"] = Slots; d["meshCells"] = GridColumns; d["renderSize"] = _pair.width;
            d["renderTargetBytes"] = (long)_pair.width * _pair.height * 4;
            d["uploadMsMax"] = _uploadMaxMs; d["uploadMsTotal"] = _uploadTotalMs;
            d["textureCreates"] = _textureCreates; d["staleReadsDiscarded"] = _staleReads;
            lock (_gate)
            {
                d["diskReads"] = _readCalls[0] + _readCalls[1]; d["diskBytes"] = _readBytes[0] + _readBytes[1];
                d["diskCacheHits"] = _readHits[0] + _readHits[1]; d["diskSeeks"] = _readSeeks[0] + _readSeeks[1];
                d["diskReadMsMax"] = Math.Max(_readMax[0], _readMax[1]);
                d["readerCacheBytes"] = (_pools[0].FrameBytes + _pools[1].FrameBytes) * 8;
                d["readBufferBytes"] = (_pools[0].FrameBytes + _pools[1].FrameBytes) * Buffers;
                d["queuedReads"] = _wanted.Count; d["readyUploads"] = _done.Count + _batch.Count;
            }
            if (_cpuCount > 0)
            {
                var ms = new float[_cpuCount]; Array.Copy(_cpuMs, ms, _cpuCount); Array.Sort(ms);
                double sum = 0; foreach (var v in ms) sum += v;
                d["cpuMsAvg"] = Math.Round(sum / ms.Length, 3); d["cpuMsP99"] = Math.Round(ms[(int)(ms.Length * .99)], 3);
                d["cpuMsMax"] = Math.Round(ms[ms.Length - 1], 3); d["momentMsMax"] = Math.Round(_momentMaxMs, 3);
            }
            if (_timeline != null)
                for (int c = 0; c < 2; c++)
                {
                    var now = _timeline.Now; var ch = _lib.Characters[c]; int f = now.Frame(c);
                    var take = ch.Takes[ch.TakeOf[f]];
                    d[ch.Name] = new Dictionary<string, object> { { "take", take.Name }, { "frame", f - take.First }, { "action", now.ActionOf(c) },
                        { "resident", _pools[c].Resident.Count }, { "textureBytes", (long)_pools[c].Resident.Count * _pools[c].FrameBytes } };
                }
            return d;
        }

        public void Dispose()
        {
            lock (_gate) { if (_closed) return; _closed = true; System.Threading.Monitor.PulseAll(_gate); }
            long started = RuntimePerf.Now();
            // A slow HDD read must not make a screen transition wait. Workers own their streams and
            // finish in finally; neither worker touches Unity objects after cancellation.
            lock (_gate) { _wanted.Clear(); _done.Clear(); _batch.Clear(); foreach (var p in _pools) p.Free.Clear(); }
            foreach (var p in _pools)
            {
                foreach (var t in p.Textures) if (t != null) UnityEngine.Object.Destroy(t);
                if (p.Quad != null) UnityEngine.Object.Destroy(p.Quad);
            }
            _commands.Release(); UnityEngine.Object.Destroy(_pair); UnityEngine.Object.Destroy(_material);
            RuntimePerf.End(RuntimePerf.Part.LibraryDispose, started);
        }
    }
}
