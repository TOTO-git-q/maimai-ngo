using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Manager;
using Monitor;
using Process;
using Process.SubSequence;

namespace PartnerLive2D.Unity
{
    // 2026-11-16, GitHub issue #1: on the genre page the last category could not be reached and its right arrow was
    // missing (Level sort: LV15; Version sort: the oldest version). Stock code, not the layout: outside freedom mode
    // every "how many categories" check does `if (!GameManager.IsFreedomMode) count--;` to keep the cursor off the
    // "connect play" category (genre 198) that MusicSelectProcess appends at the end. It appends that category only
    // when its connect list is not empty (IsConnectCategoryEnable); on home setups it usually is empty, so the check
    // hid a real category. Fix: in exactly those checks, hide the tail only when the connect category is really there.
    // Where: GenreSelectSequence.Update (scroll right, slide scroll), GenreSelectSequence.CheckButton (right arrow),
    // MusicSelectMonitor.OnStartGenreSelect and the PlayCoroutine state machine (arrow + position indicator).
    [HarmonyPatch]
    static class GenreTailFix
    {
        static MusicSelectProcess _process;
        static readonly MethodInfo Freedom = AccessTools.PropertyGetter(typeof(GameManager), nameof(GameManager.IsFreedomMode));

        static IEnumerable<MethodBase> TargetMethods()
        {
            var co = AccessTools.Method(typeof(MusicSelectMonitor), "PlayCoroutine");
            var all = new MethodBase[] {
                AccessTools.Method(typeof(GenreSelectSequence), "Update"),
                AccessTools.Method(typeof(GenreSelectSequence), "CheckButton"),
                AccessTools.Method(typeof(MusicSelectMonitor), "OnStartGenreSelect"),
                co == null ? null : AccessTools.EnumeratorMoveNext(co) };
            foreach (var m in all) if (m != null) yield return m;   // a missing one must not stop the mod's other patches
        }

        // read where the stock code reads IsFreedomMode in a "count--" check: true = do not drop the last category
        public static bool KeepTail()
        {
            if (GameManager.IsFreedomMode) return true;
            try { return _process != null && !_process.IsConnectCategoryEnable; }
            catch { return false; }
        }

        // only the IsFreedomMode reads right after `int n = <list>.Count;` (callvirt get_Count; stloc), so the other
        // freedom checks in the same methods (freedom timer, long-song caution) stay untouched
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source, MethodBase __originalMethod)
        {
            var list = new List<CodeInstruction>(source);
            int n = 0;
            for (int i = 2; i < list.Count; i++)
            {
                if (!list[i].Calls(Freedom) || !list[i - 1].IsStloc()) continue;
                var prev = list[i - 2].operand as MethodInfo;
                if (prev == null || prev.Name != "get_Count") continue;
                list[i].opcode = System.Reflection.Emit.OpCodes.Call;
                list[i].operand = AccessTools.Method(typeof(GenreTailFix), nameof(KeepTail));
                n++;
            }
            if (n == 0) Mod.Warn("genre tail fix: no category-count check found in " + __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name);
            return list;
        }

        [HarmonyPatch(typeof(MusicSelectProcess), "OnStart")]
        static class Track
        {
            static void Prefix(MusicSelectProcess __instance) => _process = __instance;
        }

        [HarmonyPatch(typeof(MusicSelectProcess), "OnRelease")]
        static class Untrack
        {
            static void Postfix(MusicSelectProcess __instance) { if (ReferenceEquals(_process, __instance)) _process = null; }
        }
    }
}
