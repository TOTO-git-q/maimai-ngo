using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PartnerLive2D.Unity
{
    // Test-only (test API, testInput): walk the whole managed object graph from its roots (every static field, the
    // closed generic singletons included, and every live MonoBehaviour / ScriptableObject) and total the objects and
    // bytes by type and by the root field that first reaches them. Two walks a few tracks apart show what the heap
    // keeps that it should have let go. Writes heapwalk\walk_<time>.tsv; the game stands still while it runs.
    static class HeapWalk
    {
        sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return RuntimeHelpers.GetHashCode(o); }
        }

        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<Type, FieldInfo[]> RefFieldCache = new Dictionary<Type, FieldInfo[]>();
        static readonly Dictionary<Type, int> SizeCache = new Dictionary<Type, int>();

        static HashSet<object> _seen;
        static Dictionary<Type, long[]> _byType;
        static Dictionary<string, long[]> _byRoot;
        static Stack<object> _stack;

        // instance fields (own and inherited) that can hold a reference, directly or inside a struct
        static FieldInfo[] RefFields(Type t)
        {
            FieldInfo[] r;
            if (RefFieldCache.TryGetValue(t, out r)) return r;
            var list = new List<FieldInfo>();
            for (var k = t; k != null && k != typeof(object); k = k.BaseType)
                foreach (var f in k.GetFields(Inst | BindingFlags.DeclaredOnly))
                    if (MayHoldRef(f.FieldType)) list.Add(f);
            r = list.ToArray(); RefFieldCache[t] = r; return r;
        }

        static bool MayHoldRef(Type t)
        {
            if (t.IsPointer || t.IsPrimitive || t.IsEnum) return false;
            if (!t.IsValueType) return true;
            return RefFields(t).Length > 0;
        }

        static int ValueSize(Type t)
        {
            if (!t.IsValueType || t.IsPointer) return 8;
            int s;
            if (SizeCache.TryGetValue(t, out s)) return s;
            if (t.IsEnum) s = ValueSize(Enum.GetUnderlyingType(t));
            else if (t == typeof(bool) || t == typeof(byte) || t == typeof(sbyte)) s = 1;
            else if (t == typeof(char) || t == typeof(short) || t == typeof(ushort)) s = 2;
            else if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) s = 4;
            else if (t.IsPrimitive) s = 8;
            else if (t == typeof(decimal)) s = 16;
            else { s = 0; foreach (var f in t.GetFields(Inst)) s += ValueSize(f.FieldType); if (s == 0) s = 1; }
            SizeCache[t] = s; return s;
        }

        static int InstanceSize(Type t)
        {
            int s;
            if (SizeCache.TryGetValue(t, out s)) return s;
            s = 16;
            for (var k = t; k != null && k != typeof(object); k = k.BaseType)
                foreach (var f in k.GetFields(Inst | BindingFlags.DeclaredOnly)) s += ValueSize(f.FieldType);
            SizeCache[t] = s; return s;
        }

        static void PushStruct(object boxed, Type t)
        {
            foreach (var f in RefFields(t))
            {
                object v; try { v = f.GetValue(boxed); } catch { continue; }
                if (v == null) continue;
                if (f.FieldType.IsValueType) PushStruct(v, f.FieldType); else _stack.Push(v);
            }
        }

        // everything newly reachable from o, credited to label
        static void Walk(object o, string label)
        {
            if (o == null) return;
            long n = 0, bytes = 0;
            _stack.Push(o);
            while (_stack.Count > 0)
            {
                var x = _stack.Pop();
                if (!_seen.Add(x)) continue;
                var t = x.GetType();
                long size;
                var s = x as string;
                var arr = x as Array;
                if (s != null) size = 20 + 2L * s.Length;
                else if (arr != null)
                {
                    var et = t.GetElementType();
                    size = 32 + (long)arr.Length * (et.IsValueType ? ValueSize(et) : 8);
                    if (et.IsPointer) { }
                    else if (!et.IsValueType) { foreach (var e in arr) if (e != null) _stack.Push(e); }
                    else if (MayHoldRef(et)) { foreach (var e in arr) if (e != null) PushStruct(e, et); }
                }
                else
                {
                    size = InstanceSize(t);
                    foreach (var f in RefFields(t))
                    {
                        object v; try { v = f.GetValue(x); } catch { continue; }
                        if (v == null) continue;
                        if (f.FieldType.IsValueType) PushStruct(v, f.FieldType); else _stack.Push(v);
                    }
                }
                n++; bytes += size;
                long[] c;
                if (!_byType.TryGetValue(t, out c)) _byType[t] = c = new long[2];
                c[0]++; c[1] += size;
            }
            if (n == 0) return;
            long[] r;
            if (!_byRoot.TryGetValue(label, out r)) _byRoot[label] = r = new long[2];
            r[0] += n; r[1] += bytes;
        }

        // a root object itself, then each of its fields walked under its own label (one level of attribution)
        static void WalkRoot(object root, string label)
        {
            if (root == null || _seen.Contains(root)) return;
            var t = root.GetType();
            if (root is string || root is Array || t.IsValueType) { Walk(root, label); return; }
            _seen.Add(root);
            foreach (var f in RefFields(t))
            {
                object v; try { v = f.GetValue(root); } catch { continue; }
                if (v == null) continue;
                string sub = label + "." + f.Name;
                if (f.FieldType.IsValueType) { PushStruct(v, f.FieldType); Drain(sub); }
                else Walk(v, sub);
            }
        }

        static void Drain(string label)
        {
            if (_stack.Count == 0) return;
            var first = _stack.Pop();
            var rest = _stack.ToArray(); _stack.Clear();
            Walk(first, label);
            foreach (var o in rest) Walk(o, label);
        }

        static IEnumerable<Type> RootTypes()
        {
            var seen = new HashSet<Type>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; } catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    // closed generic base classes (MAI2's Singleton<GameManager> and the like) hold statics too
                    for (var k = t; k != null; k = k.BaseType)
                    {
                        if (k.ContainsGenericParameters) continue;
                        if (seen.Add(k)) yield return k;
                    }
                }
            }
        }

        public static object Run()
        {
            var sw = Stopwatch.StartNew();
            GC.Collect();
            long heapAfterGc = GC.GetTotalMemory(false);
            _seen = new HashSet<object>(new RefEq());
            _byType = new Dictionary<Type, long[]>();
            _byRoot = new Dictionary<string, long[]>();
            _stack = new Stack<object>();
            int statics = 0;
            foreach (var t in RootTypes())
            {
                if (t == typeof(HeapWalk)) continue;   // its own bookkeeping
                FieldInfo[] fs;
                try { fs = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); } catch { continue; }
                foreach (var f in fs)
                {
                    if (f.IsLiteral || !MayHoldRef(f.FieldType)) continue;
                    object v; try { v = f.GetValue(null); } catch { continue; }
                    if (v == null) continue;
                    statics++;
                    string label = "static " + t.FullName + "." + f.Name;
                    if (f.FieldType.IsValueType) { PushStruct(v, f.FieldType); Drain(label); }
                    else WalkRoot(v, label);
                }
            }
            int behaviours = 0;
            foreach (var b in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.MonoBehaviour>())
            {
                if (b == null) continue;
                behaviours++;
                WalkRoot(b, "MB " + b.GetType().FullName);
            }
            foreach (var so in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
                if (so != null) WalkRoot(so, "SO " + so.GetType().FullName);

            long objects = 0, total = 0;
            foreach (var c in _byType.Values) { objects += c[0]; total += c[1]; }
            var dir = Path.Combine(Mod.AssetRoot, "heapwalk");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "walk_" + DateTime.Now.ToString("HHmmss") + ".tsv");
            using (var w = new StreamWriter(file))
            {
                w.WriteLine("kind\tname\tcount\tbytes");
                w.WriteLine("meta\theap_after_gc\t0\t" + heapAfterGc);
                w.WriteLine("meta\treached\t" + objects + "\t" + total);
                foreach (var kv in _byType) w.WriteLine("type\t" + kv.Key.FullName + "\t" + kv.Value[0] + "\t" + kv.Value[1]);
                foreach (var kv in _byRoot) w.WriteLine("root\t" + kv.Key + "\t" + kv.Value[0] + "\t" + kv.Value[1]);
            }
            var top = new List<KeyValuePair<string, long[]>>(_byRoot);
            top.Sort((a, b) => b.Value[1].CompareTo(a.Value[1]));
            var topList = new List<object>();
            for (int i = 0; i < top.Count && i < 15; i++) topList.Add(top[i].Key + " " + (top[i].Value[1] / 1048576.0).ToString("0.0") + " MB");
            var result = new Dictionary<string, object>
            {
                { "file", file }, { "ms", sw.ElapsedMilliseconds }, { "heapAfterGcMB", heapAfterGc / 1048576.0 },
                { "reachedMB", total / 1048576.0 }, { "objects", objects }, { "statics", statics }, { "behaviours", behaviours }, { "topRoots", topList },
            };
            _seen = null; _byType = null; _byRoot = null; _stack = null;
            Mod.Msg("[mem] heap walk: " + objects + " objects, " + (total / 1048576.0).ToString("0") + " MB reached of " + (heapAfterGc / 1048576.0).ToString("0") + " MB, " + sw.ElapsedMilliseconds + " ms -> " + file);
            return result;
        }
    }
}
