using UnityEngine;

namespace DVSurvival.Mod
{
    internal sealed partial class SurvivalHud
    {
        private GUIStyle modernOverlayTitle, brassOverlayTitle;

        private void DrawToast(Rect box, string message)
        {
            DrawStyledPanel(box);
            GUI.Label(new Rect(box.x + 12, box.y + 5, box.width - 24, box.height - 10),
                message, UsesBrassFrame ? brassOverlayTitle : modernOverlayTitle);
        }

        private void DrawProvisionStatus(Rect box, string title, float progress, string hint)
        {
            DrawStyledPanel(box);
            // Text styles have no background: the item title must not create a second frame.
            GUI.Label(new Rect(box.x + 8, box.y + 3, box.width - 16, 25), title,
                UsesBrassFrame ? brassOverlayTitle : modernOverlayTitle);
            Progress(new Rect(box.x + 12, box.y + 32, box.width - 24, 5), progress,
                UsesBrassFrame ? Brass : new Color(.45f, .70f, .90f));
            GUI.Label(new Rect(box.x + 12, box.y + 40, box.width - 24, 20), hint,
                UsesBrassFrame ? labelStyle : modernCenter);
        }
    }
}
