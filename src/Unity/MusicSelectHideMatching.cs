using System.Collections.Generic;
using HarmonyLib;
using Process;

namespace PartnerLive2D.Unity
{
    // Single-cabinet setups never have an in-store (LAN party) recruit, so the matching category only ever shows
    // "no recruitment". With hideMatching (default on) that category is never built, and nothing else of the game's
    // select flow is skipped:
    // - SetConnectData runs as stock; afterwards its "waiting for a recruit" placeholder is dropped, so the stock
    //   Count > 0 check in the category build never adds the matching category.
    // - The two recruit probes (IsConnectStart / IsConnectStop) report "no change", so a recruit seen later cannot
    //   re-insert the category (reinputConnectCombineData removes the LAST category, which would be a real one here).
    // PartyExec itself must keep running. In NORMAL mode it is also the step that turns START (PrepareFinish) into the
    // track start (ConnectMusicAllDecide); freedom and course mode set that flag in OnUpdate themselves. Skipping the
    // whole of PartyExec (the old version of this file) left every normal-mode START waiting for the 99 s select timer,
    // and forever with AquaMai DisableTimeout: the bug a new card hit, since a new card plays normal mode.
    [HarmonyPatch(typeof(MusicSelectProcess), "SetConnectData")]
    static class MusicSelectHideMatching
    {
        static readonly AccessTools.FieldRef<MusicSelectProcess, List<MusicSelectProcess.CombineMusicSelectData>> List =
            AccessTools.FieldRefAccess<MusicSelectProcess, List<MusicSelectProcess.CombineMusicSelectData>>("_connectCombineMusicDataList");
        internal static bool Active => MusicSelectSession.Settings.HideMatching;

        static void Postfix(MusicSelectProcess __instance)
        {
            if (!Active || __instance.RecruitData != null) return;
            List(__instance).Clear();
        }
    }

    [HarmonyPatch(typeof(MusicSelectProcess), "IsConnectStart")]
    static class MusicSelectHideMatchingStart
    {
        static bool Prefix(ref bool __result)
        {
            if (!MusicSelectHideMatching.Active) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(MusicSelectProcess), "IsConnectStop")]
    static class MusicSelectHideMatchingStop
    {
        static bool Prefix(ref bool __result)
        {
            if (!MusicSelectHideMatching.Active) return true;
            __result = false;
            return false;
        }
    }
}
