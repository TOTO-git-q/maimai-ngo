using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace PartnerLive2D.Unity
{
    // TMP 1.4's batch wrapper walks past TextCore's null sentinel. Its one-character wrapper also asks for
    // global runtime font features. Warm only this asset through TextCore, then rebuild TMP's lookup/index tables.
    sealed class NgoFontWarmup
    {
        static readonly MethodInfo AddGlyph = AccessTools.Method(typeof(FontEngine), "TryAddGlyphToTexture", new[] {
            typeof(uint), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>), typeof(List<GlyphRect>),
            typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph).MakeByRefType() });
        static readonly MethodInfo ResetAtlas = AccessTools.Method(typeof(FontEngine), "ResetAtlasTexture", new[] { typeof(Texture2D) });
        static readonly FieldInfo FreeRects = AccessTools.Field(typeof(TMP_FontAsset), "m_FreeGlyphRects");
        static readonly FieldInfo UsedRects = AccessTools.Field(typeof(TMP_FontAsset), "m_UsedGlyphRects");
        readonly TMP_FontAsset _font;
        readonly object[] _args;
        bool _changed, _textureChanged;

        public NgoFontWarmup(TMP_FontAsset font)
        {
            if (AddGlyph == null || ResetAtlas == null || FreeRects == null || UsedRects == null)
                throw new NotSupportedException("TextCore glyph insertion/atlas API unavailable");
            if (font == null || font.sourceFontFile == null || font.atlasTexture == null || !font.atlasTexture.isReadable)
                throw new InvalidOperationException("dynamic source font/readable atlas unavailable");
            _font = font;
            font.ReadFontAssetDefinition();
            var free = FreeRects.GetValue(font) as List<GlyphRect>;
            var used = UsedRects.GetValue(font) as List<GlyphRect>;
            if (free == null || used == null) throw new InvalidOperationException("atlas packing rectangles unavailable");
            if (font.atlasTexture.width == 0 || font.atlasTexture.height == 0)
            {
                if (!font.atlasTexture.Resize(font.atlasWidth, font.atlasHeight))
                    throw new InvalidOperationException("atlas resize failed");
                ResetAtlas.Invoke(null, new object[] { font.atlasTexture });
            }
            _args = new object[] { (uint)0, font.atlasPadding, GlyphPackingMode.BestShortSideFit,
                free, used, font.atlasRenderMode, font.atlasTexture, null };
        }

        public void BeginBatch()
        {
            var result = FontEngine.LoadFontFace(_font.sourceFontFile, _font.faceInfo.pointSize);
            if ((int)result != 0) throw new InvalidOperationException("LoadFontFace failed: " + result);
        }

        public bool Add(uint unicode)
        {
            if (_font.characterLookupTable.ContainsKey(unicode)) return true;
            uint index;
            if (!FontEngine.TryGetGlyphIndex(unicode, out index) || index == 0) return false;
            Glyph glyph;
            if (!_font.glyphLookupTable.TryGetValue(index, out glyph))
            {
                _args[0] = index; _args[7] = null;
                if (!(bool)AddGlyph.Invoke(null, _args)) return false;
                glyph = _args[7] as Glyph;
                if (glyph == null) throw new InvalidOperationException("TextCore returned success without a glyph");
                _font.glyphTable.Add(glyph);
                _font.glyphLookupTable.Add(index, glyph);
                _textureChanged = true;
            }
            var character = new TMP_Character(unicode, glyph);
            _font.characterTable.Add(character);
            _font.characterLookupTable.Add(unicode, character);
            _changed = true;
            return true;
        }

        public void Flush()
        {
            if (_changed) _font.ReadFontAssetDefinition();
            if (_textureChanged) _font.atlasTexture.Apply(false, false);
            _changed = _textureChanged = false;
        }
    }
}
