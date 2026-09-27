using DV.UI.LocoHUD;
using DV.Utils;
using DVSurvival.Mod;
using UnityEngine;
using Xunit;

namespace UnityEngine
{
    internal sealed class GameObject { public bool activeInHierarchy = true; }
    internal struct Vector2 { public float y; }
    internal sealed class RectTransform
    {
        public GameObject gameObject = new GameObject();
        public Vector2 anchoredPosition;
        public Rect rect = new Rect(0, 0, 800, 200);
    }
}
namespace DV.Utils
{
    internal static class SingletonBehaviour<T> { [System.ThreadStatic] public static T Instance; }
}
namespace DV.UI.LocoHUD
{
    internal sealed class HUDLocoControls { public RectTransform hudRect = new RectTransform(); }
    internal sealed class HUDManager
    {
        public bool isActiveAndEnabled = true, locoHUDVisible, cursorButtonsVisible;
        public HUDLocoControls currentHUD;
    }
}
internal static class VRManager
{
    [System.ThreadStatic] internal static bool Enabled;
    public static bool IsVREnabled() => Enabled;
}

namespace DVSurvival.Tests
{
    public sealed class NativeLocoHudVisibilityTests
    {
        private readonly NativeLocoHudVisibility visibility = new NativeLocoHudVisibility();
        private readonly HUDManager manager = new HUDManager();

        public NativeLocoHudVisibilityTests()
        {
            SingletonBehaviour<HUDManager>.Instance = manager;
            VRManager.Enabled = false;
        }

        private HUDLocoControls Open(float y = 0)
        {
            var panel = new HUDLocoControls();
            panel.hudRect.anchoredPosition = new Vector2 { y = y };
            manager.currentHUD = panel;
            manager.locoHUDVisible = manager.cursorButtonsVisible = true;
            return panel;
        }

        [Fact]
        public void NormalPlayAndMissingManagerDoNotHideSurvival()
        {
            Assert.False(visibility.BlocksSurvivalHud());
            SingletonBehaviour<HUDManager>.Instance = null;
            Assert.False(visibility.BlocksSurvivalHud());
        }

        [Fact]
        public void OpeningHidesSurvivalBeforeAnimationReachesScreen()
        {
            Open(-300);
            Assert.True(visibility.BlocksSurvivalHud());
        }

        [Theory]
        [InlineData(-199, true)] [InlineData(-200, false)] [InlineData(-300, false)]
        public void CloseByAnyControlWaitsForPanelToLeaveScreen(float y, bool expected)
        {
            var panel = Open();
            Assert.True(visibility.BlocksSurvivalHud());
            manager.currentHUD = null;
            manager.locoHUDVisible = false;
            panel.hudRect.anchoredPosition = new Vector2 { y = y };
            Assert.Equal(expected, visibility.BlocksSurvivalHud());
        }

        [Fact]
        public void MouseModeOffAndOffscreenPanelRestoreSurvival()
        {
            var panel = Open();
            Assert.True(visibility.BlocksSurvivalHud());
            manager.cursorButtonsVisible = false;
            Assert.True(visibility.BlocksSurvivalHud());
            panel.hudRect.anchoredPosition = new Vector2 { y = -300 };
            Assert.False(visibility.BlocksSurvivalHud());
            VRManager.Enabled = true;
            Assert.True(visibility.BlocksSurvivalHud());
        }

        [Fact]
        public void SwitchingLocomotivesRetainsStillClosingOldPanel()
        {
            var old = Open();
            Assert.True(visibility.BlocksSurvivalHud());
            Open(-300);
            manager.locoHUDVisible = false;
            Assert.True(visibility.BlocksSurvivalHud());
            old.hudRect.gameObject.activeInHierarchy = false;
            Assert.False(visibility.BlocksSurvivalHud());
        }

        [Fact]
        public void DisabledManagerClearsPreviousPanels()
        {
            Open();
            Assert.True(visibility.BlocksSurvivalHud());
            manager.isActiveAndEnabled = false;
            Assert.False(visibility.BlocksSurvivalHud());
            manager.isActiveAndEnabled = true;
            manager.currentHUD = null;
            Assert.False(visibility.BlocksSurvivalHud());
        }
    }
}
