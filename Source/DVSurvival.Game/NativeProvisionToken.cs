using System;
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
        public string Identity { get { return identity; } }

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
            if (spent && item != null) RemoveConsumed();
        }
        private JObject Save(JObject data)
        {
            data["DVSurvival.ItemId"] = identity;
            data["DVSurvival.Consumed"] = spent;
            return data;
        }
        private void Use()
        {
            var runtime = Main.Runtime;
            if (spent || runtime == null || !runtime.HasConfirmedLocalState || Time.realtimeSinceStartup < pendingUntil) return;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory == null || inventory.GetEquipSlotForItem(gameObject) < 0) return;
            pendingUntil = Time.realtimeSinceStartup + 5f;
            runtime.RequestPhysicalConsume(this);
        }
        public void Confirm(SurvivalResultCode result)
        {
            pendingUntil = 0f;
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
        private void OnDestroy()
        {
            if (item != null) item.Used -= Use;
            if (save != null) { save.ItemSaveDataLoaded -= Load; save.ItemSaveDataRequested -= Save; }
        }
    }
}
