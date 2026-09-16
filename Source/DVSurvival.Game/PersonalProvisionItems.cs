using System;
using System.Collections;
using custom_item_mod;
using DV.CabControls;
using DV.InventorySystem;
using DV.ThingTypes;
using DV.Utils;
using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    // At most five local stack representations. Legacy save migration only; new items come exclusively from native checkout.
    // Personal counts remain the only authority, so lost/duplicated visuals cannot duplicate food.
    internal sealed class PersonalProvisionItems : MonoBehaviour
    {
        private readonly GameObject[] instances = new GameObject[6];
        private readonly bool[] pending = new bool[6];
        private SurvivalRuntime runtime;
        private float nextCheck;

        public void Initialize(SurvivalRuntime owner) { runtime = owner; }

        public void Take(ProvisionKind kind)
        {
            var index = (int)kind;
            if (index < 1 || index > 5 || runtime == null || !runtime.HasConfirmedLocalState) return;
            if (runtime.CurrentState.GetProvisionCount(kind) <= 0)
            {
                runtime.Notify(ModLocalization.Text("Сначала купите расходник.", "Buy a supply first."));
                return;
            }
            if (pending[index]) return;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory == null) return;
            if (instances[index] != null && inventory.Contains(instances[index]))
            {
                runtime.Notify(ModLocalization.Text("Этот расходник уже в обычном инвентаре.", "This supply is already in the regular inventory."));
                return;
            }
            if (!inventory.HasFreeSlots())
            {
                runtime.Notify(ModLocalization.Text("Обычный инвентарь заполнен. Старые запасы сохранены.", "Regular inventory is full. Legacy stock is safe."));
                return;
            }
            CustomItem item;
            if (!CustomProvisionCatalog.TryGet(kind, out item))
            {
                runtime.Notify(ModLocalization.Text("Каталог предметов ещё не загружен.", "Item catalogue is not loaded yet."));
                return;
            }
            Remove(index);
            pending[index] = true;
            StartCoroutine(CreateAndStore(kind, item));
        }

        private IEnumerator CreateAndStore(ProvisionKind kind, CustomItem item)
        {
            var index = (int)kind;
            GameObject instance = null;
            try { instance = CreateInstance(kind, item); }
            catch (Exception exception) { Debug.LogWarning("DVSurvival item creation: " + exception.Message); }
            if (instance == null)
            {
                pending[index] = false;
                Remove(index);
                runtime.Notify(ModLocalization.Text("Не удалось создать предмет. Старые запасы сохранены.", "Could not create item. Legacy stock is safe."));
                yield break;
            }
            // Let native item initialization finish before adding it to inventory.
            yield return null;
            pending[index] = false;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            bool added = false;
            try
            {
                added = instance != null && runtime != null && runtime.HasConfirmedLocalState && inventory != null &&
                    runtime.CurrentState.GetProvisionCount(kind) > 0 &&
                    instance.GetComponent<ItemBase>() != null && inventory.AddItemToInventory(instance) >= 0;
            }
            catch (Exception exception) { Debug.LogWarning("DVSurvival inventory: " + exception.Message); }
            if (!added)
            {
                Remove(index);
                if (runtime != null) runtime.Notify(ModLocalization.Text("Не удалось поместить предмет в инвентарь. Старые запасы сохранены.",
                    "Could not add the item to inventory. Legacy stock is safe."));
                yield break;
            }
            runtime.Notify(ModLocalization.Text("Расходник добавлен в обычный инвентарь. Возьмите его в руки и используйте.",
                "Supply added to regular inventory. Equip it and use it."));
        }

        private GameObject CreateInstance(ProvisionKind kind, CustomItem item)
        {
            // Inactive staging parent allows assigning the owner before native item initialization.
            var staging = new GameObject("DVSurvival item staging");
            staging.SetActive(false);
            try
            {
                var instance = Instantiate(item.ItemPrefab, staging.transform, false);
                instances[(int)kind] = instance;
                instance.GetComponent<NativeProvisionToken>().enabled = false;
                instance.GetComponent<DV.CabControls.Spec.Item>().excludeFromStorageSerialization =
                    StorageType.Inventory | StorageType.World | StorageType.LostAndFound |
                    StorageType.ItemContainers | StorageType.InstalledGadgets;
                var token = instance.AddComponent<PersonalProvisionToken>();
                token.Initialize(runtime, kind);
                instance.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
                instance.transform.position = PlayerManager.PlayerTransform.position;
                instance.transform.SetParent(WorldMover.OriginShiftParent, true);
                instance.SetActive(true);
                return instance;
            }
            finally { Destroy(staging); }
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 0.5f;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            for (int i = 1; i <= 5; i++)
            {
                if (instances[i] == null && !pending[i] && runtime != null && runtime.HasConfirmedLocalState &&
                    runtime.CurrentState.GetProvisionCount((ProvisionKind)i) > 0 && inventory != null && inventory.HasFreeSlots())
                    Take((ProvisionKind)i);
                if (instances[i] == null || pending[i]) continue;
                // Dropped representations retain legacy stock, not to another player's inventory.
                var held = inventory != null && inventory.GetEquipSlotForItem(instances[i]) >= 0;
                if (runtime == null || !runtime.HasConfirmedLocalState ||
                    runtime.CurrentState.GetProvisionCount((ProvisionKind)i) <= 0 ||
                    (!held && (inventory == null || !inventory.Contains(instances[i], false))))
                    Remove(i);
            }
        }

        public void Clear()
        {
            StopAllCoroutines();
            for (int i = 1; i <= 5; i++) { pending[i] = false; Remove(i); }
        }

        private void Remove(int index)
        {
            var instance = instances[index];
            instances[index] = null;
            if (instance == null) return;
            var token = instance.GetComponent<PersonalProvisionToken>();
            if (token != null) token.Invalidate();
            try
            {
                var inventory = SingletonBehaviour<Inventory>.Instance;
                if (inventory != null && !UnloadWatcher.isUnloading)
                {
                    var item = instance.GetComponent<ItemBase>();
                    if (item != null && item.IsGrabbed()) item.ForceEndInteraction();
                    if (inventory.Contains(instance) || inventory.GetEquipSlotForItem(instance) >= 0)
                        inventory.DropItemFromHandsOrInventory(instance);
                    inventory.PurgeFromInventory(instance);
                }
            }
            catch (Exception exception) { Debug.LogWarning("DVSurvival item cleanup: " + exception.Message); }
            finally { Destroy(instance); }
        }

        private void OnDestroy() { Clear(); }
    }

    internal sealed class PersonalProvisionToken : MonoBehaviour
    {
        private SurvivalRuntime owner;
        private ProvisionKind kind;
        private ItemBase item;
        private float nextUse;

        public void Initialize(SurvivalRuntime runtime, ProvisionKind provision) { owner = runtime; kind = provision; }
        public void Invalidate() { owner = null; }

        private void Start()
        {
            item = GetComponent<ItemBase>();
            if (item != null) item.Used += OnUsed;
        }

        private void OnUsed()
        {
            if (owner == null || !owner.HasConfirmedLocalState || Time.realtimeSinceStartup < nextUse) return;
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory == null || inventory.GetEquipSlotForItem(gameObject) < 0) return;
            nextUse = Time.realtimeSinceStartup + 0.6f;
            ProvisionUseAction.Begin(gameObject, kind, () =>
            {
                if (owner != null && owner.HasConfirmedLocalState) owner.RequestConsume(kind);
            });
        }

        private void OnDestroy() { if (item != null) item.Used -= OnUsed; }
    }
}
