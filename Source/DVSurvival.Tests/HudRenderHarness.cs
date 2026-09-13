// Offline command recorder for the actual production layout methods; no Unity runtime required.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DVSurvival.Core;
using UnityEngine;
using Xunit;

namespace UnityEngine
{
    internal struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    internal struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
    }
    internal static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Clamp01(float x) => Math.Clamp(x, 0, 1);
    }
    internal static class Screen { public static float height = 260; public const float width = 1280; }
    internal sealed class GUIStyle
    {
        public string align = "left";
        public int size = 12;
        public string font = "Arial";
    }
    internal static class GUI
    {
        [ThreadStatic] public static List<object> Commands;
        public static void Label(Rect rect, string text, GUIStyle style)
        {
            Commands.Add(new { kind = "text", rect, text, style });
        }
    }
}
namespace DVSurvival.Mod
{
    internal sealed class PreviewSettings { public int HudStyle; }
    internal sealed class HudArtwork
    {
        public void DrawIcon(Rect rect, int index, Color color) => GUI.Commands.Add(new { kind = "icon", rect, index, color });
        public void DrawThermal(Rect rect) => GUI.Commands.Add(new { kind = "thermal", rect });
        public void DrawRing(Rect rect, float value, Color color) => GUI.Commands.Add(new { kind = "ring", rect, value, color });
        public void DrawDial(Rect rect, float value, Color color) => GUI.Commands.Add(new { kind = "dial", rect, value, color });
    }
    internal sealed partial class SurvivalHud
    {
        private readonly PreviewSettings settings = new PreviewSettings();
        private readonly HudArtwork artwork = new HudArtwork();
        private static readonly Color Brass = new Color(.82f, .67f, .44f);
        private readonly GUIStyle labelStyle = new GUIStyle { align = "center", font = "Georgia" };
        private readonly GUIStyle hudTextStyle = new GUIStyle { font = "Georgia" };
        private readonly GUIStyle legacyTitleStyle = new GUIStyle { font = "Georgia", size = 14 };
        private readonly GUIStyle legacyWarningStyle = new GUIStyle { font = "Georgia", size = 11 };
        private string warningTitle = "Требуется внимание", warningText = "";
        private string ambientText = "Воздух: -3.1 °C";
        private string legacyBodyText = "Температура тела 37.0 °C";
        private float cachedBodyTemperature = 37, temperatureFraction = .5f;
        private static void DrawSolid(Rect rect, Color color) => GUI.Commands.Add(new { kind = "solid", rect, color });
        private void DrawPlate(Rect rect) => GUI.Commands.Add(new { kind = "plate", rect });

        internal List<object> Record(int style, bool warning, bool english)
        {
            GUI.Commands = new List<object>();
            settings.HudStyle = style;
            Screen.height = style == HudLayout.Legacy ? 720 : 260;
            modernLeft = new GUIStyle(); modernCenter = new GUIStyle { align = "center" };
            modernNumber = new GUIStyle { align = "center", size = 13 }; modernSmall = new GUIStyle { size = 11 };
            compactBodyText = english ? "Body: 37.0 °C" : "Тело: 37.0 °C";
            legacyBodyText = english ? "Body temperature 37.0 °C" : "Температура тела 37.0 °C";
            ambientText = english ? "Ambient: -3.1 °C" : "Воздух: -3.1 °C";
            warningTitle = english ? "Needs attention" : "Требуется внимание";
            warningText = warning ? (english ? "Health" : "Здоровье") : "";
            var names = english ? new[] { "Health", "Food", "Water", "Rest" } : new[] { "Здоровье", "Сытость", "Вода", "Сон" };
            var values = new[] { .85f, .72f, .64f, .9f };
            for (var i = 0; i < 4; i++) { needNames[i] = names[i]; needFractions[i] = values[i]; needValues[i] = (values[i] * 100).ToString("F0") + "%"; }
            DrawLayout(1);
            return GUI.Commands;
        }
    }
}
namespace DVSurvival.Tests
{
    public sealed class HudRenderHarnessTests
    {
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void ProductionLayoutsShowAllSixValuesAndRemainInBounds(int style)
        {
            foreach (var english in new[] { false, true })
            foreach (var warning in new[] { false, true })
            {
                var commands = new Mod.SurvivalHud().Record(style, warning, english);
                var json = JsonSerializer.Serialize(commands, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true });
                using (var document = JsonDocument.Parse(json))
                {
                var labels = document.RootElement.EnumerateArray().Where(x => x.GetProperty("kind").GetString() == "text")
                    .Select(x => x.GetProperty("text").GetString()).ToArray();
                foreach (var value in new[] { "85%", "72%", "64%", "90%" }) Assert.Contains(value, labels);
                Assert.Contains(labels, x => x.Contains("37.0")); Assert.Contains(labels, x => x.Contains("-3.1"));
                foreach (var command in document.RootElement.EnumerateArray())
                {
                    var rect = command.GetProperty("rect");
                    Assert.InRange(rect.GetProperty("x").GetSingle(), 20, 20 + HudLayout.Width(style));
                    Assert.True(rect.GetProperty("x").GetSingle() + rect.GetProperty("width").GetSingle() <= 20 + HudLayout.Width(style));
                    Assert.InRange(rect.GetProperty("y").GetSingle(), 0, Screen.height - 20);
                    Assert.True(rect.GetProperty("y").GetSingle() + rect.GetProperty("height").GetSingle() <= Screen.height - 20);
                }
                var output = Environment.GetEnvironmentVariable("DVSURVIVAL_HUD_PREVIEW_DIR");
                if (!string.IsNullOrEmpty(output) && !warning)
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, $"style-{style}-{(english ? "en" : "ru")}.json"), json);
                }
                }
            }
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void NumericValuesNeverCoverVisualIndicators(int style)
        {
            var commands = new Mod.SurvivalHud().Record(style, false, false);
            var json = JsonSerializer.Serialize(commands, new JsonSerializerOptions { IncludeFields = true });
            using (var document = JsonDocument.Parse(json))
            {
                var all = document.RootElement.EnumerateArray().ToArray();
                var values = all.Where(x => x.GetProperty("kind").GetString() == "text" &&
                    (x.GetProperty("text").GetString().EndsWith("%") ||
                     x.GetProperty("text").GetString().Contains("37.0") ||
                     x.GetProperty("text").GetString().Contains("-3.1"))).ToArray();
                var visuals = all.Where(x =>
                {
                    var kind = x.GetProperty("kind").GetString();
                    if (kind == "ring" || kind == "dial" || kind == "thermal" || kind == "icon") return true;
                    if (kind != "solid") return false;
                    var rect = x.GetProperty("rect");
                    var height = rect.GetProperty("height").GetSingle();
                    var width = rect.GetProperty("width").GetSingle();
                    return (height <= 10f && width <= 150f) ||
                        (width <= 2.1f && height <= 12.1f);
                }).ToArray();
                // The supplied classic-HUD reference places the numeric label rect exactly at
                // the dial edge. Georgia's built-in top inset leaves the visible glyphs clear.
                var clearance = style == HudLayout.Legacy ? 0f : 10f;
                foreach (var value in values)
                foreach (var visual in visuals)
                {
                    // Rings intentionally contain the percentage. The dedicated test below
                    // checks its placement inside the hollow center and below the icon.
                    var percentageInRing = (style == 0 || style == 4) && value.GetProperty("text").GetString().EndsWith("%");
                    if (percentageInRing && (visual.GetProperty("kind").GetString() == "ring" ||
                        visual.GetProperty("kind").GetString() == "dial")) continue;
                    Assert.False(IntersectsWithClearance(value.GetProperty("rect"),
                            visual.GetProperty("rect"), percentageInRing ? 1f : clearance),
                        $"Numeric text overlaps or violates the intended clearance from {visual.GetProperty("kind").GetString()} in style {style}.");
                }
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(4, false)]
        [InlineData(4, true)]
        public void RingsAndBrassMoveTogetherTowardsFixedLabels(int style, bool english)
        {
            var commands = new Mod.SurvivalHud().Record(style, false, english);
            using (var document = JsonDocument.Parse(JsonSerializer.Serialize(commands,
                new JsonSerializerOptions { IncludeFields = true })))
            {
            var all = document.RootElement.EnumerateArray().ToArray();
            var rings = all.Where(x => x.GetProperty("kind").GetString() == (style == 0 ? "ring" : "dial")).ToArray();
            var numbers = all.Where(x => x.GetProperty("kind").GetString() == "text" &&
                x.GetProperty("text").GetString().EndsWith("%")).ToArray();
            var icons = all.Where(x => x.GetProperty("kind").GetString() == "icon" &&
                x.GetProperty("index").GetInt32() < 4).ToArray();
            Assert.Equal(4, numbers.Length);
            var names = english ? new[] { "Health", "Food", "Water", "Rest" } :
                new[] { "Здоровье", "Сытость", "Вода", "Сон" };
            for (var i = 0; i < 4; i++)
            {
                var dx = 29f + 66f * i;
                var y = 240f - HudLayout.Height(style);
                AssertRect(rings[i].GetProperty("rect"), dx, y + (style == 0 ? 30f : 33f), 62f, 62f);
                AssertRect(icons[i].GetProperty("rect"), dx + 21f, y + (style == 0 ? 41f : 46f), 20f, 20f);
                AssertRect(numbers[i].GetProperty("rect"), dx + 11f, y + (style == 0 ? 62f : 67f), 40f, 16f);
                var name = all.Single(x => x.GetProperty("kind").GetString() == "text" &&
                    x.GetProperty("text").GetString() == names[i]).GetProperty("rect");
                AssertRect(name, dx - 2f, y + (style == 0 ? 102f : 105f), 66f, style == 0 ? 16f : 17f);
                Assert.Equal(10f, name.GetProperty("y").GetSingle() -
                    rings[i].GetProperty("rect").GetProperty("y").GetSingle() - 62f);
                Assert.Equal("center", numbers[i].GetProperty("style").GetProperty("align").GetString());
                Assert.Equal(style == 0 ? 13 : 12, numbers[i].GetProperty("style").GetProperty("size").GetInt32());
            }
            }
        }

        [Fact]
        public void LegacyLayoutMatchesSuppliedReferenceAndKeepsDecorationsFixed()
        {
            var commands = new Mod.SurvivalHud().Record(HudLayout.Legacy, false, false);
            var json = JsonSerializer.Serialize(commands, new JsonSerializerOptions { IncludeFields = true });
            using (var document = JsonDocument.Parse(json))
            {
                var all = document.RootElement.EnumerateArray().ToArray();
                var dials = all.Where(x => x.GetProperty("kind").GetString() == "dial")
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                Assert.Equal(4, dials.Length);

                var percentages = all.Where(x => x.GetProperty("kind").GetString() == "text" &&
                    x.GetProperty("text").GetString().EndsWith("%"))
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                Assert.Equal(4, percentages.Length);

                var categoryNames = new[] { "Здоровье", "Сытость", "Вода", "Сон" };
                var labels = all.Where(x => x.GetProperty("kind").GetString() == "text" &&
                    categoryNames.Contains(x.GetProperty("text").GetString()))
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                Assert.Equal(4, labels.Length);

                var needPlates = all.Where(x => x.GetProperty("kind").GetString() == "plate" &&
                    x.GetProperty("rect").GetProperty("width").GetSingle() == 80f)
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                var needIcons = all.Where(x => x.GetProperty("kind").GetString() == "icon" &&
                    x.GetProperty("index").GetInt32() >= 0 && x.GetProperty("index").GetInt32() <= 3)
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                Assert.Equal(4, needPlates.Length);
                Assert.Equal(4, needIcons.Length);

                for (var i = 0; i < 4; i++)
                {
                    var dial = dials[i].GetProperty("rect");
                    var percentage = percentages[i].GetProperty("rect");
                    var label = labels[i].GetProperty("rect");
                    AssertRect(needPlates[i].GetProperty("rect"), 28f + 86f * i, 516f, 80f, 134f);
                    AssertRect(dial, 28f + 86f * i, 516f, 80f, 80f);
                    AssertRect(needIcons[i].GetProperty("rect"), 52f + 86f * i, 538f, 32f, 32f);
                    AssertRect(percentage, 32f + 86f * i, 596f, 72f, 18f);
                    AssertRect(label, 30f + 86f * i, 610f, 76f, 19f);
                    Assert.Equal(0f, percentage.GetProperty("y").GetSingle() -
                        (dial.GetProperty("y").GetSingle() + dial.GetProperty("height").GetSingle()));
                    Assert.Equal(14f, label.GetProperty("y").GetSingle() -
                        percentage.GetProperty("y").GetSingle());
                }

                var ambient = all.Single(x => x.GetProperty("kind").GetString() == "text" &&
                    x.GetProperty("text").GetString().Contains("-3.1"));
                var body = all.Single(x => x.GetProperty("kind").GetString() == "text" &&
                    x.GetProperty("text").GetString().Contains("37.0"));
                var thermal = all.Single(x => x.GetProperty("kind").GetString() == "thermal");
                var ambientPlate = all.Single(x => x.GetProperty("kind").GetString() == "plate" &&
                    x.GetProperty("rect").GetProperty("width").GetSingle() == 338f);
                var bodyPlate = all.Single(x => x.GetProperty("kind").GetString() == "plate" &&
                    x.GetProperty("rect").GetProperty("width").GetSingle() == 312f);
                var bodyIcon = all.Single(x => x.GetProperty("kind").GetString() == "icon" &&
                    x.GetProperty("index").GetInt32() == 4);
                var thermalMarker = all.Single(x => x.GetProperty("kind").GetString() == "icon" &&
                    x.GetProperty("index").GetInt32() == 7);
                AssertRect(ambientPlate.GetProperty("rect"), 28f, 652f, 338f, 36f);
                AssertRect(ambient.GetProperty("rect"), 42f, 657f, 310f, 25f);
                AssertRect(bodyPlate.GetProperty("rect"), 484f, 626f, 312f, 62f);
                AssertRect(bodyIcon.GetProperty("rect"), 496f, 637f, 34f, 34f);
                AssertRect(body.GetProperty("rect"), 542f, 630f, 236f, 20f);
                AssertRect(thermal.GetProperty("rect"), 542f, 668f, 236f, 6f);
                AssertRect(thermalMarker.GetProperty("rect"), 651f, 661f, 18f, 20f);
            }
        }

        private static void AssertRect(JsonElement rect, float x, float y, float width, float height)
        {
            Assert.Equal(x, rect.GetProperty("x").GetSingle());
            Assert.Equal(y, rect.GetProperty("y").GetSingle());
            Assert.Equal(width, rect.GetProperty("width").GetSingle());
            Assert.Equal(height, rect.GetProperty("height").GetSingle());
        }

        private static bool IntersectsWithClearance(JsonElement a, JsonElement b, float clearance)
        {
            var ax=a.GetProperty("x").GetSingle();var ay=a.GetProperty("y").GetSingle();
            var aw=a.GetProperty("width").GetSingle();var ah=a.GetProperty("height").GetSingle();
            var bx=b.GetProperty("x").GetSingle();var by=b.GetProperty("y").GetSingle();
            var bw=b.GetProperty("width").GetSingle();var bh=b.GetProperty("height").GetSingle();
            return ax < bx+bw+clearance && ax+aw > bx-clearance &&
                ay < by+bh+clearance && ay+ah > by-clearance;
        }
    }
}
