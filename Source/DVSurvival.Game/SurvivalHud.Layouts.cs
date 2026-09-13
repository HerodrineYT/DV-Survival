using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal sealed partial class SurvivalHud
    {
        private readonly float[] needFractions = new float[4];
        private readonly string[] needValues = new string[4];
        private readonly string[] needNames = new string[4];
        private static readonly Color[] NeedColors = {
            new Color(0.94f, 0.26f, 0.28f), new Color(1f, 0.63f, 0.18f),
            new Color(0.20f, 0.63f, 1f), new Color(0.62f, 0.43f, 1f) };
        private GUIStyle modernLeft, modernCenter, modernNumber, modernSmall;
        private static readonly Color[] LegacyColors = {
            new Color(.76f, .42f, .35f), new Color(.82f, .60f, .28f),
            new Color(.26f, .67f, .82f), new Color(.55f, .47f, .81f) };
        private string compactBodyText = string.Empty;

        private void DrawLayout(float scale)
        {
            var style = HudLayout.Validate(settings.HudStyle);
            if (style == HudLayout.Legacy) { DrawLegacy(scale); return; }
            var width = HudLayout.Width(style);
            const float x = 20;
            var y = Screen.height / scale - HudLayout.Height(style) - 20;
            if (style == 4) DrawPlate(new Rect(x, y, width, HudLayout.Height(style)));
            else FlatPanel(new Rect(x, y, width, HudLayout.Height(style)));
            switch (style)
            {
                case 1: DrawHorizontalBars(x, y); break;
                case 2: DrawRibbon(x, y); break;
                case 3: DrawMinimal(x, y); break;
                case 4: DrawCompactBrass(x, y); break;
                default: DrawModernRings(x, y); break;
            }
            // Only abnormal conditions occupy extra space, in a small non-interactive notice.
            if (!string.IsNullOrEmpty(warningText))
            {
                var rect = new Rect(x, y - 44, Mathf.Min(width, 292), 38);
                if (style == 4) DrawPlate(rect); else FlatPanel(rect);
                artwork.DrawIcon(new Rect(x + 8, y - 36, 21, 21), 5, Brass);
                GUI.Label(new Rect(x + 35, y - 42, rect.width - 43, 17), warningTitle, modernSmall);
                GUI.Label(new Rect(x + 35, y - 25, rect.width - 43, 16), warningText, modernSmall);
            }
        }

        private static void FlatPanel(Rect rect)
        {
            DrawSolid(rect, new Color(0.045f, 0.068f, 0.095f, 0.83f));
            DrawSolid(new Rect(rect.x, rect.y, rect.width, 1), new Color(0.45f, 0.58f, 0.70f, 0.45f));
        }

        private void NeedIcon(float x, float y, int i, float size = 20)
        {
            artwork.DrawIcon(new Rect(x, y, size, size), i, NeedColors[i]);
        }

        private static void Progress(Rect rect, float fraction, Color color)
        {
            DrawSolid(rect, new Color(0.19f, 0.23f, 0.28f, 1f));
            DrawSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
        }

        private void Thermal(float x, float y, float width, bool brass = false)
        {
            var text = brass ? hudTextStyle : modernSmall;
            var color = cachedBodyTemperature >= 38.5f ? NeedColors[0] :
                (cachedBodyTemperature < 35f ? NeedColors[2] : new Color(0.4f, 0.9f, 0.8f));
            artwork.DrawIcon(new Rect(x, y + 1, 17, 22), 4, color);
            GUI.Label(new Rect(x + 29, y, width - 29, 18), compactBodyText, text);
            GUI.Label(new Rect(x + 29, y + 22, width - 29, 18), ambientText, text);
            var track = new Rect(x + 29, y + 54, width - 32, 4);
            artwork.DrawThermal(track);
            DrawSolid(new Rect(track.x + track.width * temperatureFraction - 1, y + 50, 2, 12), Color.white);
        }

        private void DrawModernRings(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + 9 + i * 66;
                artwork.DrawRing(new Rect(dx, y + 30, 62, 62), needFractions[i], NeedColors[i]);
                NeedIcon(dx + 21, y + 41, i);
                // Move the whole indicator together, leaving ten pixels before its name.
                GUI.Label(new Rect(dx + 11, y + 62, 40, 16), needValues[i], modernNumber);
                GUI.Label(new Rect(dx - 2, y + 102, 66, 16), needNames[i], modernCenter);
            }
            GUI.Label(new Rect(x + 12, y + 126, 126, 18), compactBodyText, modernSmall);
            GUI.Label(new Rect(x + 142, y + 126, 130, 18), ambientText, modernSmall);
            artwork.DrawThermal(new Rect(x + 12, y + 160, 256, 4));
            DrawSolid(new Rect(x + 12 + 256 * temperatureFraction - 1, y + 156, 2, 12), Color.white);
        }

        private void DrawHorizontalBars(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dy = y + 8 + i * 28;
                NeedIcon(x + 10, dy + 1, i);
                GUI.Label(new Rect(x + 39, dy, 83, 22), needNames[i], modernLeft);
                Progress(new Rect(x + 123, dy + 7, 104, 9), needFractions[i], NeedColors[i]);
                GUI.Label(new Rect(x + 239, dy, 45, 22), needValues[i], modernNumber);
            }
            Thermal(x + 12, y + 120, 268);
        }

        private void DrawRibbon(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + 10 + i * 77;
                NeedIcon(dx, y + 10, i);
                GUI.Label(new Rect(dx + 30, y + 9, 37, 21), needValues[i], modernNumber);
                Progress(new Rect(dx, y + 40, 65, 4), needFractions[i], NeedColors[i]);
                GUI.Label(new Rect(dx, y + 48, 70, 16), needNames[i], modernSmall);
            }
            Thermal(x + 324, y + 8, 184);
        }

        private void DrawMinimal(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + 10 + i * 59;
                NeedIcon(dx + 16, y + 8, i, 18);
                GUI.Label(new Rect(dx, y + 36, 50, 18), needValues[i], modernCenter);
                Progress(new Rect(dx, y + 64, 50, 3), needFractions[i], NeedColors[i]);
            }
            GUI.Label(new Rect(x + 10, y + 78, 125, 18), compactBodyText, modernLeft);
            GUI.Label(new Rect(x + 10, y + 102, 228, 18), ambientText, modernSmall);
            // Short body-temperature scale sits beside its exact value.
            artwork.DrawThermal(new Rect(x + 147, y + 85, 90, 4));
            DrawSolid(new Rect(x + 147 + 90 * temperatureFraction - 1, y + 82, 2, 10), Color.white);
        }

        private void DrawLegacy(float scale)
        {
            var width = Screen.width / scale;
            var bottom = Screen.height / scale - 32;
            const float x = 28;
            for (var i = 0; i < 4; i++)
            {
                var dx = x + i * 86;
                DrawPlate(new Rect(dx, bottom - 172, 80, 134));
                artwork.DrawDial(new Rect(dx, bottom - 172, 80, 80), needFractions[i], LegacyColors[i]);
                artwork.DrawIcon(new Rect(dx + 24, bottom - 150, 32, 32), i, LegacyColors[i]);
                GUI.Label(new Rect(dx + 4, bottom - 92, 72, 18), needValues[i], labelStyle);
                GUI.Label(new Rect(dx + 2, bottom - 78, 76, 19), needNames[i], labelStyle);
            }
            if (!string.IsNullOrEmpty(warningText))
            {
                var extra = warningText.Length > 36 ? 18 : 0;
                DrawPlate(new Rect(x, bottom - 236 - extra, 338, 50 + extra));
                artwork.DrawIcon(new Rect(x + 12, bottom - 227 - extra, 30, 30), 5, Brass);
                GUI.Label(new Rect(x + 54, bottom - 233 - extra, 270, 23), warningTitle, legacyTitleStyle);
                GUI.Label(new Rect(x + 54, bottom - 212 - extra, 270, 20 + extra), warningText, legacyWarningStyle);
            }
            DrawPlate(new Rect(x, bottom - 36, 338, 36));
            GUI.Label(new Rect(x + 14, bottom - 31, 310, 25), ambientText, hudTextStyle);
            var tx = (width - 312) * 0.5f;
            DrawPlate(new Rect(tx, bottom - 62, 312, 62));
            artwork.DrawIcon(new Rect(tx + 12, bottom - 51, 34, 34), 4, Brass);
            GUI.Label(new Rect(tx + 58, bottom - 58, 236, 20), legacyBodyText, legacyTitleStyle);
            var track = new Rect(tx + 58, bottom - 20, 236, 6);
            artwork.DrawThermal(track);
            artwork.DrawIcon(new Rect(track.x + track.width * temperatureFraction - 9,
                track.y - 7, 18, 20), 7, Color.white);
        }

        private void DrawCompactBrass(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + 9 + i * 66;
                artwork.DrawDial(new Rect(dx, y + 33, 62, 62), needFractions[i], NeedColors[i]);
                NeedIcon(dx + 21, y + 46, i);
                GUI.Label(new Rect(dx + 11, y + 67, 40, 16), needValues[i], labelStyle);
                GUI.Label(new Rect(dx - 2, y + 105, 66, 17), needNames[i], labelStyle);
            }
            var color = cachedBodyTemperature >= 38.5f ? NeedColors[0] :
                (cachedBodyTemperature < 35f ? NeedColors[2] : new Color(0.4f, 0.9f, 0.8f));
            artwork.DrawIcon(new Rect(x + 12, y + 133, 17, 22), 4, color);
            GUI.Label(new Rect(x + 41, y + 132, 105, 18), compactBodyText, hudTextStyle);
            GUI.Label(new Rect(x + 151, y + 132, 119, 18), ambientText, hudTextStyle);
            artwork.DrawThermal(new Rect(x + 41, y + 164, 229, 4));
            DrawSolid(new Rect(x + 41 + 229 * temperatureFraction - 1, y + 160, 2, 12), Color.white);
        }
    }
}
