using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;
using Process;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // NGO skin: cover the cabinet power-on / self-check screens with NEEDY GIRL OVERDOSE's own boot sequence until the
    // warning screen. Lower (circle) screen: the Windose 20 boot_logo with the poketter loading sprite animation
    // (8 frames, 130 ms, pixel-crisp), nothing else. Upper screen: the game's login screen, the password "kangelkawaii2"
    // typed one letter at a time as the self-check advances (progress read from the game's own check list, which
    // keeps running underneath), the checkmark when it is done. Assets: LocalAssets/PartnerLive2D/boot/.
    [HarmonyPatch(typeof(Manager.ProcessManager), "AddProcess")]
    static class NgoBootScreen
    {
        static GameObject _overlay;
        static BootView _view;
        const float Split = 1080f / 1920f;   // the circle square is the lower 1080 of the 1080x1920 cabinet screen
        const float TopScreen = 1f - 450f / 1920f;   // the cabinet's upper monitor shows only the top 1080x450 band

        static string Dir => Path.Combine(Mod.AssetRoot, "boot");
        static bool Prepare() => File.Exists(Path.Combine(Dir, "windose_boot.png"));

        static void Postfix(ProcessBase process)
        {
            if (process == null) return;
            string name = process.GetType().Name;
            if (name == "PowerOnProcess" || name == "StartupProcess") Show();
            else if (_overlay != null && (name == "WarningProcess" || name == "AdvertiseProcess")) _view.Finish();
        }

        static Texture2D Load(string file, bool crisp = true)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!File.Exists(file) || !tex.LoadImage(File.ReadAllBytes(file))) return null;
            tex.filterMode = crisp ? FilterMode.Point : FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        static void Show()
        {
            if (_overlay != null) return;
            var boot = Load(Path.Combine(Dir, "windose_boot.png"), false);
            if (boot == null) { Mod.Warn("boot screen: cannot read windose_boot.png"); return; }
            var frames = new List<Texture2D>();
            for (int i = 0; ; i++) { var f = Load(Path.Combine(Dir, "loading_" + i + ".png")); if (f == null) break; frames.Add(f); }

            _overlay = new GameObject("PartnerLive2D_NgoBoot");
            Object.DontDestroyOnLoad(_overlay);
            var canvas = _overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
            var scaler = _overlay.AddComponent<CanvasScaler>();   // lay out in cabinet units whatever the window size
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = .5f;
            var group = _overlay.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            Anchor(NewImage(_overlay.transform, "Black", Color.black).rectTransform, 0, 0, 1, 1);

            // lower screen: boot logo + loading animation (game layout: 24x24 at (244,248) of a 512x288 screen,
            // i.e. centred near the bottom; in the centred square crop that is x .458-.542, y .056-.139 from below)
            var circle = NewRaw(_overlay.transform, "Windose", boot);
            Anchor(circle.rectTransform, 0, 0, 1, Split);
            var spin = NewRaw(circle.transform, "Loading", frames.Count > 0 ? frames[0] : null);
            Anchor(spin.rectTransform, .458f, .056f, .542f, .139f);
            spin.enabled = frames.Count > 0;

            // upper screen (the cabinet's top monitor = top 1080x450 band): one composed layer (ngo_boot_top.py: the
            // game's login background, a white card, Ame's icon, the account name, an empty password box); the
            // password is typed into the box and the checkmark appears at its right end
            var top = NewRaw(_overlay.transform, "Login", Load(Path.Combine(Dir, "top_static.png")));
            Anchor(top.rectTransform, 0, TopScreen, 1, 1);
            var field = new GameObject("Field", typeof(RectTransform)).GetComponent<RectTransform>();
            field.SetParent(top.transform, false);
            Anchor(field, .3167f, .2489f, .9130f, .4978f);
            var check = NewRaw(field, "Checkmark", Load(Path.Combine(Dir, "checkmark.png")));
            var cr = check.rectTransform; cr.anchorMin = cr.anchorMax = new Vector2(1, .5f); cr.pivot = new Vector2(1, .5f);
            cr.sizeDelta = new Vector2(27, 23) * 2.5f; cr.anchoredPosition = new Vector2(-24, 0);
            check.enabled = false;
            _view = _overlay.AddComponent<BootView>();
            _view.Init(group, frames, spin, field, check);
            Mod.Msg("boot screen: Windose 20 shown (" + frames.Count + " loading frames)");
        }

        static Image NewImage(Transform parent, string name, Color c)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(parent, false); img.color = c; img.raycastTarget = false;
            return img;
        }

        static RawImage NewRaw(Transform parent, string name, Texture tex)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(parent, false); img.texture = tex; img.raycastTarget = false;
            if (tex == null) img.enabled = false;
            return img;
        }

        // anchors as fractions of the parent, (x0,y0) bottom-left to (x1,y1) top-right
        static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        sealed class BootView : MonoBehaviour
        {
            const string Password = "kangelkawaii2";
            const int ExpectedItems = 12;   // rough size of the power-on check list, until more items are seen
            CanvasGroup _group; List<Texture2D> _frames; RawImage _spin, _check; RectTransform _field;
            TextMeshProUGUI _text;
            float _frameClock, _scanClock, _clock, _done = -1f, _shown;
            int _frame;
            readonly HashSet<string> _seen = new HashSet<string>(), _passed = new HashSet<string>();
            static readonly Regex Code = new Regex(@"^[A-E]\d$");   // touch sensor map cells (A1..E8)
            static readonly Regex Status = new Regex(@"^(GOOD|OK|CHECK|NG|BAD|ERROR|WAIT|SKIP|N/A|-+|初期化中|チェック中|確認中|接続中|待機中)$", RegexOptions.IgnoreCase);

            public void Init(CanvasGroup g, List<Texture2D> f, RawImage spin, RectTransform field, RawImage check)
            { _group = g; _frames = f; _spin = spin; _field = field; _check = check; }

            public void Finish() { if (_done < 0f) _done = 0f; }

            void Update()
            {
                float dt = Time.unscaledDeltaTime; _clock += dt;
                if (_frames.Count > 0 && (_frameClock += dt) >= .13f)
                {
                    _frameClock -= .13f; _frame = (_frame + 1) % _frames.Count; _spin.texture = _frames[_frame];
                }
                if ((_scanClock += dt) >= .25f) { _scanClock = 0f; Scan(); }

                // typed letters follow the check progress (never backwards), one letter per 100 ms at most
                float progress = _done >= 0f ? 1f : Mathf.Min(.95f, Mathf.Max(
                    (float)_passed.Count / Mathf.Max(ExpectedItems, _seen.Count),
                    Mathf.Min(.5f, _clock / 60f)));   // slow creep, so it never looks frozen
                int target = Mathf.RoundToInt(progress * Password.Length);
                if (target > _shown && _clock - _lastType >= .1f) { _shown++; _lastType = _clock; }
                SetText(Password.Substring(0, Mathf.Min((int)_shown, Password.Length)));
                bool full = _shown >= Password.Length;
                if (_check.texture != null) _check.enabled = full;

                if (_done < 0f || !full) return;
                _done += dt;
                if (_done < .35f) return;   // let the checkmark show for a moment
                _group.alpha = Mathf.Clamp01(1f - (_done - .35f) / .4f);
                if (_done >= .75f) { Destroy(gameObject); _overlay = null; }
            }
            float _lastType = -1f;

            // Read the game's self-check list: item labels and their status on the same row. Each label that has
            // shown GOOD/OK counts as passed.
            void Scan()
            {
                var lines = new List<KeyValuePair<float, string>>();
                foreach (var t in Object.FindObjectsOfType<TMP_Text>())
                {
                    if (t == _text || !t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
                    var info = t.textInfo; if (info == null) continue;
                    string s = t.GetParsedText();
                    for (int i = 0; i < info.lineCount; i++)
                    {
                        var li = info.lineInfo[i]; int a = li.firstCharacterIndex, b = li.lastCharacterIndex;
                        if (a < 0 || b >= s.Length || b < a) continue;
                        string v = s.Substring(a, b - a + 1).Trim();
                        if (v.Length == 0 || Code.IsMatch(v)) continue;
                        lines.Add(new KeyValuePair<float, string>(t.rectTransform.TransformPoint(new Vector3(0, li.baseline, 0)).y, v));
                    }
                }
                var labels = new List<KeyValuePair<float, string>>(); var stats = new List<KeyValuePair<float, string>>();
                foreach (var l in lines) (Status.IsMatch(l.Value) ? stats : labels).Add(l);
                if (labels.Count == 0) return;
                labels.Sort((p, q) => q.Key.CompareTo(p.Key));
                float tol = labels.Count > 1 ? Mathf.Max(2f, Mathf.Abs(labels[0].Key - labels[1].Key) * .5f) : 10f;
                foreach (var lab in labels)
                {
                    _seen.Add(lab.Value);
                    foreach (var s in stats)
                        if (Mathf.Abs(s.Key - lab.Key) <= tol && (s.Value.ToUpperInvariant() == "GOOD" || s.Value.ToUpperInvariant() == "OK"))
                        { _passed.Add(lab.Value); break; }
                }
            }

            void SetText(string s)
            {
                if (_text == null)
                {
                    TMP_FontAsset font = null;
                    foreach (var t in Object.FindObjectsOfType<TMP_Text>()) if (t.font != null) { font = t.font; break; }
                    if (font == null) return;
                    _text = new GameObject("Password", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                    _text.transform.SetParent(_field, false); _text.raycastTarget = false;
                    _text.font = font; _text.fontSize = 64; _text.color = new Color32(77, 35, 207, 255);
                    _text.alignment = TextAlignmentOptions.MidlineLeft; _text.enableWordWrapping = false;
                    _text.overflowMode = TextOverflowModes.Overflow;
                    Anchor(_text.rectTransform, .04f, 0f, .86f, 1f);
                }
                if (_text.text != s) _text.text = s;
            }
        }
    }
}
