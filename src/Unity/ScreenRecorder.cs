using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MelonLoader;
using PartnerLive2D.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace PartnerLive2D.Unity
{
    // Test-only screen recording (owner 2026-11-02 night: "录制一遍完整的演示"; the test game runs off screen on a locked desktop,
    // where no outside capture sees it whole). At the end of every frame the back buffer is blitted into a small render
    // texture and read back asynchronously (no stall); a writer thread feeds an ffmpeg process raw RGBA at a constant
    // frame rate, by the frames' real times: the latest frame is repeated while the game runs slower, extra ones dropped.
    //   POST /api/test/record {"path": "...mp4", "ffmpeg": "...ffmpeg.exe", "width": 720, "fps": 60, "crf": 23}   start
    //   POST /api/test/record {"stop": true}      stop (the file is finished in the background: "finished" in the answer)
    //   POST /api/test/record {}                  state
    static class ScreenRecorder
    {
        static readonly object Gate = new object();
        static readonly Queue<KeyValuePair<double, byte[]>> Frames = new Queue<KeyValuePair<double, byte[]>>();
        static readonly Stack<byte[]> Free = new Stack<byte[]>();
        static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
        static readonly System.Text.StringBuilder FfmpegLog = new System.Text.StringBuilder();
        static RenderTexture _full, _rt;
        static CommandBuffer _blit;
        static System.Diagnostics.Process _ffmpeg;
        static Thread _writer;
        static bool _on, _stopping, _finished = true;
        static int _width, _height, _fps, _inFlight, _queueLimit = 91;
        static long _captured, _written, _dropped;
        static string _path, _error;

        public static object Command(object body)
        {
            if (Json.GetBool(body, "stop")) return Stop();
            if (string.IsNullOrEmpty(Json.GetString(body, "path"))) return Info();
            if (_on || !_finished) throw new ApiError(409, "already recording");
            if (!SystemInfo.supportsAsyncGPUReadback) throw new ApiError(501, "no async GPU readback here");
            string ffmpeg = Json.GetString(body, "ffmpeg"), path = Json.GetString(body, "path");
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg)) throw new ApiError(400, "ffmpeg: path to ffmpeg.exe");
            _width = Math.Max(64, Json.GetInt(body, "width", 720)) / 2 * 2;
            _height = (int)Math.Round(_width * (double)Screen.height / Screen.width / 2) * 2;
            _fps = Math.Max(1, Math.Min(120, Json.GetInt(body, "fps", 60)));
            _queueLimit = RuntimeQuality.Policy.Low ? (int)Math.Max(2, Math.Min(8, 32L * 1024 * 1024 / Math.Max(1, (long)_width * _height * 4))) : 91;
            int crf = Math.Max(10, Math.Min(40, Json.GetInt(body, "crf", 23)));
            // the back buffer is copied 1:1 first: a blit from it straight into a smaller texture crops instead of scaling
            _full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "PartnerLive2D record full" };
            _full.Create();
            _rt = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32) { name = "PartnerLive2D record" };
            _rt.Create();
            _blit = new CommandBuffer { name = "PartnerLive2D record" };
            var args = string.Format("-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgba -s {0}x{1} -r {2} -i - {3}" +
                "-c:v libx264 -preset veryfast -crf {4} -pix_fmt yuv420p -movflags +faststart \"{5}\"", _width, _height, _fps,
                Json.GetBool(body, "vflip") ? "-vf vflip " : "", crf, path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            lock (FfmpegLog) FfmpegLog.Length = 0;
            _ffmpeg = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo(ffmpeg, args)
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true } };
            _ffmpeg.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (FfmpegLog) { if (FfmpegLog.Length < 4000) FfmpegLog.AppendLine(e.Data); } };
            _ffmpeg.Start();
            _ffmpeg.BeginErrorReadLine();
            _path = path; _error = null; _captured = _written = _dropped = 0; _inFlight = 0; _stopping = false; _finished = false;
            lock (Gate) { Frames.Clear(); Free.Clear(); }
            Clock.Reset(); Clock.Start();
            _on = true;
            _writer = new Thread(Write) { IsBackground = true, Name = "PartnerLive2D record writer" };
            _writer.Start();
            MelonCoroutines.Start(Capture());
            Mod.Msg(string.Format("[test] recording {0}x{1} at {2} fps to {3}", _width, _height, _fps, path));
            return Info();
        }

        static object Stop()
        {
            if (!_on) return Info();
            _on = false; Clock.Stop();
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (_full != null) { _full.Release(); UnityEngine.Object.Destroy(_full); _full = null; }
            if (_blit != null) { _blit.Release(); _blit = null; }
            lock (Gate) { _stopping = true; System.Threading.Monitor.PulseAll(Gate); }
            new Thread(() =>
            {
                try
                {
                    if (!_writer.Join(60000)) _error = "writer did not finish";
                    _ffmpeg.StandardInput.Close();
                    if (!_ffmpeg.WaitForExit(300000)) _error = "ffmpeg did not finish";
                    else if (_ffmpeg.ExitCode != 0) _error = "ffmpeg exit " + _ffmpeg.ExitCode;
                }
                catch (Exception e) { _error = e.Message; }
                _finished = true;
                Mod.Msg(string.Format("[test] recording finished: {0} frames captured, {1} written, {2} dropped{3}", _captured, _written, _dropped, _error == null ? "" : ", " + _error));
            }) { IsBackground = true, Name = "PartnerLive2D record finish" }.Start();
            return Info();
        }

        static Dictionary<string, object> Info()
        {
            string log; lock (FfmpegLog) log = FfmpegLog.ToString();
            return new Dictionary<string, object> { { "recording", _on }, { "finished", _finished }, { "path", _path }, { "width", _width }, { "height", _height },
                { "fps", _fps }, { "seconds", Math.Round(Clock.Elapsed.TotalSeconds, 2) }, { "captured", _captured }, { "written", _written }, { "dropped", _dropped },
                { "error", _error }, { "ffmpeg", log }, { "queueLimit", _queueLimit } };
        }

        static IEnumerator Capture()
        {
            var end = new WaitForEndOfFrame();
            while (_on)
            {
                yield return end;
                if (!_on) break;
                if (_inFlight > 4) { _dropped++; continue; }
                _blit.Clear();
                _blit.Blit(BuiltinRenderTextureType.CurrentActive, _full);
                _blit.Blit(_full, _rt);
                Graphics.ExecuteCommandBuffer(_blit);
                double t = Clock.Elapsed.TotalSeconds;
                _inFlight++;
                AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32, r => Arrived(r, t));
            }
        }

        static void Arrived(AsyncGPUReadbackRequest r, double t)
        {
            _inFlight--;
            if (!_on || r.hasError) { if (_on) _dropped++; return; }
            byte[] buffer;
            lock (Gate) buffer = Free.Count > 0 ? Free.Pop() : null;
            if (buffer == null) buffer = new byte[_width * _height * 4];
            r.GetData<byte>().CopyTo(buffer);
            _captured++;
            lock (Gate)
            {
                if (Frames.Count >= _queueLimit) { _dropped++; Free.Push(buffer); return; }     // a low-end encoder must not keep hundreds of MB alive
                Frames.Enqueue(new KeyValuePair<double, byte[]>(t, buffer));
                System.Threading.Monitor.Pulse(Gate);
            }
        }

        static void Write()
        {
            var pipe = _ffmpeg.StandardInput.BaseStream;
            byte[] last = null; double start = -1;
            try
            {
                while (true)
                {
                    KeyValuePair<double, byte[]> f;
                    lock (Gate)
                    {
                        while (Frames.Count == 0 && !_stopping) System.Threading.Monitor.Wait(Gate);
                        if (Frames.Count == 0) break;
                        f = Frames.Dequeue();
                    }
                    if (start < 0) start = f.Key;
                    long slot = (long)Math.Floor((f.Key - start) * _fps);
                    // the previous frame stays on screen until this one: repeat it for every slot it covers
                    while (last != null && _written < slot) { pipe.Write(last, 0, last.Length); _written++; }
                    if (last != null) lock (Gate) Free.Push(last);
                    last = f.Value;
                }
                if (last != null) { pipe.Write(last, 0, last.Length); _written++; }
                pipe.Flush();
            }
            catch (Exception e) { _error = "writer: " + e.Message; }
        }
    }
}
