using HarmonyLib;
using Manager;

namespace PartnerLive2D.Unity
{
    // one log line per collection item the game hands out (plates, titles, icons, partners, frames), so a data-only
    // item such as the shareable mystery plate can be checked from the log
    [HarmonyPatch(typeof(UserData), "AddCollections")]
    static class CollectionLog
    {
        // only new gets: the game re-adds its default items (plate 1, icon 1, ...) many times a credit
        static void Postfix(UserData.Collection type, int id, bool addNewGet)
        {
            if (addNewGet) Mod.Msg(string.Format("[collection] new {0} {1}", type, id));
        }
    }
}
