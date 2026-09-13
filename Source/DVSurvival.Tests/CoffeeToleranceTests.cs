using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class CoffeeToleranceTests
    {
        [Fact]
        public void EachSuccessfulCupLosesTenPercentOfBaseGainAndNeverBecomesNegative()
        {
            var s = new SurvivalState();
            for (int i = 0; i < 20; i++)
            {
                s.Rest = 20;
                Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.ConsumePhysical(s, ProvisionKind.Coffee, new SurvivalTuning()));
                Assert.Equal(20f + 16f * Math.Max(0, 10-i) / 10f, s.Rest, 4);
                Assert.Equal(Math.Min(10,i+1), s.CoffeeUsesSinceSleep);
            }
        }
        [Fact]
        public void DepletedRestEffectDoesNotReduceWaterOrWarmthBenefits()
        {
            var s = new SurvivalState { Rest = 25, Hydration = 50, BodyTemperatureCelsius = 36, CoffeeUsesSinceSleep = 10 };
            SurvivalSimulator.ConsumePhysical(s, ProvisionKind.Coffee, new SurvivalTuning());
            Assert.Equal(25, s.Rest); Assert.Equal(65, s.Hydration); Assert.Equal(36.2f, s.BodyTemperatureCelsius, 4);
        }
        [Fact]
        public void SuccessfulShortSleepRestoresCoffeeWithoutResettingUnrecoveredSleepDeprivation()
        {
            var s = new SurvivalState { Rest = 20, LowRestGameHours = 30, CoffeeUsesSinceSleep = 10 };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Sleep(s, .1f, new SurvivalEnvironment(), new SurvivalTuning()));
            Assert.Equal(0, s.CoffeeUsesSinceSleep); Assert.True(s.LowRestGameHours > 30);
            var before = s.Rest;
            SurvivalSimulator.ConsumePhysical(s, ProvisionKind.Coffee, new SurvivalTuning());
            Assert.Equal(before + 16, s.Rest, 4);
        }
        [Fact]
        public void FailedSleepDoesNotRestoreCoffee()
        {
            var s = new SurvivalState { CoffeeUsesSinceSleep = 6 };
            Assert.Equal(SurvivalResultCode.InvalidRequest, SurvivalSimulator.Sleep(s, 0, new SurvivalEnvironment(), new SurvivalTuning()));
            Assert.Equal(6, s.CoffeeUsesSinceSleep);
        }
        [Fact]
        public void NaturalTimeAndOtherItemsDoNotRestoreCoffee()
        {
            var s = new SurvivalState { CoffeeUsesSinceSleep = 6, Rest = 50, Hydration = 50 };
            SurvivalSimulator.Advance(s, new SurvivalEnvironment { GameHours = 1 }, new SurvivalTuning());
            SurvivalSimulator.ConsumePhysical(s, ProvisionKind.Water, new SurvivalTuning());
            Assert.Equal(6, s.CoffeeUsesSinceSleep);
        }
        [Fact]
        public void FailedConsumptionAndDuplicateIdentityDoNotIncreaseTolerance()
        {
            var s = new SurvivalState { Rest = 100, Hydration = 100, CaffeineHours = 4, CoffeeUsesSinceSleep = 2 };
            Assert.Equal(SurvivalResultCode.NotNeeded, SurvivalSimulator.ConsumePhysical(s, ProvisionKind.Coffee, new SurvivalTuning()));
            Assert.Equal(2, s.CoffeeUsesSinceSleep);
            s.Rest = 20;
            var ledger = new HashSet<string>(); var id = Guid.NewGuid().ToString("D");
            for (int i=0;i<2;i++)
                Assert.Equal(SurvivalResultCode.Success, PhysicalItemLedger.Consume(ledger,id,s,ProvisionKind.Coffee,new SurvivalTuning()));
            Assert.Equal(3, s.CoffeeUsesSinceSleep); Assert.Equal(32.8f, s.Rest, 4);
        }
        [Fact]
        public void ToleranceAndItsSleepResetArePersonal()
        {
            var a = new SurvivalState { Rest = 20, CoffeeUsesSinceSleep = 5 };
            var b = new SurvivalState { Rest = 20 };
            SurvivalSimulator.ConsumePhysical(a, ProvisionKind.Coffee, new SurvivalTuning());
            SurvivalSimulator.ConsumePhysical(b, ProvisionKind.Coffee, new SurvivalTuning());
            Assert.Equal(28, a.Rest); Assert.Equal(36, b.Rest);
            SurvivalSimulator.Sleep(b, 1, new SurvivalEnvironment(), new SurvivalTuning());
            Assert.Equal(6, a.CoffeeUsesSinceSleep); Assert.Equal(0, b.CoffeeUsesSinceSleep);
        }
        [Fact]
        public void CloneCodecAndSavePreserveToleranceAndOlderSavesDefaultToFullEffect()
        {
            var s = new SurvivalState { CoffeeUsesSinceSleep = 7 };
            Assert.Equal(7, s.Clone().CoffeeUsesSinceSleep);
            using (var stream = new MemoryStream())
            {
                SurvivalStateCodec.Write(new BinaryWriter(stream), s); stream.Position = 0;
                Assert.Equal(7, SurvivalStateCodec.Read(new BinaryReader(stream)).CoffeeUsesSinceSleep);
            }
            var options = new JsonSerializerOptions { IncludeFields = true };
            Assert.Equal(7, JsonSerializer.Deserialize<SurvivalState>(JsonSerializer.Serialize(s,options),options).CoffeeUsesSinceSleep);
            var old = JsonSerializer.Deserialize<SurvivalState>("{\"Rest\":45}", options);
            Assert.Equal(0, old.CoffeeUsesSinceSleep); Assert.Equal(45, old.Rest);
        }
        [Fact]
        public void WeakenedCoffeeDoesNotIncorrectlyClearSleepDeprivation()
        {
            var s = new SurvivalState { Rest = 60, LowRestGameHours = 120, CoffeeUsesSinceSleep = 5 };
            SurvivalSimulator.ConsumePhysical(s, ProvisionKind.Coffee, new SurvivalTuning());
            Assert.Equal(68, s.Rest); Assert.Equal(120, s.LowRestGameHours);
            Assert.Equal(1f/2.5f, SleepDeprivationEffects.DisplaySpeedMultiplier(s));
        }
        [Theory]
        [InlineData(-1,0)] [InlineData(100,10)]
        public void ClampBoundsTolerance(int invalid,int expected)
        {
            var s = new SurvivalState { CoffeeUsesSinceSleep = invalid };
            Assert.False(s.IsValid()); s.Clamp(); Assert.True(s.IsValid()); Assert.Equal(expected, s.CoffeeUsesSinceSleep);
        }
    }
}
