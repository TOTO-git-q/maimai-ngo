using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace PartnerLive2D.Unity
{
    // Test-only (frameLog): find what keeps growing in the managed heap over a long session. Counts the elements of
    // every collection reachable from a static field, and one level into static singletons (MAI2.Util.Singleton<T> and
    // the like), in the game, AquaMai/MuMod and this mod; logs the ones that grew most since the last census.
    static class HeapCensus
    {
        static Dictionary<string, int> _last = new Dictionary<string, int>();
        static List<FieldInfo> _statics;

        static bool Interesting(Assembly a)
        {
            var n = a.GetName().Name;
            return n == "Assembly-CSharp" || n.StartsWith("AquaMai", StringComparison.Ordinal) || n.StartsWith("MuMod", StringComparison.Ordinal)
                || n.StartsWith("MuNet", StringComparison.Ordinal) || n == "PartnerLive2D" || n.StartsWith("MelonLoader", StringComparison.Ordinal);
        }

        static int Size(object v)
        {
            if (v == null) return -1;
            if (v is string) return -1;
            var arr = v as Array; if (arr != null) return arr.Length;
            var col = v as ICollection; if (col != null) return col.Count;
            return -1;
        }

        public static void Run()
        {
            try
            {
                if (_statics == null)
                {
                    _statics = new List<FieldInfo>();
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!Interesting(a)) continue;
                        Type[] types;
                        try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
                        foreach (var t in types)
                        {
                            if (t == null || t.ContainsGenericParameters) continue;
                            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                                if (!f.IsLiteral && !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType != typeof(string)) _statics.Add(f);
                        }
                    }
                }
                var now = new Dictionary<string, int>();
                foreach (var f in _statics)
                {
                    object v;
                    try { v = f.GetValue(null); } catch { continue; }
                    if (v == null) continue;
                    string key = f.DeclaringType.Name + "." + f.Name;
                    int n = Size(v);
                    if (n >= 0) { now[key] = n; continue; }
                    var vt = v.GetType();
                    if (vt.IsValueType || v is UnityEngine.Object) continue;
                    foreach (var g in vt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))   // one level into singletons
                    {
                        if (g.FieldType.IsPrimitive || g.FieldType == typeof(string)) continue;
                        object w; try { w = g.GetValue(v); } catch { continue; }
                        int m = Size(w);
                        if (m >= 0) now[key + "." + g.Name] = m;
                    }
                }
                var grew = new List<KeyValuePair<string, int>>();
                foreach (var kv in now) { int b; if (_last.TryGetValue(kv.Key, out b) && kv.Value > b) grew.Add(new KeyValuePair<string, int>(kv.Key, kv.Value - b)); }
                grew.Sort((x, y) => y.Value.CompareTo(x.Value));
                if (_last.Count > 0)
                {
                    var sb = new System.Text.StringBuilder("[mem] collections grew:");
                    for (int i = 0; i < grew.Count && i < 10; i++) sb.Append(" ").Append(grew[i].Key).Append(" +").Append(grew[i].Value).Append(" (=").Append(now[grew[i].Key]).Append(")");
                    Mod.Msg(sb.ToString());
                }
                _last = now;
            }
            catch (Exception e) { Mod.Warn("heap census: " + e.Message); }
        }
    }
}
