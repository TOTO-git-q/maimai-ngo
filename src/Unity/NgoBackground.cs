using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // NGO skin backgrounds. settings.ini "ngoBackground = <folder>" (inside PartnerLive2D) holding main.png (1080x1080),
    // sub.png (1080x450) and float_*.png (256x256). The stock MAGiCAL scene (hill, castle, lake, 3D sky) is switched off
    // in the same frame the background is created (BGController.Awake / SubBGController.Initialize), so it never shows.
    static class NgoBackground
    {
        static string _folder;
        static Texture2D _main, _sub;
        static readonly List<Texture2D> Floats = new List<Texture2D>();

        public static void Initialize(string root, string setting)
        {
            if (string.IsNullOrWhiteSpace(setting)) return;
            try
            {
                _folder = ThemeOptions.ResolveFolder(root, setting);
                _main = Load("main.png"); _sub = Load("sub.png");
                foreach (var f in Directory.GetFiles(_folder, "float_*.png")) { var t = LoadPath(f); if (t != null) Floats.Add(t); }
                Floats.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                Mod.Msg("ngo background: main " + (_main != null) + ", sub " + (_sub != null) + ", " + Floats.Count + " floats");
            }
            catch (Exception e) { Mod.Error("ngo background: " + e.Message); _folder = null; }
        }

        static Texture2D Load(string name) { var p = Path.Combine(_folder, name); return File.Exists(p) ? LoadPath(p) : null; }
        static Texture2D LoadPath(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.Destroy(t); return null; }
            t.name = "NGO/" + Path.GetFileNameWithoutExtension(path);
            t.filterMode = FilterMode.Bilinear; t.wrapMode = TextureWrapMode.Clamp;
            UnityEngine.Object.DontDestroyOnLoad(t);
            return t;
        }

        // no "ngoBackground" in settings.ini -> the hooks are not installed at all
        static bool Wanted()
        {
            string v;
            return Mod.ReadSettings(Path.Combine(Mod.AssetRoot, "settings.ini")).TryGetValue("ngoBackground", out v) && v.Length > 0;
        }

        static Sprite MakeSprite(Texture2D t, float ppu)
        {
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        }

        // floating elements by file name: normalized position in the circle (x right, y up, radius 1) and size as a
        // fraction of the diameter. Placed in the upper half, clear of the -/+ tabs on top and the mission button
        // on the right; the lower half is covered by the song panels
        static readonly Dictionary<string, Vector3> FloatSlots = new Dictionary<string, Vector3>
        {
            { "NGO/float_heart", new Vector3(-0.62f, 0.40f, 0.14f) },
            { "NGO/float_jine", new Vector3(-0.34f, 0.70f, 0.12f) },
            { "NGO/float_halo", new Vector3(0.40f, 0.70f, 0.13f) },
            { "NGO/float_star", new Vector3(0.80f, 0.42f, 0.08f) },
            { "NGO/float_cursor", new Vector3(-0.84f, 0.08f, 0.10f) },
            { "NGO/float_pill", new Vector3(0.64f, 0.30f, 0.11f) },
        };

        [HarmonyPatch(typeof(BGController), "Awake")]
        static class MainPatch
        {
            static bool Prepare() => Wanted();
            static void Postfix(BGController __instance)
            {
                if (_main == null) return;
                try { BuildMain(__instance.transform); }
                catch (Exception e) { Mod.Error("ngo background main: " + e); }
            }
        }

        static void BuildMain(Transform root)
        {
            var adjust = root.Find("Adjust_WorldPos");
            if (adjust == null || root.Find("NGO_BG") != null) return;
            foreach (var name in new[] { "Catsle_Area", "SM_v170_Hill_Setted", "RainbowCircleS", "RainbowCircleM", "BG_Sky" })
            { var t = adjust.Find(name); if (t != null) t.gameObject.SetActive(false); }

            // the stock scene is a perspective 3D set; ours is a flat backdrop held in front of the camera that films it
            var holder = new GameObject("NGO_BG");
            holder.layer = adjust.gameObject.layer;
            holder.transform.SetParent(root, false);
            var backdrop = holder.AddComponent<NgoBackdrop>();

            // holder space: 1 unit = the camera's view height; the circle is the central square of the view
            var baseGo = new GameObject("Base"); baseGo.layer = holder.layer;
            baseGo.transform.SetParent(holder.transform, false);
            var sr = baseGo.AddComponent<SpriteRenderer>();
            sr.sprite = MakeSprite(_main, _main.height);
            sr.sortingOrder = -100;
            backdrop.Base = baseGo.transform;

            for (int i = 0; i < Floats.Count; i++)
            {
                var tex = Floats[i];
                Vector3 slot;
                if (!FloatSlots.TryGetValue(tex.name, out slot)) continue;
                var go = new GameObject("Float_" + i); go.layer = holder.layer;
                go.transform.SetParent(holder.transform, false);
                go.transform.localPosition = new Vector3(slot.x * .5f, slot.y * .5f, -0.01f - i * 0.001f);
                var f = go.AddComponent<SpriteRenderer>();
                f.sprite = MakeSprite(tex, tex.width / slot.z);
                f.sortingOrder = -99;
                var motion = go.AddComponent<NgoFloat>();
                motion.Seed = i; motion.Amp = 0.012f + (i % 4) * 0.003f;
            }
        }

        [HarmonyPatch(typeof(SubBGController), "Initialize")]
        static class SubPatch
        {
            static bool Prepare() => Wanted();
            static void Postfix(SubBGController __instance)
            {
                if (_sub == null) return;
                try { BuildSub(__instance.transform); }
                catch (Exception e) { Mod.Error("ngo background sub: " + e); }
            }
        }

        static void BuildSub(Transform root)
        {
            var adjust = root.Find("BG/Null_all/Adjust");
            if (adjust == null) { Mod.Warn("ngo background: sub layout not found"); return; }
            var bgT = adjust.Find("BG");
            var skyT = adjust.Find("Sky");
            var bg = bgT != null ? bgT.GetComponent<SpriteRenderer>() : null;
            if (bg == null || bg.sprite == null || bg.sprite.texture == null) { Mod.Warn("ngo background: sub base sprite not found"); return; }
            if (bg.sprite.texture.name.StartsWith("NGO/")) return;
            // the stock flat base sprite already has the strip's size; draw ours at the same world size
            var stock = bg.sprite;
            bg.sprite = MakeSprite(_sub, stock.pixelsPerUnit * _sub.width / stock.rect.width);
            var skyR = skyT != null ? skyT.GetComponent<MeshRenderer>() : null;
            if (skyR != null) { bg.sortingLayerID = skyR.sortingLayerID; bg.sortingOrder = skyR.sortingOrder; }
            bgT.gameObject.SetActive(true);
            foreach (var name in new[] { "Sky", "GameObject", "Pattern" })
            { var t = adjust.Find(name); if (t != null) t.gameObject.SetActive(false); }
            var tone = adjust.Find("ToneParticle"); if (tone != null) tone.gameObject.SetActive(false);
            // the animated notes keep their motion, now drawn as NGO floats
            int n = 0;
            foreach (Transform child in adjust)
            {
                if (!child.name.StartsWith("ToneKigou")) continue;
                var r = child.GetComponent<SpriteRenderer>();
                if (r == null || r.sprite == null || Floats.Count == 0) continue;
                var tex = Floats[n++ % Floats.Count];
                float worldW = r.sprite.rect.width / r.sprite.pixelsPerUnit;
                r.sprite = MakeSprite(tex, tex.width / (worldW * 1.15f));
            }
        }
    }

    // keeps the NGO backdrop square to the camera that renders the background layer, far behind everything
    sealed class NgoBackdrop : MonoBehaviour
    {
        public Transform Base;
        Camera _cam;
        float _retry;

        void LateUpdate()
        {
            if (_cam == null || !_cam.isActiveAndEnabled)
            {
                if (Time.unscaledTime < _retry) return;
                _retry = Time.unscaledTime + 1f;
                _cam = null;
                int bit = 1 << gameObject.layer;
                foreach (var c in Camera.allCameras) if ((c.cullingMask & bit) != 0) { _cam = c; break; }
                if (_cam == null) return;
                Mod.Msg(string.Format("ngo background: camera {0} ortho {1} fov {2} clip {3}-{4} aspect {5:0.##}",
                    _cam.name, _cam.orthographic, _cam.fieldOfView, _cam.nearClipPlane, _cam.farClipPlane, _cam.aspect));
            }
            var ct = _cam.transform;
            float d = _cam.nearClipPlane + (_cam.farClipPlane - _cam.nearClipPlane) * 0.9f;
            float h = _cam.orthographic ? 2f * _cam.orthographicSize : 2f * d * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            // the camera films the whole 1080x1920 cabinet screen; the circle is the bottom square, as wide as the view
            float w = h * _cam.aspect;
            transform.position = ct.position + ct.forward * d + ct.up * (-h * 0.5f + w * 0.5f);
            transform.rotation = ct.rotation;
            var parent = transform.parent;
            float ps = parent != null ? parent.lossyScale.y : 1f;
            transform.localScale = Vector3.one * (ps != 0 ? w / ps : w);
        }
    }

    // slow bob + sway for a floating background element (unscaled time, no allocation)
    sealed class NgoFloat : MonoBehaviour
    {
        public int Seed;
        public float Amp;   // bob height in the parent's local units
        Vector3 _home;
        float _phase, _speed, _tilt;
        void Start()
        {
            _home = transform.localPosition;
            _phase = Seed * 1.7f; _speed = 0.35f + (Seed % 3) * 0.08f; _tilt = 6f + (Seed % 3) * 2f;
        }
        void Update()
        {
            float t = Time.unscaledTime * _speed + _phase;
            transform.localPosition = _home + new Vector3(Mathf.Sin(t * 0.7f) * Amp * 0.5f, Mathf.Sin(t) * Amp, 0);
            transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 0.8f) * _tilt);
        }
    }
}
