using System;
using System.IO;
using PartnerLive2D.Unity;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void ThemeChecks()
        {
            Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); };
            string root = Path.Combine(Path.GetTempPath(), "partner-theme-check");
            check(ThemeOptions.ResolveFolder(root, null) == null && ThemeOptions.ResolveFolder(root, "  ") == null, "missing setting disables theme");
            check(ThemeOptions.ResolveFolder(root, " theme ") == Path.GetFullPath(Path.Combine(root, "theme")), "folder resolves beneath root");
            foreach (string bad in new[] { "..", "../escape", Path.GetPathRoot(root) })
            {
                bool failed = false;
                try { ThemeOptions.ResolveFolder(root, bad); } catch (FormatException) { failed = true; }
                check(failed, "reject escaping or rooted folder: " + bad);
            }
            var names = ThemeOptions.IndexFiles(new[] { Path.Combine(root, "UI_CMN.png"), Path.Combine(root, "other.PNG"), Path.Combine(root, "ignore.jpg") });
            check(names.Count == 2 && names.ContainsKey("UI_CMN") && names.ContainsKey("other"), "PNG names indexed without extension");
            check(!names.ContainsKey("ui_cmn") && !names.ContainsKey("UI_CMN.png"), "asset names match exactly");
            var rules = ThemeOptions.ParseLayout(new[] { "# comment", " ; comment", "Null_Logo/Magical_01 = 2 0 19", "LogoDX = 1", "Blank = 1.25 -3.5 4e1" });
            check(rules.Count == 3 && rules[0].Target == "Null_Logo/Magical_01" && rules[0].Scale == 2 && rules[0].OffsetY == 19, "layout parses path and offsets");
            check(rules[1].OffsetX == 0 && rules[1].OffsetY == 0 && rules[2].OffsetX == -3.5f && rules[2].OffsetY == 40, "layout defaults and invariant floats");
            foreach (string bad in new[] { "= 2", "a = 2 1", "a = NaN", "a = Infinity", "a = 0", "a = -1", "a = 101", "a = 1 0 nope", "a//b = 1", "/a = 1", "a = 1 = 2" })
            {
                bool failed = false;
                try { ThemeOptions.ParseLayout(new[] { bad }); } catch (FormatException) { failed = true; }
                check(failed, "reject invalid layout: " + bad);
            }
            bool duplicate = false;
            try { ThemeOptions.ParseLayout(new[] { "a=1", "a=2" }); } catch (FormatException) { duplicate = true; }
            check(duplicate, "reject duplicate targets");
            Console.WriteLine("Theme settings, names and layout parser checks passed");
        }
    }
}
