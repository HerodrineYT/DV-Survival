using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public class ActionRequestGateTests
    {
        [Fact]
        public void DuplicateAndOldRequestsCannotBeAcceptedAgain()
        {
            var gate = new ActionRequestGate();
            Assert.False(gate.TryAccept(1, 0));
            Assert.True(gate.TryAccept(1, 10));
            Assert.False(gate.TryAccept(1, 10));
            Assert.False(gate.TryAccept(1, 9));
            Assert.True(gate.TryAccept(1, 11));
        }

        [Fact]
        public void PlayersAndConnectionsAreIndependent()
        {
            var gate = new ActionRequestGate();
            Assert.True(gate.TryAccept(1, 4));
            Assert.True(gate.TryAccept(2, 4));
            gate.Remove(1);
            Assert.True(gate.TryAccept(1, 1));
            Assert.False(gate.TryAccept(2, 4));
            gate.Clear();
            Assert.True(gate.TryAccept(2, 1));
        }

        [Fact]
        public void SequenceWrapSkipsReservedZero()
        {
            var gate = new ActionRequestGate();
            Assert.True(gate.TryAccept(1, uint.MaxValue));
            Assert.True(gate.TryAccept(1, 1));
            Assert.False(gate.TryAccept(1, uint.MaxValue));
        }

        [Theory]
        [InlineData(ProvisionKind.Meal)]
        [InlineData(ProvisionKind.Water)]
        [InlineData(ProvisionKind.Coffee)]
        [InlineData(ProvisionKind.FirstAid)]
        [InlineData(ProvisionKind.HeatPack)]
        public void ReplayedUseDoesNotConsumeSecondUnitOrAffectOtherPlayer(ProvisionKind kind)
        {
            var tuning = new SurvivalTuning();
            var owner = SurvivalState.CreateDefault(tuning);
            owner.Hunger = owner.Hydration = owner.Rest = owner.Health = 10f;
            owner.BodyTemperatureCelsius = 34f;
            owner.TryChangeProvisionCount(kind, 3);
            var other = owner.Clone();
            var initialCount = owner.GetProvisionCount(kind);
            var gate = new ActionRequestGate();
            for (int i = 0; i < 3; i++)
                if (gate.TryAccept(2, 1)) Assert.Equal(SurvivalResultCode.Success, SurvivalSimulator.Consume(owner, kind, tuning));
            Assert.Equal(initialCount - 1, owner.GetProvisionCount(kind));
            Assert.Equal(initialCount, other.GetProvisionCount(kind));
            Assert.Equal(10f, other.Health);
        }
    }
}
