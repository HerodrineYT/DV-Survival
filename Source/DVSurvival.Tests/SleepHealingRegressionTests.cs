using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public class SleepHealingRegressionTests
    {
        [Theory]
        [InlineData(35.5f)]
        [InlineData(35.79f)]
        [InlineData(35.8f)]
        public void ColdBedSleepHealsDespiteSlowTemperatureSetting(float bodyTemperature)
        {
            var state = new SurvivalState { Health = 70f, BodyTemperatureCelsius = bodyTemperature };
            SurvivalSimulator.Sleep(state, 10f,
                new SurvivalEnvironment { AmbientTemperatureCelsius = -22f, Exposure = 1f },
                new SurvivalTuning { NeedsRateMultiplier = .5f, HungerHoursFromFull = 24f,
                    HydrationHoursFromFull = 18f, ThermalTimeConstantHours = 6f });
            Assert.Equal(85f, state.Health, 3);
            Assert.InRange(state.BodyTemperatureCelsius, 35.5f, 35.8f);
        }

        [Fact]
        public void BecomingHungryAtWakeDoesNotCancelEarlierHealing()
        {
            var state = new SurvivalState { Health = 60f, Hunger = 20f };
            var split = state.Clone();
            var environment = new SurvivalEnvironment { AmbientTemperatureCelsius = 22f };
            var tuning = new SurvivalTuning { HungerHoursFromFull = 24f, HydrationHoursFromFull = 36f };
            SurvivalSimulator.Sleep(state, 4f, environment, tuning);
            for (var hour = 0; hour < 4; hour++)
                SurvivalSimulator.Sleep(split, 1f, environment, tuning);
            Assert.True(state.Hunger < 15f);
            Assert.True(state.Health > 60f);
            Assert.Equal(split.Health, state.Health, 4);
        }

        [Fact]
        public void HealingIsIndependentOfDamageMultiplierAndDoesNotAffectOtherPlayers()
        {
            var state = new SurvivalState { Health = 70f, BodyTemperatureCelsius = 35.5f };
            var other = state.Clone();
            SurvivalSimulator.Sleep(state, 2f,
                new SurvivalEnvironment { AmbientTemperatureCelsius = -22f },
                new SurvivalTuning { DamageMultiplier = 0f });
            Assert.Equal(73f, state.Health, 4);
            Assert.Equal(70f, other.Health);
        }
    }
}
