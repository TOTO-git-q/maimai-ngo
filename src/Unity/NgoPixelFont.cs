using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PartnerLive2D.Unity
{
    // NGO skin: every TextMeshPro UI text is drawn in a pixel font (PixelMplus12, Japanese included). Unity 2018 cannot
    // load a TTF from disk, so the font bytes are written into the game's only Font object, LiberationSans
    // (tools/ngo_font_data.py), and a dynamic SDF font asset is made from it here. Each text's own material is cloned
    // onto the new atlas, so outlines, shadows and colours stay; glyphs the pixel font lacks fall back to the text's
    // original font. Enabled by LocalAssets/PartnerLive2D/pixel_font.txt.
    [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
    static class NgoPixelFont
    {
        const int Sampling = 48;     // 4 x the font's 12 px grid
        const int Padding = 5;
        static TMP_FontAsset _pixel;
        static bool _failed;
        static int _lastTry = -1, _tries;
        static readonly Dictionary<Material, Material> Mats = new Dictionary<Material, Material>();
        public static int MaterialCount => Mats.Count;

        static bool Prepare() => File.Exists(Path.Combine(Mod.AssetRoot, "pixel_font.txt"));

        static TMP_FontAsset Pixel()
        {
            if (_pixel != null || _failed) return _pixel;
            if (Time.frameCount == _lastTry) return null;   // the font may not be loaded yet: try again next frame
            _lastTry = Time.frameCount;
            // the Font object is only reachable through what references it: load TMP's LiberationSans SDF first
            var sdf = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            Font src = sdf != null ? sdf.sourceFontFile : null;
            if (src == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                    if (f.name == "LiberationSans") { src = f; break; }
            if (src == null)
            {
                if (++_tries == 600) Mod.Warn("pixel font: LiberationSans font object not found");
                return null;
            }
            _pixel = TMP_FontAsset.CreateFontAsset(src, Sampling, Padding, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic);
            if (_pixel == null) { _failed = true; Mod.Warn("pixel font: CreateFontAsset failed"); return null; }
            _pixel.name = "NGO Pixel SDF";
            Object.DontDestroyOnLoad(_pixel);
            if (_pixel.fallbackFontAssetTable == null) _pixel.fallbackFontAssetTable = new List<TMP_FontAsset>();
            Mod.Msg("pixel font: dynamic asset ready");
            foreach (var t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())   // texts enabled before it was ready
                if (t.gameObject.scene.IsValid()) Swap(t);
            return _pixel;
        }

        static void Prefix(TextMeshProUGUI __instance) => Swap(__instance);

        static void Swap(TextMeshProUGUI __instance)
        {
            var orig = __instance.font;
            if (orig == null) return;
            var px = Pixel();
            if (px == null || orig == px) return;
            if (!px.fallbackFontAssetTable.Contains(orig)) px.fallbackFontAssetTable.Add(orig);
            var om = __instance.fontSharedMaterial;
            Material m = null;
            if (om != null && !Mats.TryGetValue(om, out m))
            {
                m = new Material(om) { name = om.name + " (NGO pixel)" };   // keeps shader, outline, underlay, colours
                m.SetTexture(ShaderUtilities.ID_MainTex, px.atlasTexture);
                m.SetFloat(ShaderUtilities.ID_TextureWidth, px.atlasWidth);
                m.SetFloat(ShaderUtilities.ID_TextureHeight, px.atlasHeight);
                m.SetFloat(ShaderUtilities.ID_GradientScale, Padding + 1);
                m.SetFloat(ShaderUtilities.ID_WeightNormal, px.normalStyle);
                m.SetFloat(ShaderUtilities.ID_WeightBold, px.boldStyle);
                Object.DontDestroyOnLoad(m);
                Mats[om] = m;
            }
            __instance.font = px;
            if (m != null) __instance.fontSharedMaterial = m;
        }
    }
}
