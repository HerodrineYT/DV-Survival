using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class CabHeaterSwitchSettingTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(0.001f, 0f)]
        [InlineData(0.49f, 0f)]
        [InlineData(0.5f, 1f)]
        [InlineData(0.999f, 1f)]
        [InlineData(1f, 1f)]
        [InlineData(float.NaN, 0f)]
        [InlineData(float.PositiveInfinity, 0f)]
        public void PhysicalControlUsesTwoPositions(float value, float expected)
        {
            Assert.Equal(expected, CabHeaterSetting.FromControlValue(value));
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(0.33333334f, 1f)]
        [InlineData(0.6666667f, 1f)]
        [InlineData(1f, 1f)]
        [InlineData(float.NaN, 0f)]
        [InlineData(float.PositiveInfinity, 0f)]
        public void StoredIntermediateSettingsBecomeOn(float value, float expected)
        {
            Assert.Equal(expected, CabHeaterSetting.NormalizeSwitch(value));
        }
    }
}
