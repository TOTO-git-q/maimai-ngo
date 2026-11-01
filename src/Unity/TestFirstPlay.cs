using System.Collections.Generic;
using HarmonyLib;
using MAI2.Util;
using Manager;

namespace PartnerLive2D.Unity
{
    // Test only (settings.ini testInput = true AND testFirstPlay = true): the owner's registered card logs in as a card
    // that has never played, without touching the account on the server.
    // - At login (PlInformationProcess.RestoreCharadata, registered cards only) the local copy gets PlayCount 0 and the
    //   partner a new card starts with (UserDataManager.SetDefault: partner 1), so the game runs its first-play path
    //   (tutorial select, first-play info windows, normal-mode select with the stock partner).
    // - Every upload of the user detail goes through VOExtensions.Export(UserDetail); there the real play count (plus the
    //   plays made in this credit) and the real partner are put back, so the server keeps the true values. This also
    //   covers AquaMai ImmediateSave, which uploads through the same path.
    static class TestFirstPlay
    {
        public static bool Enabled;
        // the first-play path also fills the travel-partner team with the area's starter characters (and that was
        // uploaded once, 2026-11-01), so the team is part of what gets put back
        sealed class Real { public int PlayCount, Partner; public int[] Team, TeamLock; }
        // keyed by the game's own UserDetail object (two players at most; cleared on the next login of that player)
        static readonly Dictionary<Manager.UserDatas.UserDetail, Real> Faked = new Dictionary<Manager.UserDatas.UserDetail, Real>();
        const int NewCardPartner = 1;

        [HarmonyPatch(typeof(Process.PlInformationProcess), "RestoreCharadata")]
        static class AtLogin
        {
            static void Postfix(int index)
            {
                if (!TestHarness.InputHooked) return;
                try
                {
                    var d = Singleton<UserDataManager>.Instance.GetUserData(index).Detail;
                    // test runs: the values the server sent, to check after a first-play run that the account kept them
                    Mod.Msg(string.Format("[test] login player {0}: play count {1}, partner {2}", index + 1, d.PlayCount, d.EquipPartnerID));
                    if (!Enabled) return;
                    if (Faked.Count > 4) Faked.Clear();
                    Faked[d] = new Real { PlayCount = d.PlayCount, Partner = d.EquipPartnerID,
                        Team = (int[])d.CharaSlot.Clone(), TeamLock = (int[])d.CharaLockSlot.Clone() };
                    Mod.Warn(string.Format("[test] first play: player {0} play count {1} -> 0, partner {2} -> {3} (local only)",
                        index + 1, d.PlayCount, d.EquipPartnerID, NewCardPartner));
                    d.PlayCount = 0;
                    d.EquipPartnerID = NewCardPartner;
                }
                catch (System.Exception e) { Mod.Error("[test] first play: " + e); }
            }
        }

        [HarmonyPatch(typeof(Net.VO.Mai2.VOExtensions), "Export", new[] { typeof(Manager.UserDatas.UserDetail) })]
        static class AtUpload
        {
            static void Postfix(Manager.UserDatas.UserDetail src, ref Net.VO.Mai2.UserDetail __result)
            {
                Real real;
                if (src == null || !Faked.TryGetValue(src, out real)) return;
                int played = src.PlayCount;   // plays added locally since the fake 0
                __result.playCount = real.PlayCount + played;
                if (src.EquipPartnerID == NewCardPartner) __result.partnerId = real.Partner;
                __result.charaSlot = (int[])real.Team.Clone();
                __result.charaLockSlot = (int[])real.TeamLock.Clone();
                Mod.Warn(string.Format("[test] first play: upload gets play count {0} (real {1} + {2} this credit), partner {3}, team [{4}]",
                    __result.playCount, real.PlayCount, played, __result.partnerId, string.Join(",", System.Array.ConvertAll(real.Team, x => x.ToString()))));
            }
        }
    }
}
