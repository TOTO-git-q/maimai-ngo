using System;
using System.Collections.Generic;
using Manager;
using Process;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // Observations only. No input hook, setter, prefix cancellation or game-state mutation.
    sealed class MusicSelectReactionRouter
    {
        readonly int _player;
        readonly Queue<object> _events = new Queue<object>();
        string _category;
        int _songId, _scoreType, _categoryIndex, _sort;
        bool _pending;
        static readonly InputManager.ButtonSetting[] Buttons = (InputManager.ButtonSetting[])Enum.GetValues(typeof(InputManager.ButtonSetting));
        internal static readonly InputManager.TouchPanelArea[] Areas = (InputManager.TouchPanelArea[])Enum.GetValues(typeof(InputManager.TouchPanelArea));
        internal static readonly string[] AreaNames = Array.ConvertAll(Areas, a => a.ToString());
        int _difficulty = -1, _index, _frame = -1;
        float _settledAt;
        bool _first = true;
        long _serial;
        int _scrollDirection, _scrollFrame = -1;
        public Dictionary<string, object> Song { get; private set; }
        public MusicSelectReactionRouter(int player) { _player = player; }
        public void Scroll(int direction) { _scrollDirection = direction; _scrollFrame = Time.frameCount; }
        public void Emit(string type, object data = null)
        {
            _events.Enqueue(new Dictionary<string, object> { { "id", ++_serial }, { "event", type },
                { "time", Time.realtimeSinceStartup }, { "frame", Time.frameCount }, { "player", _player }, { "data", data }, { "song", Song } });
            while (_events.Count > 64) _events.Dequeue();
        }
        public void Suspend() { _first = true; _pending = false; }
        public void Tick(MusicSelectProcess p, IMusicSelectPartnerReactions view)
        {
            var data = p.GetMusic(0); // offset 0, never player index
            if (data == null || data.MusicData == null) throw new InvalidOperationException("selected song missing");
            int id = data.MusicData.GetID();
            bool placeholder = id == 2 || id == 3;
            int difficulty = p.IsExtraFolder(0) ? data.Difficulty : p.IsLevelTab(0) ? p.GetDifficultyByLevel(0) : p.GetCurrentDifficulty(_player);
            if (!placeholder && difficulty == 4 && !p.IsRemasterEnable(0)) difficulty = 3;
            if (!placeholder)
            {
                if (data.ScoreData == null || difficulty < 0 || difficulty >= data.ScoreData.Count || data.ScoreData[difficulty] == null || !data.ScoreData[difficulty].isEnable)
                    throw new InvalidOperationException("selected notes unavailable");
            }
            string category = p.CategoryNameList[p.CurrentCategorySelect];
            bool songChanged = _songId != id || _scoreType != (int)p.ScoreType || _categoryIndex != p.CurrentCategorySelect || _index != p.CurrentMusicSelect;
            bool categoryChanged = _categoryIndex != p.CurrentCategorySelect || _sort != (int)p.CategorySortSetting || _category != category;
            // Keep event snapshots immutable, but publish a new one only when observed state changes.
            if (_first || songChanged || categoryChanged || _difficulty != difficulty) Song = new Dictionary<string, object>
            {
                { "id", id }, { "kind", id == 2 ? "placeholder" : id == 3 ? "random" : "music" },
                { "title", data.MusicData.name.str }, { "genre", data.MusicData.genreName.str },
                { "difficulty", placeholder ? null : (object)difficulty }, { "difficultyName", placeholder ? null : ((MusicDifficultyID)difficulty).ToString() },
                { "level", placeholder ? null : ((MusicLevelID)data.ScoreData[difficulty].musicLevelID).GetName() }, { "bpm", placeholder ? null : (object)data.MusicData.bpm },
                { "category", category }, { "categoryIndex", p.CurrentCategorySelect },
                { "index", p.CurrentMusicSelect }, { "scoreType", p.ScoreType.ToString() }
            };
            if (_first)
            {
                Emit("initial"); _first = false;
            }
            else
            {
                if (categoryChanged) Emit("categoryChanged", category);
                if (_difficulty != difficulty) Emit("difficultyChanged", difficulty);
                if (songChanged)
                {
                    _pending = true; _settledAt = Time.realtimeSinceStartup + .2f;
                    int direction = _scrollFrame == Time.frameCount ? _scrollDirection : Math.Sign(p.CurrentMusicSelect - _index);
                    Emit("songObserved", direction);
                    if (view != null) view.LookAt(direction * .3f, 0, .45f);
                }
            }
            _songId = id; _scoreType = (int)p.ScoreType; _categoryIndex = p.CurrentCategorySelect;
            _sort = (int)p.CategorySortSetting; _category = category; _difficulty = difficulty; _index = p.CurrentMusicSelect;
            if (_pending && Time.realtimeSinceStartup >= _settledAt)
            {
                _pending = false; Emit("songChanged"); if (view != null) view.SelectSongReaction();
            }
            ObserveInput(view);
        }
        public void ObserveInput(IMusicSelectPartnerReactions view)
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            bool nod = false;
            foreach (var b in Buttons)
            {
                int n = (int)b; if (n < 0 || n > (int)InputManager.ButtonSetting.Select) continue;
                if (InputManager.GetButtonDown(_player, b)) { Emit("buttonDown", b.ToString()); nod = true; }
            }
            for (int i = 0; i < Areas.Length; i++)
            {
                if (AreaNames[i].Length != 2) continue;
                if (InputManager.GetTouchPanelAreaDown(_player, Areas[i]))
                {
                    Emit("touchDown", AreaNames[i]); nod = true;
                    var target = view as IMusicSelectTouchPartner;
                    if (target != null) target.TouchArea(AreaNames[i]);
                }
            }
            if (nod && view != null) view.Nod();
        }
        public object Info() => new Dictionary<string, object> { { "song", Song }, { "events", _events.ToArray() } };
    }
}
