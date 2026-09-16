using System.Linq;
using System.Text.Json;
using DVSurvival.Mod;
using Xunit;

namespace DVSurvival.Tests
{
    public class HudOverlayTests
    {
        [Theory]
        [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
        [InlineData(3, false)] [InlineData(4, true)] [InlineData(5, true)]
        [InlineData(-1, false)] [InlineData(99, false)]
        public void BothOverlaysUseSelectedFrameAndKeepTextInside(int style, bool brass)
        {
            using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(
                new SurvivalHud().RecordOverlays(style), new JsonSerializerOptions { IncludeFields = true })))
            {
            var commands = doc.RootElement.EnumerateArray().ToArray();
            Assert.Equal(brass ? 2 : 0, commands.Count(x => x.GetProperty("kind").GetString() == "plate"));
            Assert.Equal(brass ? 2 : 6, commands.Count(x => x.GetProperty("kind").GetString() == "solid"));
            var text = commands.Where(x => x.GetProperty("kind").GetString() == "text").ToArray();
            Assert.Equal(3, text.Length);
            foreach (var entry in text)
                Assert.Equal(brass ? "Georgia" : "Arial", entry.GetProperty("style").GetProperty("font").GetString());
            var title = text[1].GetProperty("rect");
            Assert.Equal(508f, title.GetProperty("x").GetSingle());
            Assert.Equal(503f, title.GetProperty("y").GetSingle());
            Assert.Equal(264f, title.GetProperty("width").GetSingle());
            var fill = commands.Last(x => x.GetProperty("kind").GetString() == "solid");
            Assert.Equal(256f * .635f, fill.GetProperty("rect").GetProperty("width").GetSingle(), 3);
            Assert.Equal(brass ? .82f : .45f, fill.GetProperty("color").GetProperty("r").GetSingle());
            }
        }

        [Fact]
        public void SwitchingStylesDoesNotKeepPreviousFrame()
        {
            var hud = new SurvivalHud();
            foreach (var style in new[] { 5, 0, 4, 3, 1, 2, 5 })
            {
                using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(hud.RecordOverlays(style),
                    new JsonSerializerOptions { IncludeFields = true })))
                {
                Assert.Equal(style == 4 || style == 5 ? "plate" : "solid",
                    doc.RootElement[0].GetProperty("kind").GetString());
                }
            }
        }
    }
}
