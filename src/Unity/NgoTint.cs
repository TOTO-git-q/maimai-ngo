using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // NGO skin: maimai tints its white panel textures per screen (blue mode select, mint area/ticket, green music
    // select...). The tint is applied when an Image builds its mesh, so the first frame already shows the NGO color
    // and nothing is scanned per frame. Only textures listed in LocalAssets/PartnerLive2D/ngo_tint.txt are touched
    // (one texture name per line), so colors that mean something (difficulty, clear ranks) stay as they are.
    [HarmonyPatch(typeof(Image), "OnPopulateMesh")]
    static class NgoTint
    {
        static HashSet<string> _textures;

        // no ngo_tint.txt -> the hook is not installed at all (the NGO colors are written into the game files instead)
        static bool Prepare() => File.Exists(Path.Combine(Mod.AssetRoot, "ngo_tint.txt"));

        public static void Load(string root)
        {
            string path = Path.Combine(root, "ngo_tint.txt");
            if (!File.Exists(path)) { _textures = null; return; }
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.Length > 0 && !t.StartsWith(";") && !t.StartsWith("#")) set.Add(t);
            }
            _textures = set;
            Mod.Msg("ngo tint: " + set.Count + " textures");
        }

        static void Postfix(Image __instance, VertexHelper toFill)
        {
            if (_textures == null) return;
            var sprite = __instance.sprite;
            if (sprite == null) return;
            var tex = sprite.texture;
            if (tex == null || !_textures.Contains(NameCache.Of(tex))) return;
            var v = new UIVertex();
            for (int i = 0; i < toFill.currentVertCount; i++)
            {
                toFill.PopulateUIVertex(ref v, i);
                v.color = Remap(v.color);
                toFill.SetUIVertex(v, i);
            }
        }

        // blue / cyan -> lavender-violet, mint / green -> pink; lightness, saturation and alpha kept
        static Color32 Remap(Color32 c)
        {
            float h, s, val;
            Color.RGBToHSV(c, out h, out s, out val);
            if (s < 0.15f) return c;
            float deg = h * 360f;
            if (deg >= 175f && deg < 245f) deg = 262f + (deg - 175f) * 0.3f;   // 262..283
            else if (deg >= 80f && deg < 175f) deg = 322f + (deg - 80f) * 0.15f; // 322..336
            else return c;
            var o = Color.HSVToRGB(deg / 360f, Mathf.Min(1f, s * 0.9f), val);
            return new Color32((byte)(o.r * 255), (byte)(o.g * 255), (byte)(o.b * 255), c.a);
        }
    }
}
