using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;

namespace PartnerLive2D.Unity
{
    // Pure configuration and name matching, shared with the headless harness.
    static class ThemeOptions
    {
        internal sealed class LayoutRule
        {
            public string Target;
            public float Scale, OffsetX, OffsetY;
        }

        public static List<LayoutRule> ParseLayout(IEnumerable<string> lines)
        {
            var rules = new List<LayoutRule>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int lineNumber = 0;
            foreach (string raw in lines)
            {
                lineNumber++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0 || equals != line.LastIndexOf('=')) throw new FormatException("layout.ini line " + lineNumber + ": expected target = scale [offsetX offsetY]");
                string target = line.Substring(0, equals).Trim();
                string[] parts = line.Substring(equals + 1).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (target.Length == 0 || target.StartsWith("/") || target.EndsWith("/") || target.Contains("//") || (parts.Length != 1 && parts.Length != 3) || !seen.Add(target))
                    throw new FormatException("layout.ini line " + lineNumber + ": invalid or duplicate target/values");
                float scale, x = 0, y = 0;
                if (!ParseNumber(parts[0], out scale) || scale <= 0 || scale > 100 ||
                    (parts.Length == 3 && (!ParseNumber(parts[1], out x) || !ParseNumber(parts[2], out y))))
                    throw new FormatException("layout.ini line " + lineNumber + ": invalid numeric value");
                rules.Add(new LayoutRule { Target = target, Scale = scale, OffsetX = x, OffsetY = y });
            }
            return rules;
        }

        static bool ParseNumber(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static string ResolveFolder(string root, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (Path.IsPathRooted(value)) throw new FormatException("theme folder must be relative to PartnerLive2D");
            string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(basePath, value));
            if (!full.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("theme folder must be inside PartnerLive2D");
            return full;
        }

        public static Dictionary<string, string> IndexFiles(IEnumerable<string> files)
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                if (!string.Equals(Path.GetExtension(file), ".png", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                if (!index.ContainsKey(name)) index.Add(name, file);
            }
            return index;
        }
    }
}
