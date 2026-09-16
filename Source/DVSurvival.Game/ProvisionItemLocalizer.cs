using System.Globalization;
using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Native InventoryItemSpec asks this component on the selected instance.
    // Never modify shared localization entries or the shared InventoryItemSpec asset.
    internal sealed class ProvisionItemLocalizer : MonoBehaviour, IInventoryItemLocalizer
    {
        public ProvisionKind Kind;
        private NativeProvisionToken token;
        public string GetNameParam() { return string.Empty; }
        public string GetCustomDescription()
        {
            if (token == null) token = GetComponent<NativeProvisionToken>();
            var used = token != null && token.enabled ? token.UsedUnits : 0;
            return ProvisionDescription.Format(
                ModLocalization.Text("Осталось: ", "Remaining: "),
                ModLocalization.Description(Kind), used,
                ModLocalization.IsRussian ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture);
        }
    }
}
