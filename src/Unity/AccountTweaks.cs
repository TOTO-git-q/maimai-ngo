using HarmonyLib;
using MAI2.Util;
using Manager;
using PartnerLive2D.Core;

namespace PartnerLive2D.Unity
{
    // Owner's account tweaks, applied once the card's data has been restored at login (PlInformationProcess.RestoreCharadata,
    // registered users only; the game calls it after the user data download is done and before anything uses the equipped
    // partner). They reach the server with the game's next save; the settings.ini lines can be removed after.
    //   (always)             the equipped partner has no game data (e.g. its Partner.xml was removed) -> stock partner 1
    //   partner.ini autoEquip = once   equips that custom partner at an account's first login with it installed
    //   charaLevel = 9999    every character below that level is raised to it; awakening stars follow the game's own table
    //   equipPartner = 502   equips that partner at EVERY login (only if the game has its data)
    static class AccountTweaks
    {
        public static uint CharaLevel;
        public static int EquipPartner;

        [HarmonyPatch(typeof(Process.PlInformationProcess), "RestoreCharadata")]
        static class AfterRestore
        {
            static void Postfix(int index)
            {
                try
                {
                    var user = Singleton<UserDataManager>.Instance.GetUserData(index);
                    if (user == null || user.IsGuest() || user.UserType != UserData.UserIDType.Exist) return;
                    var data = Singleton<DataManager>.Instance;
                    System.Func<int, bool> hasData = id => data.GetPartner(id) != null;

                    // uninstall safety: the result / simple-setting / friend-battle screens dereference GetPartner(equipped)
                    int fallback = PartnerLogin.Fallback(user.Detail.EquipPartnerID, hasData);
                    if (fallback > 0)
                    {
                        Mod.Warn(string.Format("[account] player {0}: equipped partner {1} has no game data; partner {2} equipped instead", index + 1, user.Detail.EquipPartnerID, fallback));
                        user.Detail.EquipPartnerID = fallback;
                    }

                    Own(index, user, hasData);
                    AutoEquip(index, user, hasData);
                    if (CharaLevel == 0 && EquipPartner == 0) return;

                    int raised = 0;
                    if (CharaLevel > 0)
                    {
                        uint level = System.Math.Min(CharaLevel, Manager.UserDatas.UserChara.LevelMax);
                        foreach (var chara in user.CharaList)
                        {
                            if (chara.Level >= level) continue;
                            chara.Level = level;
                            chara.DisplayLevel = chara.ClacDisplayLevel();
                            chara.Reincarnation = chara.ClacReincarnationLevel();
                            chara.CalcLevelToAwake();
                            raised++;
                        }
                        user.UpdateTotalAwake();
                    }
                    string partner = "";
                    if (EquipPartner > 0 && user.Detail.EquipPartnerID != EquipPartner)
                    {
                        if (hasData(EquipPartner))
                        {
                            user.Detail.EquipPartnerID = EquipPartner;
                            partner = ", partner " + EquipPartner + " equipped";
                        }
                        else partner = ", partner " + EquipPartner + " has no game data, not equipped";
                    }
                    Mod.Msg(string.Format("[account] player {0}: {1} of {2} characters raised to level {3}{4}", index + 1, raised, user.CharaList.Count, CharaLevel, partner));
                }
                catch (System.Exception e) { Mod.Error("account tweaks: " + e); }
            }
        }

        // A NEW partner this mod adds (navi chara id above the stock 0-28) is only in an account's owned list when the
        // account was created with it installed (UserDataManager.SetDefault adds the isDefault partners). An account that
        // has played before does not own it: the collection lists it as not owned and refuses to equip it, so after
        // switching away the player could never choose it again. Add it to the owned list, the way the game's own
        // ChangeDefaultPartner migration does. Stock partners are never granted here.
        static void Own(int index, UserData user, System.Func<int, bool> hasData)
        {
            foreach (var cfg in Mod.Configs)
            {
                if (cfg.PartnerId <= 0 || cfg.NaviCharaId <= 28 || !hasData(cfg.PartnerId)) continue;
                try
                {
                    if (user.AddCollections(UserData.Collection.Partner, cfg.PartnerId))
                        Mod.Msg(string.Format("[account] player {0}: partner {1} ('{2}') added to the owned partners", index + 1, cfg.PartnerId, cfg.Name));
                }
                catch (System.Exception e) { Mod.Error("own " + cfg.Name + ": " + e.Message); }
            }
        }

        // partner.ini "autoEquip = once": per partner a ledger (salted hashes of the accounts already handled) next to its ini
        static void AutoEquip(int index, UserData user, System.Func<int, bool> hasData)
        {
            bool equippedNow = false;
            foreach (var cfg in Mod.Configs)
            {
                if (!cfg.AutoEquipOnce || cfg.PartnerId <= 0) continue;
                try
                {
                    var ledger = new AutoEquipLedger(System.IO.Path.Combine(cfg.Folder, AutoEquipLedger.FileName));
                    ulong id = user.Detail.UserID;
                    var d = PartnerLogin.Decide(true, hasData(cfg.PartnerId), ledger.Contains(id), user.Detail.EquipPartnerID, cfg.PartnerId, equippedNow);
                    if (d == PartnerLogin.AutoEquip.Equip) { user.Detail.EquipPartnerID = cfg.PartnerId; equippedNow = true; }
                    if (PartnerLogin.Records(d)) ledger.Add(id);
                    if (d != PartnerLogin.AutoEquip.AlreadyHandled)
                        Mod.Msg(string.Format("[account] player {0}: autoEquip '{1}' (partner {2}): {3}", index + 1, cfg.Name, cfg.PartnerId, d));
                }
                catch (System.Exception e) { Mod.Error("autoEquip " + cfg.Name + ": " + e.Message); }
            }
        }
    }
}
