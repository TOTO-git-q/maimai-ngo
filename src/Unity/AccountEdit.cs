using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MAI2.Util;
using Manager;
using Manager.UserDatas;
using Process;

namespace PartnerLive2D.Unity
{
    // One-shot account edit (the owner's own card): after login, set the user name, equipped icon / nameplate / frame
    // and the five travel-partner slots from LocalAssets/PartnerLive2D/account_edit.ini, adding the items and characters
    // to the owned lists if missing. The game's own logout upload (UpsertUserAll) sends the profile and the new items /
    // characters to the server, so the change lands on the account. Guests are never touched. After it has been
    // applied the ini is renamed to account_edit.applied.ini (delete or rename back to apply again).
    [HarmonyPatch(typeof(ProcessManager), "AddProcess")]
    static class AccountEdit
    {
        static string IniPath => Path.Combine(Mod.AssetRoot, "account_edit.ini");
        static bool _applied;
        static bool Prepare() => File.Exists(IniPath);

        static void Postfix(ProcessBase process)
        {
            string pname = process == null ? "" : process.GetType().Name;
            // the logout upload has started with the edited data: done, do not apply again
            if (pname == "UserDataULProcess" && _applied && File.Exists(IniPath))
            {
                // an older .applied copy may still be there (the ini is re-armed by copying it back): replace it, and
                // never let a file error escape into the game's process switch
                try
                {
                    var done = Path.Combine(Mod.AssetRoot, "account_edit.applied.ini");
                    if (File.Exists(done)) File.Delete(done);
                    File.Move(IniPath, done);
                    Mod.Msg("account edit: uploaded with this logout");
                }
                catch (Exception e) { Mod.Warn("account edit: could not retire account_edit.ini: " + e.Message); }
                _applied = false;
                return;
            }
            // apply on music select: by then the server copy has been downloaded (applying at PlInformation was
            // overwritten by the download that finishes later)
            if (pname != "MusicSelectProcess") return;
            if (!File.Exists(IniPath)) return;
            try
            {
                var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(IniPath))
                {
                    var line = raw.Trim(); int eq = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || eq < 0) continue;
                    cfg[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                bool any = false;
                for (int p = 0; p < 2; p++)
                {
                    var user = Singleton<UserDataManager>.Instance.GetUserData(p);
                    if (user == null || !user.IsEntry || Net.UserID.IsGuest(user.Detail.UserID)) continue;
                    Apply(user, cfg); any = true;
                }
                if (any) { _applied = true; Mod.Msg("account edit: applied; it reaches the server with the logout upload"); }
            }
            catch (Exception e) { Mod.Warn("account edit failed: " + e); }
        }

        static void Apply(UserData user, Dictionary<string, string> cfg)
        {
            var d = user.Detail; string v;
            if (cfg.TryGetValue("name", out v) && v.Length > 0) d.UserName = v;
            if (cfg.TryGetValue("icon", out v)) { int id = int.Parse(v); Own(user.IconList, id); d.EquipIconID = id; }
            if (cfg.TryGetValue("plate", out v)) { int id = int.Parse(v); Own(user.PlateList, id); d.EquipPlateID = id; }
            if (cfg.TryGetValue("frame", out v)) { int id = int.Parse(v); Own(user.FrameList, id); d.EquipFrameID = id; }
            if (cfg.TryGetValue("chara", out v))
            {
                var ids = v.Split(',');
                for (int i = 0; i < d.CharaSlot.Length; i++)
                {
                    int id = i < ids.Length ? int.Parse(ids[i].Trim()) : 0;
                    if (id > 0 && user.CharaList.Find(c => c.ID == id) == null) user.CharaList.Add(new UserChara(id, 1, 1));
                    d.CharaSlot[i] = id;
                    d.CharaLockSlot[i] = 0;
                }
            }
            Mod.Msg(string.Format("account edit: name {0}, icon {1}, plate {2}, frame {3}, chara [{4}]", d.UserName, d.EquipIconID,
                d.EquipPlateID, d.EquipFrameID, string.Join(",", Array.ConvertAll(d.CharaSlot, x => x.ToString()))));
        }

        static void Own(List<UserItem> list, int id)
        {
            if (list.Find(x => x.itemId == id) == null) list.Add(new UserItem(id));
        }
    }
}
