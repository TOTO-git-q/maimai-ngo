using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PartnerLive2D.Core;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // The worker owns the reusable byte buffer until the main thread has uploaded it.
    // At most one drawing is queued, so even a slow device cannot accumulate packs in RAM.
    internal sealed class SpritePairLoader : IDisposable
    {
        internal sealed class LoadedClip
        {
            internal readonly SpritePairManifest.Clip Data;
            internal readonly Texture2D[] Textures;
            internal LoadedClip(SpritePairManifest.Clip data) { Data = data; Textures = new Texture2D[data.Drawings.Length]; }
            internal bool Complete { get { for (int i = 0; i < Textures.Length; i++) if (Textures[i] == null) return false; return true; } }
        }
        struct Job { internal LoadedClip Clip; internal int Drawing; }
        readonly object _gate = new object();
        readonly List<Job> _jobs = new List<Job>();
        readonly Thread _thread;
        readonly string _folder;
        readonly Stopwatch _budget = new Stopwatch();
        byte[] _buffer;
        Job _pending;
        bool _hasPending, _stopped;
        Exception _error;
        internal readonly List<LoadedClip> Clips = new List<LoadedClip>();
        internal int Loaded, Total;
        internal long Bytes;
        internal Exception Error { get { lock (_gate) return _error; } }

        internal SpritePairLoader(SpritePairManifest manifest, string folder, string loopGroup, bool select)
        {
            _folder = folder;
            foreach (var clip in manifest.Clips)
            {
                bool wanted = select ? (clip.Group == "Idle" || clip.Group == "Random" || clip.Group == "Tap" || clip.Group == "Confirm" || clip.Group == "Enter")
                    : (clip.Group == loopGroup || clip.Group == loopGroup.Replace("Loop", "Reaction"));
                if (!wanted) continue;
                var loaded = new LoadedClip(clip);
                Clips.Add(loaded); Total += clip.Drawings.Length;
                Bytes += (long)clip.Width * clip.Height * clip.Drawings.Length;
            }
            // First drawing of both loop clips becomes visible before any full clip load.
            bool ame = false, k = false;
            foreach (var c in Clips) if (c.Data.Group == loopGroup)
            {
                if (c.Data.Character == "Ame") ame = true;
                if (c.Data.Character == "K") k = true;
                _jobs.Add(new Job { Clip = c, Drawing = 0 });
            }
            if (!ame || !k) throw new FormatException("missing pair loop clips");
            foreach (var c in Clips)
                for (int i = 0; i < c.Textures.Length; i++)
                    if (i != 0 || c.Data.Group != loopGroup) _jobs.Add(new Job { Clip = c, Drawing = i });
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "Partner sprite pack reader" };
            _thread.Start();
        }

        void ReadLoop()
        {
            try
            {
                string openName = null;
                FileStream stream = null;
                try
                {
                    foreach (var job in _jobs)
                    {
                        lock (_gate) { if (_stopped) return; }
                        var d = job.Clip.Data.Drawings[job.Drawing];
                        string name = job.Clip.Data.Name;
                        if (name != openName)
                        {
                            if (stream != null) stream.Dispose();
                            stream = File.OpenRead(Path.Combine(_folder, name + ".bin")); openName = name;
                            var drawings = job.Clip.Data.Drawings;
                            var last = drawings[drawings.Length - 1];
                            if (stream.Length != last.Offset + last.Length) throw new InvalidDataException("invalid sprite pack length: " + name);
                        }
                        if (_buffer == null || _buffer.Length != d.Length) _buffer = new byte[d.Length];
                        stream.Position = d.Offset;
                        int at = 0;
                        while (at < _buffer.Length)
                        {
                            int n = stream.Read(_buffer, at, _buffer.Length - at);
                            if (n == 0) throw new EndOfStreamException(name);
                            at += n;
                        }
                        lock (_gate)
                        {
                            if (_stopped) return;
                            _pending = job; _hasPending = true; System.Threading.Monitor.PulseAll(_gate);
                            while (_hasPending && !_stopped) System.Threading.Monitor.Wait(_gate);
                            if (_stopped) return;
                        }
                    }
                }
                finally { if (stream != null) stream.Dispose(); }
            }
            catch (Exception e) { lock (_gate) { if (!_stopped) _error = e; } }
        }
        // Main thread only. A single upload can exceed the budget, but never start another
        // after the deadline. The worker cannot overwrite the buffer before it is consumed.
        internal void Pump(double milliseconds)
        {
            _budget.Restart();
            while (_budget.Elapsed.TotalMilliseconds < milliseconds)
            {
                lock (_gate)
                {
                    if (!_hasPending || _stopped) return;
                    var data = _pending.Clip.Data;
                    var tex = new Texture2D(data.Width, data.Height, TextureFormat.BC7, false);
                    try
                    {
                        tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
                        tex.LoadRawTextureData(_buffer); tex.Apply(false, true);
                        _pending.Clip.Textures[_pending.Drawing] = tex; Loaded++;
                    }
                    catch { UnityEngine.Object.Destroy(tex); throw; }
                    finally { _hasPending = false; System.Threading.Monitor.PulseAll(_gate); }
                }
            }
        }
        public void Dispose()
        {
            lock (_gate) { _stopped = true; _hasPending = false; System.Threading.Monitor.PulseAll(_gate); }
            if (Thread.CurrentThread != _thread) _thread.Join(); // no file read survives page closure
            foreach (var clip in Clips) foreach (var texture in clip.Textures) if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }
}

