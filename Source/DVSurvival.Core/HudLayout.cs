using System;

namespace DVSurvival.Core
{
    // Stable numeric IDs are persisted in UMM Settings.xml.
    public static class HudLayout
    {
        public const int Count = 6;
        public const int Legacy = 5;
        public static int Validate(int style) { return style >= 0 && style < Count ? style : 0; }
        public static float Width(int style)
        {
            switch (Validate(style)) { case 1: return 292; case 2: return 520; case 3: return 248; case Legacy: return 1140; default: return 280; }
        }
        public static float Height(int style)
        {
            switch (Validate(style)) { case 1: return 188; case 2: return 74; case 3: return 124; case 4: return 192; case Legacy: return 566; default: return 172; }
        }
        public static float FitScale(int style, float requested, float screenWidth, float screenHeight, float compactScale = 1f)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested)) requested = 1;
            requested = Math.Max(0.65f, Math.Min(1.75f, requested));
            if (Validate(style) != Legacy)
            {
                if (float.IsNaN(compactScale) || float.IsInfinity(compactScale)) compactScale = 1.75f;
                requested *= Math.Max(0.75f, Math.Min(3f, compactScale));
            }
            // Include outer padding and a two-line warning above the panel.
            return Math.Max(0.1f, Math.Min(requested,
                Math.Min(screenWidth / (Width(style) + 40), screenHeight / (Height(style) + 84))));
        }
    }
}
