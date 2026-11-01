using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using DB;
using PartnerLive2D.Core;
using Monitor;
using Manager;
using MAI2.Util;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // Harmony hooks into the result screen, the partner room and partner image loading.
    // Every hook is a no-op unless a partner.ini claims the navi chara / partner id involved.
    static class Patches
    {
        // ResultMonitor instance id -> player index (needed to read that player's touch panel). Keyed by id, not a
        // ConditionalWeakTable: on this game's Boehm GC that holds its keys strongly and kept every result screen alive.
        static readonly Dictionary<int, int> MonitorIndex = new Dictionary<int, int>();

        static readonly AccessTools.FieldRef<ResultMonitor, Transform> ResultRoot = AccessTools.FieldRefAccess<ResultMonitor, Transform>("_naviCharaRoot");
        static readonly AccessTools.FieldRef<ResultMonitor, NavigationCharacter> ResultNavi = AccessTools.FieldRefAccess<ResultMonitor, NavigationCharacter>("_navigationCharacter");
        static readonly AccessTools.FieldRef<ResultMonitor, ResultTimelineManager> ResultTimeline = AccessTools.FieldRefAccess<ResultMonitor, ResultTimelineManager>("_timelineManager");
        static readonly AccessTools.FieldRef<PartnerRoom, Transform> RoomRoot = AccessTools.FieldRefAccess<PartnerRoom, Transform>("_naviCharaRoot");
        static readonly AccessTools.FieldRef<PartnerRoom, NavigationCharacter> RoomNavi = AccessTools.FieldRefAccess<PartnerRoom, NavigationCharacter>("_navigationCharacter");

        static void Guard(string where, Action a)
        {
            try { a(); }
            catch (Exception e) { Mod.Error(where + ": " + e); }
        }

        [HarmonyPatch(typeof(ResultMonitor), "Initialize")]
        static class ResultInitialize
        {
            static void Postfix(ResultMonitor __instance, int __0)
            {
                if (MonitorIndex.Count > 64) MonitorIndex.Clear();
                MonitorIndex[__instance.GetInstanceID()] = __0;
            }
        }

        [HarmonyPatch(typeof(ResultMonitor), "SetStartData")]
        static class ResultSetStartData
        {
            static void Postfix(ResultMonitor __instance, bool __5, int __6)
            {
                if (!Mod.Ready) return;
                Guard("result partner", () =>
                {
                    int monitor;
                    // Without a confirmed player index, retain the game's own character.
                    if (!MonitorIndex.TryGetValue(__instance.GetInstanceID(), out monitor)) return;
                    int partnerId = Singleton<UserDataManager>.Instance.GetUserData(monitor).Detail.EquipPartnerID;
                    var cfg = Mod.ForPartner(partnerId);
                    if (cfg != null)
                    {
                        string tier = null;
                        if (!string.IsNullOrEmpty(cfg.SpritePath))
                        {
                            try
                            {
                                var gameplay = Singleton<GamePlayManager>.Instance;
                                tier = SpritePairMath.Tier(__5, gameplay.GetAchivement(monitor), gameplay.GetComboType(monitor) >= PlayComboflagID.AllPerfect);
                            }
                            catch (Exception e) { Mod.Warn(cfg.Name + ": result unavailable; using Live2D: " + e.Message); }
                        }
                        var view = PartnerView.Attach(ResultRoot(__instance), ResultNavi(__instance), cfg, monitor, ResultTimeline(__instance), "result", tier);
                        if (view != null) view.ApplyResultUnder(__instance.transform);
                    }
                });
            }
        }

        [HarmonyPatch(typeof(ResultMonitor), "TouchPartnerCharacter")]
        static class ResultTouchPartner
        {
            static void Prefix(ResultMonitor __instance)
            {
                var view = PartnerView.Find(ResultNavi(__instance));
                if (view != null) Guard("tap", view.OnTap);
            }
        }

        [HarmonyPatch(typeof(NavigationCharacter), "Play")]
        static class NaviPlay
        {
            static void Postfix(NavigationCharacter __instance, NavigationAnime __0)
            {
                var view = PartnerView.Find(__instance);
                if (view != null) Guard("navi play", () => view.OnNaviPlay(__0));
            }
        }

        [HarmonyPatch(typeof(PartnerRoom), "CreatePartner")]
        static class RoomCreatePartner
        {
            static void Postfix(PartnerRoom __instance, int __0)
            {
                if (!Mod.Ready) return;
                var cfg = Mod.ForNavi(__0);
                if (cfg == null) return;
                Guard("partner room", () => PartnerView.Attach(RoomRoot(__instance), RoomNavi(__instance), cfg, -1, null, "room"));
            }
        }

        [HarmonyPatch(typeof(AssetManager), "GetPartnerTexture2D", new[] { typeof(string) })]
        static class PartnerIcon
        {
            static bool Prefix(string __0, ref Texture2D __result)
            {
                if (!Mod.Ready || __0 == null || !__0.StartsWith("Partner/UI_Partner_")) return true;
                int id;
                var digits = __0.Substring("Partner/UI_Partner_".Length).Replace(".png", "");
                if (!int.TryParse(digits, out id)) return true;
                var cfg = Mod.ForPartner(id);
                var tex = cfg != null ? Mod.LoadImage(cfg.IconPath) : null;
                if (tex == null) return true;
                __result = tex;
                return false;
            }
        }

        // The collection list asks by id. GetPartnerTexture2D(int) calls the string overload, but the JIT inlines that call,
        // so the string hook above never sees it and the list got the missing dummy (a white square). Hook the id too.
        [HarmonyPatch(typeof(AssetManager), "GetPartnerTexture2D", new[] { typeof(int) })]
        static class PartnerIconById
        {
            static bool Prefix(int __0, ref Texture2D __result)
            {
                if (!Mod.Ready) return true;
                var cfg = Mod.ForPartner(__0);
                var tex = cfg != null ? Mod.LoadImage(cfg.IconPath) : null;
                if (tex == null) return true;
                __result = tex;
                return false;
            }
        }

        [HarmonyPatch(typeof(AssetManager), "GetPartnerResultTexture2D")]
        static class PartnerResultImage
        {
            static bool Prefix(int __0, ref Texture2D __result)
            {
                if (!Mod.Ready) return true;
                var cfg = Mod.ForPartner(__0);
                var tex = cfg != null ? Mod.LoadImage(cfg.ResultImagePath) : null;
                if (tex == null) return true;
                __result = tex;
                return false;
            }
        }
    }
}
