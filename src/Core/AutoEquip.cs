using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PartnerLive2D.Core
{
    // Login-time partner decisions (AccountTweaks applies them; no game types here so tests/Headless can check them).
    //  1. Uninstall safety: the equipped partner has no game data (its Partner.xml is gone) -> the stock default partner.
    //     The game dereferences GetPartner(EquipPartnerID) without a null check on the result, simple-setting and
    //     friend-battle-result screens (ResultProcess, SimpleSettingMonitor, FriendBattleResultProcess).
    //  2. partner.ini "autoEquip = once": equip the custom partner the first time an account logs in with it installed,
    //     then never again for that account (a later switch in game sticks).
    public static class PartnerLogin
    {
        public const int StockDefaultPartner = 1;   // UserDataManager.SetDefault / UserDetail: EquipPartnerID = 1

        // the partner id to equip instead of `equipped`, or -1 to leave it
        public static int Fallback(int equipped, Func<int, bool> hasData)
        {
            if (hasData(equipped)) return -1;
            return hasData(StockDefaultPartner) ? StockDefaultPartner : -1;
        }

        public enum AutoEquip { NotRegistered, NoGameData, AlreadyHandled, AlreadyEquipped, OtherPartnerTookIt, Equip }

        // registered: a card user (not a guest / new card); handled: this account is in the partner's ledger;
        // equippedThisLogin: another autoEquip partner was already equipped during this login
        public static AutoEquip Decide(bool registered, bool hasGameData, bool handled, int equipped, int partnerId, bool equippedThisLogin)
        {
            if (!registered) return AutoEquip.NotRegistered;
            if (!hasGameData) return AutoEquip.NoGameData;          // not recorded: equip once the data is installed
            if (handled) return AutoEquip.AlreadyHandled;
            if (equipped == partnerId) return AutoEquip.AlreadyEquipped;
            if (equippedThisLogin) return AutoEquip.OtherPartnerTookIt;
            return AutoEquip.Equip;
        }
        // every outcome but these marks the account as handled
        public static bool Records(AutoEquip d) { return d == AutoEquip.AlreadyEquipped || d == AutoEquip.OtherPartnerTookIt || d == AutoEquip.Equip; }

        // partner.ini "autoEquip": "once" or "off" (anything else is a format error)
        public static bool ParseAutoEquip(string value)
        {
            value = (value ?? "").Trim();
            if (value == "once") return true;
            if (value == "" || value == "off" || value == "false") return false;
            throw new FormatException("autoEquip must be once or off");
        }
    }

    // Accounts an autoEquip partner has already been equipped for (or found equipped): one file next to partner.ini holding
    // a random salt and salted SHA-256 hashes of the user ids, never the ids themselves.
    public sealed class AutoEquipLedger
    {
        public const string FileName = "autoequip_accounts.txt";
        readonly string _path;
        string _salt;
        readonly HashSet<string> _hashes = new HashSet<string>(StringComparer.Ordinal);
        public int Count { get { return _hashes.Count; } }

        public AutoEquipLedger(string path)
        {
            _path = path;
            if (File.Exists(path))
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line.StartsWith("salt = ", StringComparison.Ordinal)) _salt = line.Substring(7).Trim();
                    else if (IsHex(line, 64)) _hashes.Add(line);
                }
            if (!IsHex(_salt, 32)) { _salt = null; _hashes.Clear(); }   // no usable salt: the hashes cannot be matched
        }

        public bool Contains(ulong userId) { return _salt != null && _hashes.Contains(Hash(_salt, userId)); }

        public void Add(ulong userId)
        {
            if (_salt == null)
            {
                var bytes = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                _salt = Hex(bytes);
            }
            if (!_hashes.Add(Hash(_salt, userId))) return;
            var lines = new List<string> {
                "# PartnerLive2D autoEquip = once: accounts this partner was equipped for once (salted SHA-256, no user ids).",
                "# Delete this file to equip the partner once more for every account.",
                "salt = " + _salt };
            var sorted = new List<string>(_hashes); sorted.Sort(StringComparer.Ordinal);
            lines.AddRange(sorted);
            string tmp = _path + ".tmp";
            File.WriteAllLines(tmp, lines.ToArray());
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }

        public static string Hash(string salt, ulong userId)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.ASCII.GetBytes(salt + ":" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
        static bool IsHex(string s, int length)
        {
            if (s == null || s.Length != length) return false;
            foreach (char c in s) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }
    }
}
