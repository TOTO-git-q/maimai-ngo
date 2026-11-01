using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using MelonLoader;
using PartnerLive2D.Framework;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    static class JsonWriter
    {
        public static string Write(object o)
        {
            var sb = new StringBuilder();
            W(sb, o);
            return sb.ToString();
        }

        static void W(StringBuilder sb, object o)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { Str(sb, (string)o); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is float || o is double)
            {
                double d = Convert.ToDouble(o, CultureInfo.InvariantCulture);
                sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (o is int || o is long || o is short || o is byte || o is uint) { sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); return; }
            var dict = o as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Str(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    W(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            var list = o as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list) { if (!first) sb.Append(','); first = false; W(sb, item); }
                sb.Append(']');
                return;
            }
            Str(sb, o.ToString());
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    sealed class ApiError : Exception
    {
        public readonly int Status;
        public ApiError(int status, string message) : base(message) { Status = status; }
    }

    // Local control API + tuning page. Listens on 127.0.0.1 only.
    // Every non-GET request must carry the header "X-PartnerLive2D: 1" (blocks cross-site form posts
    // from web pages open in the browser); requests with a foreign Origin are rejected.
    sealed class ControlServer
    {
        readonly int _port;
        HttpListener _listener;
        Thread _thread;

        public ControlServer(int port) { _port = port; }

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + _port + "/");
            _listener.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "PartnerLive2D-Control" };
            _thread.Start();
            Mod.Msg("control page: http://127.0.0.1:" + _port + "/");
        }

        void Loop()
        {
            while (_listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            try
            {
                var origin = req.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin) && origin != "http://127.0.0.1:" + _port && origin != "http://localhost:" + _port)
                    throw new ApiError(403, "foreign origin");
                if (req.HttpMethod != "GET" && req.Headers["X-PartnerLive2D"] != "1")
                    throw new ApiError(403, "missing X-PartnerLive2D header");

                var path = req.Url.AbsolutePath.TrimEnd('/');
                if (path == "" ) { SendBytes(res, 200, "text/html; charset=utf-8", Page()); return; }

                byte[] png;
                var result = Route(req.HttpMethod, path, ReadBody(req), req.QueryString, out png);
                if (png != null) SendBytes(res, 200, "image/png", png);
                else SendJson(res, 200, result);
            }
            catch (ApiError e) { SendJson(res, e.Status, Err(e.Message)); }
            catch (TimeoutException e) { SendJson(res, 503, Err(e.Message)); }
            catch (Exception e) { SendJson(res, 500, Err(e.GetType().Name + ": " + e.Message)); }
        }

        static Dictionary<string, object> Err(string msg) => new Dictionary<string, object> { { "ok", false }, { "error", msg } };

        static object ReadBody(HttpListenerRequest req)
        {
            if (!req.HasEntityBody) return null;
            using (var r = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                var text = r.ReadToEnd();
                return string.IsNullOrWhiteSpace(text) ? null : Json.Parse(text);
            }
        }

        static void SendJson(HttpListenerResponse res, int status, object body)
        {
            var dict = body as Dictionary<string, object>;
            if (dict != null && !dict.ContainsKey("ok")) dict["ok"] = status < 400;
            SendBytes(res, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(JsonWriter.Write(body)));
        }

        static void SendBytes(HttpListenerResponse res, int status, string type, byte[] bytes)
        {
            try
            {
                res.StatusCode = status;
                res.ContentType = type;
                res.Headers["Cache-Control"] = "no-store";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch { }
            finally { try { res.Close(); } catch { } }
        }

        static byte[] _page;
        static byte[] Page()
        {
            if (_page != null) return _page;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PartnerLive2D.ui.html"))
            using (var ms = new MemoryStream())
            {
                if (s == null) return Encoding.UTF8.GetBytes("ui.html missing from the mod build");
                s.CopyTo(ms);
                _page = ms.ToArray();
                return _page;
            }
        }

        // ---- routing ------------------------------------------------------------------------

        static object Route(string method, string path, object body, System.Collections.Specialized.NameValueCollection query, out byte[] png)
        {
            png = null;
            var seg = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (seg.Length < 2 || seg[0] != "api") throw new ApiError(404, "not found: " + path);

            switch (seg[1])
            {
                case "theme":
                    if (seg.Length != 2) throw new ApiError(404, "unknown theme route");
                    if (method == "GET") return MainThread.Call(() => ThemeOverride.Status());
                    Need(method, "POST");
                    object enabled = Json.Get(body, "enabled");
                    if (!(enabled is bool)) throw new ApiError(400, "enabled must be a boolean");
                    return MainThread.Call(() => ThemeOverride.SetEnabled((bool)enabled));

                case "music-select":
                    if (seg.Length == 2) { Need(method, "GET"); return MainThread.Call(() => MusicSelectSession.Info()); }
                    if (seg.Length == 3 && seg[2] == "tree")
                    {
                        Need(method, "GET");
                        int depth = 3, player = -1;
                        if (query["depth"] != null && (!int.TryParse(query["depth"], out depth) || depth < 0 || depth > 20)) throw new ApiError(400, "depth must be 0..20");
                        if (query["player"] != null && (!int.TryParse(query["player"], out player) || player < 0 || player > 1)) throw new ApiError(400, "player must be 0 or 1");
                        return MainThread.Call(() => MusicSelectSession.Tree(query["root"], depth, player));
                    }
                    if (seg.Length == 3 && seg[2] == "sprite.png")
                    {
                        Need(method, "GET");
                        int player = -1;
                        if (query["player"] != null && (!int.TryParse(query["player"], out player) || player < 0 || player > 1)) throw new ApiError(400, "player must be 0 or 1");
                        png = MainThread.Call(() => MusicSelectSession.SpritePng(query["path"], player)); return null;
                    }
                    Need(method, "POST");
                    if (seg.Length != 3) throw new ApiError(404, "unknown music-select route");
                    switch (seg[2])
                    {
                        case "node": return MainThread.Call(() => MusicSelectSession.TuneNode(body));
                        case "image": return MainThread.Call(() => MusicSelectSession.TuneImage(body));
                        case "layout": return MainThread.Call(() => MusicSelectSession.Tune(body));
                        case "reset": return MainThread.Call(() => MusicSelectSession.Reset());
                        case "save": return MainThread.Call(() => MusicSelectSession.Save());
                        default: throw new ApiError(404, "unknown music-select action");
                    }

                case "test":
                    if (seg.Length == 3 && seg[2] == "processes")
                    {
                        int since = 0;
                        if (query["since"] != null) int.TryParse(query["since"], out since);
                        return TestHarness.ProcessLog(since);
                    }
                    if (seg.Length == 3 && seg[2] == "key") { Need(method, "POST"); return TestHarness.Key(body); }
                    if (seg.Length == 3 && seg[2] == "autoplay") { Need(method, "POST"); return MainThread.Call(() => TestHarness.Autoplay(body)); }
                    if (seg.Length == 3 && seg[2] == "touch") { Need(method, "POST"); return MainThread.Call(() => TestHarness.Touch(body)); }
                    if (seg.Length == 3 && seg[2] == "rank") { Need(method, "POST"); return MainThread.Call(() => TestHarness.ForceRank(body)); }
                    if (seg.Length == 3 && seg[2] == "quicklogin") return TestHarness.QuickLogin();
                    if (seg.Length == 3 && seg[2] == "entry") return MainThread.Call(() => TestHarness.EntryState());
                    if (seg.Length == 3 && seg[2] == "music")
                    {
                        int find = 0;
                        if (query["find"] != null) int.TryParse(query["find"], out find);
                        return MainThread.Call(() => MusicSelectSession.MusicProbe(find));
                    }
                    if (seg.Length == 3 && seg[2] == "ui") return MainThread.Call(() => TestHarness.UiDump());
                    if (seg.Length == 3 && seg[2] == "tree")
                    {
                        int up = 0, depth = 4;
                        if (query["up"] != null && !int.TryParse(query["up"], out up)) throw new ApiError(400, "up must be a number");
                        if (query["depth"] != null && (!int.TryParse(query["depth"], out depth) || depth < 0 || depth > 30)) throw new ApiError(400, "depth must be 0..30");
                        return MainThread.Call(() => TestHarness.Tree(query["of"], up, depth));
                    }
                    if (seg.Length == 3 && seg[2] == "renderers") return MainThread.Call(() => TestHarness.RendererDump());
                    if (seg.Length == 3 && seg[2] == "selectperf") return TestHarness.SelectPerf.Dump(query["reset"] == "1");
                    if (seg.Length == 3 && seg[2] == "heapwalk") { Need(method, "POST"); return MainThread.Call(() => HeapWalk.Run(), 600000); }
                    throw new ApiError(404, "unknown test route");

                case "status":
                    return MainThread.Call(() => Status());

                case "rescan":
                    Need(method, "POST");
                    return MainThread.Call(() => { Mod.Rescan(); return Status(); });

                case "screenshot.png":
                    png = MainThread.CallAsync<byte[]>(done => MelonCoroutines.Start(CaptureScreen(done)));
                    return null;

                case "partners":
                    if (seg.Length == 2) return MainThread.Call(() => Partners());
                    return PartnerRoute(method, seg, body);

                case "views":
                    if (seg.Length == 2) return MainThread.Call(() => Views());
                    return ViewRoute(method, seg, body, out png);
            }
            throw new ApiError(404, "not found: " + path);
        }

        static void Need(string method, string expected)
        {
            if (method != expected) throw new ApiError(405, "use " + expected);
        }

        static Dictionary<string, object> Status()
        {
            return new Dictionary<string, object>
            {
                { "version", Mod.Version },
                { "screen", new Dictionary<string, object> { { "width", Screen.width }, { "height", Screen.height } } },
                { "partners", Partners()["partners"] },
                { "views", Views()["views"] },
                { "states", new List<object>(PartnerConfig.States) },
                { "musicSelect", MusicSelectSession.Info() },
            };
        }

        static Dictionary<string, object> Partners()
        {
            var list = new List<object>();
            foreach (var c in Mod.Configs)
            {
                var d = c.ToDict();
                d["model"] = ModelInfo(c);
                var live = new List<object>();
                foreach (var v in PartnerView.All) if (v != null && v.Config == c) live.Add(v.Id);
                d["views"] = live;
                list.Add(d);
            }
            return new Dictionary<string, object> { { "partners", list } };
        }

        static Dictionary<string, object> Views()
        {
            var list = new List<object>();
            foreach (var v in PartnerView.All) if (v != null) list.Add(v.Info());
            return new Dictionary<string, object> { { "views", list } };
        }

        // motion groups / expressions / hit areas straight from model3.json (no model load needed)
        static Dictionary<string, object> ModelInfo(PartnerConfig c)
        {
            var info = new Dictionary<string, object>();
            try
            {
                var root = Json.ParseFile(c.ModelPath);
                var groups = new Dictionary<string, object>();
                var motions = Json.Path(root, "FileReferences", "Motions") as IDictionary<string, object>;
                if (motions != null) foreach (var kv in motions) groups[kv.Key] = (kv.Value as List<object>)?.Count ?? 0;
                var exprs = new List<object>();
                var el = Json.Path(root, "FileReferences", "Expressions") as List<object>;
                if (el != null) foreach (var e in el) exprs.Add(Json.GetString(e, "Name"));
                var hits = new List<object>();
                var hl = Json.GetList(root, "HitAreas");
                if (hl != null) foreach (var h in hl) hits.Add(Json.GetString(h, "Name"));
                info["motionGroups"] = groups;
                info["expressions"] = exprs;
                info["hitAreas"] = hits;
            }
            catch (Exception e) { info["error"] = e.Message; }
            return info;
        }

        static PartnerConfig NeedPartner(string name)
        {
            foreach (var c in Mod.Configs) if (c.Name == name) return c;
            throw new ApiError(404, "no partner named '" + name + "'");
        }

        static object PartnerRoute(string method, string[] seg, object body)
        {
            var name = Uri.UnescapeDataString(seg[2]);
            var action = seg.Length > 3 ? seg[3] : "";
            switch (action)
            {
                case "":
                    return MainThread.Call(() =>
                    {
                        var c = NeedPartner(name);
                        var d = c.ToDict();
                        d["model"] = ModelInfo(c);
                        return d;
                    });

                case "config":
                    Need(method, "POST");
                    return MainThread.Call(() => EditConfig(NeedPartner(name), body));

                case "save":
                    Need(method, "POST");
                    return MainThread.Call(() => { var c = NeedPartner(name); c.Save(); return (object)c.ToDict(); });

                case "reload":
                    Need(method, "POST");
                    return MainThread.Call(() => { var c = Mod.ReloadPartner(NeedPartner(name)); return (object)c.ToDict(); });

                case "preview":
                    Need(method, "POST");
                    bool show = body == null || Json.GetBool(body, "show", true);
                    return MainThread.Call(() =>
                    {
                        var c = NeedPartner(name);
                        foreach (var v in PartnerView.All.ToArray()) if (v != null && v.Context == "overlay" && v.Config == c) v.Close();
                        if (!show) return (object)new Dictionary<string, object> { { "closed", true } };
                        var view = PartnerView.AttachOverlay(c, false);
                        if (view == null) throw new ApiError(500, "model failed to load, see MelonLoader log");
                        return view.Info();
                    });
            }
            throw new ApiError(404, "unknown partner action '" + action + "'");
        }

        // body: {"values": {"scale": "1.2", "motion.Sad01": "Idle", ...}, "save": false}
        static object EditConfig(PartnerConfig c, object body)
        {
            var values = Json.Get(body, "values") as IDictionary<string, object>;
            if (values == null) throw new ApiError(400, "body needs {\"values\": {key: value}}");
            var errors = new Dictionary<string, object>();
            bool rebuild = false;
            foreach (var kv in values)
            {
                try
                {
                    var s = kv.Value == null ? "" : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                    if (kv.Value is bool) s = (bool)kv.Value ? "true" : "false";
                    c.Set(kv.Key, s);
                    if (PartnerConfig.NeedsRebuild(kv.Key)) rebuild = true;
                }
                catch (Exception e) { errors[kv.Key] = e.Message; }
            }
            foreach (var v in PartnerView.All) if (v != null && v.Config == c) v.OnConfigChanged(rebuild);
            if (Json.GetBool(body, "save", false)) c.Save();
            var d = c.ToDict();
            d["errors"] = errors;
            return d;
        }

        static PartnerView NeedView(string idText)
        {
            int id;
            if (!int.TryParse(idText, out id)) throw new ApiError(400, "bad view id");
            var v = PartnerView.FindById(id);
            if (v == null) throw new ApiError(404, "no live view #" + id + " (it disappears when the screen changes)");
            return v;
        }

        static object ViewRoute(string method, string[] seg, object body, out byte[] png)
        {
            png = null;
            var idText = seg[2];
            var action = seg.Length > 3 ? seg[3] : "";
            switch (action)
            {
                case "":
                    return MainThread.Call(() => NeedView(idText).Info());
                case "frame.png":
                    png = MainThread.Call(() => NeedView(idText).FramePng());
                    return null;
            }
            Need(method, "POST");
            return MainThread.Call<object>(() =>
            {
                var v = NeedView(idText);
                switch (action)
                {
                    case "state":
                        var state = Json.GetString(body, "state");
                        if (Array.IndexOf(PartnerConfig.States, state) < 0) throw new ApiError(400, "state must be one of " + string.Join(", ", PartnerConfig.States));
                        v.PreviewState(state);
                        break;
                    case "motion":
                        var group = Json.GetString(body, "group");
                        if (string.IsNullOrEmpty(group)) throw new ApiError(400, "group required");
                        if (!v.PlayMotion(group, Json.GetInt(body, "index", -1))) throw new ApiError(409, "motion refused (unknown group/index or a higher-priority motion is running)");
                        break;
                    case "tap": v.OnTap(); break;
                    case "expression": v.SetExpression(Json.GetString(body, "name")); break;
                    case "look": v.LookAt(Json.GetFloat(body, "x"), Json.GetFloat(body, "y"), Json.GetFloat(body, "seconds", 3f)); break;
                    case "close": v.Close(); return new Dictionary<string, object> { { "closed", true } };
                    default: throw new ApiError(404, "unknown view action '" + action + "'");
                }
                return v.Info();
            });
        }

        static IEnumerator CaptureScreen(Action<byte[]> done)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var png = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);
            done(png);
        }

        public void Stop()
        {
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }
    }
}
