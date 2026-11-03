using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PartnerLive2D.Framework;

namespace PartnerLive2D.Unity
{
    // No Unity dependency: validation and persistence also run in the headless harness.
    public sealed class MusicSelectSettings
    {
        public bool Enabled;
        public string SelectPartner = "equipped";
        public bool UseEquippedPartner => SelectPartner == "equipped";
        public bool UseStockPartner => SelectPartner == "stock";
        public bool ShouldUseStockPartner(bool equippedCustomFound) => UseStockPartner || (UseEquippedPartner && !equippedCustomFound);
        public bool DebugTouchZones;
        public bool EnterTransition = true;
        public HashSet<string> SelectTapAreas; // null means derive from the current on-screen rectangle
        public bool CategoryKeys = true;
        public bool HideMatching = true; // settings.ini hideMatching: drop the in-store matching (LAN party) category
        public readonly Dictionary<string, float[]> Groups = new Dictionary<string, float[]>
        {
            { "chains", new[] { .55f, 0f, -200f } },
            { "tabs", new[] { .45f, 0f, -140f } },
            { "selector", new[] { .55f, 0f, -220f } },
            { "status", new[] { .55f, 0f, -160f } },
            { "myStatus", new[] { .55f, 0f, -220f } },
            { "difficultyHeader", new[] { 1f, 0f, 0f } },
            { "achievementRate", new[] { 1f, 0f, 0f } },
            { "rivalData", new[] { 1f, 0f, 0f } },
            { "extraInfo", new[] { 1f, 0f, 0f } },
            { "festaDeco", new[] { 1f, 0f, 0f } },
            { "challengeBG", new[] { 1f, 0f, 0f } },
            { "challengeDeco", new[] { 1f, 0f, 0f } }
        };
        public readonly Dictionary<string, bool> GroupsHidden = new Dictionary<string, bool>();
        public readonly Dictionary<string, MusicSelectNodeOptions> Nodes = new Dictionary<string, MusicSelectNodeOptions>();
        public readonly Dictionary<string, MusicSelectImageOptions> Images = new Dictionary<string, MusicSelectImageOptions>();
        public float CropBottom;
        public MusicSelectSettings() { foreach (var key in Groups.Keys) GroupsHidden[key] = false; }
        public float BottomX, BottomY = -20, Height = 500;
        public float DifficultyOffsetY;   // partner moved by this in the Difficulty phase, where the difficulty header drops in
        public string PartnerUnder = "";  // comma list of Main paths drawn under the partner (own canvas below the screen's)
        public float MyStatusX = -200, MyStatusY = -40;
        public float LabelX = 300, LabelY = -30, LabelWidth = 130, LabelHeight = 36;
        public string TabLabelColor = "";   // "" = keep the game's own label colour (stock style)
        public float TabLabelOffsetY, TabTitleOffsetY;   // 0: the tab art follows the stock geometry

        public MusicSelectSettings With(object body)
        {
            var next = new MusicSelectSettings();
            next.CopyFrom(this);
            var d = Dict(body);
            Keys(d, "enabled", "selectPartner", "groups", "nodes", "images", "partner", "myStatusContent", "difficultyLabel", "phases", "debugTouchZones", "categoryKeys", "tabLabelColor", "tabLabelOffsetY", "tabTitleOffsetY", "save");
            if (d.ContainsKey("tabLabelColor"))
            {
                var color = d["tabLabelColor"] as string;
                if (color == "") { next.TabLabelColor = color; color = null; }
                else if (color == null || color.Length != 7 || color[0] != '#') throw new FormatException("tabLabelColor must be #RRGGBB or empty");
                if (color != null)
                {
                for (int i = 1; i < color.Length; i++)
                    if (!Uri.IsHexDigit(color[i])) throw new FormatException("tabLabelColor must be #RRGGBB");
                next.TabLabelColor = color;
                }
            }
            Read(d, "tabLabelOffsetY", ref next.TabLabelOffsetY, -540, 540);
            Read(d, "tabTitleOffsetY", ref next.TabTitleOffsetY, -540, 540);
            if (d.ContainsKey("images")) foreach (var image in Dict(d["images"]))
            {
                ValidateImageId(image.Key);
                if (image.Value == null) { next.Images.Remove(image.Key); continue; }
                MusicSelectImageOptions prior;
                next.Images.TryGetValue(image.Key, out prior);
                next.Images[image.Key] = ReadImage(prior, image.Value);
            }
            if (next.Images.Count > 64) throw new FormatException("at most 64 overlay images are supported");
            if (d.ContainsKey("enabled")) next.Enabled = Bool(d["enabled"]);
            if (d.ContainsKey("selectPartner"))
            {
                if (!(d["selectPartner"] is string)) throw new FormatException("selectPartner must be a string");
                next.SelectPartner = (string)d["selectPartner"];
                if (next.SelectPartner.Length > 100) throw new FormatException("selectPartner too long");
                foreach (char ch in next.SelectPartner) if (char.IsControl(ch)) throw new FormatException("selectPartner contains control characters");
            }
            if (d.ContainsKey("categoryKeys")) next.CategoryKeys = Bool(d["categoryKeys"]);
            if (d.ContainsKey("debugTouchZones")) next.DebugTouchZones = Bool(d["debugTouchZones"]);
            if (d.ContainsKey("save")) Bool(d["save"]);
            if (d.ContainsKey("phases"))
            {
                var p = d["phases"] as List<object>;
                if (p == null || p.Count != 1 || !Equals(p[0], "Music")) throw new FormatException("only phases: [Music] is supported");
            }
            if (d.ContainsKey("groups")) foreach (var g in Dict(d["groups"]))
            {
                if (!next.Groups.ContainsKey(g.Key)) throw new FormatException("unknown group: " + g.Key);
                var v = Dict(g.Value); Keys(v, "scale", "offsetX", "offsetY", "hidden");
                Read(v, "scale", ref next.Groups[g.Key][0], .35f, 1.2f);
                Read(v, "offsetX", ref next.Groups[g.Key][1], -540, 540);
                Read(v, "offsetY", ref next.Groups[g.Key][2], -540, 540);
                if (v.ContainsKey("hidden")) next.GroupsHidden[g.Key] = Bool(v["hidden"]);
            }
            if (d.ContainsKey("nodes")) foreach (var node in Dict(d["nodes"]))
            {
                MusicSelectNodeOptions prior;
                if (!next.Nodes.TryGetValue(node.Key, out prior)) prior = new MusicSelectNodeOptions();
                var values = Dict(node.Value);
                bool spriteOnly = values.Count == 1 && values.ContainsKey("sprite") && prior.IsSpriteOnly;
                ValidateNodePath(node.Key, spriteOnly);
                next.Nodes[node.Key] = ReadNode(prior, node.Value);
            }
            if (d.ContainsKey("partner"))
            {
                var v = Dict(d["partner"]); Keys(v, "bottomX", "bottomY", "height", "cropBottom", "difficultyOffsetY", "under");
                if (v.ContainsKey("under"))
                {
                    var under = v["under"] as string;
                    if (under == null || under.Length > 1000) throw new FormatException("partner.under must be a comma list of Main paths");
                    foreach (var path in under.Split(',')) if (path.Trim().Length != 0) ValidateNodePath(path.Trim(), false);
                    next.PartnerUnder = under;
                }
                Read(v, "difficultyOffsetY", ref next.DifficultyOffsetY, -800, 800);
                Read(v, "bottomX", ref next.BottomX, -600, 600);
                Read(v, "bottomY", ref next.BottomY, -1400, 400);
                Read(v, "height", ref next.Height, 100, 2400);
                Read(v, "cropBottom", ref next.CropBottom, 0, 1);
            }
            if (d.ContainsKey("myStatusContent"))
            {
                var v = Dict(d["myStatusContent"]); Keys(v, "offsetX", "offsetY");
                Read(v, "offsetX", ref next.MyStatusX, -540, 540);
                Read(v, "offsetY", ref next.MyStatusY, -540, 540);
            }
            if (d.ContainsKey("difficultyLabel"))
            {
                var v = Dict(d["difficultyLabel"]); Keys(v, "x", "y", "width", "height");
                Read(v, "x", ref next.LabelX, -500, 500); Read(v, "y", ref next.LabelY, -500, 500);
                Read(v, "width", ref next.LabelWidth, 80, 400); Read(v, "height", ref next.LabelHeight, 24, 120);
            }
            return next; // caller commits only this fully validated copy
        }

        void CopyFrom(MusicSelectSettings c)
        {
            EnterTransition = c.EnterTransition; SelectTapAreas = c.SelectTapAreas;
            CategoryKeys = c.CategoryKeys; HideMatching = c.HideMatching; Enabled = c.Enabled; SelectPartner = c.SelectPartner; DebugTouchZones = c.DebugTouchZones;
            foreach (var g in c.Groups) Groups[g.Key] = (float[])g.Value.Clone();
            foreach (var g in c.GroupsHidden) GroupsHidden[g.Key] = g.Value;
            foreach (var node in c.Nodes) Nodes[node.Key] = new MusicSelectNodeOptions(node.Value);
            foreach (var image in c.Images) Images[image.Key] = new MusicSelectImageOptions(image.Value);
            CropBottom = c.CropBottom;
            BottomX = c.BottomX; BottomY = c.BottomY; Height = c.Height; DifficultyOffsetY = c.DifficultyOffsetY; PartnerUnder = c.PartnerUnder;
            MyStatusX = c.MyStatusX; MyStatusY = c.MyStatusY;
            LabelX = c.LabelX; LabelY = c.LabelY; LabelWidth = c.LabelWidth; LabelHeight = c.LabelHeight;
            TabLabelColor = c.TabLabelColor; TabLabelOffsetY = c.TabLabelOffsetY; TabTitleOffsetY = c.TabTitleOffsetY;
        }

        public Dictionary<string, object> ToDict()
        {
            var groups = new Dictionary<string, object>();
            foreach (var g in Groups) groups[g.Key] = new Dictionary<string, object>
                { { "scale", g.Value[0] }, { "offsetX", g.Value[1] }, { "offsetY", g.Value[2] }, { "hidden", GroupsHidden[g.Key] } };
            var nodes = new Dictionary<string, object>();
            var images = new Dictionary<string, object>();
            foreach (var image in Images) images[image.Key] = image.Value.ToDict();
            foreach (var node in Nodes) nodes[node.Key] = IsSpriteLayerPath(node.Key)
                ? (object)new Dictionary<string, object> { { "sprite", node.Value.Sprite } } : node.Value.ToDict();
            return new Dictionary<string, object>
            {
                { "enabled", Enabled }, { "selectPartner", SelectPartner }, { "phases", new[] { "Music" } },
                { "tabLabelColor", TabLabelColor }, { "tabLabelOffsetY", TabLabelOffsetY }, { "tabTitleOffsetY", TabTitleOffsetY },
                { "groups", groups }, { "nodes", nodes }, { "images", images }, { "debugTouchZones", DebugTouchZones }, { "categoryKeys", CategoryKeys },
                { "partner", new Dictionary<string, object> { { "bottomX", BottomX }, { "bottomY", BottomY }, { "height", Height }, { "cropBottom", CropBottom }, { "difficultyOffsetY", DifficultyOffsetY }, { "under", PartnerUnder } } },
                { "myStatusContent", new Dictionary<string, object> { { "offsetX", MyStatusX }, { "offsetY", MyStatusY } } },
                { "difficultyLabel", new Dictionary<string, object> { { "x", LabelX }, { "y", LabelY }, { "width", LabelWidth }, { "height", LabelHeight } } }
            };
        }

        public static MusicSelectSettings Load(string root)
        {
            var c = new MusicSelectSettings();
            var path = Path.Combine(root, "select_layout.ini");
            // Dedicated INI stores one JSON value to share exactly the HTTP validation path.
            if (File.Exists(path)) foreach (var line in File.ReadAllLines(path))
                if (line.StartsWith("layout = ", StringComparison.Ordinal)) c = c.With(Json.Parse(line.Substring(9)));
            // Only settings.ini may opt in at startup, even if a preset contains enabled=true.
            c.Enabled = false;
            var settings = Path.Combine(root, "settings.ini");
            if (File.Exists(settings)) foreach (var raw in File.ReadAllLines(settings))
            {
                var line = raw.Trim(); if (line.StartsWith("#")) continue;
                int eq = line.IndexOf('='); if (eq < 0) continue;
                var key = line.Substring(0, eq).Trim(); var val = line.Substring(eq + 1).Trim();
                if (key == "enterTransition") c.EnterTransition = val != "off";
                if (key == "selectTapAreas")
                {
                    c.SelectTapAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var area in val.Split(',')) { var name = area.Trim(); if (name.Length != 0) c.SelectTapAreas.Add(name); }
                }
                if (key == "categoryKeys") c.CategoryKeys = val == "true";
                if (key == "hideMatching") c.HideMatching = val != "false";
                if (key == "selectView") c.Enabled = val == "true";
                if (key == "selectPartner") c.SelectPartner = val;
            }
            return c;
        }

        public void Save(string root, string json)
        {
            File.WriteAllText(Path.Combine(root, "select_layout.ini"), "# Music-select preset; positions in Main local pixels.\r\nlayout = " + json + "\r\n");
            var path = Path.Combine(root, "settings.ini");
            var lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();
            SetLine(lines, "categoryKeys", CategoryKeys ? "true" : "false");
            SetLine(lines, "selectView", Enabled ? "true" : "false"); SetLine(lines, "selectPartner", SelectPartner);
            File.WriteAllLines(path, lines.ToArray());
        }

        static void SetLine(List<string> lines, string key, string value)
        {
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim(); int eq = t.IndexOf('=');
                if (eq >= 0 && t.Substring(0, eq).Trim() == key) { lines[i] = key + " = " + value; found = true; }
            }
            if (!found) lines.Add(key + " = " + value);
        }
        static IDictionary<string, object> Dict(object o) => o as IDictionary<string, object> ?? throw new FormatException("expected JSON object");
        public MusicSelectSettings WithImage(object body)
        {
            var input = Dict(body);
            Keys(input, "id", "delete", "sprite", "parent", "siblingIndex", "x", "y", "width", "height", "hidden");
            object id;
            if (!input.TryGetValue("id", out id) || !(id is string)) throw new FormatException("image requires a string id");
            ValidateImageId((string)id);
            bool delete = input.ContainsKey("delete") && Bool(input["delete"]);
            var values = new Dictionary<string, object>();
            foreach (var pair in input) if (pair.Key != "id" && pair.Key != "delete") values.Add(pair.Key, pair.Value);
            if (delete && values.Count != 0) throw new FormatException("delete accepts only id and delete");
            return With(new Dictionary<string, object> { { "images", new Dictionary<string, object> { { (string)id, delete ? null : (object)values } } } });
        }
        static void ValidateImageId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) throw new FormatException("image id must contain 1..64 letters, digits, underscores or hyphens");
            foreach (char ch in id) if (!(ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9' || ch == '_' || ch == '-'))
                throw new FormatException("invalid image id");
        }
        static MusicSelectImageOptions ReadImage(MusicSelectImageOptions prior, object body)
        {
            var v = Dict(body); Keys(v, "sprite", "parent", "siblingIndex", "x", "y", "width", "height", "hidden");
            var next = prior == null ? new MusicSelectImageOptions() : new MusicSelectImageOptions(prior);
            if (v.ContainsKey("sprite")) next.Sprite = v["sprite"] as string;
            if (next.Sprite == null) throw new FormatException("overlay image requires a PNG sprite filename");
            ValidateSpriteName(next.Sprite);
            if (v.ContainsKey("parent")) next.Parent = v["parent"] as string;
            ValidateNodePath(next.Parent);
            if (v.ContainsKey("siblingIndex"))
            {
                object sibling = v["siblingIndex"];
                if (Equals(sibling, "first") || Equals(sibling, "last")) next.SiblingIndex = sibling;
                else
                {
                    if (!(sibling is double || sibling is float || sibling is int || sibling is long)) throw new FormatException("siblingIndex must be an integer, first or last");
                    double number = Convert.ToDouble(sibling, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 || number > int.MaxValue || number != Math.Floor(number))
                        throw new FormatException("siblingIndex must be a nonnegative integer, first or last");
                    next.SiblingIndex = (int)number;
                }
            }
            Read(v, "x", ref next.X, -10000, 10000); Read(v, "y", ref next.Y, -10000, 10000);
            Read(v, "width", ref next.Width, .01f, 8192); Read(v, "height", ref next.Height, .01f, 8192);
            if (v.ContainsKey("hidden")) next.Hidden = Bool(v["hidden"]);
            return next;
        }
        public static bool IsSpriteLayer(string name) => name == "UI_Buttons" || name == "UI_InfoButtons";
        public static bool IsSpriteLayerPath(string path)
        {
            int start = 0;
            while (start < path.Length)
            {
                int end = path.IndexOf('/', start);
                if (end < 0) end = path.Length;
                int length = end - start;
                if ((length == 10 && string.CompareOrdinal(path, start, "UI_Buttons", 0, length) == 0)
                    || (length == 14 && string.CompareOrdinal(path, start, "UI_InfoButtons", 0, length) == 0)) return true;
                start = end + 1;
            }
            return false;
        }
        // Shared live/headless policy. The exception applies only below the two known
        // hint layers, never to the layers themselves, arbitrary input layers or parents.
        public static void ValidateInputLayerAccess(string name, bool ancestor, bool spriteOnly, bool leafImage)
        {
            if (IsInputLayer(name) && !(IsSpriteLayer(name) && ancestor && spriteOnly && leafImage))
                throw new FormatException("input/touch layers are protected (only leaf Image sprites may be replaced)");
        }
        public static void ValidateNodePath(string path, bool spriteOnly = false)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 2048) throw new FormatException("invalid node path");
            foreach (char ch in path) if (char.IsControl(ch) || ch == '\\') throw new FormatException("invalid node path character");
            var parts = path.TrimStart('/').Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == "" || part == "." || part == "..") throw new FormatException("invalid node path segment");
                ValidateInputLayerAccess(part, i < parts.Length - 1, spriteOnly, true); // Image/leaf checked on live resolution.
            }
            var leaf = path.Substring(path.LastIndexOf('/') + 1);
            if (leaf == "Main" || leaf == "Canvas" || leaf.StartsWith("MusicSelectProcess", StringComparison.Ordinal))
                throw new FormatException("Main and its ancestors are protected");
        }
        public static bool IsInputLayer(string name) => name == "UI_Buttons" || name == "UI_InfoButtons"
            || name.IndexOf("touch", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0;
        public static MusicSelectNodeOptions ReadNode(MusicSelectNodeOptions prior, object value)
        {
            var v = Dict(value); Keys(v, "scale", "offsetX", "offsetY", "hidden", "sprite");
            var next = new MusicSelectNodeOptions(prior);
            Read(v, "scale", ref next.Transform[0], .35f, 2f);
            Read(v, "offsetX", ref next.Transform[1], -540, 540);
            Read(v, "offsetY", ref next.Transform[2], -540, 540);
            if (v.ContainsKey("hidden")) next.Hidden = Bool(v["hidden"]);
            if (v.ContainsKey("sprite"))
            {
                if (v["sprite"] != null && !(v["sprite"] is string)) throw new FormatException("sprite must be a PNG filename or null");
                next.Sprite = (string)v["sprite"]; ValidateSpriteName(next.Sprite);
            }
            return next;
        }
        public static void ValidateSpriteName(string name)
        {
            if (name == null) return;
            if (name.Length < 5 || name.Length > 200 || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || name.Contains(".."))
                throw new FormatException("sprite must be a single PNG filename in select_sprites");
            foreach (char ch in name) if (char.IsControl(ch)) throw new FormatException("invalid sprite filename");
        }
        static bool Bool(object o) => o is bool ? (bool)o : throw new FormatException("expected boolean");
        static void Keys(IDictionary<string, object> d, params string[] keys)
        {
            foreach (var k in d.Keys) if (Array.IndexOf(keys, k) < 0) throw new FormatException("unknown layout key: " + k);
        }
        static void Read(IDictionary<string, object> d, string key, ref float target, float min, float max)
        {
            object value; if (!d.TryGetValue(key, out value)) return;
            if (!(value is double || value is float || value is int || value is long)) throw new FormatException(key + " must be numeric");
            float f = Convert.ToSingle(value, CultureInfo.InvariantCulture);
            if (float.IsNaN(f) || float.IsInfinity(f) || f < min || f > max) throw new FormatException(key + " out of range [" + min + ", " + max + "]");
            target = f;
        }
    }
    public sealed class MusicSelectNodeOptions
    {
        public float[] Transform = { 1f, 0f, 0f };
        public bool Hidden;
        public string Sprite;
        public bool IsSpriteOnly => Transform[0] == 1f && Transform[1] == 0f && Transform[2] == 0f && !Hidden;
        public MusicSelectNodeOptions() { }
        public MusicSelectNodeOptions(MusicSelectNodeOptions prior) { Transform = (float[])prior.Transform.Clone(); Hidden = prior.Hidden; Sprite = prior.Sprite; }
        public object ToDict() => new Dictionary<string, object> { { "scale", Transform[0] }, { "offsetX", Transform[1] }, { "offsetY", Transform[2] }, { "hidden", Hidden }, { "sprite", Sprite } };
    }
}
