using System;
using System.Reflection;

namespace DVSurvival.Mod
{
    internal sealed class DvSeasonsTemperatureProvider
    {
        private FieldInfo runtimeField;
        private PropertyInfo currentStateProperty;
        private PropertyInfo temperatureProperty;
        private PropertyInfo seasonProperty;
        private bool probeAttempted;
        private PropertyInfo weatherTemperatureProperty;
        public bool IncludesWeather { get; private set; }

        public bool TryGetTemperature(out float temperature)
        {
            temperature = 0f;
            try
            {
                if (!probeAttempted) Probe();
                if (runtimeField == null || currentStateProperty == null || temperatureProperty == null)
                    return false;
                var runtime = runtimeField.GetValue(null);
                if (runtime == null) return false;
                IncludesWeather = weatherTemperatureProperty != null &&
                    (bool)weatherTemperatureProperty.GetValue(runtime, null);
                var state = currentStateProperty.GetValue(runtime, null);
                if (state == null) return false;
                var value = temperatureProperty.GetValue(state, null);
                if (!(value is float)) return false;
                temperature = (float)value;
                return !float.IsNaN(temperature) && !float.IsInfinity(temperature) &&
                    temperature >= DVSurvival.Core.SurvivalEnvironment.MinimumAirTemperature &&
                    temperature <= DVSurvival.Core.SurvivalEnvironment.MaximumAirTemperature;
            }
            catch
            {
                Reset();
                return false;
            }
        }

        public bool TryGetWinter(out bool winter)
        {
            winter = false;
            try
            {
                if (!probeAttempted) Probe();
                if (runtimeField == null || currentStateProperty == null || seasonProperty == null) return false;
                var runtime = runtimeField.GetValue(null);
                var state = runtime == null ? null : currentStateProperty.GetValue(runtime, null);
                if (state == null) return false;
                winter = string.Equals(Convert.ToString(seasonProperty.GetValue(state, null)), "Winter", StringComparison.Ordinal);
                return true;
            }
            catch { return false; }
        }

        public void Reset()
        {
            runtimeField = null;
            currentStateProperty = null;
            temperatureProperty = null;
            seasonProperty = null;
            probeAttempted = false;
            IncludesWeather = false;
            weatherTemperatureProperty = null;
        }

        private void Probe()
        {
            probeAttempted = true;
            var mainType = Type.GetType("DVSeasons.Mod.Main, DVSeasons", false);
            if (mainType == null) return;
            runtimeField = mainType.GetField("runtime", BindingFlags.NonPublic | BindingFlags.Static);
            if (runtimeField == null) return;
            weatherTemperatureProperty = runtimeField.FieldType.GetProperty("HasWeatherTemperature");
            currentStateProperty = runtimeField.FieldType.GetProperty("CurrentState",
                BindingFlags.Public | BindingFlags.Instance);
            if (currentStateProperty == null) return;
            temperatureProperty = currentStateProperty.PropertyType.GetProperty("TemperatureCelsius",
                BindingFlags.Public | BindingFlags.Instance);
            seasonProperty = currentStateProperty.PropertyType.GetProperty("Current",
                BindingFlags.Public | BindingFlags.Instance);
        }
    }
}
