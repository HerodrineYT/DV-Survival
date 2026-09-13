using System.IO;
using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class SurvivalSimulatorTests
    {
        private static SurvivalEnvironment Mild(float hours)
        {
            return new SurvivalEnvironment
            {
                GameHours = hours,
                AmbientTemperatureCelsius = 20f,
                Exposure = 0.2f,
                Activity = 0.2f
            };
        }

        [Fact]
        public void AdvanceChangesOnlyThePassedPlayerState()
        {
            var tuning = new SurvivalTuning();
            var first = SurvivalState.CreateDefault(tuning);
            var second = SurvivalState.CreateDefault(tuning);

            SurvivalSimulator.Advance(first, Mild(2f), tuning);

            Assert.True(first.Hunger < 100f);
            Assert.True(first.Hydration < 100f);
            Assert.Equal(100f, second.Hunger);
            Assert.Equal(100f, second.Hydration);
            Assert.Equal(100f, second.Rest);
        }

        [Fact]
        public void SleepRestoresOnlySleeperAndStillConsumesFoodAndWater()
        {
            var tuning = new SurvivalTuning();
            var sleeper = SurvivalState.CreateDefault(tuning);
            var awake = SurvivalState.CreateDefault(tuning);
            sleeper.Rest = 10f;
            sleeper.Hunger = 80f;
            sleeper.Hydration = 80f;
            awake.Rest = 10f;

            var result = SurvivalSimulator.Sleep(sleeper, 8f, Mild(0f), tuning);

            Assert.Equal(SurvivalResultCode.Success, result);
            Assert.True(sleeper.Rest > 95f);
            Assert.True(sleeper.Hunger < 80f);
            Assert.True(sleeper.Hydration < 80f);
            Assert.Equal(10f, awake.Rest);
        }

        [Fact]
        public void ColdRainLowersCoreTemperatureWhileCabShelterProtectsIt()
        {
            var tuning = new SurvivalTuning();
            var exposed = SurvivalState.CreateDefault(tuning);
            var sheltered = SurvivalState.CreateDefault(tuning);
            var storm = new SurvivalEnvironment
            {
                GameHours = 4f,
                AmbientTemperatureCelsius = -8f,
                RainIntensity = 1f,
                WindSpeedMetersPerSecond = 14f,
                Exposure = 1f
            };
            var cab = storm.Clone();
            cab.Exposure = 0.1f;
            cab.ShelterWarmthCelsius = 8f;

            SurvivalSimulator.Advance(exposed, storm, tuning);
            SurvivalSimulator.Advance(sheltered, cab, tuning);

            Assert.True(exposed.BodyTemperatureCelsius < 35.5f);
            Assert.True(sheltered.BodyTemperatureCelsius > exposed.BodyTemperatureCelsius + 1f);
        }

        [Fact]
        public void ProvisionsAreCountedAndCannotBeWastedAtFullNeed()
        {
            var tuning = new SurvivalTuning();
            var state = SurvivalState.CreateDefault(tuning);
            var originalMeals = state.Meals;

            Assert.Equal(SurvivalResultCode.NotNeeded,
                SurvivalSimulator.Consume(state, ProvisionKind.Meal, tuning));
            Assert.Equal(originalMeals, state.Meals);

            state.Hunger = 30f;
            Assert.Equal(SurvivalResultCode.Success,
                SurvivalSimulator.Consume(state, ProvisionKind.Meal, tuning));
            Assert.Equal(originalMeals - 1, state.Meals);
            Assert.True(state.Hunger > 70f);
        }

        [Fact]
        public void CriticalDehydrationEventuallyCausesARecoverableCollapse()
        {
            var tuning = new SurvivalTuning();
            var state = SurvivalState.CreateDefault(tuning);
            state.Hydration = 0f;
            state.Health = 5f;

            SurvivalSimulator.Advance(state, Mild(1f), tuning);

            Assert.Equal(1u, state.CollapseCount);
            Assert.Equal(10f, state.Health);
            Assert.True(state.Hydration >= 10f);
        }

        [Fact]
        public void FallDamageStartsOnlyAboveOneAndHalfPlayerHeights()
        {
            const float height = 1.8f;
            Assert.Equal(0f, SurvivalSimulator.CalculateTraumaDamage(
                TraumaKind.Fall, height * 1.5f, height));
            Assert.True(SurvivalSimulator.CalculateTraumaDamage(
                TraumaKind.Fall, height * 1.5f + 0.5f, height) > 0f);

            var state = new SurvivalState { Health = 100f };
            var result = SurvivalSimulator.ApplyTrauma(state, TraumaKind.Fall, 5f, height,
                new SurvivalTuning());
            Assert.Equal(SurvivalResultCode.Success, result);
            Assert.True(state.Health < 100f);
        }

        [Fact]
        public void DamageMultiplierScalesTraumaAndSurvivalDamage()
        {
            var normalTuning = new SurvivalTuning { DamageMultiplier = 1f };
            var highTuning = new SurvivalTuning { DamageMultiplier = 2f };
            var normalTrauma = new SurvivalState { Health = 100f };
            var highTrauma = new SurvivalState { Health = 100f };

            SurvivalSimulator.ApplyTrauma(normalTrauma, TraumaKind.Fall, 3.5f, 1.8f,
                normalTuning);
            SurvivalSimulator.ApplyTrauma(highTrauma, TraumaKind.Fall, 3.5f, 1.8f,
                highTuning);

            Assert.Equal((100f - normalTrauma.Health) * 2f, 100f - highTrauma.Health, 3);

            var normalNeeds = SurvivalState.CreateDefault(normalTuning);
            var highNeeds = SurvivalState.CreateDefault(highTuning);
            normalNeeds.Hydration = highNeeds.Hydration = 0f;
            normalNeeds.Health = highNeeds.Health = 100f;
            SurvivalSimulator.Advance(normalNeeds, Mild(0.5f), normalTuning);
            SurvivalSimulator.Advance(highNeeds, Mild(0.5f), highTuning);

            Assert.Equal((100f - normalNeeds.Health) * 2f, 100f - highNeeds.Health, 3);
        }

        [Fact]
        public void TrainDamageStartsOnlyAboveSevenKmh()
        {
            Assert.Equal(0f, SurvivalSimulator.CalculateTraumaDamage(
                TraumaKind.TrainCollision, 7f, 1.8f));
            Assert.True(SurvivalSimulator.CalculateTraumaDamage(
                TraumaKind.TrainCollision, 7.01f, 1.8f) > 0f);
        }

        [Fact]
        public void HeatedBuildingMovesCoreTemperatureTowardNormalQuickly()
        {
            var tuning = new SurvivalTuning();
            var state = SurvivalState.CreateDefault(tuning);
            state.BodyTemperatureCelsius = 34.5f;
            state.Health = 70f;
            var building = new SurvivalEnvironment
            {
                GameHours = 1f,
                AmbientTemperatureCelsius = 22f,
                Exposure = 0.03f,
                ThermalRecoveryMultiplier = 2.5f
            };

            SurvivalSimulator.Advance(state, building, tuning);

            Assert.True(state.BodyTemperatureCelsius > 36f);
            Assert.True(state.Health >= 70f);
        }

        [Fact]
        public void ShorterThermalTimeConstantChangesBodyTemperatureFaster()
        {
            var normal = SurvivalState.CreateDefault(new SurvivalTuning());
            var fast = SurvivalState.CreateDefault(new SurvivalTuning());
            normal.BodyTemperatureCelsius = fast.BodyTemperatureCelsius = 35f;
            var environment = new SurvivalEnvironment
            {
                GameHours = 0.25f,
                AmbientTemperatureCelsius = 22f,
                Exposure = 0.03f,
                ThermalRecoveryMultiplier = 2.5f
            };

            SurvivalSimulator.Advance(normal, environment,
                new SurvivalTuning { ThermalTimeConstantHours = 1.5f });
            SurvivalSimulator.Advance(fast, environment,
                new SurvivalTuning { ThermalTimeConstantHours = 1f });

            Assert.True(fast.BodyTemperatureCelsius > normal.BodyTemperatureCelsius);
        }

        [Fact]
        public void FrostbiteAndOverheatingBothDamageHealth()
        {
            var tuning = new SurvivalTuning();
            var cold = SurvivalState.CreateDefault(tuning);
            var hot = SurvivalState.CreateDefault(tuning);
            cold.BodyTemperatureCelsius = 34.8f;
            hot.BodyTemperatureCelsius = 39.2f;
            cold.Health = hot.Health = 80f;
            var coldEnvironment = new SurvivalEnvironment
            {
                GameHours = 0.5f,
                AmbientTemperatureCelsius = -20f,
                Exposure = 1f
            };
            var hotEnvironment = new SurvivalEnvironment
            {
                GameHours = 0.5f,
                AmbientTemperatureCelsius = 55f,
                Exposure = 1f
            };

            SurvivalSimulator.Advance(cold, coldEnvironment, tuning);
            SurvivalSimulator.Advance(hot, hotEnvironment, tuning);

            Assert.True(cold.Health < 80f);
            Assert.True(hot.Health < 80f);
        }

        [Fact]
        public void BodyHeatDamageStartsAbove385AndAirHeatAbove55()
        {
            var exact = SurvivalState.CreateDefault(new SurvivalTuning());
            exact.BodyTemperatureCelsius = 38.5f;
            exact.Health = 100f;
            SurvivalSimulator.Advance(exact, new SurvivalEnvironment
            {
                GameHours = 1f, AmbientTemperatureCelsius = 22f, Exposure = 0f
            }, new SurvivalTuning { PassiveHealthRecoveryPerHour = 0f });

            var hotBody = SurvivalState.CreateDefault(new SurvivalTuning());
            hotBody.BodyTemperatureCelsius = 42f;
            hotBody.Health = 100f;
            SurvivalSimulator.Advance(hotBody, new SurvivalEnvironment
            {
                GameHours = 0.1f, AmbientTemperatureCelsius = 22f, Exposure = 0f
            }, new SurvivalTuning { PassiveHealthRecoveryPerHour = 0f });

            var hotAir = SurvivalState.CreateDefault(new SurvivalTuning());
            hotAir.Health = 100f;
            SurvivalSimulator.Advance(hotAir, new SurvivalEnvironment
            {
                GameHours = 1f, AmbientTemperatureCelsius = 56f, Exposure = 0f
            }, new SurvivalTuning { PassiveHealthRecoveryPerHour = 0f });

            Assert.Equal(100f, exact.Health, 3);
            Assert.True(hotBody.Health < 100f);
            Assert.True(hotAir.Health < 100f);
        }

        [Fact]
        public void StateCodecRoundTripsAllAuthoritativeFields()
        {
            var state = new SurvivalState
            {
                Hunger = 12.5f,
                Hydration = 23.5f,
                Rest = 34.5f,
                Health = 45.5f,
                BodyTemperatureCelsius = 35.75f,
                CaffeineHours = 1.5f,
                WarmthHours = 2.5f,
                Meals = 3,
                Water = 4,
                Coffee = 5,
                FirstAid = 6,
                HeatPacks = 7,
                Revision = 42,
                CollapseCount = 2,
                SimulatedGameHours = 123.25d
            };
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    SurvivalStateCodec.Write(writer, state);
                stream.Position = 0;
                SurvivalState copy;
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                    copy = SurvivalStateCodec.Read(reader);

                Assert.Equal(state.Hunger, copy.Hunger);
                Assert.Equal(state.BodyTemperatureCelsius, copy.BodyTemperatureCelsius);
                Assert.Equal(state.HeatPacks, copy.HeatPacks);
                Assert.Equal(state.Revision, copy.Revision);
                Assert.Equal(state.CollapseCount, copy.CollapseCount);
                Assert.Equal(state.SimulatedGameHours, copy.SimulatedGameHours);
            }
        }
    }
}
