using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public class SleepOnsetRegressionTests
    {
        private static SurvivalEnvironment Room() => new SurvivalEnvironment { AmbientTemperatureCelsius = 22f };
        private static SurvivalTuning Tuning() => new SurvivalTuning { DamageMultiplier = 0f };

        [Theory]
        [InlineData(42.6988712)] // Player.log: used to wake with exactly 43.73589% rest.
        [InlineData(40)]
        [InlineData(47.999)]
        [InlineData(48)]
        [InlineData(71.99)]
        public void EightHoursRestoreFullRestWithoutNewExhaustion(double awakeHours)
        {
            var state = new SurvivalState { Rest = 0f, LowRestGameHours = awakeHours };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Sleep(state, 8, Room(), Tuning()));
            Assert.Equal(100f, state.Rest);
            Assert.Equal(0d, state.LowRestGameHours);
            Assert.Equal(0f, state.ExhaustionHoursRemaining);
            Assert.True(state.Hunger > 10f);
            Assert.True(state.Hydration > 10f);
        }

        [Theory]
        [InlineData(47.9)]
        [InlineData(71.9)]
        [InlineData(95.9)]
        public void NapPausesButDoesNotResetOrEscalateDeprivation(double awakeHours)
        {
            var state = new SurvivalState { Rest = 10f, LowRestGameHours = awakeHours };
            var divisor = SleepDeprivationEffects.DisplaySpeedDivisor(state);
            SurvivalSimulator.Sleep(state, 1, Room(), Tuning());
            Assert.Equal(22.5f, state.Rest);
            Assert.Equal(awakeHours, state.LowRestGameHours);
            Assert.Equal(divisor, SleepDeprivationEffects.DisplaySpeedDivisor(state));
            Assert.Equal(0f, state.ExhaustionHoursRemaining);
            var awake = Room();
            awake.GameHours = .2f;
            SurvivalSimulator.Advance(state, awake, Tuning());
            Assert.Equal(awakeHours + .2f, state.LowRestGameHours, 5);
            if (awakeHours < 48) Assert.True(state.ExhaustionHoursRemaining > 0f);
            else Assert.True(SleepDeprivationEffects.DisplaySpeedDivisor(state) > divisor);
        }

        [Fact]
        public void ExistingAcceleratedThirstExpiresDuringSleepWithoutExtendingDeprivation()
        {
            var state = new SurvivalState { Rest = 10f, LowRestGameHours = 50.9, ExhaustionHoursRemaining = .1f };
            SurvivalSimulator.Sleep(state, 1, Room(), Tuning());
            Assert.Equal(100f - 100f / 12f * .58f * (.1f * 3f + .9f), state.Hydration, 3);
            Assert.Equal(0f, state.ExhaustionHoursRemaining);
            Assert.Equal(50.9, state.LowRestGameHours);
        }

        [Fact]
        public void PreviewMatchesConfirmedSleepAndDoesNotChangeOtherPlayers()
        {
            var state = new SurvivalState { Rest = 10f, LowRestGameHours = 42.6988712 };
            var other = state.Clone();
            var preview = new SleepHudPreview();
            preview.Begin(7, state, 8, Room(), Tuning(), 0);
            Assert.Equal(100f, preview.Get(state, 0).Rest);
            Assert.Equal(10f, state.Rest);
            SurvivalSimulator.Sleep(state, 8, Room(), Tuning());
            Assert.Equal(preview.Get(state, 1).Rest, state.Rest);
            Assert.Equal(10f, other.Rest);
            Assert.Equal(42.6988712, other.LowRestGameHours);
        }
    }
}
