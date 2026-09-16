using System;
using System.Collections;
using DV.CabControls;
using DV.InventorySystem;
using DV.Utils;
using DVSurvival.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Native shop items: one saved object, one persistent consume ID, one use.
    // No Update, scene searches, private shop transaction or virtual stock grant.
    internal sealed class NativeProvisionToken : MonoBehaviour
    {
        public ProvisionKind Kind;
        private string identity;
        private bool spent;
        private float pendingUntil;
        private ItemBase item;
        private ItemSaveData save;
        private int requestedUnits;
        private Coroutine portionDrain;
        public string Identity { get { return identity; } }
        public int UsedUnits { get; private set; }
        public bool IsPending => Time.realtimeSinceStartup < pendingUntil;
        public SurvivalResultCode LastResult { get; private set; }

        private void Awake()
        {
            if (!enabled) return; // Legacy stack adapter disables this before activation.
            identity = Guid.NewGuid().ToString("D");
            save = GetComponent<ItemSaveData>();
            if (save != null) { save.ItemSaveDataLoaded += Load; save.ItemSaveDataRequested += Save; }
        }
        private void Start()
        {
            if (!enabled) return;
            item = GetComponent<ItemBase>();
            if (item != null) item.Used += Use;
            if (spent) RemoveConsumed();
        }
        private void Load(JObject data)
        {
            Guid parsed;
            var value = (string)data["DVSurvival.ItemId"];
            if (Guid.TryParse(value, out parsed) && parsed != Guid.Empty) identity = parsed.ToString("D");
            spent = (bool?)data["DVSurvival.Consumed"] ?? false;
            UsedUnits = Mathf.Clamp((int?)data["DVSurvival.UsedUnits"] ?? 0, 0, PartialProvisionLedger.FullUnits);
            if (UsedUnits == PartialProvisionLedger.FullUnits) spent = true;
            if (spent && item != null) RemoveConsumed();
        }
        private JObject Save(JObject data)
        {
            data["DVSurvival.ItemId"] = identity;
            data["DVSurvival.Consumed"] = spent;
            data["DVSurvival.UsedUnits"] = UsedUnits;
            return data;
        }
        private void Use()
        {
            var runtime = Main.Runtime;
            if (spent || runtime == null || !runtime.HasConfirmedLocalState || Time.realtimeSinceStartup < pendingUntil) return;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory == null || inventory.GetEquipSlotForItem(gameObject) < 0) return;
            if (PartialProvisionLedger.Supports(Kind))
            {
                LastResult = SurvivalResultCode.None;
                ProvisionUseAction.BeginPortion(this);
                return;
            }
            ProvisionUseAction.Begin(gameObject, Kind, () =>
            {
                if (spent || runtime != Main.Runtime || !runtime.HasConfirmedLocalState) return;
                pendingUntil = Time.realtimeSinceStartup + 5f;
                runtime.RequestPhysicalConsume(this);
            });
        }
        public void RequestPortion(int targetUnits)
        {
            if (spent || Main.Runtime == null || !Main.Runtime.HasConfirmedLocalState) return;
            requestedUnits = Math.Max(requestedUnits, Mathf.Clamp(targetUnits, UsedUnits, 1000));
            if (portionDrain == null) portionDrain = StartCoroutine(DrainPortions(Main.Runtime));
        }
        private IEnumerator DrainPortions(SurvivalRuntime owner)
        {
            yield return null;
            var nextRequest = 0f;
            var wait = new WaitForSecondsRealtime(.05f);
            while (!spent && requestedUnits > UsedUnits && owner == Main.Runtime && owner.HasConfirmedLocalState)
            {
                if (LastResult != SurvivalResultCode.None && LastResult != SurvivalResultCode.Success &&
                    LastResult != SurvivalResultCode.RateLimited) break;
                if (!IsPending && Time.realtimeSinceStartup >= nextRequest)
                {
                    pendingUntil = Time.realtimeSinceStartup + 5f;
                    nextRequest = Time.realtimeSinceStartup + .3f;
                    LastResult = SurvivalResultCode.None;
                    owner.RequestPhysicalConsume(this, requestedUnits);
                }
                yield return wait;
            }
            requestedUnits = UsedUnits;
            portionDrain = null;
        }
        public void Confirm(SurvivalResultCode result, int usedUnits = -1)
        {
            pendingUntil = 0f;
            LastResult = result;
            if (PartialProvisionLedger.Supports(Kind))
            {
                if (usedUnits >= 0 && usedUnits <= PartialProvisionLedger.FullUnits)
                    UsedUnits = Math.Max(UsedUnits, usedUnits);
                if (UsedUnits < PartialProvisionLedger.FullUnits) return;
            }
            if (result != SurvivalResultCode.Success) return;
            spent = true;
            RemoveConsumed();
        }
        private void RemoveConsumed()
        {
            try
            {
                var inventory = SingletonBehaviour<Inventory>.Instance;
                if (item != null && item.IsGrabbed()) item.ForceEndInteraction();
                if (inventory != null && !UnloadWatcher.isUnloading)
                {
                    if (inventory.Contains(gameObject) || inventory.GetEquipSlotForItem(gameObject) >= 0)
                        inventory.DropItemFromHandsOrInventory(gameObject);
                    inventory.PurgeFromInventory(gameObject);
                }
            }
            catch (Exception error) { Debug.LogWarning("DVSurvival consumed item cleanup: " + error.Message); }
            finally { Destroy(gameObject); }
        }
        private void OnDisable()
        {
            if (portionDrain != null) StopCoroutine(portionDrain);
            portionDrain = null;
            requestedUnits = UsedUnits;
        }
        private void OnDestroy()
        {
            if (item != null) item.Used -= Use;
            if (save != null) { save.ItemSaveDataLoaded -= Load; save.ItemSaveDataRequested -= Save; }
        }
    }
}
