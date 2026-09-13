using System;
using DVSurvival.Core;
using DVSurvival.Multiplayer;
using Xunit;

// The actual shipping getter is source-linked. Only MPAPI session/transport state is replaced.
namespace DVSurvival.Multiplayer
{
    public sealed partial class MultiplayerSurvivalBridge
    {
        public bool IsSessionActive { get; set; }
        public bool IsAuthority { get; set; }
        private sealed class SleepTestServer { public int PlayerCount; }
        private SleepTestServer server;
        public void ConfigureSleepTest(bool authority, int players, Func<bool> timeSetting)
        {
            IsSessionActive = true;
            IsAuthority = authority;
            server = new SleepTestServer { PlayerCount = players };
            nativeTimeAdvanceEnabled = timeSetting;
        }
    }
}

namespace DVSurvival.Tests
{
    public sealed class NativeSleepModeRegressionTests
    {
        [Theory]
        [InlineData(true, 1)]
        [InlineData(true, 2)]
        [InlineData(false, 2)]
        public void ActualBridgeAcceptsTenHourSleepWithDisabledTimeForSoloHostAndPeers(bool host, int players)
        {
            var bridge = new MultiplayerSurvivalBridge();
            bridge.ConfigureSleepTest(host, players, () => false);
            Assert.True(bridge.IsNativeSleepTimeSuppressed);
            var mode = PersonalSleepValidator.IsNoAdvanceMode(bridge.IsSessionActive,
                bridge.IsNativeSleepTimeSuppressed, false);
            var request = new SurvivalActionRequest { Action = SurvivalActionKind.SleepWithoutTimeAdvance, Amount = 10f };
            Assert.True(PersonalSleepValidator.IsValid(bridge.IsSessionActive, mode, true, request));
            var state = new SurvivalState { Rest = 0f, LowRestGameHours = 100d, CoffeeUsesSinceSleep = 5 };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Sleep(state, request.Amount,
                new SurvivalEnvironment { AmbientTemperatureCelsius = 22f }, new SurvivalTuning { DamageMultiplier = 0f }));
            Assert.Equal(100f, state.Rest);
            Assert.Equal(0d, state.LowRestGameHours);
            Assert.Equal(0, state.CoffeeUsesSinceSleep);
        }

        [Fact]
        public void LiveTimeSettingIsReadAgainAfterChange()
        {
            var enabled = true;
            var bridge = new MultiplayerSurvivalBridge();
            bridge.ConfigureSleepTest(true, 1, () => enabled);
            Assert.False(bridge.IsNativeSleepTimeSuppressed);
            enabled = false;
            Assert.True(bridge.IsNativeSleepTimeSuppressed);
            bridge.IsSessionActive = false;
            Assert.False(bridge.IsNativeSleepTimeSuppressed);
        }

        [Fact]
        public void UnavailableSettingDoesNotInventDisabledTime()
        {
            var bridge = new MultiplayerSurvivalBridge();
            bridge.ConfigureSleepTest(true, 1, null);
            Assert.False(bridge.IsNativeSleepTimeSuppressed);
            bridge.ConfigureSleepTest(true, 1, () => { throw new InvalidOperationException(); });
            Assert.False(bridge.IsNativeSleepTimeSuppressed);
        }

        [Theory]
        [InlineData(true, false, true, true)]
        [InlineData(true, false, false, false)]
        [InlineData(true, true, false, true)]
        [InlineData(false, true, true, false)]
        public void ClockOverrideAlsoAllowsMpSleep(bool active, bool disabled, bool overridden, bool expected)
        {
            Assert.Equal(expected, PersonalSleepValidator.IsNoAdvanceMode(active, disabled, overridden));
        }
    }
}
