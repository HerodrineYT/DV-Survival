using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DV.HUD;
using DV.Simulation.Ports;
using DV.Customization.Gadgets.Implementations;
using DVSurvival.Core;
using HarmonyLib;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Only presentation endpoints are patched. Ports, Indicator.Value, train physics and packets stay truthful.
    internal static class FatigueSpeedometer
    {
        private static readonly ConditionalWeakTable<Indicator, SpeedBinding> speedGauges = new ConditionalWeakTable<Indicator, SpeedBinding>();
        private static readonly AccessTools.FieldRef<IndicatorPortReader, float> rangeScaler =
            AccessTools.FieldRefAccess<IndicatorPortReader, float>("indicatorRangeScaler");
        private static readonly List<WeakReference> gauges = new List<WeakReference>();
        private static readonly List<HudBinding> huds = new List<HudBinding>();
        private static readonly Action<IndicatorGauge, float> rotate = (Action<IndicatorGauge, float>)Delegate.CreateDelegate(
            typeof(Action<IndicatorGauge, float>), AccessTools.Method(typeof(IndicatorGauge), "SetNeedleRotation"));
        private static readonly Action<LocoHUDProvider, float> updateNumber = (Action<LocoHUDProvider, float>)Delegate.CreateDelegate(
            typeof(Action<LocoHUDProvider, float>), AccessTools.Method(typeof(LocoHUDProvider), "SpeedometerUpdated"));
        private static readonly Action<LocoHUDProvider, float> updateVisual = (Action<LocoHUDProvider, float>)Delegate.CreateDelegate(
            typeof(Action<LocoHUDProvider, float>), AccessTools.Method(typeof(LocoHUDProvider), "SpeedometerVisualUpdated"));
        private static float previousFactor = 1f;
        private static bool scannedForActiveEffect;
        private static long testUntilUtcTicks;

        public static float Factor
        {
            get
            {
                var runtime = Main.Runtime;
                if (runtime == null || !runtime.IsSessionReady) return 1f;
                return IsTestActive ? 1f / 1.5f : SleepDeprivationEffects.DisplaySpeedMultiplier(runtime.CurrentState);
            }
        }
        public static bool IsTestActive { get { return DateTime.UtcNow.Ticks < testUntilUtcTicks; } }
        public static void BeginTest(float seconds)
        { testUntilUtcTicks = DateTime.UtcNow.AddSeconds(Mathf.Clamp(seconds, 1f, 60f)).Ticks; }
        public static float Display(float speed) { return speed * Factor; }
        public static bool IsSpeedGauge(IndicatorGauge gauge)
        { SpeedBinding marker; return speedGauges.TryGetValue(gauge, out marker); }
        public static float DisplayGauge(Indicator indicator, float value)
        {
            SpeedBinding binding;
            return speedGauges.TryGetValue(indicator, out binding)
                ? SpeedDisplayScale.Apply(value, binding.Zero, Factor) : value;
        }
        public static float DisplayHudLevel(LocoHUDProvider provider, float level)
        {
            for (int i = huds.Count - 1; i >= 0; i--)
            {
                if (huds[i].Provider.Target as LocoHUDProvider != provider) continue;
                var reader = huds[i].Reader.Target as LocoIndicatorReader;
                SpeedBinding binding;
                if (reader == null || reader.speed == null || !speedGauges.TryGetValue(reader.speed, out binding)) break;
                var indicator = reader.speed;
                float zero = SpeedDisplayScale.NormalizedZero(binding.Zero, indicator.minValue,
                    indicator.maxValue, indicator.absoluteNormalizedValue);
                return SpeedDisplayScale.Apply(level, zero, Factor);
            }
            return Display(level);
        }
        public static void Register(Indicator indicator)
        {
            if (indicator == null) return;
            SpeedBinding binding;
            if (speedGauges.TryGetValue(indicator, out binding)) return;
            speedGauges.Add(indicator, new SpeedBinding(indicator.GetComponent<IndicatorPortReader>()));
            var gauge = indicator as IndicatorGauge;
            if (gauge == null) return;
            for (int i = gauges.Count - 1; i >= 0; i--) if (!(gauges[i].Target is IndicatorGauge live) || live == null) gauges.RemoveAt(i);
            gauges.Add(new WeakReference(gauge));
            if (gauge.needle != null) rotate(gauge, gauge.Value);
        }
        public static void ApplyNeedle(IndicatorGauge gauge)
        {
            if (gauge == null || gauge.needle == null || Factor == 1f || !IsSpeedGauge(gauge)) return;
            float shown = DisplayGauge(gauge, gauge.Value);
            float angle = SpeedDisplayScale.NeedleAngle(shown, gauge.minValue, gauge.maxValue,
                gauge.minAngle, gauge.maxAngle, gauge.unclamped);
            gauge.needle.localRotation = Quaternion.AngleAxis(angle, gauge.rotationAxis);
        }
        public static void LateTick()
        {
            if (Factor == 1f) return;
            for (int i = gauges.Count - 1; i >= 0; i--)
            {
                var gauge = gauges[i].Target as IndicatorGauge;
                if (gauge == null) { gauges.RemoveAt(i); continue; }
                ApplyNeedle(gauge);
            }
        }
        public static void Bind(LocoHUDProvider provider, LocoIndicatorReader reader)
        {
            Unbind(provider, reader);
            if (reader == null || reader.speed == null) return;
            Register(reader.speed);
            huds.Add(new HudBinding(provider, reader));
        }
        public static void Unbind(LocoHUDProvider provider, LocoIndicatorReader reader)
        {
            for (int i = huds.Count - 1; i >= 0; i--)
            {
                var p = huds[i].Provider.Target as LocoHUDProvider;
                var r = huds[i].Reader.Target as LocoIndicatorReader;
                if (p == null || r == null || (p == provider && r == reader)) huds.RemoveAt(i);
            }
        }
        public static void Tick()
        {
            float factor = Factor;
            if (factor == previousFactor) return;
            previousFactor = factor;
            var state = Main.Runtime == null ? null : Main.Runtime.CurrentState;
            Debug.Log("[DVSurvival] Personal speedometer display factor changed to " +
                factor.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                (state == null ? string.Empty : "; low-rest hours=" +
                    state.LowRestGameHours.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)) + ".");
            if (factor == 1f) scannedForActiveEffect = false;
            else if (!scannedForActiveEffect)
            {
                // One scene scan at the effect boundary is a fallback for prefabs initialized
                // before patch registration. It never runs every frame.
                scannedForActiveEffect = true;
                foreach (var reader in UnityEngine.Object.FindObjectsOfType<LocoIndicatorReader>())
                    if (reader != null) Register(reader.speed);
            }
            // Refresh once at effect boundaries even when the train holds a constant speed.
            for (int i = gauges.Count - 1; i >= 0; i--)
            {
                var gauge = gauges[i].Target as IndicatorGauge;
                if (gauge == null) { gauges.RemoveAt(i); continue; }
                if (gauge.needle != null) rotate(gauge, gauge.Value);
            }
            for (int i = huds.Count - 1; i >= 0; i--)
            {
                var provider = huds[i].Provider.Target as LocoHUDProvider;
                var reader = huds[i].Reader.Target as LocoIndicatorReader;
                if (provider == null || reader == null) { huds.RemoveAt(i); continue; }
                if (reader.speed == null) continue;
                updateNumber(provider, reader.speed.Value);
                updateVisual(provider, reader.speed.NormalizedValue);
            }
        }
        private sealed class HudBinding
        {
            public readonly WeakReference Provider, Reader;
            public HudBinding(LocoHUDProvider provider, LocoIndicatorReader reader)
            { Provider = new WeakReference(provider); Reader = new WeakReference(reader); }
        }
        private sealed class SpeedBinding
        {
            private readonly IndicatorPortReader reader;
            public SpeedBinding(IndicatorPortReader reader) { this.reader = reader; }
            public float Zero
            {
                get
                {
                    if (reader == null) return 0f;
                    float zero = reader.valueOffset;
                    if (reader.useAbsoluteValue) zero = Mathf.Abs(zero);
                    return zero * rangeScaler(reader);
                }
            }
        }
    }
    [HarmonyPatch(typeof(IndicatorGauge), "Start")]
    internal static class RegisterSpeedGaugePatch
    {
        private static void Postfix(IndicatorGauge __instance)
        {
            var reader = __instance.GetComponentInParent<LocoIndicatorReader>();
            if (reader != null && reader.speed == __instance) FatigueSpeedometer.Register(__instance);
        }
    }
    [HarmonyPatch(typeof(IndicatorGauge), "SetNeedleRotation")]
    internal static class PersonalSpeedGaugePatch
    {
        // Read the untouched indicator, never the input/rotation already modified by a patch.
        // Applying the final pose is idempotent, including lagging gauges and explicit refreshes.
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(IndicatorGauge __instance)
        {
            FatigueSpeedometer.ApplyNeedle(__instance);
        }
    }
    [HarmonyPatch(typeof(IndicatorGaugeLagging), "Update")]
    internal static class PersonalLaggingSpeedGaugePatch
    {
        // DE2's stock speedometer is an IndicatorGaugeLagging. This runs after its smoothing
        // update, so no later vanilla statement restores the truthful needle pose that frame.
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(IndicatorGaugeLagging __instance)
        { FatigueSpeedometer.ApplyNeedle(__instance); }
    }
    [HarmonyPatch(typeof(IndicatorPortReader), "Init")]
    internal static class RegisterSpeedPortReaderPatch
    {
        private static void Postfix(IndicatorPortReader __instance)
        {
            var indicator = __instance == null ? null : __instance.GetComponent<Indicator>();
            var reader = __instance == null ? null : __instance.GetComponentInParent<LocoIndicatorReader>();
            if (reader != null && reader.speed == indicator) FatigueSpeedometer.Register(indicator);
        }
    }
    [HarmonyPatch(typeof(LocoHUDProvider), "SubIndicators")]
    internal static class BindSpeedHudPatch
    {
        private static void Postfix(LocoHUDProvider __instance, LocoIndicatorReader ir) { FatigueSpeedometer.Bind(__instance, ir); }
    }
    [HarmonyPatch(typeof(LocoHUDProvider), "UnsubIndicators")]
    internal static class UnbindSpeedHudPatch
    {
        private static void Postfix(LocoHUDProvider __instance, LocoIndicatorReader ir) { FatigueSpeedometer.Unbind(__instance, ir); }
    }
    [HarmonyPatch(typeof(LocoHUDProvider), "SpeedometerUpdated")]
    internal static class PersonalSpeedHudNumberPatch
    {
        private static void Prefix(ref float value) { value = FatigueSpeedometer.Display(value); }
    }
    [HarmonyPatch(typeof(LocoHUDProvider), "SpeedometerVisualUpdated")]
    internal static class PersonalSpeedHudBarPatch
    {
        private static void Prefix(LocoHUDProvider __instance, ref float level)
        { level = FatigueSpeedometer.DisplayHudLevel(__instance, level); }
    }
    [HarmonyPatch(typeof(GadgetDigitalSpeedometerLOD), "Update")]
    internal static class PersonalDigitalSpeedometerPatch
    {
        // Adjust only the float being formatted by this display, before rounding, never TryReadPort globally.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var round = AccessTools.Method(typeof(Mathf), "RoundToInt", new[] { typeof(float) });
            var display = AccessTools.Method(typeof(FatigueSpeedometer), nameof(FatigueSpeedometer.Display));
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(round))
                {
                    var inserted = new CodeInstruction(OpCodes.Call, display);
                    inserted.MoveLabelsFrom(instruction);
                    yield return inserted;
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Unsupported digital speedometer formatting API.");
        }
    }
}
