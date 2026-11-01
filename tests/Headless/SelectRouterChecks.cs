using System;
using System.Collections.Generic;
using PartnerLive2D.Unity;

// Minimal game/input doubles for the real router source. These never enter the mod build.
namespace UnityEngine
{
    static class Time { public static int frameCount; public static float realtimeSinceStartup; }
}
namespace Manager
{
    enum MusicLevelID { Level7Plus }
    static class MusicLevelIDEnum { public static string GetName(this MusicLevelID id) => "7+"; }
    enum MusicDifficultyID { Basic, Advanced, Expert, Master, ReMaster }
    static class InputManager
    {
        public enum ButtonSetting { Button01, Button02, Button03, Button04, Button05, Button06, Button07, Button08, Select, End }
        public enum TouchPanelArea { A1, A2, A3, A4, A5, A6, A7, A8, B1, B2, B3, B4, B5, B6, B7, B8, C1, C2, D1, D2, D3, D4, D5, D6, D7, D8, E1, E2, E3, E4, E5, E6, E7, E8, Blank, End }
        public static bool Down;
        public static bool GetButtonDown(int player, ButtonSetting b) => Down && b == ButtonSetting.Button01;
        public static bool GetTouchPanelAreaDown(int player, TouchPanelArea a) => Down && a == TouchPanelArea.A1;
    }
}
namespace Process
{
    sealed class MusicSelectProcess
    {
        public sealed class Name { public string str; }
        public sealed class Music
        {
            public int Id = 10;
            public Name name = new Name { str = "Song A" }, genreName = new Name { str = "Genre" };
            public float bpm = 120;
            public int GetID() => Id;
        }
        public sealed class Score { public bool isEnable = true; public int musicLevelID = 0; }
        public sealed class Data
        {
            public Music MusicData = new Music();
            public int Difficulty;
            public List<Score> ScoreData = new List<Score> { new Score(), new Score(), new Score(), new Score(), new Score() };
        }
        public Data Current = new Data();
        public int CurrentMusicSelect, CurrentCategorySelect, ScoreType, CategorySortSetting, Difficulty;
        public bool Extra, Level, Remaster = true;
        public List<string> CategoryNameList = new List<string> { "Category", "Other" };
        public Data GetMusic(int offset) { if (offset != 0) throw new Exception("must read offset zero"); return Current; }
        public bool IsExtraFolder(int offset) => Extra;
        public bool IsLevelTab(int offset) => Level;
        public int GetDifficultyByLevel(int offset) => 2;
        public int GetCurrentDifficulty(int player) => Difficulty;
        public bool IsRemasterEnable(int offset) => Remaster;
    }
}
namespace PartnerLive2D.Unity
{
    sealed class PartnerView : IMusicSelectPartnerReactions
    {
        public int Nods, Reactions;
        public float Look;
        public void Nod() { Nods++; }
        public void LookAt(float x, float y, float seconds) { Look = x; }
        public void SelectSongReaction() { Reactions++; }
    }
    sealed class StockReactionSink : IMusicSelectPartnerReactions
    {
        public int Nods, Songs, Looks;
        public void Nod() { Nods++; }
        public void SelectSongReaction() { Songs++; }
        public void LookAt(float x, float y, float seconds) { Looks++; }
    }
}
namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void SelectRouterChecks()
        {
            int checks = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception(why); checks++; };
            var p = new Process.MusicSelectProcess(); var view = new PartnerView(); var router = new MusicSelectReactionRouter(0);
            Action tick = () => { UnityEngine.Time.frameCount++; UnityEngine.Time.realtimeSinceStartup += .016f; router.Tick(p, view); };
            Func<string, int> count = type =>
            {
                int n = 0; var info = (Dictionary<string, object>)router.Info();
                foreach (Dictionary<string, object> e in (object[])info["events"]) if ((string)e["event"] == type) n++;
                return n;
            };
            tick(); var initial = router.Song;
            check(count("initial") == 1 && (string)initial["level"] == "7+", "initial metadata");
            for (int i = 0; i < 100; i++) tick(); // warm JIT/static caches before measuring
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) tick();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            check(bytes == 0 && ReferenceEquals(initial, router.Song), "stationary router must allocate zero bytes; actual=" + bytes);
            p.Current = new Process.MusicSelectProcess.Data(); p.Current.MusicData.Id = 11; p.Current.MusicData.name.str = "Song B"; p.CurrentMusicSelect++;
            tick();
            check(!ReferenceEquals(initial, router.Song) && (string)initial["title"] == "Song A", "earlier event metadata stays immutable");
            check(count("songObserved") == 1 && count("songChanged") == 0 && view.Look > 0, "song observation precedes debounce and gazes right");
            for (int i = 0; i < 12; i++) tick();
            check(view.Reactions == 0, "no reaction before 200 ms");
            tick(); check(view.Reactions == 1 && count("songChanged") == 1, "one settled song reaction");
            for (int i = 0; i < 30; i++) tick();
            check(view.Reactions == 1, "no repeated settled reaction");
            Manager.InputManager.Down = true; tick(); router.ObserveInput(view); router.Tick(p, view);
            check(view.Nods == 1 && count("buttonDown") == 1 && count("touchDown") == 1 && Manager.InputManager.Down, "input observed once and never consumed");
            Manager.InputManager.Down = false;
            p.Difficulty = 4; p.Remaster = false; tick();
            check((int)router.Song["difficulty"] == 3 && count("difficultyChanged") == 1, "ReMaster fallback");
            p.Remaster = true; p.Extra = true; p.Current.Difficulty = 1; tick();
            check((int)router.Song["difficulty"] == 1, "extra folder follows chart difficulty");
            p.Extra = false; p.Level = true; tick();
            check((int)router.Song["difficulty"] == 2, "level tab follows chart difficulty");
            p.Level = false; p.ScoreType = 1; tick();
            check((string)router.Song["scoreType"] == "1", "score type refreshes metadata");
            p.CurrentCategorySelect = 1; tick();
            check((string)router.Song["category"] == "Other", "category index refreshes metadata");
            p.CategorySortSetting++; tick();
            check(count("categoryChanged") == 2, "sort identity observed even at same category index");
            p.Current.MusicData.Id = 3; tick();
            check((string)router.Song["kind"] == "random" && router.Song["bpm"] == null && router.Song["level"] == null, "random metadata does not invent notes");
            router.Suspend(); for (int i = 0; i < 20; i++) tick();
            check(view.Reactions == 1 && count("initial") == 2, "suspend cancels pending reaction and resumes with initial snapshot");
            for (int i = 0; i < 100; i++) router.Emit("bounded");
            check(((object[])((Dictionary<string, object>)router.Info())["events"]).Length == 64, "event history bounded");
            var stock = new StockReactionSink();
            var stockRouter = new MusicSelectReactionRouter(1);
            var stockProcess = new Process.MusicSelectProcess();
            Action stockTick = () => { UnityEngine.Time.frameCount++; UnityEngine.Time.realtimeSinceStartup += .016f; stockRouter.Tick(stockProcess, stock); };
            stockTick(); stockProcess.CurrentMusicSelect++; stockProcess.Current.MusicData.Id++;
            stockTick();
            check(stock.Looks == 1 && stock.Songs == 0, "stock sink receives same scroll hook before debounce");
            for (int i = 0; i < 20; i++) stockTick();
            check(stock.Songs == 1, "stock sink receives one settled song reaction");
            Manager.InputManager.Down = true; stockTick(); stockRouter.ObserveInput(stock);
            check(stock.Nods == 1 && Manager.InputManager.Down, "stock receives deduplicated observing input hook");
            Manager.InputManager.Down = false;
            stockRouter.Tick(stockProcess, null); // absent/texture presentation can safely ignore reactions
            Console.WriteLine("Select router QA checks passed: " + checks + "; stationary 1000-frame managed allocations=" + bytes + " bytes (game/input doubles; not Unity runtime)");
        }
    }
}
