using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class SleepDeprivationEffectsTests
    {
        private static SurvivalTuning Tuning() { return new SurvivalTuning { DamageMultiplier = 0, PassiveHealthRecoveryPerHour = 0 }; }
        private static SurvivalEnvironment Room(float hours = 0) { return new SurvivalEnvironment { AmbientTemperatureCelsius = 22, Exposure = 0, GameHours = hours }; }
        private static void Advance(SurvivalState s, float hours)
        {
            while (hours > 0) { var step = Math.Min(hours, 24); SurvivalSimulator.Advance(s, Room(step), Tuning()); hours -= step; }
        }
        [Fact]
        public void ExactlyTwoDaysStartsConsequencesOnlyOnceAndThreeDaysStartsSpeedDistortion()
        {
            var s = new SurvivalState { Rest = 70, Health = 65 };
            Advance(s, 47.5f); Assert.Equal(0, s.ExhaustionHoursRemaining);
            s.Hunger = s.Hydration = 80; s.Rest = 60;
            Advance(s, .5f); Assert.Equal(48d, s.LowRestGameHours); Assert.Equal(3, s.ExhaustionHoursRemaining);
            Assert.Equal(10, s.Hunger); Assert.Equal(10, s.Hydration); Assert.Equal(10, s.Rest);
            Assert.Equal(65, s.Health); Assert.Equal(1, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
            Advance(s, 3); Assert.Equal(0, s.ExhaustionHoursRemaining);
            s.Hunger = s.Hydration = 90; s.Rest = 65;
            Advance(s, 1); Assert.True(s.Hunger > 10); Assert.True(s.Hydration > 10);
            Advance(s, 19.5f); Assert.Equal(1, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
            Advance(s, .5f); Assert.Equal(72, s.LowRestGameHours);
            Assert.Equal(60, 90 * SleepDeprivationEffects.DisplaySpeedMultiplier(s), 4);
            Advance(s, 24); Assert.Equal(96, s.LowRestGameHours);
            Assert.Equal(45, 90 * SleepDeprivationEffects.DisplaySpeedMultiplier(s), 4);
        }
        [Fact]
        public void OnsetNeverRaisesAlreadyLowerNeedsOrDamagesHealthDirectly()
        {
            var s = new SurvivalState { Rest = 2, Hunger = 4, Hydration = 3, Health = 8,
                BodyTemperatureCelsius = 34, LowRestGameHours = 47.5, CaffeineHours = 2, WarmthHours = 2 };
            SleepDeprivationEffects.AdvanceClock(s, .5f,
                new SurvivalEnvironment { IsWinter = true, AmbientTemperatureCelsius = -10 });
            Assert.Equal(4, s.Hunger); Assert.Equal(3, s.Hydration); Assert.Equal(2, s.Rest);
            Assert.Equal(34, s.BodyTemperatureCelsius); Assert.Equal(8, s.Health);
            Assert.Equal(0, s.CaffeineHours); Assert.Equal(0, s.WarmthHours);
        }
        [Theory]
        [InlineData(true,-10,35)] [InlineData(true,22,37)] [InlineData(false,-10,37)]
        public void ColdCapRequiresWinterAndSubzeroLocalAir(bool winter,float air,float expected)
        {
            var s = new SurvivalState { Rest = 50, LowRestGameHours = 47 };
            SleepDeprivationEffects.AdvanceClock(s, 1,
                new SurvivalEnvironment { IsWinter = winter, AmbientTemperatureCelsius = air });
            Assert.Equal(expected, s.BodyTemperatureCelsius);
        }
        [Fact]
        public void RestValueAloneNeverResetsSleepDeprivation()
        {
            var s = new SurvivalState { Rest = 70, LowRestGameHours = 144, ExhaustionHoursRemaining = 1 };
            SleepDeprivationEffects.RecoverAfterFullSleep(s, 0); Assert.Equal(144, s.LowRestGameHours);
            s.Rest = 100f; SleepDeprivationEffects.RecoverAfterFullSleep(s, 7.99f);
            Assert.Equal(144, s.LowRestGameHours); Assert.Equal(1, s.ExhaustionHoursRemaining);
            Assert.Equal(1f / 3f, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
        }
        [Fact]
        public void CalendarTimeCountsEvenWhenRestStartsAboveSeventy()
        {
            var s = new SurvivalState { Rest = 80 };
            Advance(s, 3);
            Assert.Equal(3d, s.LowRestGameHours, 5);
            Assert.Equal(65, s.Rest, 4);
        }
        [Fact]
        public void HighRestDoesNotPauseAnExistingCalendarStreak()
        {
            var s = new SurvivalState { Rest = 71, LowRestGameHours = 47.9 };
            Advance(s, 1);
            Assert.Equal(48.9d, s.LowRestGameHours, 5);
            Assert.InRange(s.ExhaustionHoursRemaining, 2.09f, 2.11f);
        }
        [Fact]
        public void CoffeeAboveThresholdDoesNotClearSleepDeprivationOrDistortion()
        {
            var s = new SurvivalState { Rest = 60, LowRestGameHours = 120 };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.ConsumePhysical(s, ProvisionKind.Coffee, Tuning()));
            Assert.Equal(76, s.Rest); Assert.Equal(120, s.LowRestGameHours);
            Assert.Equal(1f / 2.5f, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
            Advance(s, 1);
            Assert.Equal(121, s.LowRestGameHours);
            Assert.Equal(1f / 2.5f, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
        }
        [Fact]
        public void ShortSleepBelowThresholdDoesNotResetCounter()
        {
            var s = new SurvivalState { Rest = 40, LowRestGameHours = 20 };
            SurvivalSimulator.Sleep(s, 1, Room(), Tuning());
            Assert.Equal(52.5f, s.Rest); Assert.Equal(21d, s.LowRestGameHours);
        }
        [Fact]
        public void ShortSleepCrossingAboveSeventyDoesNotRemoveHallucinations()
        {
            var s = new SurvivalState { Rest = 60, LowRestGameHours = 120 };
            SurvivalSimulator.Sleep(s, 1, Room(), Tuning());
            Assert.Equal(72.5f, s.Rest); Assert.Equal(121, s.LowRestGameHours, 5);
            Assert.Equal(1f / 2.5f, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
        }
        [Theory]
        [InlineData(0.1f)]
        [InlineData(7.99f)]
        public void SleepShorterThanEightHoursNeverClearsExistingEffects(float sleepHours)
        {
            var s = new SurvivalState { Rest = 80, LowRestGameHours = 144,
                ExhaustionHoursRemaining = 2f };
            SurvivalSimulator.Sleep(s, sleepHours, Room(), Tuning());
            Assert.Equal(144, s.LowRestGameHours);
            Assert.True(SleepDeprivationEffects.DisplaySpeedDivisor(s) > 1f);
        }
        [Fact]
        public void OneCompletedEightHourSleepClearsAllSleepDeprivationEffects()
        {
            var s = new SurvivalState { Rest = 10, Hydration = 80, LowRestGameHours = 144,
                ExhaustionHoursRemaining = 2f };
            Assert.Equal(SurvivalResultCode.Success,
                SurvivalSimulator.Sleep(s, SleepDeprivationEffects.FullSleepRecoveryHours,
                    Room(), Tuning()));
            Assert.Equal(0, s.LowRestGameHours);
            Assert.Equal(0, s.ExhaustionHoursRemaining);
            Assert.Equal(1, SleepDeprivationEffects.DisplaySpeedDivisor(s));
        }
        [Fact]
        public void RejectedSleepRequestCannotClearSleepDeprivation()
        {
            var s = new SurvivalState { Rest = 100, LowRestGameHours = 144,
                ExhaustionHoursRemaining = 2f };
            Assert.Equal(SurvivalResultCode.InvalidRequest,
                SurvivalSimulator.Sleep(s, 0f, Room(), Tuning()));
            Assert.Equal(144, s.LowRestGameHours);
            Assert.Equal(2f, s.ExhaustionHoursRemaining);
            Assert.Equal(3f, SleepDeprivationEffects.DisplaySpeedDivisor(s));
        }
        [Fact]
        public void HydrationLossIsTripledForThreeHoursOnly()
        {
            var sober = new SurvivalState { Rest = 60 };
            var exhausted = new SurvivalState { Rest = 60, LowRestGameHours = 48, ExhaustionHoursRemaining = 3 };
            Advance(sober, .5f); Advance(exhausted, .5f);
            Assert.Equal((100-sober.Hydration)*3, 100-exhausted.Hydration, 3);
            Assert.Equal(sober.Rest, exhausted.Rest); Assert.Equal(sober.Hunger, exhausted.Hunger);
            Advance(exhausted, 2.5f); Assert.Equal(0, exhausted.ExhaustionHoursRemaining);
            exhausted.Hydration = sober.Hydration = 100;
            Advance(sober, .25f); Advance(exhausted, .25f); Assert.Equal(sober.Hydration, exhausted.Hydration);
        }
        [Fact]
        public void LongTickSplitsAtTheEndOfAcceleratedThirst()
        {
            var s = new SurvivalState { Rest = 60, LowRestGameHours = 50.75, ExhaustionHoursRemaining = .25f };
            Advance(s, 1);
            Assert.Equal(100 - 100f/12*.82f*(.25f*3+.75f), s.Hydration, 3);
            Assert.Equal(51.75, s.LowRestGameHours);
        }
        [Fact]
        public void WakePenaltyIsRetainedWhenSleepStartsInFirstTwoHoursEvenIfRestRecovers()
        {
            var s = new SurvivalState { Rest = 60, LowRestGameHours = 48.5, ExhaustionHoursRemaining = 2.5f };
            SurvivalSimulator.Sleep(s, 4, Room(), Tuning());
            Assert.True(s.Hydration <= 10); Assert.Equal(52.5, s.LowRestGameHours, 5);
        }
        [Fact]
        public void SleepAfterWakePenaltyWindowDoesNotCapHydration()
        {
            var s = new SurvivalState { Rest = 60, LowRestGameHours = 50.1, ExhaustionHoursRemaining = .9f };
            SurvivalSimulator.Sleep(s, .1f, Room(), Tuning()); Assert.True(s.Hydration > 90);
        }
        [Fact]
        public void WakePenaltyNeverIncreasesAlreadyLowHydration()
        {
            var s = new SurvivalState { Rest = 10, Hydration = 3, LowRestGameHours = 48, ExhaustionHoursRemaining = 3 };
            SurvivalSimulator.Sleep(s, .1f, Room(), Tuning()); Assert.InRange(s.Hydration, 0, 3);
        }
        [Fact]
        public void SleepCanCrossTheTwoDayBoundaryIfRestNeverExceedsSeventy()
        {
            var s = new SurvivalState { Rest = 0, LowRestGameHours = 47.5 };
            SurvivalSimulator.Sleep(s, 1, Room(), Tuning());
            Assert.Equal(48.5, s.LowRestGameHours); Assert.Equal(2.5f, s.ExhaustionHoursRemaining);
            Assert.True(s.Hydration <= 10);
        }
        [Fact]
        public void CounterUsesEveryGameHourNotRestNeedsMultiplierOrRealSeconds()
        {
            var s = new SurvivalState { Rest = 100 };
            var tuning = Tuning(); tuning.NeedsRateMultiplier = 3;
            SurvivalSimulator.Advance(s, Room(.5f), tuning, 30f); Assert.Equal(.5d, s.LowRestGameHours);
        }
        [Fact]
        public void PlayersHaveIndependentCountersAndRecovery()
        {
            var a = new SurvivalState { Rest = 60, LowRestGameHours = 143.5 };
            var b = new SurvivalState { Rest = 60, LowRestGameHours = 23 };
            Advance(a, .5f); Assert.Equal(3f, SleepDeprivationEffects.DisplaySpeedDivisor(a));
            Assert.Equal(1, SleepDeprivationEffects.DisplaySpeedMultiplier(b)); Assert.Equal(23, b.LowRestGameHours);
            SurvivalSimulator.Sleep(b, 8, Room(), Tuning()); Assert.Equal(144, a.LowRestGameHours);
            Assert.Equal(0, b.LowRestGameHours);
        }
        [Fact]
        public void CloneBinaryAndJsonKeepNewTimersButOldAlcoholSaveDoesNotCarryEffects()
        {
            var s = new SurvivalState { Rest = 10, LowRestGameHours = 126.75, ExhaustionHoursRemaining = 2.25f };
            Assert.Equal(s.LowRestGameHours, s.Clone().LowRestGameHours);
            using (var stream = new MemoryStream())
            {
                SurvivalStateCodec.Write(new BinaryWriter(stream), s); stream.Position = 0;
                var copy = SurvivalStateCodec.Read(new BinaryReader(stream));
                Assert.Equal(126.75, copy.LowRestGameHours); Assert.Equal(2.25f, copy.ExhaustionHoursRemaining);
            }
            var options = new JsonSerializerOptions { IncludeFields = true };
            var restored = JsonSerializer.Deserialize<SurvivalState>(JsonSerializer.Serialize(s, options), options);
            Assert.Equal(126.75, restored.LowRestGameHours);
            var old = JsonSerializer.Deserialize<SurvivalState>("{\"Health\":45,\"Rest\":10,\"AlcoholBottles\":3,\"HangoverHours\":2}", options);
            Assert.Equal(45, old.Health); Assert.Equal(10, old.Rest); Assert.Equal(0, old.LowRestGameHours);
            Assert.Equal(0, old.ExhaustionHoursRemaining); Assert.True(old.IsValid());
        }
        [Fact]
        public void ManySmallTicksReachMaximumDistortionWithoutRestartingExhaustion()
        {
            var s = new SurvivalState { Rest = 70, Health = 65 };
            for (int i = 0; i < 14401; i++) Advance(s, .01f);
            Assert.Equal(SleepDeprivationEffects.MaximumLowRestHours, s.LowRestGameHours);
            Assert.Equal(0, s.ExhaustionHoursRemaining);
            Assert.Equal(SleepDeprivationEffects.MaximumSpeedDivisor,
                SleepDeprivationEffects.DisplaySpeedDivisor(s));
            Assert.Equal(1f / 3f, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
        }
        [Theory]
        [InlineData(0, 1)]
        [InlineData(71.999, 1)]
        [InlineData(72, 1.5)]
        [InlineData(95.999, 1.5)]
        [InlineData(96, 2)]
        [InlineData(119.999, 2)]
        [InlineData(120, 2.5)]
        [InlineData(143.999, 2.5)]
        [InlineData(144, 3)]
        [InlineData(240, 3)]
        public void SpeedDivisorIncreasesEveryDayAfterHallucinationsAndIsCapped(
            double hours, float expectedDivisor)
        {
            var s = new SurvivalState { Rest = 60, LowRestGameHours = hours };
            Assert.Equal(expectedDivisor, SleepDeprivationEffects.DisplaySpeedDivisor(s));
            Assert.Equal(1f / expectedDivisor,
                SleepDeprivationEffects.DisplaySpeedMultiplier(s), 6);
        }
        [Fact]
        public void HighCurrentRestDoesNotHideStoredDistortion()
        {
            var s = new SurvivalState { Rest = 70.01f, LowRestGameHours = 144 };
            Assert.Equal(3f, SleepDeprivationEffects.DisplaySpeedDivisor(s));
            Assert.Equal(1f / 3f, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
        }
        [Theory]
        [InlineData(144, true, 144)]
        [InlineData(144.001, false, 144)]
        public void StateValidationAndClampUseExtendedLowRestLimit(
            double hours, bool initiallyValid, double expectedAfterClamp)
        {
            var s = new SurvivalState { LowRestGameHours = hours };
            Assert.Equal(initiallyValid, s.IsValid());
            s.Clamp();
            Assert.True(s.IsValid());
            Assert.Equal(expectedAfterClamp, s.LowRestGameHours);
        }
        [Fact]
        public void SleepingToExactlySeventyDoesNotResetTheStreak()
        {
            var s = new SurvivalState { Rest = 57.5f, LowRestGameHours = 20 };
            SurvivalSimulator.Sleep(s, 1, Room(), Tuning());
            Assert.Equal(70, s.Rest); Assert.Equal(21, s.LowRestGameHours);
        }
        [Fact]
        public void RemovedBottleKindCannotBeConsumedOrRecorded()
        {
            var state = new SurvivalState(); var ledger = new HashSet<string>();
            Assert.Equal(SurvivalResultCode.InvalidRequest, PhysicalItemLedger.Consume(ledger, Guid.NewGuid().ToString("D"), state, (ProvisionKind)6, Tuning()));
            Assert.Empty(ledger); Assert.Equal(100, state.Hydration);
        }
        [Theory]
        [InlineData(39.9f,100,100,100,false)] [InlineData(100,24.9f,100,100,false)]
        [InlineData(100,100,29.9f,100,false)] [InlineData(100,100,100,29.9f,false)]
        [InlineData(40,25,30,30,true)]
        public void ExistingRunThresholdsArePreserved(float health,float hunger,float water,float rest,bool expected)
        {
            Assert.Equal(expected, SurvivalSimulator.CanRun(new SurvivalState { Health=health,Hunger=hunger,Hydration=water,Rest=rest }));
        }
    }
}
