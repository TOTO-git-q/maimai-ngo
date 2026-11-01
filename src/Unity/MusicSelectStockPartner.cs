using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // Independent of PartnerView/its custom model registry: never hides/replaces a game navi.
    sealed class MusicSelectStockPartner : MonoBehaviour, IMusicSelectPartnerReactions
    {
        public int PartnerId { get; private set; }
        public int NaviId { get; private set; }
        NavigationCharacter _navi;
        RectTransform _fit;
        RectMask2D _cropMask;
        float _nativeHeight, _centerX, _bottomY, _aspect;
        bool _canReact;
        float _reactionUntil;
        string _fallbackReason;
        Action<Exception> _failure;

        public static MusicSelectStockPartner Attach(Transform main, int partnerId, int naviId,
            MusicSelectSettings layout, Action<Exception> failure)
        {
            var go = new GameObject("PartnerLive2D_Select_Stock_" + partnerId, typeof(RectTransform));
            go.layer = main.gameObject.layer;
            go.transform.SetParent(main, false);
            go.transform.SetAsFirstSibling();
            var view = go.AddComponent<MusicSelectStockPartner>();
            view.PartnerId = partnerId; view.NaviId = naviId; view._failure = failure;
            try
            {
                var root = (RectTransform)go.transform;
                root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
                root.pivot = new Vector2(.5f, 0);
                root.sizeDelta = Vector2.zero;
                var group = go.AddComponent<CanvasGroup>();
                group.interactable = false; group.blocksRaycasts = false;
                try { view.CreateNavi(main.gameObject.layer); }
                catch (Exception e)
                {
                    view._fallbackReason = e.GetType().Name + ": " + e.Message;
                    if (view._fit != null) { view._fit.gameObject.SetActive(false); Destroy(view._fit.gameObject); }
                    view._fit = null; view._navi = null; view._canReact = false;
                    view.CreateTexture();
                    Mod.Warn("select stock partner " + partnerId + " uses full-body texture: " + view._fallbackReason);
                }
                view.SetSelectLayout(layout);
                return view;
            }
            catch { go.SetActive(false); Destroy(go); throw; }
        }

        void CreateNavi(int layer)
        {
            var assets = AssetManager.Instance();
            // Same path as GetNaviCharaPrefab; load the exact navi so its silent navi-1
            // fallback cannot display a different partner when this asset is unavailable.
            var prefab = assets.LoadAsset<GameObject>("NaviChara/Prefab/UI_Navichara_" + NaviId.ToString("D2") + ".prefab");
            var source = prefab == null ? null : prefab.GetComponent<NavigationCharacter>();
            if (source == null) throw new InvalidOperationException("equipped navi prefab unavailable");
            var fitGo = new GameObject("StockFit", typeof(RectTransform));
            _fit = (RectTransform)fitGo.transform;
            _fit.SetParent(transform, false);
            _fit.anchorMin = _fit.anchorMax = _fit.pivot = new Vector2(.5f, 0);
            _fit.sizeDelta = Vector2.zero;
            _navi = Instantiate(source, _fit); // ResultMonitor / PartnerRoom presentation
            _navi.gameObject.SetActive(true);
            foreach (var child in _fit.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            foreach (var canvas in _fit.GetComponentsInChildren<Canvas>(true)) canvas.overrideSorting = false;
            foreach (var raycaster in _fit.GetComponentsInChildren<GraphicRaycaster>(true)) raycaster.enabled = false;

            int animationLayer = (int)AccessTools.Field(typeof(NavigationCharacter), "_animationLayerIndex").GetValue(_navi);
            bool canDefault = Supports(animationLayer, "Navi_Default");
            _canReact = canDefault && Supports(animationLayer, "Navi_Fun_Loop_02");
            if (canDefault)
            {
                _navi.Play(NavigationAnime.Default, 0f);
                foreach (var animator in _navi.NaviAnimator) if (animator != null) animator.Update(0f);
            }
            foreach (var graphic in _fit.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            Measure();
            _remeasure = 5;
        }

        // The footprint is read in the fitting container's own (unscaled) space, after the animator has posed the
        // navi and the canvas has laid it out. Measuring only once at creation read a footprint far too small (the
        // navi was not fully posed yet), and the scale derived from it blew the stock partner up over the screen.
        int _remeasure;
        const float MaxStockWidth = 520f;   // Main units (the circle is 1080 wide)
        // a width-capped navi hangs this far below the partner rect's top edge, clear of the freedom-mode banner
        // ("Aime をかざすと…") that runs under the circle's top rim
        const float StockTopDrop = 135f;
        void Measure()
        {
            Canvas.ForceUpdateCanvases();
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            float coreMin = float.MaxValue, coreMax = float.MinValue;   // head + body only: emptied limb/tail rects skew the centre
            var corners = new Vector3[4];
            foreach (var graphic in _fit.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.gameObject.activeInHierarchy || !graphic.enabled || graphic.canvasRenderer.GetAlpha() <= 0f) continue;
                string n = graphic.gameObject.name;
                bool core = n == "Head" || n == "Head_Ring" || n == "Body";
                graphic.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var p = _fit.InverseTransformPoint(corner);
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                    if (core) { coreMin = Mathf.Min(coreMin, p.x); coreMax = Mathf.Max(coreMax, p.x); }
                }
            }
            _nativeHeight = maxY - minY;
            if (!(_nativeHeight > 0) || !(maxX > minX) || float.IsInfinity(_nativeHeight) || float.IsNaN(_nativeHeight))
                throw new InvalidOperationException("navi has no usable UI footprint");
            _centerX = coreMax > coreMin ? (coreMin + coreMax) * .5f : (minX + maxX) * .5f; _bottomY = minY;
            _aspect = (maxX - minX) / _nativeHeight;
            Mod.Msg(string.Format("select stock partner {0}: footprint {1:0}x{2:0} at ({3:0},{4:0})", PartnerId, maxX - minX, _nativeHeight, _centerX, _bottomY));
        }

        bool Supports(int layer, string state)
        {
            var animators = _navi.NaviAnimator;
            if (animators == null || animators.Length == 0 || animators[0] == null) return false;
            if (layer < 0 || layer >= animators[0].layerCount) return false;
            int hash = Animator.StringToHash(layer == 0 ? state : animators[0].GetLayerName(layer) + "." + state);
            bool found = false;
            foreach (var animator in animators)
            {
                if (animator == null) continue;
                if (animator.runtimeAnimatorController == null || layer >= animator.layerCount || !animator.HasState(layer, hash)) return false;
                found = true;
            }
            return found;
        }

        void CreateTexture()
        {
            // GetPartnerTexture2D is an icon. Use the full-body result asset path from
            // GetPartnerResultTexture2D directly, bypassing this mod's custom image hook
            // and the game's unrelated partner-1 dummy fallback. AssetManager owns it.
            var texture = AssetManager.Instance().LoadAsset<Texture2D>("Partner/UI_PartnerResult_" + PartnerId.ToString("000000") + ".png");
            if (texture == null || texture.height <= 0) throw new InvalidOperationException("equipped stock partner prefab and full-body texture unavailable; native: " + _fallbackReason);
            var image = gameObject.AddComponent<RawImage>();
            image.texture = texture; image.raycastTarget = false;
            _aspect = (float)texture.width / texture.height;
        }

        MusicSelectSettings _lastLayout;
        public void SetSelectLayout(MusicSelectSettings layout)
        {
            if (ReferenceEquals(_lastLayout, layout)) return;
            _lastLayout = layout;
            var root = (RectTransform)transform;
            float cropped = layout.Height * layout.CropBottom;
            root.sizeDelta = new Vector2(layout.Height * _aspect, layout.Height - cropped);
            root.localPosition = new Vector3(layout.BottomX, layout.BottomY + cropped, 0);
            root.localScale = Vector3.one;
            if (_fit == null)
            {
                var image = GetComponent<RawImage>();
                if (image != null) image.uvRect = new Rect(0, layout.CropBottom, 1, 1 - layout.CropBottom);
                return;
            }
            // The partner rectangle is tuned for a tall full-body figure (its feet hidden behind the song panel).
            // A squat stock navi scaled to that height would be wider than the screen, so cap its width and hang it
            // from the same top edge instead, so the whole navi stays visible above the panel.
            float scale = layout.Height / _nativeHeight;
            float width = _nativeHeight * _aspect;
            if (width * scale > MaxStockWidth)
            {
                scale = MaxStockWidth / width;
                float h = _nativeHeight * scale, top = layout.BottomY + layout.Height - StockTopDrop;
                root.sizeDelta = new Vector2(width * scale, h);
                root.localPosition = new Vector3(layout.BottomX, top - h, 0);
                cropped = 0;
                if (_cropMask != null) _cropMask.enabled = false;
                _fit.localScale = Vector3.one * scale;
                _fit.localPosition = new Vector3(-_centerX * scale, -_bottomY * scale, 0);
                return;
            }
            if (_cropMask == null && layout.CropBottom > 0) _cropMask = gameObject.AddComponent<RectMask2D>();
            if (_cropMask != null) _cropMask.enabled = layout.CropBottom > 0;
            _fit.localScale = Vector3.one * scale;
            _fit.localPosition = new Vector3(-_centerX * scale, -_bottomY * scale - cropped, 0);
        }

        public void LookAt(float x, float y, float seconds) { } // no stock gaze API
        public void Nod() { SelectSongReaction(); }
        public void SelectSongReaction()
        {
            if (!_canReact || _navi == null || _reactionUntil > Time.realtimeSinceStartup) return;
            _navi.Play(NavigationAnime.FunStartLoop, 0f);
            _reactionUntil = Time.realtimeSinceStartup + 1.3333334f; // PartnerRoom timing
        }
        void Update()
        {
            if (_remeasure > 0 && _fit != null && _navi != null)
            {
                _remeasure--;
                float h = _nativeHeight, a = _aspect;
                try { Measure(); } catch (InvalidOperationException) { }
                if (Mathf.Abs(_nativeHeight - h) > 0.5f || Mathf.Abs(_aspect - a) > 0.001f)
                {
                    var layout = _lastLayout; _lastLayout = null;
                    if (layout != null) SetSelectLayout(layout);
                }
            }
            if (_reactionUntil == 0 || Time.realtimeSinceStartup < _reactionUntil) return;
            try { _reactionUntil = 0; if (_navi != null) _navi.Play(NavigationAnime.Default, 0f); }
            catch (Exception e) { gameObject.SetActive(false); _failure?.Invoke(e); }
        }
        // Keep the navi upright on the select screen: the stock idle and reaction clips tilt the head and body,
        // which looked lopsided. Positions (bob) still animate; only rotations are cleared after the animator ran.
        Transform[] _parts;
        void LateUpdate()
        {
            if (_navi == null) return;
            if (_parts == null) _parts = _navi.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < _parts.Length; i++)
                if (_parts[i] != null && _parts[i].localRotation != Quaternion.identity) _parts[i].localRotation = Quaternion.identity;
        }
        public void Suspend()
        {
            gameObject.SetActive(false);
            if (_reactionUntil == 0) return;
            _reactionUntil = 0;
            if (_navi != null) _navi.Play(NavigationAnime.Default, 0f);
        }
        public void Close() { gameObject.SetActive(false); Destroy(gameObject); }
        public object Info() => new Dictionary<string, object>
        {
            { "kind", "stock" }, { "partnerId", PartnerId }, { "naviCharaId", NaviId },
            { "presentation", _navi == null ? "fullBodyTexture" : "navigationCharacter" },
            { "reactions", _canReact }, { "fallbackReason", _fallbackReason }
        };
    }
}
