using System;
namespace PartnerLive2D.Unity
{
    // MainSequence: Init, Fade, FirstStaging, Update, ReleaseWait, Release.
    internal static class MusicSelectPhasePolicy
    {
        // 0: revert to the original timer, 1: wait, 2: release now.
        public static int EnterDecision(bool valid, bool anyPlaying, float elapsed)
        {
            if (!valid) return 0;
            return !anyPlaying || elapsed >= 4f ? 2 : 1;
        }
        public static bool IsLayoutSequence(int mainSequence) => mainSequence >= 0 && mainSequence <= 3;
        // In Genre only the selector container and its position indicator come back; the stock backdrop shapes
        // (Null_Selector_ColorBase, Null_Selector_Base, Shadow) stay hidden so the NGO board alone is the backdrop.
        public static bool ShowGenreNode(string phase, string path)
        {
            return phase == "Genre" && path.IndexOf("/UI_MSS_GenreSelect(Clone)/", StringComparison.Ordinal) >= 0
                && (path.EndsWith("/UI_SelectorBG", StringComparison.Ordinal) || path.EndsWith("/Null_Indicator", StringComparison.Ordinal));
        }
    }
}
