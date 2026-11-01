using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // NGO skin: the "talking to the server" screen after a play (UserDataULProcess, drawn by the DataSave prefab) is
    // redesigned. The prefab's character group DataSave/Chara holds every maimai mascot the screen may pick at random
    // (Tapp, the two Slides, Rand/Dolly, the gloves, the three bear rigs), so the whole group is made invisible with one
    // CanvasGroup instead of hunting single parts; one picture (LocalAssets/PartnerLive2D/ngo_upload/upload_scene.png:
    // Ame, KAngel and P-chan delivering the data) walks on the strip instead, bobbing, inside the same screen so it
    // comes and goes with it.
    [HarmonyPatch(typeof(Process.UserDataULProcess), "OnStart")]
    static class NgoUploadScene
    {
        static string Png => Path.Combine(Mod.AssetRoot, "ngo_upload", "upload_scene.png");
        static bool Prepare() => File.Exists(Png);

        static void Postfix()
        {
            try
            {
                NgoUploadSceneRunner.Current?.End();
                var go = new GameObject("PartnerLive2D_NgoUploadScene");
                UnityEngine.Object.DontDestroyOnLoad(go);
                NgoUploadSceneRunner.Current = go.AddComponent<NgoUploadSceneRunner>();
                NgoUploadSceneRunner.Current.Png = Png;
            }
            catch (Exception e) { Mod.Error("ngo upload scene: " + e.Message); }
        }
    }

    // the screen's process ends (MapProcess.OnRelease, declared on the base class): the picture goes, the group comes back
    [HarmonyPatch(typeof(Process.MapCore.MapProcess), "OnRelease")]
    static class NgoUploadSceneEnd
    {
        static bool Prepare() => File.Exists(Path.Combine(Mod.AssetRoot, "ngo_upload", "upload_scene.png"));
        static void Postfix(Process.MapCore.MapProcess __instance)
        {
            if (!(__instance is Process.UserDataULProcess)) return;
            try { NgoUploadSceneRunner.Current?.End(); }
            catch (Exception e) { Mod.Error("ngo upload scene end: " + e.Message); }
        }
    }

    sealed class NgoUploadSceneRunner : MonoBehaviour
    {
        public static NgoUploadSceneRunner Current;
        public string Png;
        RectTransform _scene;
        CanvasGroup _group;
        Vector3 _home;
        float _started, _nextLook;

        void Start() { _started = Time.realtimeSinceStartup; }

        void LateUpdate()
        {
            try
            {
                if (_scene == null)
                {
                    if (Time.realtimeSinceStartup - _started > 15f) { Diagnose(); End(); return; }   // the screen never showed
                    if (Time.realtimeSinceStartup < _nextLook) return;
                    _nextLook = Time.realtimeSinceStartup + .25f;
                    Setup();
                    return;
                }
                if (!_scene.gameObject.activeInHierarchy) { End(); return; }
                if (_group != null) _group.alpha = 0f;
                float t = Time.realtimeSinceStartup;
                _scene.localPosition = _home + new Vector3(0f, Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.4f)) * 10f, 0f);
            }
            catch (Exception e) { Mod.Error("ngo upload scene: " + e.Message); End(); }
        }

        static bool IsMap(Image img) =>
            img.sprite != null && img.sprite.texture != null && NameCache.Of(img.sprite.texture) == "UI_CMN_DataSave_Continue_MugenMap";

        // the screen's own character group: DataSave/Chara holding Tapp. The continue screen's prefab (loaded alongside)
        // carries the same strip and its own Chara, so the strip alone does not say which screen it belongs to.
        void Setup()
        {
            Transform chara = null;
            foreach (var t in FindObjectsOfType<RectTransform>())
                if (NameCache.Of(t) == "Chara" && t.parent != null && NameCache.Of(t.parent) == "DataSave" && t.Find("Tapp") != null) { chara = t; break; }
            if (chara == null) return;
            var window = chara.parent.Find("BackGround/Window");
            Image map = null;
            if (window != null) foreach (var img in window.GetComponentsInChildren<Image>()) if (IsMap(img) && img.isActiveAndEnabled) { map = img; break; }
            if (map == null) return;
            var tex = Mod.LoadImage(Png);
            if (tex == null) { End(); return; }
            _group = chara.GetComponent<CanvasGroup>() ?? chara.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f; _group.blocksRaycasts = false;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, 0f), 100f, 0, SpriteMeshType.FullRect);
            var go = new GameObject("PartnerLive2D_UploadScene", typeof(RectTransform), typeof(Image));
            go.layer = map.gameObject.layer;
            go.transform.SetParent(window, false);
            go.transform.SetAsLastSibling();          // above every copy of the scrolling strip (it loops two images)
            var image = go.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = false;
            _scene = (RectTransform)go.transform;
            _scene.pivot = new Vector2(.5f, 0f);
            float w = 860f, h = w * tex.height / tex.width;
            _scene.sizeDelta = new Vector2(w, h);
            // feet on the strip's walkway (measured on the cabinet capture 2026-10-31)
            var mr = (RectTransform)map.transform;
            var centre = mr.localPosition + Vector3.Scale((Vector3)mr.rect.center, mr.localScale);
            _home = centre + new Vector3(0f, -5f, 0f);
            _scene.localPosition = _home;
            Mod.Msg("ngo upload scene: character group hidden (" + chara.childCount + " mascots)");
        }

        // setup never matched: log where the strips and character groups really are, for the next fix
        static void Diagnose()
        {
            foreach (var img in FindObjectsOfType<Image>())
                if (IsMap(img)) Mod.Msg("ngo upload scene: strip at " + MusicSelectLayout.Path(img.transform));
            foreach (var t in FindObjectsOfType<RectTransform>())
                if (t.name == "Tapp" || t.name == "Chara") Mod.Msg("ngo upload scene: " + t.name + " at " + MusicSelectLayout.Path(t));
        }

        public void End()
        {
            if (_group != null) { _group.alpha = 1f; _group.blocksRaycasts = true; }
            _group = null;
            if (_scene != null) Destroy(_scene.gameObject);
            if (Current == this) Current = null;
            Destroy(gameObject);
        }
    }
}
