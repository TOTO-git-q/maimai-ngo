using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Manager;
using Monitor;
using Process;
using Process.SubSequence;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // Extend only the stock E2/E8 conditions. All earlier stock branches, including
    // EachPushDecision, remain in place and retain their priority and lock guards.
    [HarmonyPatch(typeof(MusicSelectSequence), "Update")]
    static class MusicSelectCategoryInput
    {
        sealed class State
        {
            public readonly MusicSelectCategoryWindow Window = new MusicSelectCategoryWindow();
            public int Ready;
            public bool Reached;
        }
        // One state per player's sequence, dropped when the song select ends. Not a ConditionalWeakTable: on this
        // game's Boehm GC it holds its keys strongly, so every finished song select stayed alive with everything it
        // referenced (about 20 MB a visit), and each later collection had more to scan.
        static readonly List<KeyValuePair<MusicSelectSequence, State>> States = new List<KeyValuePair<MusicSelectSequence, State>>();

        static State StateOf(MusicSelectSequence sequence)
        {
            for (int i = 0; i < States.Count; i++)
                if (ReferenceEquals(States[i].Key, sequence)) return States[i].Value;
            if (States.Count >= 4) States.RemoveAt(0);
            var state = new State();
            States.Add(new KeyValuePair<MusicSelectSequence, State>(sequence, state));
            return state;
        }

        public static void Clear() { States.Clear(); _current = null; }
        static readonly FieldInfo Processing = AccessTools.Field(typeof(SequenceBase), "ProcessProcessing");
        static readonly FieldInfo Player = AccessTools.Field(typeof(SequenceBase), "PlayerIndex");
        static readonly MethodInfo Right = AccessTools.Method(typeof(MusicSelectSequence), "CategoryScrollRight");
        static readonly MethodInfo Left = AccessTools.Method(typeof(MusicSelectSequence), "CategoryScrollLeft");
        [ThreadStatic] static MusicSelectSequence _current;

        public static void Reset(MusicSelectSequence sequence) => StateOf(sequence).Window.Reset();

        static void Prefix(MusicSelectSequence __instance)
        {
            _current = __instance;
            var state = StateOf(__instance); state.Ready = 0; state.Reached = false;
            try
            {
                var p = Processing.GetValue(__instance) as MusicSelectProcess;
                int player = (int)Player.GetValue(__instance);
                // Update owns phase/category eligibility; dispatch still occurs only at
                // the original E2/E8 branch, after all higher-priority stock branches.
                bool eligible = p != null && MusicSelectCategoryWindow.CanSample(MusicSelectSession.CategoryInputsEnabled,
                    __instance.IsValidity, p.SharedIsInputLock, p.IsInputLocking(player));
                state.Ready = state.Window.Sample(Time.realtimeSinceStartup * 1000.0, eligible,
                    InputManager.GetButtonDown(player, InputManager.ButtonSetting.Button02),
                    InputManager.GetButtonDown(player, InputManager.ButtonSetting.Button07),
                    InputManager.GetButtonPush(player, InputManager.ButtonSetting.Button02),
                    InputManager.GetButtonPush(player, InputManager.ButtonSetting.Button07),
                    InputManager.GetTouchPanelAreaDown(player, InputManager.TouchPanelArea.A2),
                    InputManager.GetTouchPanelAreaDown(player, InputManager.TouchPanelArea.A7));
            }
            catch (Exception e) { state.Window.Reset(); MusicSelectSession.Fail(e); }
        }
        static Exception Finalizer(Exception __exception)
        {
            if (_current != null)
            {
                var state = StateOf(_current);
                // Drop queued presses if any earlier branch won (scroll, NEXT, combo, etc.).
                if (!state.Reached) state.Window.Reset();
                state.Ready = 0; _current = null;
            }
            return __exception;
        }
        public static bool TouchDown(int player, InputManager.TouchPanelArea area)
        {
            bool stock = InputManager.GetTouchPanelAreaDown(player, area);
            if (_current == null || (area != InputManager.TouchPanelArea.E2 && area != InputManager.TouchPanelArea.E8)) return stock;
            var state = StateOf(_current); state.Reached = true;
            if (stock) { state.Window.Reset(); state.Ready = 0; return true; }
            if (state.Ready != (area == InputManager.TouchPanelArea.E2 ? 1 : -1)) return false;
            state.Ready = 0; return true;
        }
        // Cached reflection invokes the original private game methods; the stock
        // Update branch performs ChangeBGM/ChangeTargetRate immediately afterwards.
        public static void ScrollRight(MusicSelectSequence sequence, MusicSelectMonitor[] monitors) => Right.Invoke(sequence, new object[] { monitors });
        public static void ScrollLeft(MusicSelectSequence sequence, MusicSelectMonitor[] monitors) => Left.Invoke(sequence, new object[] { monitors });
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            var touch = AccessTools.Method(typeof(InputManager), "GetTouchPanelAreaDown", new[] { typeof(int), typeof(InputManager.TouchPanelArea) });
            int right = 0, left = 0, touches = 0;
            var result = new List<CodeInstruction>();
            foreach (var instruction in source)
            {
                if (instruction.Calls(touch)) { instruction.operand = AccessTools.Method(typeof(MusicSelectCategoryInput), nameof(TouchDown)); touches++; }
                else if (instruction.Calls(Right)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MusicSelectCategoryInput), nameof(ScrollRight)); right++; }
                else if (instruction.Calls(Left)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MusicSelectCategoryInput), nameof(ScrollLeft)); left++; }
                result.Add(instruction);
            }
            if (right != 1 || left != 1 || touches < 2) throw new InvalidOperationException("Music category input patch: unsupported stock Update IL");
            return result;
        }
    }

    [HarmonyPatch(typeof(MusicSelectSequence), "OnStartSequence")]
    static class MusicSelectCategoryStartPatch
    {
        static void Prefix(MusicSelectSequence __instance) => MusicSelectCategoryInput.Reset(__instance);
    }
}
