using System;
using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class SleepCalendarReconcilerTests
    {
        private const double Grace = 10d;
        private static readonly DateTime Epoch =
            new DateTime(2016, 7, 21, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ActionThenJumpAndJumpThenActionProduceTheSameAllocation()
        {
            var actionFirst = new SleepCalendarReconciler(Grace);
            Assert.True(actionFirst.TryRecordSleep(Sleep(1, 7, 0, 8, 0)));
            Assert.True(actionFirst.TryRecordJump(Jump(1, 0, 8, 1)));

            var jumpFirst = new SleepCalendarReconciler(Grace);
            Assert.True(jumpFirst.TryRecordJump(Jump(1, 0, 8, 0)));
            Assert.True(jumpFirst.TryRecordSleep(Sleep(1, 7, 0, 8, 1)));

            var first = Single(actionFirst.DrainReady(12));
            var second = Single(jumpFirst.DrainReady(11));
            Assert.Equal(first.TotalHours, second.TotalHours, 10);
            Assert.Equal(first.GetMatchedSleepHours(1), second.GetMatchedSleepHours(1), 10);
            Assert.Equal(0d, first.GetAwakeHours(1), 10);
            Assert.Equal(first.GetAwakeHours(1), second.GetAwakeHours(1), 10);
            Assert.Equal(8d, first.GetAwakeHours(2), 10);
        }

        [Fact]
        public void JumpIsHeldThroughTheInclusiveGraceDeadline()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 8, 5));

            Assert.Empty(reconciler.DrainReady(14.999));
            Assert.Empty(reconciler.DrainReady(15));
            Assert.Equal(1, reconciler.PendingJumpCount);
            Assert.Single(reconciler.DrainReady(15.001));
            Assert.Equal(0, reconciler.PendingJumpCount);
        }

        [Fact]
        public void UnmatchedJumpAdvancesEveryPlayerInFullAfterGrace()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 2, 10, 0));

            var advance = Single(reconciler.DrainReady(Grace + .001));
            Assert.Equal(8d, advance.TotalHours, 10);
            Assert.Empty(advance.MatchedSleepHours);
            Assert.Equal(8d, advance.GetAwakeHours(1), 10);
            Assert.Equal(8d, advance.GetAwakeHours(255), 10);
        }

        [Fact]
        public void ExpiredUnmatchedSleepCannotCaptureALaterUnrelatedJump()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            Assert.Empty(reconciler.DrainReady(Grace + .001));
            Assert.Equal(0, reconciler.PendingSleepCount);

            reconciler.TryRecordJump(Jump(1, 24, 32, 11));
            var advance = Single(reconciler.DrainReady(21.001));
            Assert.Equal(8d, advance.GetAwakeHours(1), 10);
            Assert.Equal(0d, advance.GetMatchedSleepHours(1), 10);
        }

        [Fact]
        public void NonOverlappingCalendarIntervalsNeverMatchInsideGrace()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            reconciler.TryRecordJump(Jump(1, 8, 16, 1));

            var advance = Single(reconciler.DrainReady(11.001));
            Assert.Equal(8d, advance.GetAwakeHours(1), 10);
        }

        [Fact]
        public void OverlappingIntervalsOutsideGraceNeverMatch()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            reconciler.TryRecordJump(Jump(1, 0, 8, Grace + .001));

            var advance = Single(reconciler.DrainReady(20.002));
            Assert.Equal(8d, advance.GetAwakeHours(1), 10);
        }

        [Fact]
        public void DuplicateRequestIdIsRejectedPerPlayerAndCannotExtendExclusion()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            Assert.True(reconciler.TryRecordSleep(Sleep(1, 9, 0, 4, 0)));
            Assert.False(reconciler.TryRecordSleep(Sleep(1, 9, 4, 8, 1)));
            Assert.True(reconciler.TryRecordSleep(Sleep(2, 9, 0, 8, 1)));
            reconciler.TryRecordJump(Jump(1, 0, 8, 2));

            var advance = Single(reconciler.DrainReady(12.001));
            Assert.Equal(4d, advance.GetMatchedSleepHours(1), 10);
            Assert.Equal(4d, advance.GetAwakeHours(1), 10);
            Assert.Equal(8d, advance.GetMatchedSleepHours(2), 10);
            Assert.Equal(0d, advance.GetAwakeHours(2), 10);
        }

        [Fact]
        public void DuplicateJumpSequenceIsRejected()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            Assert.True(reconciler.TryRecordJump(Jump(4, 0, 8, 0)));
            Assert.False(reconciler.TryRecordJump(Jump(4, 8, 16, 1)));

            var advance = Single(reconciler.DrainReady(Grace + .001));
            Assert.Equal(8d, advance.TotalHours, 10);
            Assert.Equal(0d, advance.Jump.BeforeTicks - Ticks(0));
        }

        [Fact]
        public void TwoPlayersReceiveIndependentAwakeHoursForOneJump()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(3, 1, 0, 8, 0));
            reconciler.TryRecordSleep(Sleep(4, 1, 2, 6, .5));
            reconciler.TryRecordJump(Jump(1, 0, 8, 1));

            var advance = Single(reconciler.DrainReady(11.001));
            Assert.Equal(0d, advance.GetAwakeHours(3), 10);
            Assert.Equal(4d, advance.GetAwakeHours(4), 10);
            Assert.Equal(8d, advance.GetAwakeHours(5), 10);
        }

        [Fact]
        public void TwoSequentialSleepsForOnePlayerAreCombined()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(3, 1, 0, 3, 0));
            reconciler.TryRecordSleep(Sleep(3, 2, 3, 8, .5));
            reconciler.TryRecordJump(Jump(1, 0, 8, 1));

            var advance = Single(reconciler.DrainReady(11.001));
            Assert.Equal(8d, advance.GetMatchedSleepHours(3), 10);
            Assert.Equal(0d, advance.GetAwakeHours(3), 10);
        }

        [Fact]
        public void OverlappingSleepReportsAreUnionedInsteadOfDoubleCounted()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(3, 1, 0, 6, 0));
            reconciler.TryRecordSleep(Sleep(3, 2, 4, 8, .5));
            reconciler.TryRecordJump(Jump(1, 0, 10, 1));

            var advance = Single(reconciler.DrainReady(11.001));
            Assert.Equal(8d, advance.GetMatchedSleepHours(3), 10);
            Assert.Equal(2d, advance.GetAwakeHours(3), 10);
        }

        [Fact]
        public void OnlyIntersectionIsExcludedAndOrdinaryResidualTimeRemainsAwake()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 1, 9, 0));
            reconciler.TryRecordJump(Jump(1, 0, 9.25, 1));

            var advance = Single(reconciler.DrainReady(11.001));
            Assert.Equal(8d, advance.GetMatchedSleepHours(1), 10);
            Assert.Equal(1.25d, advance.GetAwakeHours(1), 10);
        }

        [Fact]
        public void OneSleepCanBeReconciledAcrossSplitCalendarJumps()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            reconciler.TryRecordJump(Jump(1, 0, 3, 1));
            reconciler.TryRecordJump(Jump(2, 3, 8, 2));

            var advances = reconciler.DrainReady(12.001);
            Assert.Equal(2, advances.Count);
            Assert.Equal(0d, advances[0].GetAwakeHours(1), 10);
            Assert.Equal(0d, advances[1].GetAwakeHours(1), 10);
            Assert.Equal(3d, advances[0].GetAwakeHours(2), 10);
            Assert.Equal(5d, advances[1].GetAwakeHours(2), 10);
        }

        [Fact]
        public void SleepArrivingAfterAJumpWasCommittedCannotChangeItOrFutureJumps()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 8, 0));
            var committed = Single(reconciler.DrainReady(Grace + .001));
            Assert.Equal(8d, committed.GetAwakeHours(1), 10);

            Assert.True(reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 10.1)));
            reconciler.TryRecordJump(Jump(2, 24, 32, 11));
            var later = Single(reconciler.DrainReady(21.001));
            Assert.Equal(8d, later.GetAwakeHours(1), 10);
        }

        [Fact]
        public void DisconnectRemovesPendingMatchesAndAllowsRequestIdsToBeReused()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 7, 0, 8, 0));
            reconciler.TryRecordJump(Jump(1, 0, 8, 1));

            reconciler.DisconnectPlayer(1);

            Assert.Equal(0, reconciler.PendingSleepCount);
            Assert.True(reconciler.TryRecordSleep(Sleep(1, 7, 24, 32, 2)));
            var oldJump = Single(reconciler.DrainReady(11.001));
            Assert.Equal(8d, oldJump.GetAwakeHours(1), 10);
        }

        [Fact]
        public void ResetClearsPendingEventsAndReplayProtection()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 7, 0, 8, 0));
            reconciler.TryRecordJump(Jump(1, 0, 8, 1));

            reconciler.Reset();

            Assert.Equal(0, reconciler.PendingJumpCount);
            Assert.Equal(0, reconciler.PendingSleepCount);
            Assert.Empty(reconciler.DrainReady(20));
            Assert.True(reconciler.TryRecordSleep(Sleep(1, 7, 24, 32, 20)));
            Assert.True(reconciler.TryRecordJump(Jump(1, 24, 32, 20)));
        }

        [Fact]
        public void InvalidIntervalsIdsAndMonotonicTimesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Jump(0, 0, 8, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sleep(1, 0, 0, 8, 0));
            Assert.Throws<ArgumentException>(() => Jump(1, 8, 8, 0));
            Assert.Throws<ArgumentException>(() => Sleep(1, 1, 8, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Jump(1, 0, 8, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SleepCalendarReconciler(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SleepCalendarReconciler().DrainReady(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SleepCalendarReconciler().TryRecordJump(default(SleepCalendarJump)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SleepCalendarReconciler().TryRecordSleep(default(SleepCalendarSleep)));
        }

        [Fact]
        public void TransactionSplitsPreSleepSleepAndPostSleepInCalendarOrder()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 10, 0));
            reconciler.TryRecordSleep(Sleep(1, 9, 1, 9, 1));

            var operations = reconciler.DrainOperations(Grace + .001);

            Assert.Equal(4, operations.Count);
            AssertAdvance(operations[0], 0, 1, 1, 1);
            AssertAdvance(operations[1], 1, 9, 1, 0);
            Assert.Equal(SleepCalendarOperationKind.CommitSleep, operations[2].Kind);
            Assert.Equal(9u, operations[2].Sleep.RequestId);
            AssertAdvance(operations[3], 9, 10, 1, 1);
            Assert.Equal(1d / 10d, operations[0].Advance.SourceFraction, 10);
            Assert.False(operations[0].Advance.IsFinalSourceSegment);
            Assert.True(operations[3].Advance.IsFinalSourceSegment);
        }

        [Fact]
        public void PendingCalendarBeforeFullSleepIsEmittedBeforeSleepCommit()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 1, 0));
            reconciler.TryRecordJump(Jump(2, 1, 9, 0));
            reconciler.TryRecordSleep(Sleep(1, 2, 1, 9, 1));

            var operations = reconciler.DrainOperations(Grace + .001);

            Assert.Equal(SleepCalendarOperationKind.Advance, operations[0].Kind);
            Assert.Equal(Ticks(0), operations[0].Advance.Jump.BeforeTicks);
            Assert.Equal(Ticks(1), operations[0].Advance.Jump.AfterTicks);
            Assert.Equal(1d, operations[0].Advance.GetAwakeHours(1), 10);
            Assert.Equal(SleepCalendarOperationKind.Advance, operations[1].Kind);
            Assert.Equal(0d, operations[1].Advance.GetAwakeHours(1), 10);
            Assert.Equal(SleepCalendarOperationKind.CommitSleep, operations[2].Kind);
        }

        [Fact]
        public void TransactionOrderIsEquivalentForSleepFirstAndJumpFirst()
        {
            var sleepFirst = new SleepCalendarReconciler(Grace);
            sleepFirst.TryRecordSleep(Sleep(1, 7, 0, 8, 0));
            sleepFirst.TryRecordJump(Jump(1, 0, 8, 1));
            var jumpFirst = new SleepCalendarReconciler(Grace);
            jumpFirst.TryRecordJump(Jump(1, 0, 8, 0));
            jumpFirst.TryRecordSleep(Sleep(1, 7, 0, 8, 1));

            var first = sleepFirst.DrainOperations(11.001);
            var second = jumpFirst.DrainOperations(10.001);

            Assert.Equal(2, first.Count);
            Assert.Equal(first.Count, second.Count);
            for (var i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Kind, second[i].Kind);
                Assert.Equal(first[i].Sleep.RequestId, second[i].Sleep.RequestId);
                if (first[i].Advance != null)
                    Assert.Equal(first[i].Advance.GetAwakeHours(1),
                        second[i].Advance.GetAwakeHours(1), 10);
            }
        }

        [Fact]
        public void PartialCalendarOverlapRejectsSleepAndAdvancesJumpInFull()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            reconciler.TryRecordJump(Jump(1, 0, 4, 1));

            var operations = reconciler.DrainOperations(11.001);

            Assert.Equal(2, operations.Count);
            AssertAdvance(operations[0], 0, 4, 1, 4);
            Assert.Equal(SleepCalendarOperationKind.RejectSleep, operations[1].Kind);
            Assert.Equal(1u, operations[1].Sleep.RequestId);
        }

        [Fact]
        public void ExactSleepAcrossTwoJumpsWaitsAndCommitsOnce()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            reconciler.TryRecordJump(Jump(1, 0, 3, 0));
            reconciler.TryRecordJump(Jump(2, 3, 8, 5));

            Assert.Empty(reconciler.DrainOperations(10.001));
            var operations = reconciler.DrainOperations(15.001);

            Assert.Equal(3, operations.Count);
            AssertAdvance(operations[0], 0, 3, 1, 0);
            AssertAdvance(operations[1], 3, 8, 1, 0);
            Assert.Equal(SleepCalendarOperationKind.CommitSleep, operations[2].Kind);
            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.CommitSleep));
        }

        [Fact]
        public void RecentPartialCoverageWaitsForAStillPossibleFollowingJump()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 4, 0));
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 9));

            Assert.Empty(reconciler.DrainOperations(10.001));

            reconciler.TryRecordJump(Jump(2, 4, 8, 10.5));
            Assert.Empty(reconciler.DrainOperations(19));
            var operations = reconciler.DrainOperations(20.501);
            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.CommitSleep));
            Assert.Equal(2, Count(operations, SleepCalendarOperationKind.Advance));
        }

        [Fact]
        public void ExpiredSleepProducesRejectionAndCannotClaimLaterJump()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));

            var rejection = reconciler.DrainOperations(10.001);
            Assert.Single(rejection);
            Assert.Equal(SleepCalendarOperationKind.RejectSleep, rejection[0].Kind);

            reconciler.TryRecordJump(Jump(1, 0, 8, 11));
            var later = reconciler.DrainOperations(21.001);
            Assert.Single(later);
            AssertAdvance(later[0], 0, 8, 1, 8);
        }

        [Fact]
        public void TwoPlayersCanCommitIndependentSleepsInOneCalendarJump()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 8, 0));
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 1));
            reconciler.TryRecordSleep(Sleep(2, 1, 2, 6, 1));

            var operations = reconciler.DrainOperations(10.001);

            Assert.Equal(2, Count(operations, SleepCalendarOperationKind.CommitSleep));
            AssertAdvance(operations[0], 0, 2, 1, 0);
            Assert.Equal(2d, operations[0].Advance.GetAwakeHours(2), 10);
            AssertAdvance(operations[1], 2, 6, 1, 0);
            Assert.Equal(0d, operations[1].Advance.GetAwakeHours(2), 10);
            Assert.Equal(SleepCalendarOperationKind.CommitSleep, operations[2].Kind);
            Assert.Equal((byte)2, operations[2].Sleep.PlayerId);
            AssertAdvance(operations[3], 6, 8, 1, 0);
            Assert.Equal(2d, operations[3].Advance.GetAwakeHours(2), 10);
            Assert.Equal(SleepCalendarOperationKind.CommitSleep, operations[4].Kind);
            Assert.Equal((byte)1, operations[4].Sleep.PlayerId);
        }

        [Fact]
        public void TransactionalDisconnectAndResetRemovePendingSleepAuthorization()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 8, 0));
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 1));
            reconciler.DisconnectPlayer(1);

            var afterDisconnect = reconciler.DrainOperations(10.001);
            Assert.Single(afterDisconnect);
            AssertAdvance(afterDisconnect[0], 0, 8, 1, 8);

            reconciler.TryRecordJump(Jump(2, 8, 16, 20));
            reconciler.TryRecordSleep(Sleep(1, 1, 8, 16, 20));
            reconciler.Reset();
            Assert.Empty(reconciler.DrainOperations(40));
        }

        [Fact]
        public void TransactionOperationsPreserveFullSleepResetChronology()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 10, 0));
            reconciler.TryRecordSleep(Sleep(1, 1, 1, 9, 1));
            var state = new SurvivalState { Rest = 60, LowRestGameHours = 100 };
            var tuning = new SurvivalTuning { DamageMultiplier = 0,
                PassiveHealthRecoveryPerHour = 0 };

            var operations = reconciler.DrainOperations(10.001);
            for (var i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                if (operation.Kind == SleepCalendarOperationKind.Advance)
                {
                    var hours = (float)operation.Advance.GetAwakeHours(1);
                    if (hours > 0f)
                        SurvivalSimulator.Advance(state, new SurvivalEnvironment
                        {
                            GameHours = hours,
                            AmbientTemperatureCelsius = 22f
                        }, tuning);
                }
                else if (operation.Kind == SleepCalendarOperationKind.CommitSleep)
                {
                    Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Sleep(state,
                        (float)operation.Sleep.Hours,
                        new SurvivalEnvironment { AmbientTemperatureCelsius = 22f }, tuning));
                }
            }

            Assert.Equal(1d, state.LowRestGameHours, 10);
        }

        [Fact]
        public void OneTickGapPreventsExactSleepAuthorization()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            reconciler.TryRecordJump(new SleepCalendarJump(1, Ticks(0), Ticks(4), 0));
            reconciler.TryRecordJump(new SleepCalendarJump(2, Ticks(4) + 1, Ticks(8), 0));

            var operations = reconciler.DrainOperations(10.001);

            Assert.Equal(0, Count(operations, SleepCalendarOperationKind.CommitSleep));
            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.RejectSleep));
        }

        [Fact]
        public void DifferentJumpSequencesCannotCommitOneSleepTwice()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            reconciler.TryRecordJump(Jump(1, 0, 8, 0));
            reconciler.TryRecordJump(Jump(2, 0, 8, 0));

            var operations = reconciler.DrainOperations(10.001);

            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.CommitSleep));
        }

        [Fact]
        public void TwoSequentialSleepsProduceTwoOrderedCommits()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 4, 0));
            reconciler.TryRecordSleep(Sleep(1, 2, 4, 12, 0));
            reconciler.TryRecordJump(Jump(1, 0, 4, 0));
            reconciler.TryRecordJump(Jump(2, 4, 12, 0));

            var operations = reconciler.DrainOperations(10.001);

            Assert.Equal(2, Count(operations, SleepCalendarOperationKind.CommitSleep));
            var firstCommit = -1;
            var secondCommit = -1;
            for (var i = 0; i < operations.Count; i++)
            {
                if (operations[i].Kind != SleepCalendarOperationKind.CommitSleep) continue;
                if (operations[i].Sleep.RequestId == 1u) firstCommit = i;
                if (operations[i].Sleep.RequestId == 2u) secondCommit = i;
            }
            Assert.True(firstCommit >= 0 && secondCommit > firstCommit);
        }

        [Fact]
        public void OrderedTailRunsAfterSleepWithoutStartingAnotherGraceWindow()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 8, 0));
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 1));
            Assert.True(reconciler.TryRecordOrderedAdvance(Jump(2, 8, 9, 9.5)));

            var operations = reconciler.DrainOperations(10.001);

            Assert.Equal(3, operations.Count);
            AssertAdvance(operations[0], 0, 8, 1, 0);
            Assert.Equal(SleepCalendarOperationKind.CommitSleep, operations[1].Kind);
            AssertAdvance(operations[2], 8, 9, 1, 1);
            Assert.True(operations[2].Advance.IsFinalSourceSegment);
        }

        [Fact]
        public void OrderedAdvanceCannotAuthorizeSleepEvenWithIdenticalTicks()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 0, 8, 0));
            reconciler.TryRecordOrderedAdvance(Jump(1, 0, 8, 1));

            var operations = reconciler.DrainOperations(10.001);

            Assert.Equal(0, Count(operations, SleepCalendarOperationKind.CommitSleep));
            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.RejectSleep));
            AssertAdvance(operations[0], 0, 8, 1, 8);
        }

        [Fact]
        public void PendingSleepIntersectionCanClassifyAnAuthorityInterval()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordSleep(Sleep(1, 1, 2, 8, 0));

            Assert.False(reconciler.IntersectsPendingSleep(Ticks(0), Ticks(2)));
            Assert.True(reconciler.IntersectsPendingSleep(Ticks(0), Ticks(3)));
            Assert.True(reconciler.IntersectsPendingSleep(Ticks(7), Ticks(9)));
            Assert.False(reconciler.IntersectsPendingSleep(Ticks(8), Ticks(9)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CapturedTimeAdvanceAlignsEqualDurationAcrossClientAndHostClocks(
            bool jumpFirst)
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            var hostJump = new SleepCalendarJump(1, Ticks(100), Ticks(108), 1,
                allowsDurationAlignment: true);
            var clientSleep = Sleep(4, 77, 0, 8, 1.25);

            if (jumpFirst)
            {
                reconciler.TryRecordJump(hostJump);
                reconciler.TryRecordSleep(clientSleep);
            }
            else
            {
                reconciler.TryRecordSleep(clientSleep);
                reconciler.TryRecordJump(hostJump);
            }

            var operations = reconciler.DrainOperations(11.001);
            Assert.Equal(2, operations.Count);
            AssertAdvance(operations[0], 100, 108, 4, 0);
            Assert.Equal(SleepCalendarOperationKind.CommitSleep, operations[1].Kind);
            Assert.Equal(77u, operations[1].Sleep.RequestId);
        }

        [Fact]
        public void DurationAlignmentRejectsAmbiguousCapturedJumps()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(new SleepCalendarJump(1, Ticks(100), Ticks(108), 0,
                allowsDurationAlignment: true));
            reconciler.TryRecordJump(new SleepCalendarJump(2, Ticks(108), Ticks(116), 8,
                allowsDurationAlignment: true));
            reconciler.TryRecordSleep(Sleep(2, 9, 0, 8, 7.5));

            var operations = reconciler.DrainOperations(18.001);

            Assert.Equal(0, Count(operations, SleepCalendarOperationKind.CommitSleep));
            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.RejectSleep));
            AssertAdvance(operations[0], 100, 108, 2, 8);
            AssertAdvance(operations[1], 108, 116, 2, 8);
        }

        [Fact]
        public void ExactHostCoordinatesWinAmongMultipleEqualDurationAdvances()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(new SleepCalendarJump(1, Ticks(100), Ticks(108), 0,
                allowsDurationAlignment: true));
            reconciler.TryRecordJump(new SleepCalendarJump(2, Ticks(108), Ticks(116), 8,
                allowsDurationAlignment: true));
            reconciler.TryRecordSleep(Sleep(2, 9, 108, 116, 7.5));

            var operations = reconciler.DrainOperations(18.001);

            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.CommitSleep));
            AssertAdvance(operations[0], 100, 108, 2, 8);
            AssertAdvance(operations[1], 108, 116, 2, 0);
        }

        [Fact]
        public void DurationAlignmentDoesNotAlsoClaimAnAccidentalAbsoluteOverlap()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 4, 1));
            reconciler.TryRecordJump(new SleepCalendarJump(2, Ticks(100), Ticks(108), 1.1,
                allowsDurationAlignment: true));
            reconciler.TryRecordSleep(Sleep(2, 9, 0, 8, 1.2));

            var operations = reconciler.DrainOperations(11.201);

            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.CommitSleep));
            AssertAdvance(operations[0], 0, 4, 2, 4);
            AssertAdvance(operations[1], 100, 108, 2, 0);
        }

        [Fact]
        public void DurationAlignmentNeverCrossesTheCorrelationWindow()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(new SleepCalendarJump(1, Ticks(100), Ticks(108), 0,
                allowsDurationAlignment: true));
            reconciler.TryRecordSleep(Sleep(2, 9, 0, 8, Grace + .001));

            var operations = reconciler.DrainOperations(Grace * 2 + .01);

            Assert.Equal(0, Count(operations, SleepCalendarOperationKind.CommitSleep));
            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.RejectSleep));
            AssertAdvance(operations[0], 100, 108, 2, 8);
        }

        [Fact]
        public void OutOfWindowOverlappingJumpCannotShareACommittedSleep()
        {
            var reconciler = new SleepCalendarReconciler(Grace);
            reconciler.TryRecordJump(Jump(1, 0, 8, 0));
            reconciler.TryRecordJump(Jump(2, 0, 8, 20));
            reconciler.TryRecordSleep(Sleep(1, 3, 0, 8, 20));

            var operations = reconciler.DrainOperations(30.001);

            Assert.Equal(1, Count(operations, SleepCalendarOperationKind.CommitSleep));
            AssertAdvance(operations[0], 0, 8, 1, 8);
            AssertAdvance(operations[1], 0, 8, 1, 0);
        }

        private static SleepCalendarJump Jump(uint sequence, double beforeHours,
            double afterHours, double observedAtSeconds)
        {
            return new SleepCalendarJump(sequence, Ticks(beforeHours), Ticks(afterHours),
                observedAtSeconds);
        }

        private static SleepCalendarSleep Sleep(byte playerId, uint requestId, double beforeHours,
            double afterHours, double receivedAtSeconds)
        {
            return new SleepCalendarSleep(playerId, requestId, Ticks(beforeHours), Ticks(afterHours),
                receivedAtSeconds);
        }

        private static long Ticks(double hours) { return Epoch.AddHours(hours).Ticks; }

        private static SleepCalendarAdvance Single(System.Collections.Generic.IReadOnlyList<SleepCalendarAdvance> values)
        {
            return Assert.Single(values);
        }

        private static void AssertAdvance(SleepCalendarOperation operation, double before,
            double after, byte playerId, double awakeHours)
        {
            Assert.Equal(SleepCalendarOperationKind.Advance, operation.Kind);
            Assert.NotNull(operation.Advance);
            Assert.Equal(Ticks(before), operation.Advance.Jump.BeforeTicks);
            Assert.Equal(Ticks(after), operation.Advance.Jump.AfterTicks);
            Assert.Equal(awakeHours, operation.Advance.GetAwakeHours(playerId), 10);
        }

        private static int Count(System.Collections.Generic.IReadOnlyList<SleepCalendarOperation> values,
            SleepCalendarOperationKind kind)
        {
            var result = 0;
            for (var i = 0; i < values.Count; i++) if (values[i].Kind == kind) result++;
            return result;
        }
    }
}
