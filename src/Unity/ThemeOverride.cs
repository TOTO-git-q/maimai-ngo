using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // Active only while enabled. Unity components retain their stock asset; replacements are ours.
    static class ThemeOverride
    {
        sealed class SpriteBinding { public Sprite Original, Replacement; }
        sealed class TextureBinding { public Texture Original, Replacement; }
        sealed class LayoutBinding
        {
            public ThemeOptions.LayoutRule Rule;
            public Vector3 OriginalScale, OriginalPosition, AppliedScale, AppliedPosition;
        }
        static readonly Dictionary<Transform, LayoutBinding> LayoutBindings = new Dictionary<Transform, LayoutBinding>();
        static List<ThemeOptions.LayoutRule> LayoutRules = new List<ThemeOptions.LayoutRule>();
        static string _folder;
        static bool _enabled, _internal;
        static HarmonyLib.Harmony _harmony;
        static GameObject _runnerObject;
        static Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        static readonly Dictionary<Sprite, Sprite> Sprites = new Dictionary<Sprite, Sprite>();
        static readonly Dictionary<Image, SpriteBinding> Images = new Dictionary<Image, SpriteBinding>();
        static readonly Dictionary<RawImage, TextureBinding> RawImages = new Dictionary<RawImage, TextureBinding>();
        static readonly Dictionary<SpriteRenderer, SpriteBinding> Renderers = new Dictionary<SpriteRenderer, SpriteBinding>();
        static readonly HashSet<string> Loaded = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> Errors = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly HashSet<string> BadFiles = new HashSet<string>(StringComparer.Ordinal);

        public static void Initialize(string root, string setting)
        {
            try { _folder = ThemeOptions.ResolveFolder(root, setting); if (_folder != null) SetEnabled(true); }
            catch (Exception e) { Mod.Error("theme initialization: " + e.Message); }
        }

        public static object SetEnabled(bool enabled)
        {
            if (enabled == _enabled) return Status();
            if (enabled)
            {
                if (_folder == null) throw new ApiError(409, "set theme = <folder> in settings.ini and restart first");
                if (!Directory.Exists(_folder)) throw new ApiError(409, "theme folder not found: " + _folder);
                _files = ThemeOptions.IndexFiles(Directory.GetFiles(_folder));
                Loaded.Clear(); Errors.Clear(); BadFiles.Clear();
                string layoutPath = Path.Combine(_folder, "layout.ini");
                LayoutRules = File.Exists(layoutPath) ? ThemeOptions.ParseLayout(File.ReadAllLines(layoutPath)) : new List<ThemeOptions.LayoutRule>();
                try
                {
                    _harmony = new HarmonyLib.Harmony("PartnerLive2D.ThemeOverride");
                    _harmony.Patch(AccessTools.PropertySetter(typeof(Image), "sprite"), prefix: new HarmonyMethod(typeof(ThemeOverride), "ImagePrefix"));
                    _harmony.Patch(AccessTools.PropertySetter(typeof(RawImage), "texture"), prefix: new HarmonyMethod(typeof(ThemeOverride), "RawPrefix"));
                    if (LayoutRules.Count > 0)
                        _harmony.Patch(AccessTools.Method(typeof(GameObject), "SetActive"), postfix: new HarmonyMethod(typeof(ThemeOverride), "SetActivePostfix"));
                    _enabled = true;
                    _runnerObject = new GameObject("PartnerLive2D.ThemeOverride");
                    UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
                    _runnerObject.AddComponent<ThemeRunner>();
                    Scan();
                }
                catch { Disable(); throw; }
            }
            else Disable();
            return Status();
        }

        public static object Status()
        {
            var loaded = new List<string>(Loaded); loaded.Sort(StringComparer.Ordinal);
            var missed = new List<string>();
            foreach (var name in _files.Keys) if (!Loaded.Contains(name)) missed.Add(name);
            missed.Sort(StringComparer.Ordinal);
            return new Dictionary<string, object> { { "enabled", _enabled }, { "folder", _folder },
                { "loaded", loaded }, { "missed", missed }, { "errors", new Dictionary<string, string>(Errors) } };
        }

        static void Disable()
        {
            _enabled = false;
            if (_harmony != null) { _harmony.UnpatchSelf(); _harmony = null; }
            _internal = true;
            try
            {
                foreach (var p in Images) if (p.Key != null && p.Value.Replacement != null && p.Key.sprite == p.Value.Replacement) p.Key.sprite = p.Value.Original;
                foreach (var p in RawImages) if (p.Key != null && p.Value.Replacement != null && p.Key.texture == p.Value.Replacement) p.Key.texture = p.Value.Original;
                foreach (var p in Renderers) if (p.Key != null && p.Value.Replacement != null && p.Key.sprite == p.Value.Replacement) p.Key.sprite = p.Value.Original;
            }
            finally { _internal = false; }
            foreach (var p in LayoutBindings) RestoreLayout(p.Key, p.Value);
            LayoutBindings.Clear(); LayoutRules.Clear();
            Images.Clear(); RawImages.Clear(); Renderers.Clear();
            foreach (var s in Sprites.Values) if (s != null) UnityEngine.Object.Destroy(s);
            foreach (var t in Textures.Values) if (t != null) UnityEngine.Object.Destroy(t);
            Sprites.Clear(); Textures.Clear();
            if (_runnerObject != null) UnityEngine.Object.Destroy(_runnerObject);
            _runnerObject = null;
        }

        static Texture2D FindTexture(Texture original)
        {
            if (original == null) return null;
            return FindTexture(original.name, original);
        }

        static Texture2D FindTexture(string name, Texture original)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string path;
            if (!_files.TryGetValue(name, out path) || BadFiles.Contains(name)) return null;
            Texture2D tex;
            if (Textures.TryGetValue(name, out tex)) return tex;
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (new FileInfo(path).Length > 32 * 1024 * 1024 || !tex.LoadImage(File.ReadAllBytes(path))) throw new Exception("invalid or oversized PNG");
                if (tex.width > 8192 || tex.height > 8192 || (long)tex.width * tex.height > 16777216) throw new Exception("PNG dimensions exceed limit");
                tex.name = "PartnerLive2D/theme/" + name;
                tex.filterMode = original.filterMode; tex.wrapMode = original.wrapMode;
                Textures.Add(name, tex); return tex;
            }
            catch (Exception e) { Errors[name] = e.Message; BadFiles.Add(name); UnityEngine.Object.Destroy(tex); return null; }
        }

        static Texture ReplaceTexture(Texture original)
        {
            var tex = FindTexture(original);
            if (tex == null) return null;
            if (tex.width != original.width || tex.height != original.height)
            { Errors[original.name] = "RawImage requires original texture dimensions"; return null; }
            Loaded.Add(original.name); return tex;
        }

        static Sprite ReplaceSprite(Sprite original)
        {
            if (original == null || original.texture == null) return null;
            Sprite existing;
            if (Sprites.TryGetValue(original, out existing)) return existing;
            var source = original.texture;
            // the redrawn PNGs are named after the texture, or after the sprite when it lives in a shared atlas
            string key = source.name;
            var tex = FindTexture(source);
            if (tex == null && original.name != source.name) { key = original.name; tex = FindTexture(original.name, source); }
            if (tex == null) return null;
            Rect rect;
            // a PNG the size of the sprite itself covers its whole rect (also for atlas sprites trimmed when packed)
            if (tex.width == Mathf.RoundToInt(original.rect.width) && tex.height == Mathf.RoundToInt(original.rect.height))
                rect = new Rect(0, 0, tex.width, tex.height);
            else if (key == source.name && tex.width == source.width && tex.height == source.height)
            {
                // a PNG of the whole texture: cut the sprite's own rect out of it (a tight mesh only trims transparency)
                if (original.packed && original.packingRotation != SpritePackingRotation.None)
                { Errors[key] = "rotated atlas sprite requires an unpacked sprite-sized PNG"; return null; }
                rect = original.rect;
            }
            else { Errors[key] = "PNG dimensions must match the texture atlas or sprite"; return null; }
            try
            {
                var pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
                var replacement = Sprite.Create(tex, rect, pivot, original.pixelsPerUnit, 0, SpriteMeshType.FullRect, original.border);
                replacement.name = "PartnerLive2D/theme/" + original.name;
                Sprites.Add(original, replacement); Loaded.Add(key); Errors.Remove(key); return replacement;
            }
            catch (Exception e) { Errors[key] = e.Message; return null; }
        }

        static void ImagePrefix(Image __instance, ref Sprite value)
        {
            if (!_enabled || _internal || __instance == null) return;
            try
            {
                SpriteBinding old;
                if (Images.TryGetValue(__instance, out old) && value == old.Replacement) return;
                var replacement = ReplaceSprite(value);
                if (replacement == null) { if (old != null) { old.Original = value; old.Replacement = null; } }
                else { Images[__instance] = new SpriteBinding { Original = value, Replacement = replacement }; value = replacement; }
            }
            catch (Exception e) { Mod.Error("theme Image: " + e.Message); }
        }

        static void RawPrefix(RawImage __instance, ref Texture value)
        {
            if (!_enabled || _internal || __instance == null) return;
            try
            {
                TextureBinding old;
                if (RawImages.TryGetValue(__instance, out old) && value == old.Replacement) return;
                var replacement = ReplaceTexture(value);
                if (replacement == null) { if (old != null) { old.Original = value; old.Replacement = null; } }
                else { RawImages[__instance] = new TextureBinding { Original = value, Replacement = replacement }; value = replacement; }
            }
            catch (Exception e) { Mod.Error("theme RawImage: " + e.Message); }
        }

        public static void Scan()
        {
            if (!_enabled) return;
            foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
                if (image != null && image.gameObject.scene.IsValid()) ApplyImage(image);
            foreach (var image in Resources.FindObjectsOfTypeAll<RawImage>())
                if (image != null && image.gameObject.scene.IsValid()) ApplyRaw(image);
            foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
                if (renderer != null && renderer.gameObject.scene.IsValid()) ApplyRenderer(renderer);
            ScanLayouts();
            Prune();
        }
        static void ApplyImage(Image i)
        {
            Sprite v = i.sprite; SpriteBinding b;
            if (Images.TryGetValue(i, out b) && v == b.Replacement) return;
            ImagePrefix(i, ref v); if (i.sprite != v) { _internal = true; try { i.sprite = v; } finally { _internal = false; } }
        }
        static void ApplyRaw(RawImage i)
        {
            Texture v = i.texture; TextureBinding b;
            if (RawImages.TryGetValue(i, out b) && v == b.Replacement) return;
            RawPrefix(i, ref v); if (i.texture != v) { _internal = true; try { i.texture = v; } finally { _internal = false; } }
        }
        static void ApplyRenderer(SpriteRenderer r)
        {
            var value = r.sprite; SpriteBinding b;
            if (Renderers.TryGetValue(r, out b) && value == b.Replacement) return;
            var replacement = ReplaceSprite(value);
            if (replacement == null) { if (b != null) { b.Original = value; b.Replacement = null; } }
            else { Renderers[r] = new SpriteBinding { Original = value, Replacement = replacement }; r.sprite = replacement; }
        }
        public static void Reapply()
        {
            if (!_enabled) return;
            foreach (var i in new List<Image>(Images.Keys)) if (i != null) ApplyImage(i);
            foreach (var i in new List<RawImage>(RawImages.Keys)) if (i != null) ApplyRaw(i);
            foreach (var r in new List<SpriteRenderer>(Renderers.Keys)) if (r != null) ApplyRenderer(r);
            foreach (var pair in new List<KeyValuePair<Transform, LayoutBinding>>(LayoutBindings))
                if (pair.Key != null && pair.Key.gameObject.activeInHierarchy) ApplyLayout(pair.Key, pair.Value);
        }
        static void SetActivePostfix(GameObject __instance, bool value)
        {
            if (!_enabled || !value || __instance == null || !__instance.scene.IsValid()) return;
            try { ScanLayouts(); } catch (Exception e) { Mod.Error("theme layout activation: " + e.Message); }
        }

        static string ObjectPath(Transform t)
        {
            string result = t.name;
            while (t.parent != null) { t = t.parent; result = t.name + "/" + result; }
            return result;
        }

        static void RestoreLayout(Transform t, LayoutBinding b)
        {
            if (t == null) return;
            if (t.localScale == b.AppliedScale) t.localScale = b.OriginalScale;
            if (t.localPosition == b.AppliedPosition) t.localPosition = b.OriginalPosition;
        }

        static void ScanLayouts()
        {
            if (LayoutRules.Count == 0) return;
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            var selected = new Dictionary<Transform, ThemeOptions.LayoutRule>();
            var conflicts = new HashSet<Transform>();
            foreach (var rule in LayoutRules)
            {
                var matches = new List<Transform>();
                foreach (var t in all)
                {
                    if (t == null || !t.gameObject.scene.IsValid()) continue;
                    string path = ObjectPath(t);
                    if (rule.Target.IndexOf('/') < 0 ? t.name == rule.Target :
                        (path == rule.Target || path.EndsWith("/" + rule.Target, StringComparison.Ordinal))) matches.Add(t);
                }
                string errorKey = "layout:" + rule.Target;
                if (rule.Target.IndexOf('/') < 0 && matches.Count > 1)
                {
                    Errors[errorKey] = "name is not unique; use a path";
                    continue;
                }
                Errors.Remove(errorKey);
                foreach (var t in matches)
                {
                    if (selected.ContainsKey(t) || conflicts.Contains(t))
                    {
                        selected.Remove(t); conflicts.Add(t);
                        Errors[errorKey] = "multiple rules match the same object";
                    }
                    else selected.Add(t, rule);
                }
            }
            foreach (var pair in new List<KeyValuePair<Transform, LayoutBinding>>(LayoutBindings))
            {
                ThemeOptions.LayoutRule rule;
                if (pair.Key == null || !selected.TryGetValue(pair.Key, out rule) || rule != pair.Value.Rule)
                {
                    RestoreLayout(pair.Key, pair.Value);
                    LayoutBindings.Remove(pair.Key);
                }
            }
            foreach (var pair in selected)
            {
                var t = pair.Key;
                if (!t.gameObject.activeInHierarchy) continue;
                LayoutBinding binding;
                if (!LayoutBindings.TryGetValue(t, out binding))
                {
                    binding = new LayoutBinding { Rule = pair.Value, OriginalScale = t.localScale, OriginalPosition = t.localPosition,
                        AppliedScale = t.localScale, AppliedPosition = t.localPosition };
                    LayoutBindings.Add(t, binding);
                }
                ApplyLayout(t, binding);
            }
        }

        static void ApplyLayout(Transform t, LayoutBinding b)
        {
            if (t.localScale != b.AppliedScale) b.OriginalScale = t.localScale;
            if (t.localPosition != b.AppliedPosition) b.OriginalPosition = t.localPosition;
            b.AppliedScale = b.OriginalScale * b.Rule.Scale;
            b.AppliedPosition = b.OriginalPosition + new Vector3(b.Rule.OffsetX, b.Rule.OffsetY, 0);
            t.localScale = b.AppliedScale;
            t.localPosition = b.AppliedPosition;
            if (b.Rule.Scale != 1f)
            {
                var image = t.GetComponent<Image>();
                var renderer = t.GetComponent<SpriteRenderer>();
                var raw = t.GetComponent<RawImage>();
                Texture texture = image != null && image.sprite != null ? image.sprite.texture :
                    renderer != null && renderer.sprite != null ? renderer.sprite.texture : raw != null ? raw.texture : null;
                // Only touch textures owned by the theme, never the stock asset.
                foreach (var replacement in Textures.Values)
                    if (texture == replacement) { replacement.filterMode = FilterMode.Point; break; }
            }
        }

        static void Prune()
        {
            foreach (var t in new List<Transform>(LayoutBindings.Keys)) if (t == null) LayoutBindings.Remove(t);
            foreach (var i in new List<Image>(Images.Keys)) if (i == null) Images.Remove(i);
            foreach (var i in new List<RawImage>(RawImages.Keys)) if (i == null) RawImages.Remove(i);
            foreach (var r in new List<SpriteRenderer>(Renderers.Keys)) if (r == null) Renderers.Remove(r);
            var used = new HashSet<Sprite>();
            foreach (var b in Images.Values) if (b.Original != null && b.Replacement != null) used.Add(b.Original);
            foreach (var b in Renderers.Values) if (b.Original != null && b.Replacement != null) used.Add(b.Original);
            foreach (var pair in new List<KeyValuePair<Sprite, Sprite>>(Sprites))
                if (pair.Key == null || !used.Contains(pair.Key))
                {
                    if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value);
                    Sprites.Remove(pair.Key);
                }
        }
    }

    [DefaultExecutionOrder(32000)]
    sealed class ThemeRunner : MonoBehaviour
    {
        float _nextScan;
        void OnEnable() { Canvas.willRenderCanvases += Reapply; }
        void OnDisable() { Canvas.willRenderCanvases -= Reapply; }
        void LateUpdate()
        {
            // the skin covers login to music select; never scan the scene while a chart is being played
            if (TestHarness.InPlay) return;
            try { if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 2f; ThemeOverride.Scan(); } ThemeOverride.Reapply(); }
            catch (Exception e) { Mod.Error("theme update: " + e.Message); }
        }
        void Reapply() { if (TestHarness.InPlay) return; try { ThemeOverride.Reapply(); } catch (Exception e) { Mod.Error("theme canvas: " + e.Message); } }
    }
}


