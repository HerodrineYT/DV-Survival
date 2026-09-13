using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DVSurvival.Mod
{
    // Remove only the retired prefab from deserialized storage lists before native resource loading.
    // No disk-save rewrite, object scans or changes to unrelated inventory records.
    [HarmonyPatch(typeof(StorageSerializer), nameof(StorageSerializer.LoadStorageData))]
    internal static class RemovedProvisionMigration
    {
        internal static bool IsRemoved(string prefabName)
        { return string.Equals(prefabName, "custom_item_mod/DVSurvival_Moonshine/prefab", StringComparison.Ordinal); }

        private static void Postfix(List<StorageItemData> __result)
        {
            if (__result == null) return;
            var mod = UnityModManagerNet.UnityModManager.FindMod("DVSurvival");
            if (mod == null || !mod.Active) return;
            __result.RemoveAll(item => item != null && IsRemoved(item.itemPrefabName));
        }
    }
}
