using System.Collections.Generic;
using DV.UI.LocoHUD;
using DV.Utils;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Follow native visibility, not the F4 key: the panel can also be closed by mouse,
    // remapped controls, changing locomotives or leaving a cab.
    internal sealed class NativeLocoHudVisibility
    {
        private readonly List<RectTransform> panels = new List<RectTransform>(2);

        public bool BlocksSurvivalHud()
        {
            var manager = SingletonBehaviour<HUDManager>.Instance;
            if (manager == null || !manager.isActiveAndEnabled)
            {
                panels.Clear();
                return false;
            }

            var current = manager.currentHUD == null ? null : manager.currentHUD.hudRect;
            if (current != null && !panels.Contains(current)) panels.Add(current);
            var visible = current != null && manager.locoHUDVisible &&
                (manager.cursorButtonsVisible || VRManager.IsVREnabled());
            for (var i = panels.Count - 1; i >= 0; i--)
            {
                var panel = panels[i];
                // HUDManager slides native bottom-anchored panels out, then disables them
                // only once anchored Y + local top is below zero. Keep the same boundary
                // so Survival cannot reappear over the closing animation.
                var onScreen = panel != null && panel.gameObject.activeInHierarchy &&
                    panel.anchoredPosition.y + panel.rect.yMax > 0f;
                visible |= onScreen;
                if (!onScreen && panel != current) panels.RemoveAt(i);
            }
            return visible;
        }
    }
}
