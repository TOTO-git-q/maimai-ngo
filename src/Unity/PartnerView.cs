using System;
using System.Collections.Generic;
using HarmonyLib;
using Manager;
using PartnerLive2D.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // Lives next to the game's NavigationCharacter, hides it, and shows the Live2D model instead.
    // State comes from:
    //   - the result timeline (Default -> FunStart -> FunStartLoop when cleared, Default -> Sad01 otherwise)
    //   - NavigationCharacter.Play calls (after the timeline, on skip, in the partner room)
    //   - touching the partner (ResultMonitor.TouchPartnerCharacter) -> tap motion
    //   - the control API (preview a state / motion / expression / gaze, live layout edits)
    // and held touch-panel zones steer where the model looks.
    public sealed class PartnerView : MonoBehaviour, IMusicSelectPartnerReactions, IMusicSelectTouchPartner
    {
        static readonly Dictionary<NavigationCharacter, PartnerView> ByNavi = new Dictionary<NavigationCharacter, PartnerView>();
        public static readonly List<PartnerView> All = new List<PartnerView>();
        static int _nextId = 1;

        static readonly AccessTools.FieldRef<ResultTimelineManager, PlayableDirector> DirectorRef =
            AccessTools.FieldRefAccess<ResultTimelineManager, PlayableDirector>("_director");
        static readonly AccessTools.FieldRef<ResultTimelineManager, bool> IsClearRef =
            AccessTools.FieldRefAccess<ResultTimelineManager, bool>("_isClear");
        static readonly AccessTools.FieldRef<ResultTimelineManager, double> FunStartEndRef =
            AccessTools.FieldRefAccess<ResultTimelineManager, double>("_naviFunStartEndTime");

        public int Id { get; private set; }
        public string Context { get; private set; }     // result | room | overlay
        public PartnerConfig Config => _cfg;
        public string State => _state;

        PartnerConfig _cfg;
        CubismAvatar _avatar;
        Live2DRenderer _renderer;
        SpritePairBackend _sprite;
        SpriteLibraryBackend _library;      // music select from the frame library (partner.ini "library"); else _sprite
        bool _libraryFailed;
        string _spriteLoop = "Idle";
        RenderTexture TargetTexture { get { return _library != null ? _library.Texture : _sprite != null ? _sprite.Texture : _renderer.Texture; } }
        float TargetAspect { get { return _library != null ? 1f : _sprite != null ? _sprite.Aspect : _renderer.Aspect; } }
        bool Sprites { get { return _sprite != null || _library != null; } }
        public bool IsSprite { get { return Sprites; } }
        // the session's sprite groups as the library's moments
        static string MomentOf(string group) { return group == "Enter" ? "start" : group == "Confirm" ? "confirm" : group == "Tap" ? "tap" : group; }
        public bool SpriteCanPlay(string group)
        {
            if (_library != null) return _library.CanPlay(MomentOf(group));
            return _sprite != null && _sprite.Playback != null && _sprite.Playback.CanPlay(group);
        }
        public bool SpritePlay(string group)
        {
            if (_library != null) return _library.Moment(MomentOf(group), -1);
            return _sprite != null && _sprite.Playback != null && _sprite.Playback.Play(group, -1);
        }
        public bool SpritePlaying(string group)
        {
            if (_library != null) return _library.Playing(MomentOf(group));
            return _sprite != null && _sprite.Playback != null && _sprite.Playback.IsPlaying(group);
        }
        RawImage _image;
        NavigationCharacter _original;
        CanvasGroup _hider;
        ResultTimelineManager _timeline;
        Transform _scope;              // result monitor, where resultUnder is looked up
        readonly List<Canvas> _underCanvases = new List<Canvas>();
        GameObject _overlayCanvas;
        int _monitor = -1;
        bool _placed;
        string _state;
        string _loopGroup;
        int _tapFrame = -1;

        // footprint of the stock partner in the parent's local space (bottom-centre + height)
        float _baseCx, _baseMinY, _baseH;

        // API overrides
        float _manualUntil;            // timeline sync paused while previewing a state
        float _lookUntil;
        Vector2 _lookOverride;
        MusicSelectSettings _selectLayout;
        Action<Exception> _selectFailure;
        float _nodUntil;
        float _selectMotionUntil;

        public static PartnerView AttachMusicSelect(Transform main, PartnerConfig cfg, int monitor, MusicSelectSettings layout, Action<Exception> failure)
        {
            var go = new GameObject("PartnerLive2D_Select_" + cfg.Name, typeof(RectTransform));
            go.layer = main.gameObject.layer; go.transform.SetParent(main, false); go.transform.SetAsFirstSibling();
            var view = go.AddComponent<PartnerView>();
            view.Context = "musicSelect"; view._selectLayout = layout; view._selectFailure = failure;
            try
            {
                if (!view.Init(null, cfg, monitor, null)) throw new InvalidOperationException("select partner model load failed");
                view.ApplyLayout(); view._placed = true; view._image.enabled = true;
                return view;
            }
            catch { go.SetActive(false); Destroy(go); throw; }
        }

        public void SetSelectLayout(MusicSelectSettings layout) { if (ReferenceEquals(_selectLayout, layout)) return; _selectLayout = layout; ApplyLayout(); }
        // test: show the view's rectangle as one flat colour (preview compositing measures what the UI covers with a black
        // and a white fill, and the background with a clear one); null/"" shows the partner again
        public bool Fill(string hex)
        {
            Color c;
            if (string.IsNullOrEmpty(hex)) { _image.texture = TargetTexture; _image.color = Color.white; return true; }
            if (!ColorUtility.TryParseHtmlString(hex, out c)) return false;
            _image.texture = Texture2D.whiteTexture; _image.color = c;
            return true;
        }
        public void SelectSongReaction()
        {
            if (Sprites) return;
            if (_avatar.GetMotionCount("TapBody") > 0 && _avatar.StartRandomMotion("TapBody", 2))
                _selectMotionUntil = Time.realtimeSinceStartup + .65f;
        }
        public void Nod() { if (Sprites) return; _nodUntil = Time.realtimeSinceStartup + .32f; }

        // music select: what happened on the page (MusicSelectReactionRouter events) as the library's moments
        bool _entered, _countdown;
        int _lastDifficulty = -1;
        public void SelectEvent(string type, object data)
        {
            if (_library == null) return;
            try
            {
                switch (type)
                {
                    case "initial":   // the router starts over after every visit to another phase: once per page
                        if (_entered) return;
                        _entered = true;
                        _library.Moment(GameManager.MusicTrackNumber <= 1 ? "enter" : "return", -1);
                        break;
                    case "categoryChanged": _library.Moment("category", -1); break;
                    case "songChanged": _library.Moment("song", -1); break;
                    case "difficultyChanged":
                        int d = data is int ? (int)data : -1;
                        if (d >= 3 && (_lastDifficulty < 0 || d > _lastDifficulty)) _library.Moment("master", -1);   // up to Master / Re:Master
                        if (d >= 0) _lastDifficulty = d;
                        break;
                }
            }
            catch (Exception e) { Mod.Warn(_cfg.Name + ": select event " + type + ": " + e.Message); }
        }
        // the select timer shows 10: once per page
        public void SelectCountdown()
        {
            if (_library == null || _countdown) return;
            _countdown = true;
            _library.Moment("countdown", -1);
        }
        // the third tap on the same one within 1.5 s (one finger often covers two sensors: taps 0.15 s apart count once)
        readonly float[][] _tapTimes = { new float[3], new float[3] };
        readonly int[] _tapCount = new int[2];
        bool Poked(int who)
        {
            float now = Time.realtimeSinceStartup; var times = _tapTimes[who]; int n = _tapCount[who];
            if (n > 0 && now - times[(n - 1) % 3] < .15f) return false;
            times[n % 3] = now; _tapCount[who] = ++n;
            return n >= 3 && now - times[n % 3] <= 1.5f;
        }
        // a touch sensor's centre on the pair canvas (1024 square, y down), when it lies inside the partner rectangle; the
        // sensors are laid out around the centre of the monitor's Main canvas (select: the view's parent; room: SetTouchSpace)
        bool SensorOnPair(string name, out Vector2 pair)
        {
            pair = Vector2.zero;
            int i = name[1] - '1'; char ring = name[0];
            if (i < 0 || i > 7 || (ring != 'A' && ring != 'B' && ring != 'C' && ring != 'D' && ring != 'E')) return false;
            float radius = ring == 'A' || ring == 'D' ? 490 : ring == 'B' ? 295 : ring == 'E' ? 205 : 0;
            float angle = (45 * i + (ring == 'A' || ring == 'B' ? 22.5f : 0)) * Mathf.Deg2Rad;
            var point = new Vector3(Mathf.Sin(angle) * radius + (ring == 'C' ? (i == 0 ? -30 : 30) : 0), Mathf.Cos(angle) * radius, 0);
            var rect = (RectTransform)transform;
            var local = rect.InverseTransformPoint((_touchSpace != null ? _touchSpace : transform.parent).TransformPoint(point));
            var r = rect.rect;
            if (!r.Contains(new Vector2(local.x, local.y))) return false;
            float crop = Context == "musicSelect" ? _selectLayout.CropBottom : 0f;
            float v = crop + (local.y - r.yMin) / r.height * (1 - crop);
            pair = new Vector2((local.x - r.xMin) / r.width * 1024f, (1 - v) * 1024f);
            return true;
        }
        // one of the pair touched (pair canvas x, < 0 unknown): squashed now, reacts after; the other one notices a beat later
        // (owner 2026-11-02: "戳糖糖/超天酱的时候另一个角色的副反应"), only when the touched one does react
        void TouchPair(int who, float x)
        {
            _library.Finger(who, x);
            if (Poked(who) && _library.Moment("poke", who)) _library.Moment("otherPoke", 1 - who);
            else if (_library.Moment("tap", who)) _library.Moment("otherTap", 1 - who);
        }
        // whoever is under the finger reacts: K in front, Ame behind her
        void TouchSensor(string name)
        {
            Vector2 pair;
            if (!SensorOnPair(name, out pair)) return;
            int who = _library.HitTest(pair.x, pair.y);
            if (Context == "room") _library.Log?.Invoke(string.Format("touch {0} at ({1:0}, {2:0}) on the pair: {3}", name, pair.x, pair.y, _library.CharacterName(who)));
            if (who >= 0) TouchPair(who, pair.x);
        }
        // partner room (2026-11-02 night, owner "搭档房间也换"): the sensors the game itself takes as touching the partner
        // (CollectionProcess.InputPartnerPresentSequence: B1-B8, C1, C2, E1), read every frame as the select page's router does
        static readonly InputManager.TouchPanelArea[] RoomAreas = {
            InputManager.TouchPanelArea.B1, InputManager.TouchPanelArea.B2, InputManager.TouchPanelArea.B3, InputManager.TouchPanelArea.B4,
            InputManager.TouchPanelArea.B5, InputManager.TouchPanelArea.B6, InputManager.TouchPanelArea.B7, InputManager.TouchPanelArea.B8,
            InputManager.TouchPanelArea.C1, InputManager.TouchPanelArea.C2, InputManager.TouchPanelArea.E1 };
        static readonly string[] RoomAreaNames = { "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "C1", "C2", "E1" };
        Transform _touchSpace;
        int _roomPlayer = -1;
        public void SetTouchSpace(Transform space, int player) { _touchSpace = space; _roomPlayer = player; }
        void RoomTouches()
        {
            if (_roomPlayer < 0 || _touchSpace == null || !_placed) return;
            for (int i = 0; i < RoomAreas.Length; i++)
                if (InputManager.GetTouchPanelAreaDown(_roomPlayer, RoomAreas[i])) TouchSensor(RoomAreaNames[i]);
        }
        // partner room: a present was given (PartnerRoom.PlayPartnerEmotionEffectOnceTime) - both of them are pleased
        public void RoomPresent()
        {
            if (_library != null) _library.Moment("roomPresent", -1);
        }
        public void TouchArea(string name)
        {
            if (_library != null)
            {
                if (Context != "musicSelect" || name == null || name.Length != 2) return;
                if (_selectLayout.SelectTapAreas != null && !_selectLayout.SelectTapAreas.Contains(name)) return;
                TouchSensor(name);
                return;
            }
            if (_sprite == null || Context != "musicSelect" || name == null || name.Length != 2) return;
            if (_selectLayout.SelectTapAreas != null)
            {
                if (_selectLayout.SelectTapAreas.Contains(name)) OnTap();
                return;
            }
            int i = name[1] - '1'; char ring = name[0];
            if (i < 0 || i > 7 || (ring != 'A' && ring != 'B' && ring != 'C' && ring != 'D' && ring != 'E')) return;
            float radius = ring == 'A' || ring == 'D' ? 490 : ring == 'B' ? 295 : ring == 'E' ? 205 : 0;
            float angle = (45 * i + (ring == 'A' || ring == 'B' ? 22.5f : 0)) * Mathf.Deg2Rad;
            var point = new Vector3(Mathf.Sin(angle) * radius + (ring == 'C' ? (i == 0 ? -30 : 30) : 0), Mathf.Cos(angle) * radius, 0);
            var rect = (RectTransform)transform;
            var local = rect.InverseTransformPoint(transform.parent.TransformPoint(point));
            if (rect.rect.Contains(new Vector2(local.x, local.y))) OnTap();
        }

        public static PartnerView Find(NavigationCharacter navi)
        {
            PartnerView v;
            return navi != null && ByNavi.TryGetValue(navi, out v) && v != null ? v : null;
        }

        public static PartnerView FindById(int id)
        {
            foreach (var v in All) if (v != null && v.Id == id) return v;
            return null;
        }

        public static PartnerView Attach(Transform root, NavigationCharacter original, PartnerConfig cfg, int monitor, ResultTimelineManager timeline, string context, string spriteTier = null)
        {
            if (root == null || original == null) return null;
            foreach (var old in root.GetComponentsInChildren<PartnerView>(true)) Destroy(old.gameObject);

            var go = new GameObject("PartnerLive2D_" + cfg.Name, typeof(RectTransform));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            go.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
            var view = go.AddComponent<PartnerView>();
            view.Context = context;
            if (spriteTier != null) { view._spriteLoop = "Result" + spriteTier + "Loop"; view._resultTier = spriteTier; }
            if (!view.Init(original, cfg, monitor, timeline)) { Destroy(go); return null; }
            return view;
        }

        // result screen: the stock partner sits after the achievement band and before the score, rank and rating, but the
        // rating's own band (UI_DXRating/Base) comes later and covers the partner's legs. partner.ini resultUnder lists such
        // objects; each gets its own canvas one step below the screen's canvas, so it draws under everything there
        // (the partner included) and the hierarchy the game's animations bind to stays as it is
        public void ApplyResultUnder(Transform scope)
        {
            if (scope != null) _scope = scope;
            if (Context != "result" || _scope == null) return;
            foreach (var c in _underCanvases) if (c != null) DestroyImmediate(c);   // now: the same object may get one again below
            _underCanvases.Clear();
            foreach (var raw in (_cfg.ResultUnder ?? "").Split(','))
            {
                var spec = raw.Trim();
                if (spec.Length == 0) continue;
                var target = FindUnder(_scope, spec);
                var screen = target != null ? target.GetComponentInParent<Canvas>() : null;
                if (target == null || screen == null || target.GetComponent<Canvas>() != null) { Mod.Warn(_cfg.Name + ": resultUnder '" + spec + "' not found or already a canvas"); continue; }
                var own = target.gameObject.AddComponent<Canvas>();
                own.overrideSorting = true;
                own.sortingLayerID = screen.sortingLayerID;
                own.sortingOrder = screen.sortingOrder - 1;
                _underCanvases.Add(own);
            }
        }

        // first object under scope whose name, or path relative to scope, ends with spec (active ones first)
        static Transform FindUnder(Transform scope, string spec)
        {
            Transform inactive = null;
            foreach (var t in scope.GetComponentsInChildren<Transform>(true))
            {
                if (t == scope) continue;
                string path = t.name;
                for (var p = t.parent; p != null && p != scope; p = p.parent) path = p.name + "/" + path;
                if (path != spec && !path.EndsWith("/" + spec)) continue;
                if (t.gameObject.activeInHierarchy) return t;
                if (inactive == null) inactive = t;
            }
            return inactive;
        }

        // the model on its own top-most canvas; demo = cycle through every state by itself
        public static PartnerView AttachOverlay(PartnerConfig cfg, bool demo)
        {
            var canvasGo = new GameObject("PartnerLive2D_Overlay", typeof(Canvas));
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var go = new GameObject("PartnerLive2D_" + cfg.Name, typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var view = go.AddComponent<PartnerView>();
            view.Context = "overlay";
            view._overlayCanvas = canvasGo;
            view._overlayDemo = demo;
            if (!view.Init(null, cfg, 0, null)) { Destroy(canvasGo); return null; }
            return view;
        }

        bool Init(NavigationCharacter original, PartnerConfig cfg, int monitor, ResultTimelineManager timeline)
        {
            _cfg = cfg; _original = original; _monitor = monitor; _timeline = timeline;
            if (!LoadBackend()) return false;

            _image = gameObject.AddComponent<RawImage>();
            _image.texture = TargetTexture;
            _image.raycastTarget = false;
            var premul = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            if (premul != null) _image.material = new Material(premul);
            _image.enabled = false;   // shown once placed

            if (original != null)
            {
                _hider = original.GetComponent<CanvasGroup>();
                if (_hider == null) _hider = original.gameObject.AddComponent<CanvasGroup>();
                ByNavi[original] = this;
            }
            Id = _nextId++;
            All.Add(this);
            // partner room: the pair greets you as the room opens (the game itself only moves the partner on touches and presents)
            if (_library != null && Context == "room") _library.Moment("roomEnter", -1);
            if (!Sprites) EnterState("Default");
            else _state = _spriteLoop;
            return true;
        }

        bool LoadBackend()
        {
            if (_sprite != null) { _sprite.Dispose(); _sprite = null; }
            if (_library != null) { _library.Dispose(); _library = null; }
            // the select page, (2026-11-02) the result screen and the partner room play from the frame library
            if ((Context == "musicSelect" || Context == "result" || Context == "room") && !_libraryFailed && !string.IsNullOrEmpty(_cfg.LibraryPath))
            {
                try
                {
                    if (!System.IO.File.Exists(_cfg.LibraryPath)) throw new System.IO.FileNotFoundException("library not found", _cfg.LibraryPath);
                    _library = new SpriteLibraryBackend(_cfg.LibraryPath, RuntimeQuality.Policy.RenderSize(_cfg.RenderSize));
                    var name = _cfg.Name; var where = Context == "result" ? " result: " : Context == "room" ? " room: " : " select: ";
                    _library.Log = line => Mod.Msg(name + where + line);
                    if (_renderer != null) { _renderer.Dispose(); _renderer = null; }
                    if (_avatar != null) { _avatar.Dispose(); _avatar = null; }
                    return true;
                }
                catch (Exception e) { _library = null; _libraryFailed = true; Mod.Warn(_cfg.Name + ": frame library unavailable; using the sprite clips: " + e.Message); }
            }
            // the partner room plays like the select page: idle loop, random moves, taps
            bool pageClips = Context == "musicSelect" || Context == "room";
            if ((pageClips || (Context == "result" && _spriteLoop != "Idle")) && !string.IsNullOrEmpty(_cfg.SpritePath))
            {
                try
                {
                    _sprite = new SpritePairBackend(_cfg.SpritePath, _spriteLoop, pageClips, RuntimeQuality.Policy.RenderSize(_cfg.RenderSize));
                    if (_renderer != null) { _renderer.Dispose(); _renderer = null; }
                    if (_avatar != null) { _avatar.Dispose(); _avatar = null; }
                    return true;
                }
                catch (Exception e) { Mod.Warn(_cfg.Name + ": sprite unavailable; using Live2D: " + e.Message); }
            }
            else if ((pageClips || Context == "result") && string.IsNullOrEmpty(_cfg.SpritePath)) Mod.Msg(_cfg.Name + ": sprites not configured; using Live2D");
            return LoadModel();
        }
        void SpriteFailure(Exception e)
        {
            Mod.Warn(_cfg.Name + ": sprite unavailable; using Live2D: " + e.Message);
            _sprite.Dispose(); _sprite = null;
            if (!LoadModel()) throw new InvalidOperationException("sprite fallback model failed", e);
            _image.texture = _renderer.Texture; _state = null; EnterState("Default");
        }
        // the frame library broke mid-page: the sprite clips (or Live2D) take over for the rest of this page
        void LibraryFailure(Exception e)
        {
            Mod.Warn(_cfg.Name + ": frame library failed; using the sprite clips: " + e);
            _libraryFailed = true;
            _library.Dispose(); _library = null;
            if (!LoadBackend()) throw new InvalidOperationException("library fallback failed", e);
            _image.texture = TargetTexture; _state = Sprites ? _spriteLoop : null;
            if (!Sprites) EnterState("Default");
            if (_placed) ApplyLayout();
        }

        bool LoadModel()
        {
            CubismAvatar loading = null;
            try
            {
                var t0 = Time.realtimeSinceStartup;
                var avatar = loading = CubismAvatar.Load(_cfg.ModelPath);
                if (!string.IsNullOrEmpty(_cfg.IdleGroup)) avatar.IdleGroup = _cfg.IdleGroup;
                var renderer = new Live2DRenderer(avatar, RuntimeQuality.Policy.RenderSize(_cfg.RenderSize));
                if (_renderer != null) _renderer.Dispose();
                if (_avatar != null) _avatar.Dispose();
                _avatar = avatar;
                _renderer = renderer;
                loading = null;
                Mod.Msg(string.Format("{0}: model loaded in {1:0} ms, {2} drawables, RT {3}x{4}",
                    _cfg.Name, (Time.realtimeSinceStartup - t0) * 1000f, avatar.Model.DrawableCount, renderer.Texture.width, renderer.Texture.height));
                return true;
            }
            catch (Exception e)
            {
                if (loading != null) loading.Dispose();
                Mod.Error(_cfg.Name + ": model load failed. " + e);
                return false;
            }
        }

        // ---- control API --------------------------------------------------------------

        public void SetConfig(PartnerConfig cfg) { _cfg = cfg; }

        // called after the config changed; rebuild=true reloads model + renderer
        public bool OnConfigChanged(bool rebuild)
        {
            ApplyResultUnder(null);
            if (rebuild)
            {
                _libraryFailed = false;
                if (!LoadBackend()) return false;
                _image.texture = TargetTexture;
                var s = _state; _state = null; if (!Sprites) EnterState(s ?? "Default"); else _state = _spriteLoop;
            }
            else if (_avatar != null && !string.IsNullOrEmpty(_cfg.IdleGroup)) _avatar.IdleGroup = _cfg.IdleGroup;
            if (_placed) ApplyLayout();
            return true;
        }

        public void PreviewState(string state)
        {
            if (Sprites) return;
            _manualUntil = Time.realtimeSinceStartup + 10f;
            _state = null;
            EnterState(state);
        }

        public bool PlayMotion(string group, int index)
        {
            if (_library != null) return _library.Moment(MomentOf(group), index < 0 ? -1 : index);   // index: 0 Ame, 1 K, -1 both
            if (_sprite != null) return _sprite.Playback != null && _sprite.Playback.Play(group, index);
            _selectMotionUntil = 0;
            _manualUntil = Time.realtimeSinceStartup + 10f;
            return index < 0 ? _avatar.StartRandomMotion(group, 3) : _avatar.StartMotion(group, index, 3);
        }

        public void SetExpression(string name) { if (_avatar != null) _avatar.SetExpression(string.IsNullOrEmpty(name) ? null : name); }

        public void LookAt(float x, float y, float seconds)
        {
            _lookOverride = new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
            _lookUntil = Time.realtimeSinceStartup + Mathf.Max(0.1f, seconds);
        }

        public byte[] FramePng()
        {
            var rt = TargetTexture;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var png = tex.EncodeToPNG();
            Destroy(tex);
            return png;
        }

        public Dictionary<string, object> LibraryTimeline()
        {
            if (_library == null) throw new ApiError(409, "this view does not play the frame library");
            return _library.Timeline();
        }

        public Dictionary<string, object> Info()
        {
            var groups = new Dictionary<string, object>();
            if (_sprite != null && _sprite.Loader != null)
                foreach (var c in _sprite.Loader.Clips) { object n; groups.TryGetValue(c.Data.Group, out n); groups[c.Data.Group] = n == null ? 1 : (int)n + 1; }
            else if (_avatar != null) foreach (var g in _avatar.MotionGroups) groups[g] = _avatar.GetMotionCount(g);
            var rt = (RectTransform)transform;
            return new Dictionary<string, object>
            {
                { "id", Id }, { "partner", _cfg.Name }, { "context", Context }, { "monitor", _monitor },
                { "state", _state }, { "placed", _placed },
                { "x", rt.localPosition.x }, { "y", rt.localPosition.y },
                { "width", rt.sizeDelta.x }, { "height", rt.sizeDelta.y },
                { "baseHeight", _baseH },
                { "renderWidth", TargetTexture.width }, { "renderHeight", TargetTexture.height },
                { "loadedTextures", _sprite == null || _sprite.Loader == null ? 0 : _sprite.Loader.Loaded },
                { "totalTextures", _sprite == null || _sprite.Loader == null ? 0 : _sprite.Loader.Total },
                { "spriteTextureBytes", _sprite == null || _sprite.Loader == null ? 0L : _sprite.Loader.Bytes },
                { "estimatedVramBytes", _sprite == null || _sprite.Loader == null ? 0L : _sprite.Loader.Bytes + 8L * TargetTexture.width * TargetTexture.height },
                { "library", _library == null ? null : _library.Info() },
                { "motionGroups", groups },
                { "expressions", _avatar == null ? new List<object>() : new List<object>(_avatar.ExpressionNames) },
                { "hitAreas", _avatar == null ? new List<object>() : new List<object>(_avatar.HitAreaNames) },
                { "drawables", _avatar == null ? 0 : _avatar.Model.DrawableCount }, { "parameters", _avatar == null ? 0 : _avatar.Model.ParameterCount },
            };
        }

        public void Close()
        {
            gameObject.SetActive(false); // stop Tick/Render immediately, before deferred Destroy
            if (_sprite != null) { _sprite.Dispose(); _sprite = null; }
            if (_library != null) { _library.Dispose(); _library = null; }
            if (_overlayCanvas != null) Destroy(_overlayCanvas); else Destroy(gameObject);
        }

        // ---- overlay demo -----------------------------------------------------------------

        bool _overlayDemo;
        float _overlayClock;

        void OverlayDemo()
        {
            _overlayClock += Time.deltaTime;
            int step = (int)(_overlayClock / 4f);
            EnterState(PartnerConfig.States[step % PartnerConfig.States.Length]);
            if (step % 3 == 2 && _tapFrame < 0) OnTap();
            float a = _overlayClock * 1.5f;
            _avatar.SetLookTarget(Mathf.Cos(a), Mathf.Sin(a));
        }

        // ---- game events ----------------------------------------------------------------

        public void OnNaviPlay(NavigationAnime anime)
        {
            // the staging was skipped (or ran without the timeline): the rank is up when the stock partner reacts
            if (_library != null && Context == "result" && (anime == NavigationAnime.FunStart || anime == NavigationAnime.FunStartLoop || anime == NavigationAnime.Sad01))
            {
                ResultReaction();
                return;
            }
            // partner room: the game's FunStartLoop (every second touch, and after a present) would repeat the reactions
            // RoomTouches and RoomPresent already started
            if (_library != null && Context == "room") return;
            if (Sprites)
            {
                // partner room: closeness went up (present given) -> the pair reacts
                if (Context == "room" && anime == NavigationAnime.FunStart) SpritePlay("Confirm");
                return;
            }
            if (Time.frameCount == _tapFrame) return;   // the tap already chose the motion
            if (Time.realtimeSinceStartup < _manualUntil) return;
            EnterState(anime.ToString());
        }

        public void OnTap()
        {
            _tapFrame = Time.frameCount;
            // result screen: the game only says the partner was touched, not where - one of them, by chance
            if (_library != null) { TouchPair(Context == "result" ? (UnityEngine.Random.value < .5f ? 0 : 1) : 1, -1); return; }
            if (_sprite != null) { SpritePlay(Context == "result" ? _spriteLoop.Replace("Loop", "Reaction") : "Tap"); return; }
            if (!string.IsNullOrEmpty(_cfg.TapGroup))
                _avatar.StartRandomMotion(_cfg.TapGroup, 3);
        }

        void EnterState(string state)
        {
            if (state == _state) return;
            _state = state;
            string expr;
            if (_cfg.Expressions.TryGetValue(state, out expr)) _avatar.SetExpression(string.IsNullOrEmpty(expr) ? null : expr);

            string group;
            _cfg.Motions.TryGetValue(state, out group);
            bool loops = state == "FunStartLoop" || state == "Sad01";
            _loopGroup = loops && !string.IsNullOrEmpty(group) ? group : null;
            if (!string.IsNullOrEmpty(group)) _avatar.StartRandomMotion(group, 2);
        }

        // result screen from the frame library: the pair reacts to the rank (owner 2026-11-02: "结算页面根据成绩出现的反应")
        // when the stock partner would - FunStart when cleared, Sad01 when not; tiers as SpritePairMath.Tier
        string _resultTier;
        bool _resultReacted;
        void ResultCue()
        {
            if (_resultReacted || _timeline == null || _original == null) return;
            var director = DirectorRef(_timeline);
            if (director == null || director.state != UnityEngine.Playables.PlayState.Playing) return;
            double at;
            if (IsClearRef(_timeline)) { var fun = _original.FunStart; at = FunStartEndRef(_timeline) - (fun != null ? fun.length : 0f); }
            else { var def = _original.Default; at = def != null ? def.length : 0f; }
            if (director.time >= at) ResultReaction();
        }
        void ResultReaction()
        {
            if (_resultReacted || _resultTier == null) return;
            _resultReacted = true;
            _library.Moment("result" + _resultTier, -1);
        }

        // timeline-driven state while the result staging is playing
        void SyncTimeline()
        {
            if (_timeline == null || Time.realtimeSinceStartup < _manualUntil) return;
            var director = DirectorRef(_timeline);
            if (director == null || director.state != UnityEngine.Playables.PlayState.Playing) return;
            double t = director.time;
            if (IsClearRef(_timeline))
            {
                double end = FunStartEndRef(_timeline);
                var fun = _original.FunStart;
                double start = end - (fun != null ? fun.length : 0f);
                EnterState(t < start ? "Default" : (t < end ? "FunStart" : "FunStartLoop"));
            }
            else
            {
                var def = _original.Default;
                EnterState(t < (def != null ? def.length : 0f) ? "Default" : "Sad01");
            }
        }

        // ---- look-at from held touch zones -------------------------------------------------

        static readonly InputManager.TouchPanelArea[] Areas = (InputManager.TouchPanelArea[])Enum.GetValues(typeof(InputManager.TouchPanelArea));

        Vector2 TouchDirection()
        {
            if (_monitor < 0 || _cfg.LookStrength <= 0f) return Vector2.zero;
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (var a in Areas)
            {
                // Select uses the cached names; preserve the existing result/room path.
                string n = Context == "musicSelect" ? MusicSelectReactionRouter.AreaNames[(int)a] : a.ToString();
                if (n.Length != 2) continue;          // skip Blank / End
                if (!InputManager.GetTouchPanelAreaPush(_monitor, a)) continue;
                int k = n[1] - '1';
                float deg, r;
                switch (n[0])
                {
                    case 'A': deg = 22.5f + 45f * k; r = 1f; break;
                    case 'B': deg = 22.5f + 45f * k; r = 0.6f; break;
                    case 'D': deg = 45f * k; r = 1f; break;
                    case 'E': deg = 45f * k; r = 0.4f; break;
                    default: deg = 0f; r = 0f; break;  // C1/C2 = centre
                }
                float rad = deg * Mathf.Deg2Rad;
                sum += new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * r;
                count++;
            }
            if (count == 0) return Vector2.zero;
            var dir = sum / count;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            return dir * _cfg.LookStrength;
        }

        // ---- frame loop ---------------------------------------------------------------------

        void Update()
        {
            if (Context != "musicSelect") { UpdateAvatar(); return; }
            try { UpdateAvatar(); }
            catch (Exception e) { gameObject.SetActive(false); _selectFailure?.Invoke(e); }
        }

        void UpdateAvatar()
        {
            if (_library != null)
            {
                try { _library.Tick(Time.unscaledDeltaTime); if (Context == "result") ResultCue(); else if (Context == "room") RoomTouches(); } catch (Exception e) { LibraryFailure(e); }
                return;
            }
            if (_sprite != null)
            {
                try { _sprite.Tick(Time.deltaTime); } catch (Exception e) { SpriteFailure(e); }
                return;
            }
            if (_avatar == null) return;
            if (_selectMotionUntil > 0 && Time.realtimeSinceStartup >= _selectMotionUntil)
            { _selectMotionUntil = 0; _avatar.StopMotions(); }
            SyncTimeline();
            if (_loopGroup != null && _avatar.IsMotionFinished) _avatar.StartRandomMotion(_loopGroup, 2);
            if (Context == "musicSelect" && Time.realtimeSinceStartup < _nodUntil)
                _avatar.SetLookTarget(Time.realtimeSinceStartup < _lookUntil ? _lookOverride.x : 0,
                    -.18f * Mathf.Sin((_nodUntil - Time.realtimeSinceStartup) / .32f * Mathf.PI));
            else if (Time.realtimeSinceStartup < _lookUntil) _avatar.SetLookTarget(_lookOverride.x, _lookOverride.y);
            else if (_overlayDemo) OverlayDemo();
            else
            {
                var look = TouchDirection();
                _avatar.SetLookTarget(look.x, look.y);
            }
            _avatar.Tick(Mathf.Min(Time.deltaTime, 0.1f));
        }

        // music select: extra vertical offset for the current phase (difficulty header), eased in and out
        float _phaseDrop;
        bool _difficultyPhase;
        public void SetSelectPhase(string phase) { _difficultyPhase = phase == "Difficulty" || phase == "UtageDifficulty"; }

        void LateUpdate()
        {
            if (Context != "musicSelect") { RenderAvatar(); return; }
            float target = _difficultyPhase && _selectLayout != null ? _selectLayout.DifficultyOffsetY : 0;
            if (_phaseDrop != target)
            {
                // critically damped-ish: about 0.2 s to settle, snaps the last half unit
                _phaseDrop = Mathf.Lerp(_phaseDrop, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
                if (Mathf.Abs(_phaseDrop - target) < 0.5f) _phaseDrop = target;
                if (_placed) ApplyLayout();
            }
            try { RenderAvatar(); }
            catch (Exception e) { gameObject.SetActive(false); _selectFailure?.Invoke(e); }
        }

        void RenderAvatar()
        {
            if (_renderer == null && !Sprites) return;
            if (Context != "overlay" && Context != "musicSelect" && _original == null) { Destroy(gameObject); return; }
            if (!_placed) Place();
            if (_hider != null) _hider.alpha = 0f;
            if (_library != null) { try { _library.Render(); } catch (Exception e) { LibraryFailure(e); } }
            else if (_sprite != null) { try { _sprite.DrawingBlendSeconds = _cfg.SpriteBlend; _sprite.Render(); } catch (Exception e) { SpriteFailure(e); } }
            else _renderer.Render();

            if (Context != "musicSelect" && _cfg.DebugDump && _dumps < DumpTimes.Length && Time.realtimeSinceStartup - _placedAt > DumpTimes[_dumps])
            {
                _dumps++;
                DumpDebug(_dumps);
            }
        }

        static readonly float[] DumpTimes = { 3f, 9f, 15f };
        int _dumps;
        float _placedAt;

        // one-shot proof images for checking the render without watching the screen
        void DumpDebug(int n)
        {
            try
            {
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(_cfg.Folder, "debug_model_" + n + ".png"), FramePng());
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_cfg.Folder, "debug_screen_" + n + ".png"));
                Mod.Msg(_cfg.Name + ": debug images written to " + _cfg.Folder);
            }
            catch (Exception e) { Mod.Error("debug dump: " + e); }
        }

        // measure the stock partner's footprint once (bottom-centre anchored), then lay out
        void Place()
        {
            if (Context == "overlay")
            {
                var parent = (RectTransform)transform.parent;
                _baseH = parent.rect.height * 0.8f;
                _baseCx = 0f;
                _baseMinY = -parent.rect.height * 0.5f;
            }
            else
            {
                var root = transform.parent;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                var corners = new Vector3[4];
                foreach (var g in _original.GetComponentsInChildren<Graphic>(true))
                {
                    g.rectTransform.GetWorldCorners(corners);
                    foreach (var c in corners)
                    {
                        var p = root.InverseTransformPoint(c);
                        if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                        if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                    }
                }
                _baseH = maxY > minY ? maxY - minY : 800f;
                _baseCx = maxX > minX ? (minX + maxX) * 0.5f : 0f;
                _baseMinY = minY == float.MaxValue ? -_baseH * 0.5f : minY;
            }
            ApplyLayout();
            _image.enabled = true;
            _placed = true;
            _placedAt = Time.realtimeSinceStartup;
            var rt = (RectTransform)transform;
            Mod.Msg(string.Format("{0} [{1} #{2}]: placed at ({3:0},{4:0}) size {5:0}x{6:0} (base height {7:0})",
                _cfg.Name, Context, Id, rt.localPosition.x, rt.localPosition.y, rt.sizeDelta.x, rt.sizeDelta.y, _baseH));
        }

        void ApplyLayout()
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            if (Context == "musicSelect")
            {
                float crop = _selectLayout.CropBottom, remaining = 1 - crop;
                rt.sizeDelta = new Vector2(_selectLayout.Height * TargetAspect, _selectLayout.Height * remaining);
                rt.localPosition = new Vector3(_selectLayout.BottomX, _selectLayout.BottomY + _selectLayout.Height * crop + _phaseDrop, 0);
                rt.localScale = Vector3.one;
                _image.uvRect = _cfg.FlipY ? new Rect(0, remaining, 1, -remaining) : new Rect(0, crop, 1, remaining);
                return;
            }
            bool room = Context == "room";
            float height = _baseH * (room ? _cfg.RoomScale : _cfg.Scale);
            rt.sizeDelta = new Vector2(height * TargetAspect, height);
            rt.localPosition = new Vector3(_baseCx + (room ? _cfg.RoomOffsetX : _cfg.OffsetX) * _baseH, _baseMinY + (room ? _cfg.RoomOffsetY : _cfg.OffsetY) * _baseH, 0f);
            rt.localScale = Vector3.one;
            _image.uvRect = _cfg.FlipY ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (_original != null)
            {
                ByNavi.Remove(_original);
                if (_hider != null) _hider.alpha = 1f;
            }
            if (_sprite != null) _sprite.Dispose();
            if (_library != null) _library.Dispose();
            if (_renderer != null) _renderer.Dispose();
            if (_avatar != null) _avatar.Dispose();
            if (_image != null && _image.material != null && _image.material.shader != null && _image.material.shader.name.StartsWith("Legacy"))
                Destroy(_image.material);
        }
    }
}
