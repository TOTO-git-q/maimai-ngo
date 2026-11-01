using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    sealed class MusicSelectSprites : IDisposable
    {
        sealed class Override
        {
            public Image Image;
            public Sprite Original, Replacement;
            public string File;
            public void Dispose()
            {
                if (Image != null && Image.sprite == Replacement) Image.sprite = Original;
                if (Replacement != null) UnityEngine.Object.Destroy(Replacement);
            }
        }
        readonly Dictionary<Transform, Override> _overrides = new Dictionary<Transform, Override>();
        public static string Root;
        internal static string FilePath(string file)
        {
            MusicSelectSettings.ValidateSpriteName(file);
            if (Root == null) throw new ApiError(409, "sprite asset root unavailable");
            return Path.Combine(Root, "select_sprites", file);
        }
        public static void ValidateFile(string file)
        {
            if (file != null) MusicSelectPngCache.Get(file);
        }
        public void Maintain()
        {
            foreach (var entry in _overrides.Values)
                if (entry.Image != null && entry.Image.sprite != entry.Replacement) entry.Image.sprite = entry.Replacement;
        }
        public void Apply(Transform target, string file)
        {
            Override prior;
            if (_overrides.TryGetValue(target, out prior))
            {
                if (prior.File == file)
                {
                    // Animators may write Image.sprite each frame; the override wins only in Music.
                    prior.Image.sprite = prior.Replacement; return;
                }
                prior.Dispose(); _overrides.Remove(target);
            }
            if (file == null) return;
            var texture = MusicSelectPngCache.Get(file);
            var image = target.GetComponent<Image>();
            if (image == null || image.sprite == null) throw new ApiError(400, "sprite override requires Image with an original sprite");
            var original = image.sprite;
            var entry = new Override { Image = image, Original = original, File = file };
            try
            {
                var pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
                entry.Replacement = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    pivot, original.pixelsPerUnit, 0, SpriteMeshType.FullRect, original.border);
                entry.Replacement.name = "select_sprites/" + file;
                image.sprite = entry.Replacement; // Never SetNativeSize: preserve sizeDelta.
                _overrides.Add(target, entry);
            }
            catch { entry.Dispose(); throw; }
        }
        public void Dispose()
        {
            foreach (var entry in _overrides.Values) entry.Dispose();
            _overrides.Clear();
        }

        public static byte[] Export(Image image)
        {
            if (image == null || image.sprite == null) throw new ApiError(400, "node has no current Image sprite");
            var sprite = image.sprite; var atlas = sprite.texture;
            var previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
            RenderTexture temporary = null; Texture2D readable = null, output = null;
            try
            {
                // Default blit copies RGBA without alpha blending: atlas RGB remains straight,
                // including semitransparent pixels. No UI tint/material or premultiply pass.
                temporary = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(atlas, temporary); RenderTexture.active = temporary;
                readable = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0); readable.Apply();
                var pixels = readable.GetPixels32();
                int width = Mathf.RoundToInt(sprite.rect.width), height = Mathf.RoundToInt(sprite.rect.height);
                var result = new Color32[width * height];
                if (!sprite.packed || (sprite.packingMode == SpritePackingMode.Rectangle && sprite.packingRotation == SpritePackingRotation.None))
                {
                    // Copy every texel of a rectangular sprite, including RGB in transparent
                    // padding. Mesh coverage must not trim the ordinary atlas rectangle.
                    var rect = sprite.textureRect;
                    var offset = sprite.textureRectOffset;
                    int sourceX = Mathf.RoundToInt(rect.x), sourceY = Mathf.RoundToInt(rect.y);
                    int offsetX = Mathf.RoundToInt(offset.x), offsetY = Mathf.RoundToInt(offset.y);
                    for (int y = 0; y < Mathf.RoundToInt(rect.height); y++)
                        for (int x = 0; x < Mathf.RoundToInt(rect.width); x++)
                            if (x + offsetX >= 0 && x + offsetX < width && y + offsetY >= 0 && y + offsetY < height)
                                result[(y + offsetY) * width + x + offsetX] = pixels[(sourceY + y) * atlas.width + sourceX + x];
                }
                else
                {
                    // Sprite mesh UVs reconstruct the exact logical sprite rect, including transparent
                    // margins and tight/rotated atlas packing, without exporting neighboring sprites.
                    var vertices = sprite.vertices; var uv = sprite.uv; var triangles = sprite.triangles;
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int ia = triangles[t], ib = triangles[t + 1], ic = triangles[t + 2];
                        Vector2 a = vertices[ia] * sprite.pixelsPerUnit + sprite.pivot;
                        Vector2 b = vertices[ib] * sprite.pixelsPerUnit + sprite.pivot;
                        Vector2 c = vertices[ic] * sprite.pixelsPerUnit + sprite.pivot;
                        float denominator = Cross(b - a, c - a); if (Mathf.Abs(denominator) < .00001f) continue;
                        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
                        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                        for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                        {
                            var p = new Vector2(x + .5f, y + .5f) - a;
                            float wb = Cross(p, c - a) / denominator, wc = Cross(b - a, p) / denominator, wa = 1 - wb - wc;
                            if (wa < -.0001f || wb < -.0001f || wc < -.0001f) continue;
                            var sample = uv[ia] * wa + uv[ib] * wb + uv[ic] * wc;
                            int sx = Mathf.Clamp(Mathf.FloorToInt(sample.x * atlas.width), 0, atlas.width - 1);
                            int sy = Mathf.Clamp(Mathf.FloorToInt(sample.y * atlas.height), 0, atlas.height - 1);
                            result[y * width + x] = pixels[sy * atlas.width + sx];
                        }
                    }
                }
                output = new Texture2D(width, height, TextureFormat.RGBA32, false);
                output.SetPixels32(result); output.Apply(); return output.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (readable != null) UnityEngine.Object.Destroy(readable);
                if (output != null) UnityEngine.Object.Destroy(output);
            }
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
