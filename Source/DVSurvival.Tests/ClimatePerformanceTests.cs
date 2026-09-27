using System;
using System.Globalization;
using DVSurvival.Core;
using DVSurvival.Mod;
using Xunit;

namespace DVSurvival.Mod
{
    // Only localization is substituted; tests call the production lazy formatter.
    internal static class ModLocalization
    {
        [ThreadStatic] internal static bool IsRussian;
        [ThreadStatic] internal static int Calls;
        internal static string Text(string ru, string en) { Calls++; return IsRussian ? ru : en; }
        internal static string Number(float value, string format)
        { Calls++; return value.ToString(format, IsRussian ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture); }
    }
}

namespace DVSurvival.Tests
{
    public class ClimatePerformanceTests
    {
        [Fact]
        public void MissingControlsBackOffAfterThreeAttemptsButCanStillRecover()
        {
            var retry = new DiscoveryRetry();
            retry.RecordAttempt(0); Assert.Equal(3f, retry.NextAttempt);
            retry.RecordAttempt(3); Assert.Equal(6f, retry.NextAttempt);
            retry.RecordAttempt(6); Assert.Equal(36f, retry.NextAttempt);
            retry.RecordAttempt(36); Assert.Equal(66f, retry.NextAttempt);
            Assert.Equal(3, retry.Attempts);
            retry.Reset();
            Assert.Equal(0f, retry.NextAttempt);
            retry.RecordAttempt(40); Assert.Equal(43f, retry.NextAttempt);
        }

        [Fact]
        public void SamplingWithoutSettingsWindowDoesNotFormatStatus()
        {
            ModLocalization.IsRussian = false; ModLocalization.Calls = 0;
            var status = new CabinDiagnosticStatus();
            for (var i = 0; i < 1000; i++)
            {
                status.Clear();
                status.Capture(true, 4, false, true, true, false, true, -20f);
            }
            Assert.Equal(0, ModLocalization.Calls);
            var text = status.Text;
            Assert.Equal("Cabin: openings open [4]; heater on; fan off; engine on; outdoors -20.0 °C", text);
            var calls = ModLocalization.Calls;
            Assert.Same(text, status.Text);
            Assert.Equal(calls, ModLocalization.Calls);
            status.Clear();
            Assert.Equal(string.Empty, status.Text);
        }

        [Fact]
        public void StatusRefreshesAfterLanguageAndSampleChanges()
        {
            ModLocalization.IsRussian = false;
            var status = new CabinDiagnosticStatus();
            status.Capture(false, 2, true, false, false, true, false, 12.5f);
            Assert.Equal("Cabin: openings closed [2]; heater off; fan on; outdoors 12.5 °C", status.Text);
            ModLocalization.IsRussian = true;
            Assert.Equal("Кабина: проёмы закрыты [2]; отопитель выключен; вентилятор включён; улица 12,5 °C", status.Text);
            status.Capture(true, 1, false, false, false, false, true, 0f);
            Assert.Equal("Кабина: проёмы открыты [1]; двигатель включён; улица 0,0 °C", status.Text);
            ModLocalization.IsRussian = false;
        }
    }
}
