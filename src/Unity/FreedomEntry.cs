using HarmonyLib;
using Manager;
using Monitor.ModeSelect;

namespace PartnerLive2D.Unity
{
    // settings.ini "freedomEntry = true": AquaMai's EntryToMusicSelection jumps from login straight to music select but
    // always as a normal (credit) play. Turn that first music select into a freedom-mode play instead, the same flags
    // ModeSelectProcess sets for FREEDOM; MusicSelectProcess then starts the freedom timer itself (and AquaMai
    // FreedomTimer InfinityTime keeps it paused).
    [HarmonyPatch(typeof(Process.MusicSelectProcess), "OnStart")]
    static class FreedomEntry
    {
        public static bool Enabled;

        static void Prefix()
        {
            if (!Enabled || !GameManager.IsNormalMode || GameManager.IsFreedomMode || GameManager.IsCourseMode) return;
            if (GameManager.MusicTrackNumber != 1) return;
            GameManager.IsNormalMode = false;
            GameManager.IsFreedomMode = true;
            GameManager.SelectedModeID = ModeSelectMonitor.ModeSelectType.ModeSelectTypeFreedom;
            Mod.Msg("freedomEntry: first music select switched to freedom mode");
        }
    }

    // settings.ini "freedomInfinity = true" (for AquaMai FreedomTimer InfinityTime): the freedom-mode clock would sit at
    // 10:00 forever; show the game's own countdown infinity mark (CommonTimer/Infinity) in its place instead.
    // "freedomInfinity = hidden" hides the clock and shows nothing.
    [HarmonyPatch(typeof(Monitor.PleaseWaitMonitor), "SetTime")]
    static class FreedomInfinity
    {
        public static bool Enabled, MarkHidden;
        static readonly System.Reflection.FieldInfo Time10 = AccessTools.Field(typeof(Monitor.PleaseWaitMonitor), "_time10");
        static readonly System.Reflection.FieldInfo Time1 = AccessTools.Field(typeof(Monitor.PleaseWaitMonitor), "_time1");
        static readonly System.Reflection.FieldInfo Colon = AccessTools.Field(typeof(Monitor.PleaseWaitMonitor.SpriteTimer), "_colon");

        static void Postfix(Monitor.PleaseWaitMonitor __instance)
        {
            if (!Enabled || !GameManager.IsFreedomMode) return;
            var t10 = (Monitor.PleaseWaitMonitor.SpriteTimer)Time10.GetValue(__instance);
            var t1 = (Monitor.PleaseWaitMonitor.SpriteTimer)Time1.GetValue(__instance);
            t10.SetVisible(false);
            t1.SetVisible(false);
            if (MarkHidden) return;
            var colon = (UnityEngine.UI.Image)Colon.GetValue(t10);
            var parent = colon.transform.parent;
            var mark = parent.Find("PartnerInfinity");
            if (mark == null)
            {
                UnityEngine.Transform source = null;
                foreach (var ct in UnityEngine.Resources.FindObjectsOfTypeAll<CommonTimer>())
                {
                    source = ct.transform.Find("Infinity");
                    if (source != null) break;
                }
                if (source == null) { Mod.Warn("freedomInfinity: no CommonTimer/Infinity to copy"); return; }
                mark = UnityEngine.Object.Instantiate(source.gameObject, parent, false).transform;
                mark.name = "PartnerInfinity";
                mark.localPosition = colon.transform.localPosition;
                mark.localScale = UnityEngine.Vector3.one * InfinityScale;
            }
            mark.gameObject.SetActive(true);
        }

        // the countdown mark is drawn for a two-digit timer; the freedom clock digits are about three times taller
        const float InfinityScale = 3f;
    }
}
