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
        static readonly AccessTools.FieldRef<MonitorBase, CanvasGroup> MonitorMain = AccessTools.FieldRefAccess<MonitorBase, CanvasGroup>("Main");

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
                        if (!string.IsNullOrEmpty(cfg.SpritePath) || !string.IsNullOrEmpty(cfg.LibraryPath))
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
                Guard("partner room", () =>
                {
                    var view = PartnerView.Attach(RoomRoot(__instance), RoomNavi(__instance), cfg, -1, null, "room");
                    // touches on the pair: the sensors sit around the centre of the collection monitor's Main canvas
                    var monitor = __instance.GetComponentInParent<CollectionMonitor>();
                    var main = monitor != null ? MonitorMain(monitor) : null;
                    if (view != null && main != null) view.SetTouchSpace(main.transform, monitor.MonitorIndex);
                });
            }
        }

        // a present was given in the partner room (CollectionProcess.InputPartnerPresentConfirmSequence)
        [HarmonyPatch(typeof(PartnerRoom), "PlayPartnerEmotionEffectOnceTime")]
        static class RoomPresent
        {
            static void Postfix(PartnerRoom __instance)
            {
                var view = PartnerView.Find(RoomNavi(__instance));
                if (view != null) Guard("room present", view.RoomPresent);
            }
        }

        // ---- screens that build their own navi character (pair_fx 2026-11-13). Each instantiates the navi prefab of a
        // naviChara id; a custom partner has none, so the game falls back to stock navi 1 (AssetManager.GetNaviCharaPrefab).
        // When a partner.ini claims the partner shown, the pair (frame library idle, no reactions) goes next to that
        // figure, which PartnerView hides, sized from its footprint ("screen" context: result scale and lift, centred).
        static readonly AccessTools.FieldRef<SimpleSettingMonitor, Transform> SettingRoot = AccessTools.FieldRefAccess<SimpleSettingMonitor, Transform>("_partnerObject");
        static readonly AccessTools.FieldRef<SimpleSettingMonitor, NavigationCharacter> SettingNavi = AccessTools.FieldRefAccess<SimpleSettingMonitor, NavigationCharacter>("_partnerCharacter");
        static readonly AccessTools.FieldRef<FriendBattleResultMonitor, Transform> BattleRoot = AccessTools.FieldRefAccess<FriendBattleResultMonitor, Transform>("_characterParent");
        static readonly AccessTools.FieldRef<FriendBattleResultMonitor, NavigationCharacter> BattleNavi = AccessTools.FieldRefAccess<FriendBattleResultMonitor, NavigationCharacter>("_navigationCharacter");
        static readonly AccessTools.FieldRef<ShopPartnerGetWindow, Transform> ShopRoot = AccessTools.FieldRefAccess<ShopPartnerGetWindow, Transform>("partnerRoot");
        static readonly AccessTools.FieldRef<ShopPartnerGetWindow, NavigationCharacter> ShopNavi = AccessTools.FieldRefAccess<ShopPartnerGetWindow, NavigationCharacter>("navigationCharacter");
        static readonly AccessTools.FieldRef<global::Monitor.LoginBonus.LoginBonusMonitor, GameObject> BonusCharacter = AccessTools.FieldRefAccess<global::Monitor.LoginBonus.LoginBonusMonitor, GameObject>("_Character");
        static readonly AccessTools.FieldRef<global::Monitor.LoginBonus.LoginBonusMonitor, int> BonusItem = AccessTools.FieldRefAccess<global::Monitor.LoginBonus.LoginBonusMonitor, int>("_currentBonusItemRefID");

        static void AttachScreen(string where, Transform root, NavigationCharacter navi, PartnerConfig cfg, int monitor)
        {
            if (cfg == null || root == null || navi == null) return;
            var view = PartnerView.Attach(root, navi, cfg, monitor, null, "screen");
            Mod.Msg(cfg.Name + ": " + where + (view != null ? ": pair shown instead of the stock figure" : ": pair unavailable, stock figure stays"));
        }

        // first play of the day: the equipped partner greets (SimpleSettingMonitor.Initialize builds it only then)
        [HarmonyPatch(typeof(SimpleSettingMonitor), "Initialize")]
        static class SettingPartner
        {
            static void Postfix(SimpleSettingMonitor __instance, bool __1)
            {
                if (!Mod.Ready || !__1) return;
                Guard("first-play settings partner", () =>
                {
                    var navi = SettingNavi(__instance);
                    if (navi == null) return;
                    int id = Singleton<UserDataManager>.Instance.GetUserData(__instance.MonitorIndex).Detail.EquipPartnerID;
                    AttachScreen("first-play settings", SettingRoot(__instance), navi, Mod.ForPartner(id), __instance.MonitorIndex);
                });
            }
        }

        // friend battle result: the equipped partner's navi chara (FriendBattleResultProcess passes partner.naviChara.id)
        [HarmonyPatch(typeof(FriendBattleResultMonitor), "SetUserData")]
        static class BattlePartner
        {
            static void Postfix(FriendBattleResultMonitor __instance, int __2)
            {
                if (!Mod.Ready) return;
                Guard("friend battle partner", () => AttachScreen("friend battle result", BattleRoot(__instance), BattleNavi(__instance), Mod.ForNavi(__2), __instance.MonitorIndex));
            }
        }

        // a partner received (shop, present, map treasure, regional reward): the RECEIVED partner, not the equipped one
        [HarmonyPatch(typeof(ShopPartnerGetWindow), "Initalize")]
        static class ShopPartner
        {
            static void Postfix(ShopPartnerGetWindow __instance, int __0, int __1, int __3)
            {
                if (!Mod.Ready) return;
                Guard("partner get window", () => AttachScreen("partner get window", ShopRoot(__instance), ShopNavi(__instance), Mod.ForPartner(__1) ?? Mod.ForNavi(__0), __3));
            }
        }

        // login bonus get window: the partner the bonus GIVES (only for a partner bonus), not the equipped one
        [HarmonyPatch(typeof(global::Monitor.LoginBonus.LoginBonusMonitor), "InitializeGetWindow")]
        static class BonusPartner
        {
            static void Postfix(global::Monitor.LoginBonus.LoginBonusMonitor __instance)
            {
                if (!Mod.Ready) return;
                Guard("login bonus partner", () =>
                {
                    var data = __instance.GetPartnerData(BonusItem(__instance));
                    if (data == null || data.BonusType != Manager.MaiStudio.RewardItemType.Partner) return;
                    var character = BonusCharacter(__instance);
                    var navi = character != null ? character.GetComponent<NavigationCharacter>() : null;
                    if (navi == null) return;
                    AttachScreen("login bonus", character.transform.parent, navi, Mod.ForPartner(data.PartnerId.id), __instance.MonitorIndex);
                });
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
