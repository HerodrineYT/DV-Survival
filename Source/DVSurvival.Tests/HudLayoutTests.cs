using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class HudLayoutTests
    {
        [Theory]
        [InlineData(0, 280, 172)]
        [InlineData(1, 292, 188)]
        [InlineData(2, 520, 74)]
        [InlineData(3, 248, 124)]
        [InlineData(4, 280, 192)]
        public void FiveCompactLayoutsHaveStableDimensions(int style, float width, float height)
        {
            Assert.Equal(style, HudLayout.Validate(style));
            Assert.Equal(width, HudLayout.Width(style)); Assert.Equal(height, HudLayout.Height(style));
            Assert.Equal(1f, HudLayout.FitScale(style, 1, 1920, 1080));
        }
        [Theory]
        [InlineData(-1)]
        [InlineData(6)]
        [InlineData(int.MaxValue)]
        public void InvalidSavedSelectionFallsBackToRings(int style)
        {
            Assert.Equal(0, HudLayout.Validate(style));
            Assert.Equal(280f, HudLayout.Width(style));
        }
        [Theory]
        [InlineData(640, 480)]
        [InlineData(800, 600)]
        [InlineData(1280, 720)]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1440)]
        public void AllStylesFitIncludingWarningAtMaximumScale(float width, float height)
        {
            for (var style = 0; style < HudLayout.Count; style++)
            {
                var scale = HudLayout.FitScale(style, 1.75f, width, height);
                Assert.True((HudLayout.Width(style) + 40) * scale <= width + 0.01f);
                Assert.True((HudLayout.Height(style) + 84) * scale <= height + 0.01f);
                Assert.InRange(scale, 0.1f, 1.75f);
            }
        }
        [Fact]
        public void BadScaleIsSanitized()
        {
            Assert.Equal(1f, HudLayout.FitScale(0, float.NaN, 1920, 1080));
            Assert.Equal(1f, HudLayout.FitScale(0, float.PositiveInfinity, 1920, 1080));
            Assert.Equal(0.65f, HudLayout.FitScale(0, -5, 1920, 1080));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void NewLayoutsEnlargeByDefaultAndSliderWorks(int style)
        {
            Assert.Equal(1.75f, HudLayout.FitScale(style, 1, 2560, 1440, 1.75f));
            Assert.Equal(2f, HudLayout.FitScale(style, 1, 2560, 1440, 2f));
            Assert.Equal(3f, HudLayout.FitScale(style, 1, 2560, 1440, 3f));
            var small = HudLayout.FitScale(style, 1.75f, 640, 480, 3f);
            Assert.True((HudLayout.Width(style) + 40) * small <= 640.01f);
            Assert.True((HudLayout.Height(style) + 84) * small <= 480.01f);
        }

        [Fact]
        public void OriginalHudIgnoresNewLayoutsMultiplier()
        {
            Assert.Equal(5, HudLayout.Validate(HudLayout.Legacy));
            Assert.Equal(1f, HudLayout.FitScale(HudLayout.Legacy, 1, 1920, 1080, 3));
        }
    }
}
