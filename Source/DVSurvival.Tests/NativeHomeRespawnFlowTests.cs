using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class NativeHomeRespawnFlowTests
    {
        [Fact]
        public void NoDeathDoesNotStartTravel()
        {
            Assert.False(new HomeRespawnFlow().TryStart(true, false));
        }
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public void NotReadyOrExistingNativeTravelRetainsPendingDeath(bool ready, bool busy)
        {
            var flow = new HomeRespawnFlow(); flow.Begin();
            Assert.False(flow.TryStart(ready, busy));
            Assert.True(flow.Pending);
            Assert.True(flow.TryStart(true, false));
        }
        [Fact]
        public void NoDuplicateCoroutineWhileRunning()
        {
            var flow = new HomeRespawnFlow(); flow.Begin();
            Assert.True(flow.TryStart(true, false));
            Assert.False(flow.TryStart(true, false));
            Assert.True(flow.Finish(flow.Revision, true));
            Assert.False(flow.Pending); Assert.False(flow.Running);
            Assert.False(flow.Finish(flow.Revision, true));
        }
        [Fact]
        public void FailureCanRetryWithoutNewDeath()
        {
            var flow = new HomeRespawnFlow(); flow.Begin(); flow.TryStart(true, false);
            Assert.True(flow.Finish(flow.Revision, false));
            Assert.True(flow.Pending);
            Assert.True(flow.TryStart(true, false));
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OldCallbackCannotFinishNewSessionOrDeath(bool newDeath)
        {
            var flow = new HomeRespawnFlow(); flow.Begin(); flow.TryStart(true, false);
            var old = flow.Revision;
            flow.Reset();
            if (newDeath) { flow.Begin(); flow.TryStart(true, false); }
            Assert.False(flow.Finish(old, true));
            Assert.Equal(newDeath, flow.Pending);
            Assert.Equal(newDeath, flow.Running);
        }
    }
}
