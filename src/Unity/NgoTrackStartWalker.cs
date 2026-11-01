using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // NGO skin: on the track-start screen the game walks its bear puppet (prefab UI_CMN_Derakkuma, mounted under
    // BackGrounds/Null_UI_CMN_Derakkuma and BackGrounds_Boss/...) along the bottom. Dressing that puppet's parts in cut-up
    // Ame pieces gave a bear-shaped Ame with the bear's own eyes, mouth and tail on top. Here the game's motion stays
    // (the puppet root's animated position, scale and flip) and only the drawing changes: the puppet's two views are made
    // invisible with a CanvasGroup and a whole Ame walk cycle (LocalAssets/PartnerLive2D/ngo_ame_walk/frame_1..4.png)
    // is drawn on the puppet root, feet where the puppet's feet are.
    [HarmonyPatch(typeof(Process.TrackStartProcess), "OnStart")]
    static class NgoTrackStartWalker
    {
        static string Dir => Path.Combine(Mod.AssetRoot, "ngo_ame_walk");
        static bool Prepare() => File.Exists(Path.Combine(Dir, "frame_1.png"));

        static void Postfix()
        {
            try
            {
                var go = new GameObject("PartnerLive2D_NgoTrackStartWalker");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<NgoTrackStartWalkerRunner>().Dir = Dir;
            }
            catch (Exception e) { Mod.Error("ngo track start walker: " + e.Message); }
        }
    }

    sealed class NgoTrackStartWalkerRunner : MonoBehaviour
    {
        public string Dir;
        // puppet root units (screen px = units x mount scale 0.2): feet at -290, head top at +500 (stock puppet,
        // measured from the track-start capture); the walker is drawn a little larger than the bear
        const float Feet = -290f, Height = 900f, Fps = 8f;
        static Sprite[] _frames;
        readonly List<Image> _walkers = new List<Image>();
        float _started, _nextLook;

        void Start() { _started = Time.realtimeSinceStartup; }

        static Sprite[] Frames(string dir)
        {
            if (_frames != null) return _frames;
            var list = new List<Sprite>();
            for (int i = 1; i <= 8; i++)
            {
                var tex = Mod.LoadImage(Path.Combine(dir, "frame_" + i + ".png"));
                if (tex == null) break;
                var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, 0f), 100f, 0, SpriteMeshType.FullRect);
                DontDestroyOnLoad(s); list.Add(s);
            }
            return _frames = list.ToArray();
        }

        void LateUpdate()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now >= _nextLook && now - _started < 12f && _walkers.Count == 0) { _nextLook = now + .5f; Attach(); }   // puppets spawn a moment later; one scan finds both mounts
                _walkers.RemoveAll(w => w == null);
                if (_walkers.Count == 0 && now - _started > 12f) { Destroy(gameObject); return; }
                var frames = Frames(Dir);
                if (frames.Length == 0) return;
                var f = frames[(int)(now * Fps) % frames.Length];
                foreach (var w in _walkers) if (w.sprite != f) w.sprite = f;
            }
            catch (Exception e) { Mod.Error("ngo track start walker: " + e.Message); Destroy(gameObject); }
        }

        void Attach()
        {
            var frames = Frames(Dir);
            if (frames.Length == 0) return;
            foreach (var t in FindObjectsOfType<RectTransform>())
            {
                if (!NameCache.Of(t).StartsWith("UI_CMN_Derakkuma", StringComparison.Ordinal)) continue;
                if (t.parent == null || NameCache.Of(t.parent) != "Null_UI_CMN_Derakkuma") continue;   // the track-start mounts only
                if (t.Find("PartnerLive2D_AmeWalker") != null) continue;
                foreach (var view in new[] { "Derakkuma", "Side_Derakkuma" })
                {
                    var v = t.Find(view);
                    if (v == null) continue;
                    var g = v.GetComponent<CanvasGroup>() ?? v.gameObject.AddComponent<CanvasGroup>();
                    g.alpha = 0f; g.blocksRaycasts = false;
                }
                var go = new GameObject("PartnerLive2D_AmeWalker", typeof(RectTransform), typeof(Image));
                go.layer = t.gameObject.layer;
                go.transform.SetParent(t, false);
                var img = go.GetComponent<Image>(); img.sprite = frames[0]; img.raycastTarget = false;
                var rt = (RectTransform)go.transform;
                rt.pivot = new Vector2(.5f, 0f);
                var tex = frames[0].texture;
                rt.sizeDelta = new Vector2(Height * tex.width / tex.height, Height);
                rt.localPosition = new Vector3(0f, Feet, 0f);
                _walkers.Add(img);
                Mod.Msg("ngo track start walker on " + t.parent.parent?.name);
            }
        }
    }
}
