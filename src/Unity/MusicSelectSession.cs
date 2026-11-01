using System;
using System.Collections.Generic;
using HarmonyLib;
using MAI2.Util;
using Manager;
using Monitor;
using Process;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    static class MusicSelectSession
    {
        sealed class Side : IDisposable
        {
            public MusicSelectMonitor Monitor;
            public int Player;
            public MusicSelectLayout Layout;
            public MusicSelectReactionRouter Router;
            public PartnerView View;
            public MusicSelectStockPartner Stock;
            public IMusicSelectPartnerReactions Reactions => View != null ? (IMusicSelectPartnerReactions)View : Stock;
            public MusicSelectTouchDebug Debug;
            public string Phase, Reason, LoggedReason = "", NoPartner;
            public int LayoutFrame = -1;
            public bool Placeholder, EnterParticipant;
            public int PlaceholderCategory, PlaceholderIndex, PlaceholderScoreType;
            public void SuspendPartner()
            {
                Router?.Suspend();
                Debug?.Dispose(); Debug = null;
                if (View != null) View.gameObject.SetActive(false);
                if (Stock != null) Stock.Suspend();
            }
            public void Restore()
            {
                Layout?.Restore(); Router?.Suspend();
                Debug?.Dispose(); Debug = null; LayoutFrame = -1;
                if (View != null) View.gameObject.SetActive(false);
                if (Stock != null) Stock.Suspend();
            }
            public void Dispose()
            {
                try { Restore(); }
                finally
                {
                    if (View != null) View.Close(); View = null;
                    if (Stock != null) Stock.Close(); Stock = null;
                }
            }
        }
        sealed class Session
        {
            public int Id;
            public readonly List<Side> Sides = new List<Side>();
            public bool Decided, EnterActive;
            public float EnterStarted;
            public int PreviewCue = -1, PreviewSerial;
            public bool PreviewPending, PreviewStarted;
            public bool PreviewRequestEvent, PreviewSawStopped;
            public int PreviewFrame;
        }
        static readonly Dictionary<MusicSelectProcess, Session> Sessions = new Dictionary<MusicSelectProcess, Session>();
        // Harmony supports an enum field's underlying type; initialize inside the failure guard.
        static AccessTools.FieldRef<MusicSelectProcess, int> MainSequence;
        static AccessTools.FieldRef<MusicSelectProcess, MusicSelectProcess.SubSequence[]> SubSequences;
        static readonly string[] PhaseNames = Enum.GetNames(typeof(MusicSelectProcess.SubSequence));
        static int _nextId;
        static string _root, _failure;
        public static MusicSelectSettings Settings { get; private set; } = new MusicSelectSettings();

        public static void Initialize(string root)
        {
            _root = root; MusicSelectSprites.Root = root;
            Guard(() =>
            {
                MainSequence = AccessTools.FieldRefAccess<MusicSelectProcess, int>("_mainSequence");
                SubSequences = AccessTools.FieldRefAccess<MusicSelectProcess, MusicSelectProcess.SubSequence[]>("_currentPlayerSubSequence");
                Settings = MusicSelectSettings.Load(root);
                Preload(Settings);
            });
        }
        static void Preload(MusicSelectSettings settings)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                foreach (var node in settings.Nodes) PreloadFile(node.Value.Sprite);
                foreach (var image in settings.Images) PreloadFile(image.Value.Sprite);
            }
            finally { TestHarness.SelectPerf.Add("preload", (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency); }
        }
        static void PreloadFile(string file)
        {
            if (file == null) return;
            // Preserve the previous lazy failure behavior for bad saved presets.
            try { MusicSelectPngCache.Get(file); }
            catch (Exception e) { Mod.Warn("music-select PNG preload failed: " + file + ": " + e.Message); }
        }
        static void InvalidateLayouts()
        {
            foreach (var session in Sessions.Values) foreach (var side in session.Sides) side.LayoutFrame = -1;
        }
        public static void Guard(Action action)
        {
            try { action(); } catch (Exception e) { Fail(e); }
        }
        public static void Fail(Exception e)
        {
            Settings.Enabled = false; _failure = e.GetType().Name + ": " + e.Message;
            foreach (var s in Sessions.Values) foreach (var side in s.Sides)
                try { side.Dispose(); side.View = null; } catch (Exception restore) { Mod.Error("select restore: " + restore); }
            Mod.Error("music-select disabled and restored: " + e);
        }
        public static void Start(MusicSelectProcess p)
        {
            if (!Mod.Ready) return;
            Release(p);
            var s = new Session { Id = ++_nextId }; Sessions.Add(p, s);
            for (int i = 0; i < p.MonitorArray.Length; i++)
            {
                var side = new Side { Monitor = p.MonitorArray[i], Player = i, Router = new MusicSelectReactionRouter(i) };
                s.Sides.Add(side); // register before any fallible setup
                if (!p.IsEntry(i) || !side.Monitor.IsActive()) { side.Reason = "inactive player"; continue; }
                side.Layout = new MusicSelectLayout(side.Monitor);
                Mod.Msg("music-select baseline: " + MusicSelectLayout.Path(side.Layout.Main));
            }
            Update(p);
        }
        public static void Release(MusicSelectProcess p)
        {
            Session s; if (!Sessions.TryGetValue(p, out s)) return;
            try { foreach (var side in s.Sides) { try { try { side.Dispose(); } finally { side.Layout?.Dispose(); } } catch (Exception e) { Mod.Error("select release: " + e); } } }
            finally { Sessions.Remove(p); }
        }
        static bool HasSong(MusicSelectProcess p)
        {
            var lists = p.CombineMusicDataList;
            int c = p.CurrentCategorySelect;
            if (lists == null || c < 0 || c >= lists.Count || lists[c] == null || lists[c].Count == 0) return false;
            if (p.CategoryNameList == null || c >= p.CategoryNameList.Count) return false;
            // the game's own lookup: an index past the category's end wraps into the next category, as the list shows it
            // (browsing the level categories leaves CurrentMusicSelect past the end of the category in view)
            try { var m = p.GetMusic(0); return m != null && m.MusicData != null; }
            catch (Exception) { return false; }
        }
        static string NormalReason(MusicSelectProcess p, int player, string phase)
        {
            if (!p.IsEntry(player) || !p.MonitorArray[player].IsActive()) return "inactive player";
            if (!MusicSelectPhasePolicy.IsLayoutSequence(MainSequence(p))) return "outside selection";
            bool song = HasSong(p);
            // only the Music phase is about one song; genre, difficulty, menu and option keep the layout without one
            if (!song && phase == "Music") return "empty/unavailable selection";
            if (GameManager.IsCourseMode || GameManager.IsKaleidxScopeMode || GameManager.IsMagicalPassMode || Singleton<FestaManager>.Instance.IsFestaAppearance() || Singleton<SpecialRomManager>.Instance.IsTournamentMode()
                || Singleton<SpecialRomManager>.Instance.IsSpecialStage() || p.IsUtageMusicFolder() || p.IsChallengeFolder() || p.IsTournamentFolder(0)) return "special mode/folder";
            if (!song) return null;
            int id = p.GetMusic(0).MusicData.GetID();
            // ID 2 is the matching card on the same normal Music chain, not a phase.
            if (id == 3) return "placeholder/random selection";
            return null;
        }
        // test API (read only): the song under the cursor, and where a given music id sits in the category lists
        // (the category nearest the current one), so a demo can walk there with the left/right buttons
        public static object MusicProbe(int findId)
        {
            var r = new Dictionary<string, object>();
            foreach (var p in Sessions.Keys)
            {
                var lists = p.CombineMusicDataList;
                int c = p.CurrentCategorySelect;
                r["category"] = c; r["index"] = p.CurrentMusicSelect;
                r["categories"] = lists == null ? 0 : lists.Count;
                r["categoryName"] = p.CategoryNameList != null && c >= 0 && c < p.CategoryNameList.Count ? p.CategoryNameList[c] : null;
                r["count"] = lists != null && c >= 0 && c < lists.Count && lists[c] != null ? lists[c].Count : 0;
                try { var m = p.GetMusic(0); r["id"] = m?.MusicData?.GetID(); } catch (Exception) { r["id"] = null; }
                if (findId > 0 && lists != null)
                {
                    int best = int.MaxValue;
                    for (int k = 0; k < lists.Count; k++)
                    {
                        if (lists[k] == null) continue;
                        int dist = Math.Min(Math.Abs(k - c), lists.Count - Math.Abs(k - c));
                        if (dist >= best) continue;
                        for (int i = 0; i < lists[k].Count; i++)
                        {
                            var sel = lists[k][i]?.musicSelectData;
                            if (sel == null) continue;
                            bool hit = false;
                            foreach (var d in sel) if (d?.MusicData != null && d.MusicData.GetID() == findId) { hit = true; break; }
                            if (!hit) continue;
                            best = dist; r["findCategory"] = k; r["findIndex"] = i;
                            r["findCategoryName"] = p.CategoryNameList != null && k < p.CategoryNameList.Count ? p.CategoryNameList[k] : null;
                            break;
                        }
                    }
                }
                break;
            }
            return r;
        }
        public static bool CategoryInputsEnabled => Settings.Enabled && Settings.CategoryKeys && _failure == null;
        static PartnerConfig Partner(int player)
        {
            int id = Singleton<UserDataManager>.Instance.GetUserData(player).Detail.EquipPartnerID;
            var c = Mod.ForPartner(id); if (c != null || Settings.UseEquippedPartner) return c;
            for (int i = 0; i < Mod.Configs.Count; i++) if (Mod.Configs[i].Name == Settings.SelectPartner) return Mod.Configs[i];
            return null;
        }
        static void DropPartner(Side side, string partnerKey, Exception e)
        {
            side.NoPartner = partnerKey;
            if (side.View != null) { side.View.Close(); side.View = null; }
            if (side.Stock != null) { side.Stock.Close(); side.Stock = null; }
            side.Router?.Suspend();
            Mod.Warn(string.Format("music-select player {0}: partner '{1}' cannot be drawn here, select view stays on without it: {2}", side.Player + 1, partnerKey, e.Message));
        }
        public static void Update(MusicSelectProcess p)
        {
            Session s; if (!Sessions.TryGetValue(p, out s)) return;
            if (s.EnterActive && IsReleaseWait(p)) return; // keep sprite views active through Enter
            if (SubSequences == null) return; // initialization fault already disabled the feature
            var seq = SubSequences(p);
            foreach (var side in s.Sides)
            {
                // Read the last Music frame even when NEXT changed phase inside the original update.
                if (Settings.Enabled && side.Phase == "Music" && MainSequence(p) == 3 && side.Layout != null && side.Layout.Applied) side.Router.ObserveInput(side.Reactions);
                string phase = PhaseNames[(int)seq[side.Player]];
                if (phase != side.Phase)
                {
                    if (Settings.Enabled && side.Phase == "Music" && (phase == "Difficulty" || phase == "UtageDifficulty"))
                        side.Router.Emit("selectionConfirmed", "NEXT accepted; not final start");
                    if (phase == "Menu" && side.View != null && side.View.gameObject.activeInHierarchy) side.View.SpritePlay("Confirm");
                    side.Phase = phase; side.LayoutFrame = -1; side.Router.Emit("phase", phase);
                }
                side.Reason = !Settings.Enabled ? "disabled" : NormalReason(p, side.Player, phase);
                if (side.Reason != side.LoggedReason && p.IsEntry(side.Player))
                {
                    // one line per change, so a player's log shows why the select view went off or came back
                    Mod.Msg(string.Format("music-select player {0} ({1}): {2}", side.Player + 1, phase, side.Reason ?? "select view on"));
                    side.LoggedReason = side.Reason;
                }
                if (side.Reason != null || side.Layout == null)
                {
                    // Placeholder metadata still explicitly identifies random/wait items; no reaction or model.
                    side.Restore();
                    if (Settings.Enabled && p.IsMusicSeq(side.Player) && HasSong(p) && side.Reason == "placeholder/random selection")
                    {
                        if (!side.Placeholder || side.PlaceholderCategory != p.CurrentCategorySelect || side.PlaceholderIndex != p.CurrentMusicSelect || side.PlaceholderScoreType != (int)p.ScoreType)
                        {
                            side.Router.Tick(p, null); side.Placeholder = true;
                            side.PlaceholderCategory = p.CurrentCategorySelect; side.PlaceholderIndex = p.CurrentMusicSelect; side.PlaceholderScoreType = (int)p.ScoreType;
                        }
                    }
                    else side.Placeholder = false;
                    continue;
                }
                side.Placeholder = false;
                if (!side.Layout.CanApply) { side.Reason = "restore completing at end of frame"; continue; }
                if (side.LayoutFrame < 0 || side.Layout.Dirty)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    side.Layout.Apply(Settings, phase); side.LayoutFrame = Time.frameCount;
                    TestHarness.SelectPerf.Add("apply", sw.Elapsed.TotalMilliseconds);
                }
                var cfg = Settings.UseStockPartner ? null : Partner(side.Player);
                bool stockPartner = Settings.ShouldUseStockPartner(cfg != null);
                int partnerId = Singleton<UserDataManager>.Instance.GetUserData(side.Player).Detail.EquipPartnerID;
                string partnerKey = stockPartner ? "stock " + partnerId : cfg != null ? cfg.Name : "none";
                // The partner decorates the select view; it is never a precondition for it. A partner this cabinet or
                // this account cannot draw (no data, no prefab, no Live2D core, failed sprites) leaves the layout on
                // without a partner, logged once; it used to switch the whole select view off until restart.
                if (side.NoPartner != partnerKey)
                {
                    try
                    {
                        side.NoPartner = null;
                        if (stockPartner)
                        {
                            if (side.View != null) { side.View.Close(); side.View = null; }
                            if (side.Stock != null && side.Stock.PartnerId != partnerId) { side.Stock.Close(); side.Stock = null; }
                            if (side.Stock == null)
                            {
                                var data = Singleton<DataManager>.Instance.GetPartner(partnerId);
                                if (data == null || data.naviChara == null) throw new InvalidOperationException("equipped stock partner data unavailable");
                                var owner = side;
                                side.Stock = MusicSelectStockPartner.Attach(side.Layout.Main, partnerId, data.naviChara.id, Settings, e => DropPartner(owner, partnerKey, e));
                            }
                        }
                        else
                        {
                            if (side.Stock != null) { side.Stock.Close(); side.Stock = null; }
                            if (cfg == null) throw new InvalidOperationException("no custom current partner or valid selectPartner fallback");
                            if (side.View != null && side.View.Config.Name != cfg.Name) { side.View.Close(); side.View = null; }
                            var owner = side;
                            if (side.View == null) side.View = PartnerView.AttachMusicSelect(side.Layout.Main, cfg, side.Player, Settings, e => DropPartner(owner, partnerKey, e));
                        }
                    }
                    catch (Exception e) { DropPartner(side, partnerKey, e); }
                }
                if (side.View != null) { side.View.SetSelectLayout(Settings); side.View.gameObject.SetActive(true); }
                if (side.Stock != null) { side.Stock.SetSelectLayout(Settings); side.Stock.gameObject.SetActive(true); }
                if (Settings.DebugTouchZones)
                {
                    if (side.Debug == null) side.Debug = new MusicSelectTouchDebug(side.Layout.Main);
                    side.Debug.Tick(side.Player);
                }
                else { side.Debug?.Dispose(); side.Debug = null; }
                if (phase == "Music" && MainSequence(p) == 3 && !p.IsPreparationCompletes[side.Player]
                    && !p.PrepareFinish && !p.JoinActive && !p.RecruitActive && !p.IsConnectingMusic)
                    side.Router.Tick(p, side.Reactions);
                else side.Router.Suspend();
            }
            if (s.PreviewRequestEvent)
            {
                s.PreviewRequestEvent = false;
                EmitActive(p, s, "previewRequested", new Dictionary<string, object> { { "cueId", s.PreviewCue }, { "request", s.PreviewSerial } });
            }
            if (s.PreviewPending && !SoundManager.IsPlaying(SoundManager.PlayerID.Music)) s.PreviewSawStopped = true;
            if (s.PreviewPending && s.PreviewSawStopped && Time.frameCount > s.PreviewFrame && SoundManager.IsPlaying(SoundManager.PlayerID.Music)
                && HasSong(p) && p.GetMusic(0).MusicData.cueName.id == s.PreviewCue)
            {
                s.PreviewPending = false; s.PreviewStarted = true;
                EmitActive(p, s, "previewStarted", new Dictionary<string, object> { { "cueId", s.PreviewCue }, { "request", s.PreviewSerial }, { "evidence", "engine IsPlaying; not speaker verification" } });
            }
        }
        static void EmitActive(MusicSelectProcess p, Session s, string type, object data)
        {
            TestHarness.Mark("select event " + type);
            foreach (var side in s.Sides) if (Settings.Enabled && side.Phase == "Music" && side.Layout != null && side.Layout.Applied) side.Router.Emit(type, data);
        }
        public static void Preview(int cue)
        {
            TestHarness.Mark("preview play " + cue);
            foreach (var pair in Sessions)
            {
                var s = pair.Value; s.PreviewCue = cue; s.PreviewSerial++; s.PreviewFrame = Time.frameCount;
                s.PreviewPending = true; s.PreviewStarted = false; s.PreviewRequestEvent = true;
                s.PreviewSawStopped = !SoundManager.IsPlaying(SoundManager.PlayerID.Music);
            }
        }
        public static void PreviewEnd()
        {
            foreach (var pair in Sessions)
            {
                var s = pair.Value;
                if (s.PreviewPending || s.PreviewStarted) EmitActive(pair.Key, s, "previewEnded", s.PreviewSerial);
                s.PreviewPending = s.PreviewStarted = s.PreviewRequestEvent = false;
            }
        }
        public static void Scroll(MusicSelectMonitor monitor, int direction)
        {
            foreach (var s in Sessions.Values) foreach (var side in s.Sides)
                if (side.Monitor == monitor) side.Router.Scroll(direction);
        }
        public static bool IsReleaseWait(MusicSelectProcess p) => MainSequence(p) == 4;
        public static void Decide(MusicSelectProcess p, bool wasReleaseWait)
        {
            Session s; if (wasReleaseWait || !IsReleaseWait(p) || !Sessions.TryGetValue(p, out s) || s.Decided) return;
            s.Decided = true;
            // Freedom is the owner's normal play mode, so it keeps the Enter clip.
            bool eligible = Settings.Enabled && Settings.EnterTransition && !GameManager.IsCourseMode
                && !GameManager.IsKaleidxScopeMode && !GameManager.IsMagicalPassMode
                && !Singleton<FestaManager>.Instance.IsFestaAppearance() && !Singleton<SpecialRomManager>.Instance.IsTournamentMode()
                && !Singleton<SpecialRomManager>.Instance.IsSpecialStage() && !p.IsUtageMusicFolder() && !p.IsChallengeFolder() && !p.IsTournamentFolder(0);
            int sprites = 0;
            foreach (var side in s.Sides)
                if (p.IsEntry(side.Player) && side.View != null && side.View.IsSprite && side.View.gameObject.activeInHierarchy)
                {
                    sprites++; side.EnterParticipant = true;
                    if (side.Reason != null || !side.View.SpriteCanPlay("Enter")) eligible = false;
                }
            s.EnterActive = eligible && sprites > 0;
            s.EnterStarted = Time.realtimeSinceStartup;
            if (s.EnterActive)
                foreach (var side in s.Sides)
                    if (side.EnterParticipant)
                        if (!side.View.SpritePlay("Enter")) s.EnterActive = false;
            foreach (var side in s.Sides)
            {
                if (Settings.Enabled && p.IsEntry(side.Player)) side.Router.Emit("songDecided", new Dictionary<string, object>
                { { "musicId", GameManager.SelectMusicID[side.Player] }, { "difficulty", GameManager.SelectDifficultyID[side.Player] }, { "meaning", "final start committed" } });
                if (!s.EnterActive) side.Dispose();
                else if (!side.EnterParticipant) side.Restore();
            }
        }
        public static void ReleaseWait(MusicSelectProcess p, ref float counter)
        {
            Session s; if (!Sessions.TryGetValue(p, out s) || !s.EnterActive || !IsReleaseWait(p)) return;
            float elapsed = Time.realtimeSinceStartup - s.EnterStarted;
            bool playing = false, valid = true;
            foreach (var side in s.Sides)
            {
                if (!side.EnterParticipant) continue;
                if (side.View == null || !side.View.IsSprite || !side.View.gameObject.activeInHierarchy || !side.View.SpriteCanPlay("Enter")) valid = false;
                else if (side.View.SpritePlaying("Enter")) playing = true;
            }
            int decision = MusicSelectPhasePolicy.EnterDecision(valid, playing, elapsed);
            if (decision == 0) { s.EnterActive = false; foreach (var side in s.Sides) side.Dispose(); counter = Math.Max(0, elapsed * 1000f); return; }
            if (decision == 2) { s.EnterActive = false; foreach (var side in s.Sides) side.Dispose(); counter = 1000f; return; }
            if (counter >= 999f) counter = 999f;
        }
        public static void AbortEnter(MusicSelectProcess p, ref float counter)
        {
            Session s;
            if (Sessions.TryGetValue(p, out s) && s.EnterActive)
            {
                counter = Math.Max(0, (Time.realtimeSinceStartup - s.EnterStarted) * 1000f);
                s.EnterActive = false;
                foreach (var side in s.Sides) side.Dispose();
            }
        }
        public static object Tune(object body)
        {
            MusicSelectSettings next;
            try { next = Settings.With(body); } catch (Exception e) { throw new ApiError(400, e.Message); }
            return Commit(next, PartnerLive2D.Framework.Json.GetBool(body, "save", false));
        }
        static object Commit(MusicSelectSettings next, bool save)
        {
            ValidateNodes(next);
            if (_failure != null && next.Enabled) throw new ApiError(409, "feature faulted; restart game after fixing: " + _failure);
            Settings = next;
            InvalidateLayouts();
            // Apply the latest committed preset on the next eligible update. Disable restores immediately.
            if (!Settings.Enabled) foreach (var s in Sessions.Values) foreach (var side in s.Sides) side.Dispose();
            if (save) Save();
            return Info();
        }
        static void ValidateNodes(MusicSelectSettings next)
        {
            foreach (var image in next.Images)
            {
                MusicSelectSprites.ValidateFile(image.Value.Sprite);
                bool hasSide = false, matched = false;
                foreach (var session in Sessions.Values) foreach (var side in session.Sides)
                {
                    if (side.Layout == null) continue;
                    hasSide = true;
                    if (!side.Layout.AppliesPath(image.Value.Parent)) continue;
                    side.Layout.Resolve(image.Value.Parent, true); matched = true;
                }
                if (hasSide && !matched) throw new ApiError(404, "image parent does not match an active side: " + image.Value.Parent);
            }
            foreach (var node in next.Nodes)
            {
                MusicSelectSprites.ValidateFile(node.Value.Sprite);
                bool hasSide = false, matched = false;
                foreach (var session in Sessions.Values) foreach (var side in session.Sides)
                {
                    if (side.Layout == null) continue;
                    hasSide = true;
                    if (!side.Layout.AppliesPath(node.Key)) continue;
                    var target = side.Layout.Resolve(node.Key, true, node.Value.IsSpriteOnly);
                    if (node.Value.Sprite != null)
                    {
                        var image = target.GetComponent<UnityEngine.UI.Image>();
                        if (image == null || image.sprite == null) throw new ApiError(400, "sprite override requires Image with an original sprite");
                    }
                    matched = true;
                }
                if (hasSide && !matched) throw new ApiError(404, "node path does not match an active side: " + node.Key);
            }
        }
        public static object TuneNode(object body)
        {
            var input = body as IDictionary<string, object>;
            if (input == null || !input.ContainsKey("path") || !(input["path"] is string)) throw new ApiError(400, "node requires a string path");
            var values = new Dictionary<string, object>();
            foreach (var pair in input) if (pair.Key != "path") values.Add(pair.Key, pair.Value);
            string path = (string)input["path"];
            var result = Tune(new Dictionary<string, object> { { "nodes", new Dictionary<string, object> { { path, values } } } });
            // Re-posting a filename reloads an edited file, even when its name is unchanged.
            if (values.ContainsKey("sprite")) foreach (var session in Sessions.Values) foreach (var side in session.Sides)
                side.Layout?.RefreshSprite(path);
            return result;
        }
        public static object TuneImage(object body)
        {
            MusicSelectSettings next;
            try { next = Settings.WithImage(body); } catch (Exception e) { throw new ApiError(400, e.Message); }
            var result = Commit(next, false);
            var input = (IDictionary<string, object>)body;
            if (input.ContainsKey("sprite")) foreach (var session in Sessions.Values) foreach (var side in session.Sides)
                side.Layout?.RefreshImage((string)input["id"]);
            return result;
        }
        public static object Tree(string root, int depth, int player)
        {
            foreach (var session in Sessions.Values) foreach (var side in session.Sides)
            {
                if (side.Layout == null || (player >= 0 && player != side.Player)) continue;
                if (!string.IsNullOrEmpty(root) && !side.Layout.AppliesPath(root)) continue;
                return new Dictionary<string, object> { { "player", side.Player }, { "depth", depth }, { "tree", side.Layout.Tree(root, depth) } };
            }
            throw new ApiError(409, "no matching active music-select side");
        }
        public static byte[] SpritePng(string path, int player)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ApiError(400, "sprite export requires path");
            foreach (var session in Sessions.Values) foreach (var side in session.Sides)
            {
                if (side.Layout == null || (player >= 0 && player != side.Player) || !side.Layout.AppliesPath(path)) continue;
                return MusicSelectSprites.Export(side.Layout.Resolve(path, false).GetComponent<UnityEngine.UI.Image>());
            }
            throw new ApiError(409, "no matching active music-select side");
        }
        public static object Reset()
        {
            Settings = new MusicSelectSettings();
            foreach (var s in Sessions.Values) foreach (var side in s.Sides) side.Dispose();
            return Info();
        }
        public static object Save()
        {
            if (_root == null) throw new ApiError(409, "mod not initialized");
            Settings.Save(_root, JsonWriter.Write(Settings.ToDict())); return Info();
        }
        public static object Info()
        {
            var sessions = new List<object>();
            foreach (var pair in Sessions)
            {
                var sides = new List<object>();
                foreach (var side in pair.Value.Sides) sides.Add(new Dictionary<string, object>
                { { "player", side.Player }, { "phase", side.Phase }, { "reason", side.Reason }, { "layout", side.Layout?.Info() },
                  { "viewId", side.View == null ? null : (object)side.View.Id }, { "stockPartner", side.Stock == null ? null : side.Stock.Info() }, { "reaction", side.Router.Info() } });
                sessions.Add(new Dictionary<string, object> { { "id", pair.Value.Id }, { "mainSequence", AccessTools.Field(typeof(MusicSelectProcess), "_mainSequence").GetValue(pair.Key).ToString() }, { "sides", sides } });
            }
            return new Dictionary<string, object> { { "preset", Settings.ToDict() }, { "failure", _failure }, { "sessions", sessions } };
        }
    }

    [HarmonyPatch(typeof(MusicSelectProcess), "OnStart")]
    static class SelectStartPatch { static void Postfix(MusicSelectProcess __instance) => MusicSelectSession.Guard(() => MusicSelectSession.Start(__instance)); }
    [HarmonyPatch(typeof(MusicSelectProcess), "OnUpdate")]
    static class SelectUpdatePatch
    {
        static void Prefix(MusicSelectProcess __instance, ref float ____timeCounter)
        {
            try { MusicSelectSession.ReleaseWait(__instance, ref ____timeCounter); }
            catch (Exception e) { MusicSelectSession.AbortEnter(__instance, ref ____timeCounter); Mod.Warn("sprite enter fallback: " + e.Message); }
        }
        static void Postfix(MusicSelectProcess __instance)
        {
            try { MusicSelectSession.Update(__instance); } catch (Exception e) { MusicSelectSession.Fail(e); }
        }
    }
    [HarmonyPatch(typeof(MusicSelectProcess), "Next")]
    static class SelectNextPatch
    {
        static void Postfix(MusicSelectProcess __instance)
        {
            try { MusicSelectSession.Update(__instance); } catch (Exception e) { MusicSelectSession.Fail(e); }
        }
    }
    [HarmonyPatch(typeof(MusicSelectProcess), "OnRelease")]
    static class SelectReleasePatch
    {
        static void Prefix(MusicSelectProcess __instance)
        {
            MusicSelectSession.Guard(() => MusicSelectSession.Release(__instance));
            MusicSelectCategoryInput.Clear();
        }
    }
    [HarmonyPatch(typeof(MusicSelectProcess), "OnGameStart")]
    static class SelectDecidePatch
    {
        static void Prefix(MusicSelectProcess __instance, out bool __state)
        {
            bool prior = true; MusicSelectSession.Guard(() => prior = MusicSelectSession.IsReleaseWait(__instance)); __state = prior;
        }
        static void Postfix(MusicSelectProcess __instance, bool __state) => MusicSelectSession.Guard(() => MusicSelectSession.Decide(__instance, __state));
    }
    [HarmonyPatch(typeof(SoundManager), "PreviewPlay")]
    static class SelectPreviewPatch { static void Postfix(int __0) => MusicSelectSession.Guard(() => MusicSelectSession.Preview(__0)); }
    [HarmonyPatch(typeof(SoundManager), "PreviewEnd")]
    static class SelectPreviewEndPatch { static void Postfix() => MusicSelectSession.Guard(MusicSelectSession.PreviewEnd); }
    [HarmonyPatch(typeof(MusicSelectMonitor), "ScrollMusicLeft")]
    static class SelectScrollLeftPatch
    {
        static void Postfix(MusicSelectMonitor __instance)
        {
            try { MusicSelectSession.Scroll(__instance, -1); } catch (Exception e) { MusicSelectSession.Fail(e); }
        }
    }
    [HarmonyPatch(typeof(MusicSelectMonitor), "ScrollMusicRight")]
    static class SelectScrollRightPatch
    {
        static void Postfix(MusicSelectMonitor __instance)
        {
            try { MusicSelectSession.Scroll(__instance, 1); } catch (Exception e) { MusicSelectSession.Fail(e); }
        }
    }
}
