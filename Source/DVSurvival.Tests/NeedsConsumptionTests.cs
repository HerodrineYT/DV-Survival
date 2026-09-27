using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class NeedsConsumptionTests
    {
        [Theory]
        [InlineData(0d)] [InlineData(47.99999999d)] [InlineData(71.99999999d)]
        public void DisabledNeedsDoNotDrainOrAccumulateDeprivationAcrossDays(double awakeHours)
        {
            var state = new SurvivalState { Hunger = 70, Hydration = 60, Rest = 50, LowRestGameHours = awakeHours };
            var tuning = new SurvivalTuning { DisableNeedsConsumption = true, NeedsRateMultiplier = 5 };
            for (var day = 0; day < 4; day++)
                SurvivalSimulator.Advance(state, new SurvivalEnvironment { GameHours = 24, Activity = 1 }, tuning);
            Assert.Equal(70, state.Hunger);
            Assert.Equal(60, state.Hydration);
            Assert.Equal(50, state.Rest);
            Assert.Equal(awakeHours, state.LowRestGameHours);
            Assert.Equal(96d, state.SimulatedGameHours);
        }

        [Fact]
        public void DisabledNeedsPreserveFoodAndWaterDuringSleepIncludingExhaustionWakePenalty()
        {
            var state = new SurvivalState
            {
                Hunger = 80, Hydration = 60, Rest = 20, Health = 50,
                LowRestGameHours = 60, ExhaustionHoursRemaining = 3, CoffeeUsesSinceSleep = 25
            };
            SurvivalSimulator.Sleep(state, 8, new SurvivalEnvironment { AmbientTemperatureCelsius = 22 },
                new SurvivalTuning { DisableNeedsConsumption = true });
            Assert.Equal(80, state.Hunger);
            Assert.Equal(60, state.Hydration);
            Assert.Equal(100, state.Rest);
            Assert.True(state.Health > 50);
            Assert.Equal(0, state.LowRestGameHours);
            Assert.Equal(0, state.CoffeeUsesSinceSleep);
        }

        [Fact]
        public void ExistingExhaustionCannotDrainDisabledNeedsAndExpiresNormally()
        {
            var state = new SurvivalState
            { Hunger = 50, Hydration = 60, Rest = 40, LowRestGameHours = 50, ExhaustionHoursRemaining = 3 };
            SurvivalSimulator.Advance(state, new SurvivalEnvironment { GameHours = 4, Activity = 1 },
                new SurvivalTuning { DisableNeedsConsumption = true });
            Assert.Equal(50, state.Hunger);
            Assert.Equal(60, state.Hydration);
            Assert.Equal(40, state.Rest);
            Assert.Equal(50, state.LowRestGameHours);
            Assert.Equal(0, state.ExhaustionHoursRemaining);
        }

        [Theory]
        [InlineData(-30f)] [InlineData(65f)]
        public void TemperatureAndThermalDamageContinueWhenNeedsAreDisabled(float air)
        {
            var state = new SurvivalState { Hunger = 80, Hydration = 80, Rest = 80 };
            SurvivalSimulator.Advance(state, new SurvivalEnvironment { GameHours = 1, AmbientTemperatureCelsius = air },
                new SurvivalTuning { DisableNeedsConsumption = true });
            Assert.NotEqual(37f, state.BodyTemperatureCelsius);
            Assert.True(state.Health < 100);
            Assert.Equal(80, state.Hunger);
            Assert.Equal(80, state.Hydration);
            Assert.Equal(80, state.Rest);
        }

        [Fact]
        public void AlreadyLowNeedsStillCauseDamageWithoutBeingRefilled()
        {
            var state = new SurvivalState { Hunger = 5, Hydration = 5, Rest = 5 };
            SurvivalSimulator.Advance(state, new SurvivalEnvironment { GameHours = .1f },
                new SurvivalTuning { DisableNeedsConsumption = true });
            Assert.True(state.Health < 100);
            Assert.Equal(5, state.Hunger);
            Assert.Equal(5, state.Hydration);
            Assert.Equal(5, state.Rest);
        }

        [Fact]
        public void ReenablingDrainDoesNotCatchUpDisabledDaysOrResetTheRate()
        {
            var state = new SurvivalState();
            var tuning = new SurvivalTuning { DisableNeedsConsumption = true, NeedsRateMultiplier = 2 };
            SurvivalSimulator.Advance(state, new SurvivalEnvironment { GameHours = 24 }, tuning);
            tuning.DisableNeedsConsumption = false;
            var expected = new SurvivalState();
            SurvivalSimulator.Advance(expected, new SurvivalEnvironment { GameHours = 1 }, tuning);
            SurvivalSimulator.Advance(state, new SurvivalEnvironment { GameHours = 1 }, tuning);
            Assert.Equal(expected.Hunger, state.Hunger);
            Assert.Equal(expected.Hydration, state.Hydration);
            Assert.Equal(expected.Rest, state.Rest);
            Assert.Equal(expected.LowRestGameHours, state.LowRestGameHours);
            Assert.Equal(2, tuning.NeedsRateMultiplier);
        }

        [Fact]
        public void DefaultSettingsKeepNormalConsumptionAndItemsStillRestoreDisabledNeeds()
        {
            var tuning = new SurvivalTuning();
            Assert.False(tuning.DisableNeedsConsumption);
            var state = new SurvivalState();
            SurvivalSimulator.Advance(state, new SurvivalEnvironment { GameHours = 1 }, tuning);
            Assert.True(state.Hunger < 100 && state.Hydration < 100 && state.Rest < 100);
            tuning.DisableNeedsConsumption = true;
            state.Hunger = state.Hydration = state.Rest = 40;
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.ConsumePhysical(state, ProvisionKind.Meal, tuning));
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.ConsumePhysical(state, ProvisionKind.Water, tuning));
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.ConsumePhysical(state, ProvisionKind.Coffee, tuning));
            Assert.True(state.Hunger > 40 && state.Hydration > 40 && state.Rest > 40);
        }

        [Fact]
        public void SleepHudPreviewUsesNoDrainTuningWithoutChangingConfirmedState()
        {
            var state = new SurvivalState { Hunger = 60, Hydration = 60, Rest = 30 };
            var preview = new SleepHudPreview();
            preview.Begin(1, state, 8, new SurvivalEnvironment(),
                new SurvivalTuning { DisableNeedsConsumption = true }, 0);
            Assert.Equal(60, preview.Get(state, 1).Hunger);
            Assert.Equal(60, preview.Get(state, 1).Hydration);
            Assert.Equal(100, preview.Get(state, 1).Rest);
            Assert.Equal(30, state.Rest);
        }

        [Theory]
        [InlineData(0, 25)] [InlineData(10, 25)] [InlineData(80, 80)]
        public void DeathRaisesEachNeedFloorByFifteenPointsButPreservesHigherValues(float needs, float expected)
        {
            var state = new SurvivalState
            { Health = 1, Hunger = needs, Hydration = needs, Rest = needs, FirstAidSecondsRemaining = 10 };
            SurvivalSimulator.ApplyTrauma(state, TraumaKind.Fall, 10, 1.8f, new SurvivalTuning());
            Assert.Equal(25, state.Health);
            Assert.Equal(expected, state.Hunger);
            Assert.Equal(expected, state.Hydration);
            Assert.Equal(expected, state.Rest);
            Assert.Equal(0, state.FirstAidSecondsRemaining);
            Assert.Equal(1u, state.CollapseCount);
            Assert.InRange(state.BodyTemperatureCelsius, 35.5f, 38.5f);
        }
    }
}
