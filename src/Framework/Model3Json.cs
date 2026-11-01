using System;
using System.Collections.Generic;
using System.IO;

namespace PartnerLive2D.Framework
{
    public sealed class MotionEntry
    {
        public string File;        // relative to the model directory
        public float FadeInTime = -1f;   // < 0 : not specified
        public float FadeOutTime = -1f;  // < 0 : not specified
        public string Sound;       // parsed but unused
    }

    public sealed class ExpressionEntry
    {
        public string Name;
        public string File;
    }

    public sealed class HitAreaEntry
    {
        public string Id;
        public string Name;
    }

    // Parsed *.model3.json (CubismModelSettingJson subset).
    public sealed class Model3Json
    {
        public string Directory;               // absolute directory of the model3.json (with trailing separator)
        public string MocFile;                 // relative
        public List<string> TextureFiles = new List<string>();   // relative
        public string PhysicsFile;             // relative, or null
        public string PoseFile;                // relative, or null
        public List<ExpressionEntry> Expressions = new List<ExpressionEntry>();
        public List<string> MotionGroupNames = new List<string>();
        public Dictionary<string, List<MotionEntry>> Motions = new Dictionary<string, List<MotionEntry>>();
        public List<string> EyeBlinkIds = new List<string>();
        public List<string> LipSyncIds = new List<string>();
        public List<HitAreaEntry> HitAreas = new List<HitAreaEntry>();

        public static Model3Json Load(string path)
        {
            string full = Path.GetFullPath(path);
            object root = Json.ParseFile(full);
            var m = new Model3Json();
            string dir = Path.GetDirectoryName(full);
            if (!dir.EndsWith(Path.DirectorySeparatorChar.ToString())) dir += Path.DirectorySeparatorChar;
            m.Directory = dir;

            object refs = Json.Get(root, "FileReferences");
            m.MocFile = Json.GetString(refs, "Moc");
            if (string.IsNullOrEmpty(m.MocFile)) throw new InvalidDataException("model3.json has no Moc");

            var textures = Json.GetList(refs, "Textures");
            if (textures != null)
                foreach (object t in textures) m.TextureFiles.Add(t as string ?? "");

            m.PhysicsFile = NullIfEmpty(Json.GetString(refs, "Physics"));
            m.PoseFile = NullIfEmpty(Json.GetString(refs, "Pose"));

            var exps = Json.GetList(refs, "Expressions");
            if (exps != null)
                foreach (object e in exps)
                    m.Expressions.Add(new ExpressionEntry { Name = Json.GetString(e, "Name"), File = Json.GetString(e, "File") });

            var motions = Json.Get(refs, "Motions") as JsonObject;
            if (motions != null)
            {
                foreach (string group in motions.KeyOrder)
                {
                    var list = new List<MotionEntry>();
                    var arr = motions[group] as List<object>;
                    if (arr != null)
                    {
                        foreach (object item in arr)
                        {
                            var me = new MotionEntry();
                            me.File = Json.GetString(item, "File");
                            me.FadeInTime = Json.IsNull(item, "FadeInTime") ? -1f : Json.GetFloat(item, "FadeInTime", -1f);
                            me.FadeOutTime = Json.IsNull(item, "FadeOutTime") ? -1f : Json.GetFloat(item, "FadeOutTime", -1f);
                            me.Sound = Json.GetString(item, "Sound");
                            list.Add(me);
                        }
                    }
                    m.MotionGroupNames.Add(group);
                    m.Motions[group] = list;
                }
            }

            var groups = Json.GetList(root, "Groups");
            if (groups != null)
            {
                foreach (object g in groups)
                {
                    string name = Json.GetString(g, "Name");
                    var ids = Json.GetList(g, "Ids");
                    if (ids == null) continue;
                    List<string> target = null;
                    if (name == "EyeBlink") target = m.EyeBlinkIds;
                    else if (name == "LipSync") target = m.LipSyncIds;
                    if (target == null) continue;
                    foreach (object id in ids) { var s = id as string; if (s != null) target.Add(s); }
                }
            }

            var hits = Json.GetList(root, "HitAreas");
            if (hits != null)
                foreach (object h in hits)
                    m.HitAreas.Add(new HitAreaEntry { Id = Json.GetString(h, "Id"), Name = Json.GetString(h, "Name") });

            return m;
        }

        public string Resolve(string relative)
        {
            return Path.GetFullPath(Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        static string NullIfEmpty(string s) { return string.IsNullOrEmpty(s) ? null : s; }
    }
}
