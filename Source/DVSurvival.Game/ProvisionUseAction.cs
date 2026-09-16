using System;
using DV.InventorySystem;
using DV.Utils;
using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Exists only on a held item while it is in use; no polling of shop stock.
    internal sealed class ProvisionUseAction : MonoBehaviour
    {
        internal static ProvisionUseAction Active { get; private set; }
        private Action complete;
        private ProvisionUseProgress progress;
        private NativeProvisionToken portion;
        private float desiredUnits, nextSend;
        internal bool IsPortion => portion != null;
        internal bool IsWorking => portion == null || Input.GetMouseButton(0);
        internal float Progress => portion != null ? 1f - portion.UsedUnits / 1000f : progress == null ? 0f : progress.Fraction;
        internal ProvisionKind Kind { get; private set; }
        internal static void BeginPortion(NativeProvisionToken token)
        {
            if (Active != null) return;
            var action = token.gameObject.AddComponent<ProvisionUseAction>();
            action.Kind = token.Kind;
            action.portion = token;
            action.desiredUnits = token.UsedUnits;
            Active = action;
        }

        internal static void Begin(GameObject item, ProvisionKind kind, Action onComplete)
        {
            if (Active != null) return;
            var action = item.AddComponent<ProvisionUseAction>();
            action.Kind = kind;
            action.progress = new ProvisionUseProgress(kind);
            action.complete = onComplete;
            Active = action;
        }

        private void Update()
        {
            var runtime = Main.Runtime;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (runtime == null || !runtime.IsSessionReady || !runtime.HasConfirmedLocalState || runtime.IsRespawning ||
                runtime.IsHomeTravelPending || UnloadWatcher.isUnloading ||
                LoadingScreenManager.IsLoading || FastTravelController.IsFastTravelling ||
                inventory == null || inventory.GetEquipSlotForItem(gameObject) < 0 ||
                Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            { FlushPortion(); Cancel(); return; }
            // Pausing does not let a pending action finish in the background.
            if (runtime.IsGamePaused() || Cursor.visible) return;
            if (portion != null)
            {
                if (portion.LastResult == SurvivalResultCode.NotNeeded)
                { desiredUnits = portion.UsedUnits; Cancel(); return; }
                desiredUnits = Mathf.Max(desiredUnits, portion.UsedUnits);
                if (Input.GetMouseButton(0))
                    desiredUnits = Mathf.Min(1000f, desiredUnits + Time.deltaTime * 1000f / ProvisionUseTiming.Seconds(Kind));
                if (Time.realtimeSinceStartup >= nextSend) FlushPortion();
                return;
            }
            if (!progress.Advance(Time.deltaTime)) return;
            var callback = complete;
            Cancel();
            callback?.Invoke();
        }

        private void Cancel()
        {
            complete = null;
            progress?.Cancel();
            if (Active == this) Active = null;
            enabled = false;
            Destroy(this);
        }
        private void FlushPortion()
        {
            if (portion == null) return;
            var target = Mathf.Clamp(Mathf.FloorToInt(desiredUnits), 0, 1000);
            if (target <= portion.UsedUnits) return;
            nextSend = Time.realtimeSinceStartup + .3f;
            portion.RequestPortion(target);
        }
        private void OnDisable()
        {
            if (Active == this) Active = null;
            complete = null;
            progress?.Cancel();
            Destroy(this);
        }
    }
}
