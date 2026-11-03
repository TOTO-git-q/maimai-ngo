using System;
using System.Collections.Generic;
using HarmonyLib;
using Monitor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // Captured once after OnStart, never recaptured from a transformed value.
    sealed class MusicSelectLayout : IDisposable
    {
        sealed class Baseline
        {
            public readonly Transform Target, Parent;
            public readonly int Sibling;
            public readonly Vector3 Position, Scale;
            public readonly Quaternion Rotation;
            public readonly bool Active;
            public readonly Vector3 MainPosition, MainScale;
            readonly MusicSelectNodeState _state;
            public Baseline(Transform t, Transform main)
            {
                Target = t; Parent = t.parent; Sibling = t.GetSiblingIndex();
                Position = t.localPosition; Scale = t.localScale; Rotation = t.localRotation; Active = t.gameObject.activeSelf;
                _state = new MusicSelectNodeState(new[] { Position.x, Position.y, Position.z, Scale.x, Scale.y, Scale.z }, v =>
                {
                    if (Target == null) return;
                    Target.localPosition = new Vector3(v[0], v[1], v[2]);
                    Target.localScale = new Vector3(v[3], v[4], v[5]);
                });
                MainPosition = main.InverseTransformPoint(t.position);
                MainScale = new Vector3(t.lossyScale.x / main.lossyScale.x, t.lossyScale.y / main.lossyScale.y, t.lossyScale.z / main.lossyScale.z);
                if (!Finite(Position) || !Finite(Scale) || !Finite(MainPosition) || !Finite(MainScale))
                    throw new InvalidOperationException("nonfinite baseline: " + Path(t));
            }
            public void Apply(float[] v, bool explicitNode = false)
            {
                if (Target == null || Target.parent != Parent) throw new InvalidOperationException("layout target lost/reparented");
                _state.Apply(v, explicitNode);
            }
            public void Restore()
            {
                if (Target == null) return;
                // We never change parent, sibling, rotation or active state; those remain game-owned.
                _state.Restore();
                Target.localPosition = Position; Target.localScale = Scale;
            }
            public void RestoreChanged() { _state.Restore(); }
            public void Reapply() { _state.Reapply(); }
            public object Info() => new Dictionary<string, object>
            {
                { "path", Path(Target) }, { "parent", Path(Parent) }, { "sibling", Sibling }, { "activeAtCapture", Active },
                { "baselinePosition", XYZ(Position) }, { "baselineScale", XYZ(Scale) },
                { "position", Target == null ? null : XYZ(Target.localPosition) },
                { "scale", Target == null ? null : XYZ(Target.localScale) }
            };
        }

        public readonly Transform Main;
        readonly string _mainPath, _mainPrefix;
        readonly HashSet<string> _validatedPaths = new HashSet<string>();
        readonly HashSet<string> _validatedSpritePaths = new HashSet<string>();
        readonly Dictionary<string, Baseline> _groups = new Dictionary<string, Baseline>();
        readonly List<Baseline> _fixedHints = new List<Baseline>();
        readonly List<Baseline> _medals = new List<Baseline>();
        sealed class Visibility
        {
            public CanvasGroup Group;
            public bool Added;
            public MusicSelectHiddenState State;
        }
        readonly Dictionary<Transform, Visibility> _visibility = new Dictionary<Transform, Visibility>();
        readonly Dictionary<Transform, Baseline> _baselines = new Dictionary<Transform, Baseline>();
        readonly Dictionary<string, Baseline> _nodes = new Dictionary<string, Baseline>();
        readonly List<Transform> _decoration = new List<Transform>();
        readonly List<Transform> _phaseHidden = new List<Transform>();
        readonly HashSet<Transform> _hidden = new HashSet<Transform>();
        static readonly float[] OptionAdjustment = { .8f, -45f, 0f };
        readonly MusicSelectSprites _sprites = new MusicSelectSprites();
        readonly MusicSelectImages _images = new MusicSelectImages();
        readonly List<Baseline> _lateNodes = new List<Baseline>();
        readonly Dictionary<string, Baseline> _phaseNodes = new Dictionary<string, Baseline>();
        sealed class TabLabel
        {
            public TextMeshProUGUI Text;
            public Vector3 Position;
            public Color Color;
        }
        sealed class TabTitle
        {
            public Image Image;
            public Vector3 Position;
            public Image Bg;            // the selected tab's base: kept white so its violet face ignores the category tint
            public Color BgColor;
        }
        static readonly System.Reflection.FieldInfo MiniTextField = AccessTools.Field(typeof(MiniTabPanel), "_categoryNameText");
        static readonly System.Reflection.FieldInfo MainTitleField = AccessTools.Field(typeof(MainTabPanel), "_titleImage");
        static readonly System.Reflection.FieldInfo MainBgField = AccessTools.Field(typeof(MainTabPanel), "_bgImage");
        readonly List<TabLabel> _tabLabels = new List<TabLabel>();
        readonly List<TabTitle> _tabTitles = new List<TabTitle>();
        Color _tabColor; bool _tabColorOn;
        readonly MusicSelectLateRunner _runner;
        MusicSelectSettings _current;
        bool _difficultyPhase;
        readonly Image _source;
        Image _label;
        Baseline _myContent;
        public bool Applied { get; private set; }
        public bool Dirty { get; private set; }
        int _restoreFrame = -1;
        public bool CanApply => _restoreFrame != Time.frameCount;
        public void RefreshImage(string id) { _images.Refresh(id); }
        public void RefreshSprite(string path)
        {
            if (AppliesPath(path)) _sprites.Apply(Resolve(path, true, true), null);
        }

        public MusicSelectLayout(MusicSelectMonitor monitor)
        {
            Main = monitor.transform.Find("Canvas/Main");
            if (Main == null) throw new InvalidOperationException("missing MusicSelectProcess/Canvas/Main");
            _mainPath = Path(Main); _mainPrefix = _mainPath + "/";
            string[] names = { "UI_ChainLists", "Null_GenreControll", "Null_SelectorBackgroundTarget", "Null_UI_MSS_MusicPlayStatus", "Null_UI_MSS_MyStatus" };
            string[] keys = { "chains", "tabs", "selector", "status", "myStatus" };
            for (int i = 0; i < names.Length; i++)
            {
                var t = Main.Find(names[i]);
                if (t == null || t.GetComponent<Animator>() != null) throw new InvalidOperationException("missing/animated outer container: " + names[i]);
                _groups.Add(keys[i], Capture(t));
            }
            string[] extraNames = { "UI_CMN_TitleBase", "Null_UI_MSS_MusicAchievementRate", "Null_UI_MSS_RivalData",
                "UI_MSS_ExtraInfo", "Null_UI_FST_MSSelecter_Deco", "Null_TestChallengeBGTarget", "Null_TestChallengeBGDecoTarget" };
            string[] extraKeys = { "difficultyHeader", "achievementRate", "rivalData", "extraInfo", "festaDeco", "challengeBG", "challengeDeco" };
            for (int i = 0; i < extraNames.Length; i++)
            {
                var target = Main.Find(extraNames[i]);
                if (target == null && extraKeys[i] == "extraInfo") target = Main.Find("Null_UI_MSS_ExtraInfo");
                if (target != null) _groups.Add(extraKeys[i], Capture(target));
            }
            var tabs = _groups["tabs"].Target;
            foreach (var b in tabs.GetComponentsInChildren<TabButton>(true))
                _fixedHints.Add(Capture(b.transform.parent)); // static socket outside button Animator
            if (_fixedHints.Count == 0 && monitor.IsActive()) throw new InvalidOperationException("tab hint sockets not found");
            foreach (var t in tabs.GetComponentsInChildren<Transform>(true))
                if (t.name == "Allclear_Rank" || t.name == "Allclear_Medal") _medals.Add(Capture(t));
            foreach (var t in _groups["myStatus"].Target.GetComponentsInChildren<Transform>(true))
                if (t.name == "MyStatusBase") { _myContent = Capture(t); break; }
            var title = Main.Find("UI_CMN_TitleBase");
            var controller = title == null ? null : title.GetComponent<DifficultyContoroller>();
            if (controller == null) throw new InvalidOperationException("difficulty controller not found");
            _source = (Image)AccessTools.Field(typeof(DifficultyContoroller), "_difficutlyNameImage").GetValue(controller);
            if (_source == null) throw new InvalidOperationException("difficulty image not found");
            // Only central art is suppressed; difficulty control sockets and freedom info stay untouched.
            var art = title.Find("Null_UI_CMN_TitleBase");
            if (art == null) throw new InvalidOperationException("difficulty art root missing");
            foreach (Transform t in art) if (t.name == "Title" || t.name == "Base" || t.name == "Base_Shadow") _decoration.Add(t);
            if (_decoration.Count == 0) throw new InvalidOperationException("difficulty decoration not found");
            // Capture stock local values before any later tuning request or Animator sample.
            CaptureStockNodes(Main);
            string[] phasePaths = { "UI_ChainLists/NormalChainList" };
            foreach (var path in phasePaths) { var target = Main.Find(path); if (target != null) _phaseNodes[path] = Capture(target); }
            string[] hiddenPaths = {
                "Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/Scrolling_Jacket",
                "Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/UI_SelectorBG/Null_CanvasGroup/Text",
                "Null_SelectorBackgroundTarget/UI_CMN_SelectorBG(Clone)/Null_CanvasGroup/Text",
                "Null_UI_MSS_RivalData/UI_MSS_NationalData(Clone)"
            };
            foreach (var path in hiddenPaths) { var target = Main.Find(path); if (target != null) _phaseHidden.Add(target); }
            _runner = Main.gameObject.AddComponent<MusicSelectLateRunner>();
            _runner.Layout = this;
        }
        void CaptureStockNodes(Transform parent)
        {
            foreach (Transform child in parent)
            {
                if (MusicSelectSettings.IsInputLayer(child.name)) continue;
                Capture(child); CaptureStockNodes(child);
            }
        }

        public void Apply(MusicSelectSettings c, string phase)
        {
            if (!Applied)
            {
                // Mark before mutation so partial failures also restore.
                Applied = true;
                var go = new GameObject("PartnerLive2D_DifficultyLabel", typeof(RectTransform), typeof(Image));
                go.layer = Main.gameObject.layer; go.transform.SetParent(Main, false);
                go.transform.SetSiblingIndex(Main.Find("UI_Buttons").GetSiblingIndex()); // keep warnings/buttons above the added label
                _label = go.GetComponent<Image>(); _label.raycastTarget = false; _label.preserveAspect = true;
            }
            _current = c;
            CaptureTabLabels(c);
            if (_difficultyPhase && phase != "Difficulty" && phase != "UtageDifficulty")
            {
                Baseline oldHeader;
                if (_groups.TryGetValue("difficultyHeader", out oldHeader)) oldHeader.Restore();
            }
            _difficultyPhase = phase == "Difficulty" || phase == "UtageDifficulty";
            _lateNodes.Clear();
            foreach (var b in _phaseNodes.Values) b.RestoreChanged();
            // Shared targets reuse the original stock baseline, including group/node aliases.
            foreach (var node in c.Nodes)
            {
                if (!AppliesPath(node.Key) || (phase != "Music" && MusicSelectSettings.IsSpriteLayerPath(node.Key))) continue;
                var target = Resolve(node.Key, true, node.Value.IsSpriteOnly);
                if (MusicSelectSettings.IsSpriteLayerPath(node.Key)) continue;
                Baseline previous;
                if (!_nodes.TryGetValue(node.Key, out previous) || previous.Target != target) _nodes[node.Key] = Capture(target);
                _lateNodes.Add(_nodes[node.Key]);
            }
            foreach (var g in _groups) g.Value.Apply(c.Groups[g.Key]);
            foreach (var b in _fixedHints) Pin(b, b.MainPosition, b.MainScale); // cancel only the moved/scaled tab ancestor
            foreach (var b in _medals) Pin(b, new Vector3(b.MainPosition.x * .45f, -30, b.MainPosition.z), b.MainScale * .55f);
            if (_myContent != null)
            {
                _myContent.Restore();
                var world = _myContent.Target.position + Main.TransformVector(new Vector3(c.MyStatusX, c.MyStatusY, 0));
                _myContent.Target.position = world;
            }
            var hidden = _hidden;
            hidden.Clear();
            if (!_difficultyPhase) foreach (var target in _decoration) hidden.Add(target);
            foreach (var g in _groups) if (c.GroupsHidden[g.Key]) hidden.Add(g.Value.Target);
            foreach (var node in c.Nodes)
            {
                if (!AppliesPath(node.Key)) continue;
                if (MusicSelectSettings.IsSpriteLayerPath(node.Key))
                {
                    // Do not capture/write game-owned transform or visibility, even identity.
                    _sprites.Apply(Resolve(node.Key, true, node.Value.IsSpriteOnly), phase == "Music" ? node.Value.Sprite : null);
                    continue;
                }
                var baseline = _nodes[node.Key];
                baseline.Apply(node.Value.Transform, true);
                _sprites.Apply(baseline.Target, node.Value.Sprite);
                if (node.Value.Hidden && !MusicSelectPhasePolicy.ShowGenreNode(phase, node.Key)) hidden.Add(baseline.Target); else hidden.Remove(baseline.Target);
            }
            Baseline adjustment;
            if (phase == "Option" && _phaseNodes.TryGetValue("UI_ChainLists/NormalChainList", out adjustment))
            { adjustment.Apply(OptionAdjustment); _lateNodes.Add(adjustment); }
            foreach (var target in _phaseHidden) hidden.Add(target);
            foreach (var target in hidden) SetHidden(target);
            foreach (var v in _visibility) if (!hidden.Contains(v.Key)) v.Value.State.Apply(false);
            var rt = _label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.pivot = new Vector2(.5f, .5f);
            rt.localPosition = new Vector3(c.LabelX, c.LabelY, 0); rt.sizeDelta = new Vector2(c.LabelWidth, c.LabelHeight);
            _label.sprite = _source.sprite; _label.color = _source.color;
            _label.enabled = !_difficultyPhase && _source.enabled && _source.gameObject.activeInHierarchy && !c.GroupsHidden["difficultyHeader"];
            if (_difficultyPhase) PositionDifficultyHeader();
            ApplyUnder(c.PartnerUnder);
            _images.Apply(c.Images, this);
            MaintainTabLabels();
        }
        void CaptureTabLabels(MusicSelectSettings settings)
        {
            if (MiniTextField == null || MainTitleField == null) throw new InvalidOperationException("tab label fields not found");
            _tabColorOn = !string.IsNullOrEmpty(settings.TabLabelColor);
            if (_tabColorOn && !ColorUtility.TryParseHtmlString(settings.TabLabelColor, out _tabColor)) throw new InvalidOperationException("invalid tab label color");
            Dirty = false;
            for (int i = _tabLabels.Count - 1; i >= 0; i--) if (_tabLabels[i].Text == null) _tabLabels.RemoveAt(i);
            for (int i = _tabTitles.Count - 1; i >= 0; i--) if (_tabTitles[i].Image == null) _tabTitles.RemoveAt(i);
            var tabs = _groups["tabs"].Target;
            var minis = tabs.GetComponentsInChildren<MiniTabPanel>(true);
            var mains = tabs.GetComponentsInChildren<MainTabPanel>(true);
            if (minis.Length == 0 || mains.Length == 0) Dirty = true;
            foreach (var panel in minis)
            {
                var text = (TextMeshProUGUI)MiniTextField.GetValue(panel);
                if (text == null) { Dirty = true; continue; }
                bool found = false;
                for (int i = 0; i < _tabLabels.Count; i++) if (_tabLabels[i].Text == text) { found = true; break; }
                if (!found) _tabLabels.Add(new TabLabel { Text = text, Position = text.transform.localPosition, Color = text.color });
            }
            foreach (var panel in mains)
            {
                var title = (Image)MainTitleField.GetValue(panel);
                if (title == null) { Dirty = true; continue; }
                bool found = false;
                for (int i = 0; i < _tabTitles.Count; i++) if (_tabTitles[i].Image == title) { found = true; break; }
                var bg = MainBgField == null ? null : (Image)MainBgField.GetValue(panel);
                if (!found) _tabTitles.Add(new TabTitle { Image = title, Position = title.transform.localPosition, Bg = bg, BgColor = bg != null ? bg.color : Color.white });
            }
        }
        void MaintainTabLabels()
        {
            for (int i = 0; i < _tabLabels.Count; i++)
            {
                var item = _tabLabels[i];
                if (item.Text == null) { Dirty = true; continue; }
                var position = item.Position; position.y += _current.TabLabelOffsetY;
                if (item.Text.transform.localPosition != position) item.Text.transform.localPosition = position;
                if (_tabColorOn && item.Text.color != _tabColor) item.Text.color = _tabColor;
            }
            for (int i = 0; i < _tabTitles.Count; i++)
            {
                var item = _tabTitles[i];
                if (item.Image == null) { Dirty = true; continue; }
                var position = item.Position; position.y += _current.TabTitleOffsetY;
                if (item.Image.transform.localPosition != position) item.Image.transform.localPosition = position;
                if (item.Bg != null)
                {
                    var c = item.Bg.color;   // keep the game's alpha (fades), drop its category tint
                    if (c.r != 1f || c.g != 1f || c.b != 1f) item.Bg.color = new Color(1f, 1f, 1f, c.a);
                }
            }
        }
        // Cached references only: called after Animator sampling and before canvas rendering.
        public void Maintain()
        {
            if (!Applied || _current == null) return;
            MaintainTabLabels();
            _sprites.Maintain();
            foreach (var target in _hidden)
            {
                Visibility v;
                if (!_visibility.TryGetValue(target, out v) || v.Group == null) continue;
                if (v.Group.alpha != 0) v.Group.alpha = 0;
                if (v.Group.blocksRaycasts) v.Group.blocksRaycasts = false;
            }
            if (_label == null || _source == null) return;
            if (_label.sprite != _source.sprite) _label.sprite = _source.sprite;
            if (_label.color != _source.color) _label.color = _source.color;
            bool enabled = !_difficultyPhase && _source.enabled && _source.gameObject.activeInHierarchy && !_current.GroupsHidden["difficultyHeader"];
            if (_label.enabled != enabled) _label.enabled = enabled;
        }
        public void ReapplyTransforms()
        {
            if (!Applied || _current == null) return;
            foreach (var g in _groups) g.Value.Reapply();
            foreach (var b in _fixedHints) Pin(b, b.MainPosition, b.MainScale);
            foreach (var b in _medals) Pin(b, new Vector3(b.MainPosition.x * .45f, -30, b.MainPosition.z), b.MainScale * .55f);
            if (_myContent != null)
            {
                _myContent.Target.localPosition = _myContent.Position;
                _myContent.Target.localScale = _myContent.Scale;
                _myContent.Target.position += Main.TransformVector(new Vector3(_current.MyStatusX, _current.MyStatusY, 0));
            }
            for (int i = 0; i < _lateNodes.Count; i++) _lateNodes[i].Reapply();
            if (_difficultyPhase) PositionDifficultyHeader();
        }
        void PositionDifficultyHeader()
        {
            Baseline header;
            if (_groups.TryGetValue("difficultyHeader", out header))
                Pin(header, header.MainPosition, header.MainScale);
        }
        void Pin(Baseline b, Vector3 pos, Vector3 scale)
        {
            if (b.Target == null) throw new InvalidOperationException("hint/medal target lost");
            var p = b.Target.parent.lossyScale; var m = Main.lossyScale;
            // An in/out animation can temporarily collapse an ancestor. Do not divide by zero.
            if (Mathf.Abs(p.x) < .00001f || Mathf.Abs(p.y) < .00001f || Mathf.Abs(p.z) < .00001f) return;
            b.Target.position = Main.TransformPoint(pos);
            b.Target.localScale = new Vector3(scale.x * m.x / p.x, scale.y * m.y / p.y, scale.z * m.z / p.z);
        }
        public void Restore()
        {
            if (!Applied) return;
            if (_difficultyPhase)
            {
                Baseline header;
                if (_groups.TryGetValue("difficultyHeader", out header)) header.Restore();
            }
            _current = null; _lateNodes.Clear();
            _difficultyPhase = false;
            _images.Dispose();
            _sprites.Dispose();
            _restoreFrame = Time.frameCount; // wait for deferred component destruction before another Apply
            foreach (var b in _groups.Values) b.RestoreChanged();
            foreach (var b in _fixedHints) b.Restore();
            foreach (var b in _medals) b.Restore();
            if (_myContent != null) _myContent.Restore();
            foreach (var b in _nodes.Values) b.RestoreChanged();
            foreach (var b in _phaseNodes.Values) b.RestoreChanged();
            foreach (var item in _tabLabels) if (item.Text != null) { item.Text.transform.localPosition = item.Position; item.Text.color = item.Color; }
            foreach (var item in _tabTitles)
            {
                if (item.Image != null) item.Image.transform.localPosition = item.Position;
                if (item.Bg != null) item.Bg.color = item.BgColor;
            }
            _tabLabels.Clear(); _tabTitles.Clear(); Dirty = false;
            foreach (var v in _visibility.Values) if (v.Group != null)
            {
                v.State.Restore();
                if (v.Added) UnityEngine.Object.Destroy(v.Group);
            }
            _visibility.Clear();
            if (_label != null) { _label.gameObject.SetActive(false); UnityEngine.Object.Destroy(_label.gameObject); }
            _label = null; Applied = false;
            RemoveUnder();
        }
        // partner.under: the partner is Main's first child, so everything in Main draws over it. Each listed node gets
        // its own canvas one order below the screen's canvas: it then draws before all of that canvas, partner included
        // (the difficulty header's purple band, so the pair can overlap it while the MASTER text stays on top).
        readonly List<Canvas> _under = new List<Canvas>();
        string _underSpec;
        void ApplyUnder(string spec)
        {
            if (spec == _underSpec) return;
            RemoveUnder(); _underSpec = spec;
            var screen = Main.GetComponentInParent<Canvas>();
            if (screen == null) return;
            var targets = new List<Transform>();
            foreach (var raw in (spec ?? "").Split(','))
            {
                var path = raw.Trim();
                if (path.Length == 0) continue;
                var target = Main.Find(path);
                if (target == null || target.GetComponent<Canvas>() != null) { Mod.Warn("select partner.under '" + path + "' not found or already a canvas"); continue; }
                targets.Add(target);
            }
            // listed order = draw order (a shadow listed before its band stays under it)
            for (int i = 0; i < targets.Count; i++)
            {
                var own = targets[i].gameObject.AddComponent<Canvas>();
                own.overrideSorting = true;
                own.sortingLayerID = screen.sortingLayerID;
                own.sortingOrder = screen.sortingOrder - (targets.Count - i);
                _under.Add(own);
            }
        }
        void RemoveUnder()
        {
            foreach (var c in _under) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            _under.Clear(); _underSpec = null;
        }
        Baseline Capture(Transform target)
        {
            Baseline baseline;
            if (!_baselines.TryGetValue(target, out baseline)) _baselines[target] = baseline = new Baseline(target, Main);
            return baseline;
        }
        void SetHidden(Transform target)
        {
            Visibility v;
            if (!_visibility.TryGetValue(target, out v))
            {
                var group = target.GetComponent<CanvasGroup>(); bool added = group == null;
                if (added) group = target.gameObject.AddComponent<CanvasGroup>();
                v = new Visibility { Group = group, Added = added,
                    State = new MusicSelectHiddenState(group.alpha, group.blocksRaycasts, (alpha, raycasts) =>
                    { if (group != null) { group.alpha = alpha; group.blocksRaycasts = raycasts; } }) };
                _visibility.Add(target, v);
            }
            v.State.Apply(true);
        }
        public Transform Resolve(string path, bool control, bool spriteOnly = false)
        {
            if (control)
            {
                var validated = spriteOnly ? _validatedSpritePaths : _validatedPaths;
                if (!validated.Contains(path)) { MusicSelectSettings.ValidateNodePath(path, spriteOnly); validated.Add(path); }
            }
            if (string.IsNullOrEmpty(path)) return Main;
            var clean = path.TrimStart('/');
            Transform target = clean == _mainPath ? Main : clean.StartsWith(_mainPrefix, StringComparison.Ordinal)
                ? Main.Find(clean.Substring(_mainPrefix.Length)) : Main.Find(clean);
            if (target == null) throw new ApiError(404, "node not found under this side's Main: " + path);
            if (control)
            {
                if (target == Main || !target.IsChildOf(Main)) throw new ApiError(400, "Main and its ancestors are protected");
                var image = target.GetComponent<Image>();
                bool leafImage = target.childCount == 0 && image != null && image.sprite != null;
                for (var ancestor = target; ancestor != Main; ancestor = ancestor.parent)
                {
                    try { MusicSelectSettings.ValidateInputLayerAccess(ancestor.name, ancestor != target, spriteOnly, leafImage); }
                    catch (FormatException e) { throw new ApiError(400, e.Message); }
                }
                if (ContainsInputLayer(target)) throw new ApiError(400, "ancestors of input/touch layers are protected");
            }
            return target;
        }
        static bool ContainsInputLayer(Transform target)
        {
            if (MusicSelectSettings.IsInputLayer(target.name)) return true;
            for (int i = 0; i < target.childCount; i++)
                if (ContainsInputLayer(target.GetChild(i))) return true;
            return false;
        }
        public bool AppliesPath(string path)
        {
            string clean = path.TrimStart('/');
            bool full = clean.IndexOf("/Canvas/Main/", StringComparison.Ordinal) >= 0 || clean.EndsWith("/Canvas/Main", StringComparison.Ordinal);
            return !full || clean == _mainPath || clean.StartsWith(_mainPrefix, StringComparison.Ordinal);
        }
        public object Tree(string root, int depth) => TreeNode(Resolve(root, false), depth);
        object TreeNode(Transform target, int depth)
        {
            var children = new List<object>();
            if (depth > 0) foreach (Transform child in target) children.Add(TreeNode(child, depth - 1));
            var components = new List<string>();
            foreach (var component in target.GetComponents<Component>()) components.Add(component == null ? "MissingScript" : component.GetType().Name);
            var rect = target as RectTransform;
            object bounds = null;
            if (rect != null)
            {
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                var points = new List<object>();
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var corner in corners)
                {
                    var p = Main.InverseTransformPoint(corner); points.Add(XYZ(p));
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
                bounds = new Dictionary<string, object> { { "corners", points }, { "x", minX }, { "y", minY }, { "width", maxX - minX }, { "height", maxY - minY } };
            }
            return new Dictionary<string, object> { { "name", target.name }, { "path", Path(target) },
                { "relativePath", target == Main ? "" : Path(target).Substring(Path(Main).Length + 1) },
                { "active", target.gameObject.activeSelf }, { "activeInHierarchy", target.gameObject.activeInHierarchy },
                { "localPosition", XYZ(target.localPosition) }, { "localScale", XYZ(target.localScale) },
                { "sizeDelta", rect == null ? null : (object)new[] { rect.sizeDelta.x, rect.sizeDelta.y } },
                { "anchors", rect == null ? null : new Dictionary<string, object> { { "min", new[] { rect.anchorMin.x, rect.anchorMin.y } }, { "max", new[] { rect.anchorMax.x, rect.anchorMax.y } } } },
                { "worldRectInMain", bounds }, { "childCount", target.childCount }, { "components", components }, { "children", children } };
        }
        public void Dispose() { Restore(); if (_runner != null) UnityEngine.Object.Destroy(_runner); }
        public object Info()
        {
            var groups = new Dictionary<string, object>(); foreach (var g in _groups) groups[g.Key] = g.Value.Info();
            var canvas = Main.GetComponentInParent<Canvas>();
            return new Dictionary<string, object> { { "applied", Applied }, { "main", Path(Main) }, { "groups", groups },
                { "canvasMode", canvas == null ? "missing" : canvas.renderMode.ToString() },
                { "sortingLayer", canvas == null ? "" : canvas.sortingLayerName }, { "sortingOrder", canvas == null ? 0 : canvas.sortingOrder },
                { "fixedHintSockets", _fixedHints.Count }, { "allclearGraphics", _medals.Count } };
        }
        static object XYZ(Vector3 v) => new[] { v.x, v.y, v.z };
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        public static string Path(Transform t) => t == null ? null : (t.parent == null ? t.name : Path(t.parent) + "/" + t.name);
    }
}
