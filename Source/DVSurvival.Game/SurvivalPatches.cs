using DV;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace DVSurvival.Mod
{
    [HarmonyPatch]
    internal static class NativeSleepOriginPatch
    {
        [ThreadStatic] private static int advanceDepth;
        [ThreadStatic] private static BedSleeping activeBed;
        internal static bool IsAdvancingSleep => advanceDepth > 0;

        internal static void CaptureBed(DVSurvival.Core.SurvivalActionRequest request)
        {
            if (!IsAdvancingSleep || activeBed == null || request == null) return;
            var anchor = activeBed.pillowTarget != null ? activeBed.pillowTarget : activeBed.transform;
            var position = anchor.position - WorldMover.currentMove;
            request.HasNativeBed = true;
            request.BedWorldX = position.x;
            request.BedWorldY = position.y;
            request.BedWorldZ = position.z;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.EnumeratorMoveNext(
                AccessTools.Method(typeof(BedSleepingController), "SleepCoro"));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(TimeAdvance), "AdvanceTime",
                new[] { typeof(float), typeof(bool) });
            var replacement = AccessTools.Method(typeof(NativeSleepOriginPatch), "AdvanceSleepTime");
            var bedField = AccessTools.Field(TargetMethod().DeclaringType, "bed");
            if (bedField == null || bedField.FieldType != typeof(BedSleeping))
                throw new MissingFieldException("Native SleepCoro bed field changed.");
            var rewritten = new List<CodeInstruction>();
            var count = 0;
            foreach (var instruction in instructions)
            {
                if (!instruction.Calls(original)) { rewritten.Add(instruction); continue; }
                var loadCoroutine = new CodeInstruction(OpCodes.Ldarg_0);
                loadCoroutine.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                loadCoroutine.blocks.AddRange(instruction.blocks);
                instruction.blocks.Clear();
                rewritten.Add(loadCoroutine);
                rewritten.Add(new CodeInstruction(OpCodes.Ldfld, bedField));
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                rewritten.Add(instruction);
                count++;
            }
            if (count != 1) throw new InvalidOperationException(
                "Native sleep must contain exactly one TimeAdvance call; found " + count + ".");
            return rewritten;
        }

        private static void AdvanceSleepTime(float amountOfTimeToSkipInSeconds, bool force, BedSleeping bed)
        {
            // Scope only the actual native sleep time skip, never the animation's wait period.
            // Fast travel, console commands and relayed MP packets cannot acquire this marker.
            advanceDepth++;
            var previousBed = activeBed;
            activeBed = bed;
            try { TimeAdvance.AdvanceTime(amountOfTimeToSkipInSeconds, force); }
            finally { activeBed = previousBed; advanceDepth--; }
        }
    }

    [HarmonyPatch(typeof(TimeAdvance), "AdvanceTime")]
    internal static class CompletedNativeSleepPatch
    {
        private struct CalendarCapture
        {
            public bool HasCalendar;
            public bool IsNativeSleep;
            public bool SleepRequestSent;
            public long BeforeTicks;
        }

        [HarmonyBefore("Multiplayer")]
        private static void Prefix(float amountOfTimeToSkipInSeconds, bool force,
            out CalendarCapture __state)
        {
            __state = default(CalendarCapture);
            var runtime = Main.Runtime;
            if (force || runtime == null) return;
            __state.IsNativeSleep = NativeSleepOriginPatch.IsAdvancingSleep;
            DateTime before;
            if (!runtime.TryGetGameDateTime(out before) || before == DateTime.MinValue ||
                before == DateTime.MaxValue)
                return;
            __state.HasCalendar = true;
            __state.BeforeTicks = before.Ticks;
            if (__state.IsNativeSleep)
                __state.SleepRequestSent = runtime.OnLocalSleepStarting(
                    amountOfTimeToSkipInSeconds, __state.BeforeTicks);
        }

        private static void Postfix(float amountOfTimeToSkipInSeconds, bool force, CalendarCapture __state)
        {
            var runtime = Main.Runtime;
            if (force || runtime == null) return;
            if (!__state.HasCalendar)
            {
                if (__state.IsNativeSleep) UnityEngine.Debug.LogWarning(
                    "[DVSurvival] Native sleep was not credited: game calendar unavailable.");
                return;
            }
            DateTime after;
            if (!runtime.TryGetGameDateTime(out after) || after.Ticks <= __state.BeforeTicks)
            {
                if (__state.IsNativeSleep && runtime.OnLocalSleepWithoutTimeAdvanceCompleted(
                    amountOfTimeToSkipInSeconds)) return;
                if (__state.IsNativeSleep) UnityEngine.Debug.LogWarning(
                    "[DVSurvival] Native sleep did not advance the game calendar; check time override / MP time advance settings.");
                return;
            }
            runtime.OnWorldTimeAdvanceCompleted(__state.BeforeTicks, after.Ticks,
                __state.IsNativeSleep);
            if (__state.IsNativeSleep && !__state.SleepRequestSent)
                runtime.OnLocalSleepCompleted(amountOfTimeToSkipInSeconds,
                    __state.BeforeTicks, after.Ticks);
        }

    }

    [HarmonyPatch(typeof(CustomFirstPersonController), "ProcessInputAndGetSpeed")]
    internal static class PlayerMovementSurvivalPatch
    {
        private static void Postfix(CustomFirstPersonController __instance, ref float __result)
        {
            var runtime = Main.Runtime;
            if (runtime == null || !runtime.IsSessionReady) return;
            if ((!DVSurvival.Core.SurvivalSimulator.CanRun(runtime.CurrentState) ||
                (ProvisionUseAction.Active != null && ProvisionUseAction.Active.IsWorking)) && !__instance.m_IsWalking)
            {
                var run = __instance.baseRunSpeed * __instance.runSpeedMultipiler;
                // Preserve the native crouch/sitting and movement factors, replacing only sprint speed.
                if (run > 0f) __result *= UnityEngine.Mathf.Min(1f, __instance.baseWalkSpeed / run);
                __instance.m_IsWalking = true;
            }
            __result *= runtime.MovementMultiplier;
            if (ProvisionUseAction.Active != null && ProvisionUseAction.Active.IsWorking) __result *= 0.6f;
        }
    }
}
