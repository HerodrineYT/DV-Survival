using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class PersonalSleepTests
    {
        private static SurvivalActionRequest Request(float hours = 10f)
        {
            return new SurvivalActionRequest { Action = SurvivalActionKind.SleepWithoutTimeAdvance, Amount = hours };
        }

        [Fact]
        public void TenHoursWithoutWorldJumpRestoreOnlySleepingPlayer()
        {
            var request = Request();
            Assert.True(PersonalSleepValidator.IsValid(true, true, true, request));
            var sleeper = new SurvivalState { Rest = 0f, LowRestGameHours = 100d, CoffeeUsesSinceSleep = 8 };
            var otherPlayer = new SurvivalState { Rest = 25f, LowRestGameHours = 80d };
            var tuning = new SurvivalTuning { DamageMultiplier = 0f };
            var room = new SurvivalEnvironment { AmbientTemperatureCelsius = 22f, Exposure = 0f };
            Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Sleep(sleeper, request.Amount, room, tuning));
            Assert.Equal(100f, sleeper.Rest);
            Assert.Equal(0d, sleeper.LowRestGameHours);
            Assert.Equal(0, sleeper.CoffeeUsesSinceSleep);
            Assert.Equal(1f, SleepDeprivationEffects.DisplaySpeedDivisor(sleeper));
            Assert.Equal(25f, otherPlayer.Rest);
            Assert.Equal(80d, otherPlayer.LowRestGameHours);
        }

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public void AuthorityMustConfirmModeAndBed(bool multiplayer, bool suppressed, bool nearBed)
        {
            Assert.False(PersonalSleepValidator.IsValid(multiplayer, suppressed, nearBed, Request()));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(25f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void RejectsInvalidDuration(float hours)
        {
            Assert.False(PersonalSleepValidator.IsValid(true, true, true, Request(hours)));
        }

        [Fact]
        public void RejectsMixedCalendarAndItemActions()
        {
            var request = Request();
            request.CalendarBeforeTicks = 1;
            Assert.False(PersonalSleepValidator.IsValid(true, true, true, request));
            request = Request(); request.Provision = ProvisionKind.Coffee;
            Assert.False(PersonalSleepValidator.IsValid(true, true, true, request));
            request = Request(); request.Action = SurvivalActionKind.Sleep;
            Assert.False(PersonalSleepValidator.IsValid(true, true, true, request));
        }
    }
}
