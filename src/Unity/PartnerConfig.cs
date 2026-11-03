using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PartnerLive2D.Unity
{
    // One custom partner = one folder under Package\LocalAssets\PartnerLive2D\<name>\ with a partner.ini.
    // Format: "key = value" lines, '#' comments. Paths are relative to that folder.
    // Values are kept as raw strings (Values) so the control API can edit them and Save() can write
    // them back into the same file without losing comments or ordering.
    public sealed class PartnerConfig
    {
        public string Folder;
        public string Name;
        public string IniPath => Path.Combine(Folder, "partner.ini");

        public string SpritePath;
        public string LibraryPath;               // music select and result screen from the frame library (library.json); empty = the sprite clips
        public string ModelPath;                 // absolute path of *.model3.json
        public int NaviCharaId = -1;             // navi chara id this model draws for
        public int PartnerId = -1;               // partner id whose icon/result image we supply (optional)
        public string IconPath, ResultImagePath; // absolute, may be null

        // NavigationAnime -> motion group; "" = keep idle
        public readonly Dictionary<string, string> Motions = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Expressions = new Dictionary<string, string>();
        public string TapGroup = "TapBody";
        public string IdleGroup = "Idle";

        public float Scale = 1f;          // relative to the original partner's height
        public float OffsetX, OffsetY;    // in units of the original partner's height
        public float RoomScale = 1f, RoomOffsetX, RoomOffsetY;   // the same for the collection's partner room
        public float SpriteBlend = 0.06f; // sprite pairs: seconds the next drawing fades in before its turn (drawings are on threes); 0 = hard cuts
        public int RenderSize = 1024;     // longest side of the offscreen texture
        public bool FlipY;
        public float LookStrength = 1f;   // 0 disables look-at following touches
        public string ResultUnder = "";   // result screen: comma list of result-monitor objects (name or path end) drawn under the partner
        public bool DebugDump;            // write debug_model_N.png / debug_screen_N.png into the folder (3 s, 9 s, 15 s after shown)
        public bool DebugOverlay;         // show the model on its own overlay canvas ~20 s after boot, cycling states

        // raw "key -> value" exactly as written in partner.ini (plus edits made through the API)
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();

        public static readonly string[] States = { "Default", "Welcom", "FunStart", "FunStartLoop", "Sad01" };

        // keys whose change needs the model/renderer rebuilt rather than a layout refresh
        public static bool NeedsRebuild(string key) => key == "model" || key == "renderSize" || key == "sprites" || key == "library";

        public static List<PartnerConfig> LoadAll(string root, Action<string> log)
        {
            var list = new List<PartnerConfig>();
            if (!Directory.Exists(root)) return list;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var ini = Path.Combine(dir, "partner.ini");
                if (!File.Exists(ini)) continue;
                try
                {
                    var c = Load(dir);
                    if (c.ModelPath == null || !File.Exists(c.ModelPath)) { log("skip " + dir + ": model not found"); continue; }
                    if (c.NaviCharaId < 0) { log("skip " + dir + ": naviCharaId missing"); continue; }
                    list.Add(c);
                }
                catch (Exception e) { log("skip " + dir + ": " + e.Message); }
            }
            return list;
        }

        public static PartnerConfig Load(string dir)
        {
            var c = new PartnerConfig { Folder = dir, Name = Path.GetFileName(dir) };
            c.Motions["Default"] = "Idle";
            c.Motions["Welcom"] = "";
            c.Motions["FunStart"] = "TapBody";
            c.Motions["FunStartLoop"] = "";
            c.Motions["Sad01"] = "";
            foreach (var kv in ReadIni(c.IniPath)) c.Set(kv.Key, kv.Value);
            return c;
        }

        static List<KeyValuePair<string, string>> ReadIni(string ini)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var raw in File.ReadAllLines(ini))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                result.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
            }
            return result;
        }

        // apply one key; throws FormatException on a bad value (nothing is changed then)
        public void Set(string key, string val)
        {
            val = val == null ? "" : val.Trim();
            switch (key)
            {
                case "model": ModelPath = Full(Folder, val); break;
                case "sprites": try { SpritePath = Full(Folder, val); } catch (Exception) { SpritePath = val; } break;
                case "library": try { LibraryPath = Full(Folder, val); } catch (Exception) { LibraryPath = val; } break;
                case "naviCharaId": NaviCharaId = I(val); break;
                case "partnerId": PartnerId = I(val); break;
                case "icon": IconPath = Full(Folder, val); break;
                case "resultImage": ResultImagePath = Full(Folder, val); break;
                case "tap": TapGroup = val; break;
                case "idle": IdleGroup = val; break;
                case "scale": Scale = Clamp(F(val), 0.05f, 10f); break;
                case "offsetX": OffsetX = Clamp(F(val), -5f, 5f); break;
                case "offsetY": OffsetY = Clamp(F(val), -5f, 5f); break;
                case "roomScale": RoomScale = Clamp(F(val), 0.05f, 10f); break;
                case "roomOffsetX": RoomOffsetX = Clamp(F(val), -5f, 5f); break;
                case "roomOffsetY": RoomOffsetY = Clamp(F(val), -5f, 5f); break;
                case "spriteBlend": SpriteBlend = Clamp(F(val), 0f, 0.5f); break;
                case "renderSize": RenderSize = Math.Max(64, Math.Min(4096, I(val))); break;
                case "flipY": FlipY = B(val); break;
                case "lookStrength": LookStrength = Clamp(F(val), 0f, 3f); break;
                case "resultUnder": ResultUnder = val; break;
                case "debugDump": DebugDump = B(val); break;
                case "debugOverlay": DebugOverlay = B(val); break;
                default:
                    if (key.StartsWith("motion.")) Motions[key.Substring(7)] = val;
                    else if (key.StartsWith("expression.")) Expressions[key.Substring(11)] = val;
                    else throw new FormatException("unknown key '" + key + "'");
                    break;
            }
            Values[key] = val;
        }

        // write Values back into partner.ini: existing "key = ..." lines are replaced in place,
        // new keys are appended; comments and order are kept
        public void Save()
        {
            var lines = File.Exists(IniPath) ? new List<string>(File.ReadAllLines(IniPath)) : new List<string>();
            var written = new HashSet<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.Length == 0 || t[0] == '#') continue;
                int eq = t.IndexOf('=');
                if (eq < 0) continue;
                var key = t.Substring(0, eq).Trim();
                string v;
                if (Values.TryGetValue(key, out v)) { lines[i] = key + " = " + v; written.Add(key); }
            }
            foreach (var kv in Values)
                if (!written.Contains(kv.Key)) lines.Add(kv.Key + " = " + kv.Value);
            File.WriteAllLines(IniPath, lines.ToArray());
        }

        public Dictionary<string, object> ToDict()
        {
            var values = new Dictionary<string, object>();
            foreach (var kv in Values) values[kv.Key] = kv.Value;
            var motions = new Dictionary<string, object>();
            foreach (var kv in Motions) motions[kv.Key] = kv.Value;
            var expressions = new Dictionary<string, object>();
            foreach (var kv in Expressions) expressions[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            {
                { "name", Name }, { "folder", Folder }, { "modelPath", ModelPath },
                { "naviCharaId", NaviCharaId }, { "partnerId", PartnerId },
                { "scale", Scale }, { "offsetX", OffsetX }, { "offsetY", OffsetY },
                { "roomScale", RoomScale }, { "roomOffsetX", RoomOffsetX }, { "roomOffsetY", RoomOffsetY },
                { "spriteBlend", SpriteBlend }, { "renderSize", RenderSize }, { "flipY", FlipY }, { "lookStrength", LookStrength },
                { "tap", TapGroup }, { "idle", IdleGroup }, { "resultUnder", ResultUnder }, { "library", LibraryPath },
                { "motions", motions }, { "expressions", expressions }, { "values", values },
            };
        }

        static string Full(string dir, string rel) => string.IsNullOrEmpty(rel) ? null : Path.GetFullPath(Path.Combine(dir, rel));
        static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        static int I(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        static bool B(string s) => s == "true" || s == "1" || s == "True";
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
