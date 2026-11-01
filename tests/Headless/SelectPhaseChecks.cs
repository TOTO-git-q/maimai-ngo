using System;
using System.IO;
using PartnerLive2D.Unity;
namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void SelectPhaseChecks()
        {
            for (int i = -1; i <= 5; i++)
                if (MusicSelectPhasePolicy.IsLayoutSequence(i) != (i >= 0 && i <= 3)) throw new Exception("phase lifecycle eligibility: " + i);
            const string genre = "Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/Scrolling_Jacket";
            if (MusicSelectPhasePolicy.ShowGenreNode("Genre", genre)) throw new Exception("genre jacket must remain hidden");
            if (!MusicSelectPhasePolicy.ShowGenreNode("Genre", "Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/UI_SelectorBG")) throw new Exception("genre selector artwork must be visible");
            if (MusicSelectPhasePolicy.ShowGenreNode("Music", genre) || MusicSelectPhasePolicy.ShowGenreNode("Genre", "Null_GenreControll/SelectorTab(Clone)/Null_Tab_L01")) throw new Exception("genre artwork exemption too broad");
            if (MusicSelectPhasePolicy.ShowGenreNode("Genre", "Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/UI_SelectorBG/Null_CanvasGroup/Text")) throw new Exception("genre marquee must remain hidden");
            const string bg = "Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/UI_SelectorBG/Null_CanvasGroup/";
            if (!MusicSelectPhasePolicy.ShowGenreNode("Genre", bg + "Null_Indicator")) throw new Exception("genre position indicator must be visible");
            foreach (var shape in new[] { "Null_Selector_ColorBase", "Null_Selector_Base", "Shadow" })
                if (MusicSelectPhasePolicy.ShowGenreNode("Genre", bg + shape)) throw new Exception("stock backdrop shape must stay hidden in Genre: " + shape);
            if (MusicSelectPhasePolicy.EnterDecision(true, true, 3.99f) != 1 ||
                MusicSelectPhasePolicy.EnterDecision(true, false, .8f) != 2 ||
                MusicSelectPhasePolicy.EnterDecision(true, true, 4f) != 2 ||
                MusicSelectPhasePolicy.EnterDecision(false, true, 4f) != 0)
                throw new Exception("enter transition hold/cap/fallback");
            string configRoot = Path.Combine(Path.GetTempPath(), "sprite-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configRoot);
            try
            {
                var defaults = MusicSelectSettings.Load(configRoot);
                if (!defaults.EnterTransition || defaults.SelectTapAreas != null) throw new Exception("sprite settings defaults");
                File.WriteAllText(Path.Combine(configRoot, "settings.ini"), "enterTransition = off\nselectTapAreas = A1, C2\n");
                var configured = MusicSelectSettings.Load(configRoot);
                if (configured.EnterTransition || configured.SelectTapAreas.Count != 2 || !configured.SelectTapAreas.Contains("A1") || !configured.SelectTapAreas.Contains("C2")) throw new Exception("sprite settings override");
                var copy = configured.With(new System.Collections.Generic.Dictionary<string, object>());
                if (copy.EnterTransition || !copy.SelectTapAreas.Contains("C2")) throw new Exception("sprite settings copy");
            }
            finally { Directory.Delete(configRoot, true); }
            Console.WriteLine("Select phase policy: 16 assertions passed");
        }
    }
}
