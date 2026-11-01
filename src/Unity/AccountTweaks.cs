using HarmonyLib;
using MAI2.Util;
using Manager;

namespace PartnerLive2D.Unity
{
    // Owner's account tweaks, applied once the card's data has been restored at login (PlInformationProcess.RestoreCharadata,
    // registered users only). They reach the server with the game's next save; the settings.ini lines can be removed after.
    //   charaLevel = 9999    every character below that level is raised to it; awakening stars follow the game's own table
    //   equipPartner = 502   equips that partner (only if the game has its data)
    static class AccountTweaks
    {
        public static uint CharaLevel;
        public static int EquipPartner;

        [HarmonyPatch(typeof(Process.PlInformationProcess), "RestoreCharadata")]
        static class AfterRestore
        {
            static void Postfix(int index)
            {
                if (CharaLevel == 0 && EquipPartner == 0) return;
                try
                {
                    var user = Singleton<UserDataManager>.Instance.GetUserData(index);
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
                        if (Singleton<DataManager>.Instance.GetPartner(EquipPartner) != null)
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
    }
}
