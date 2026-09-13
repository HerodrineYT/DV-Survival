using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public class SpeedDisplayScaleTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(30f)]
        [InlineData(60f)]
        [InlineData(90f)]
        public void De2CabNeedleUsesActualPrefabCalibrationAndNeverAccumulates(float actualSpeed)
        {
            // Verified from LocoDE2_Interior: 0..90 km/h, -226..46 degrees, Y rotation axis.
            float display = SpeedDisplayScale.Apply(actualSpeed, 0f, 1f / 1.5f);
            float angle = SpeedDisplayScale.NeedleAngle(display, 0f, 90f, -226f, 46f, false);
            float dialReading = (angle + 226f) / 272f * 90f;
            Assert.Equal(actualSpeed / 1.5f, dialReading, 4);
            for (int refresh = 0; refresh < 100; refresh++)
                Assert.Equal(angle, SpeedDisplayScale.NeedleAngle(
                    SpeedDisplayScale.Apply(actualSpeed, 0f, 1f / 1.5f), 0f, 90f, -226f, 46f, false));
            Assert.Equal(actualSpeed, SpeedDisplayScale.Apply(actualSpeed, 0f, 1f));
        }

        [Theory]
        [InlineData(0f, 1f, 60f)]
        [InlineData(100f, -1f, 60f)]
        [InlineData(1f, -.01f, 60f)]
        [InlineData(-100f, 1f, 60f)]
        [InlineData(20f, 3.6f, 60f)]
        [InlineData(0f, 1f, -60f)]
        [InlineData(100f, -1f, 0f)]
        public void EncodedGaugeReadsOneThirdSpeedInsteadOfScalingItsZero(float zero, float multiplier, float speed)
        {
            float raw = zero + speed * multiplier;
            float shown = SpeedDisplayScale.Apply(raw, zero, 1f / 3f);
            Assert.Equal(speed / 3f, (shown - zero) / multiplier, 3);
            Assert.Equal(raw, SpeedDisplayScale.Apply(raw, zero, 1f));
        }

        [Theory]
        [InlineData(100f, 0f, 100f, false, 1f)]
        [InlineData(0f, -120f, 120f, false, .5f)]
        [InlineData(0f, -120f, 120f, true, 0f)]
        [InlineData(-100f, -100f, 100f, true, 1f)]
        [InlineData(100f, 100f, 0f, false, 0f)]
        public void HudScalesAboutTheSameZeroAsTheGauge(float zero, float min, float max, bool absolute, float expected)
        {
            float origin = SpeedDisplayScale.NormalizedZero(zero, min, max, absolute);
            Assert.Equal(expected, origin);
            Assert.Equal(origin, SpeedDisplayScale.Apply(origin, origin, 1f / 3f));
            float displayed = SpeedDisplayScale.Apply(.4f, origin, 1f / 3f);
            Assert.Equal((.4f - origin) / 3f, displayed - origin, 5);
        }
    }
}
