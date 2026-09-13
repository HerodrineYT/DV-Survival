using System;
using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class GameCalendarClockTests
    {
        private static readonly DateTime Epoch =
            new DateTime(2016, 7, 21, 23, 30, 0, DateTimeKind.Utc);

        [Fact]
        public void FirstObservationInitializesWithoutSimulatingOfflineTime()
        {
            var clock = new GameCalendarClock();

            Assert.False(clock.IsInitialized);
            Assert.Equal(0d, clock.Observe(Epoch));
            Assert.True(clock.IsInitialized);
        }

        [Fact]
        public void ForwardDeltaUsesCalendarHoursAcrossMidnightAndDateBoundaries()
        {
            var clock = new GameCalendarClock();
            clock.Observe(Epoch);

            Assert.Equal(2.25d, clock.Observe(Epoch.AddHours(2.25d)), 10);
            Assert.Equal(24d, clock.Observe(Epoch.AddHours(26.25d)), 10);
        }

        [Fact]
        public void SameTimestampProducesNoElapsedGameTime()
        {
            var clock = new GameCalendarClock();
            clock.Observe(Epoch);

            Assert.Equal(0d, clock.Observe(Epoch));
        }

        [Fact]
        public void RollbackProducesZeroAndRebasesTheNextDelta()
        {
            var clock = new GameCalendarClock();
            clock.Observe(Epoch);

            Assert.Equal(0d, clock.Observe(Epoch.AddHours(-6d)));
            Assert.Equal(1.5d, clock.Observe(Epoch.AddHours(-4.5d)), 10);
        }

        [Fact]
        public void LargeForwardJumpIsCappedButStillRebasesAtObservedTime()
        {
            var clock = new GameCalendarClock();
            clock.Observe(Epoch);

            Assert.Equal(GameCalendarClock.MaximumObservedJumpHours,
                clock.Observe(Epoch.AddHours(300d)));
            Assert.Equal(1d, clock.Observe(Epoch.AddHours(301d)), 10);
        }

        [Fact]
        public void ResetForcesTheNextTimestampToBecomeANewBaseline()
        {
            var clock = new GameCalendarClock();
            clock.Observe(Epoch);
            clock.Observe(Epoch.AddHours(4d));

            clock.Reset();

            Assert.False(clock.IsInitialized);
            Assert.Equal(0d, clock.Observe(Epoch.AddDays(20d)));
            Assert.True(clock.IsInitialized);
            Assert.Equal(.25d, clock.Observe(Epoch.AddDays(20d).AddMinutes(15d)), 10);
        }

        [Theory]
        [InlineData(144d, 1d)]
        [InlineData(10d, 1d)]
        [InlineData(1d, 1d)]
        public void SmoothProgressAtConfiguredDayLengthDoesNotEnterSleepBuffer(
            double dayMinutes, double realSeconds)
        {
            var expected = realSeconds * 24d / (dayMinutes * 60d);
            Assert.False(GameCalendarClock.IsDiscontinuousAdvance(expected,
                realSeconds, dayMinutes));
        }

        [Theory]
        [InlineData(144d, .1d)]
        [InlineData(10d, .1d)]
        [InlineData(1d, .1d)]
        public void ExplicitTimeSkipIsDetectedEvenForVeryShortConfiguredDays(
            double dayMinutes, double skippedHours)
        {
            const double realSeconds = 1d;
            var smooth = realSeconds * 24d / (dayMinutes * 60d);
            Assert.True(GameCalendarClock.IsDiscontinuousAdvance(smooth + skippedHours,
                realSeconds, dayMinutes));
        }
    }
}
