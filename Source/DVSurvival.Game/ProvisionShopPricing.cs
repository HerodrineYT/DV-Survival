using DV.UserManagement;
using DV.Utils;
using DVSurvival.Core;

namespace DVSurvival.Mod
{
    internal static class ProvisionShopPricing
    {
        public static int LocalPrice(int configuredPrice)
        {
            var manager = SingletonBehaviour<UserManager>.Instance;
            var user = manager == null ? null : manager.CurrentUser;
            var session = user == null ? null : user.CurrentSession;
            return ProvisionPricing.Resolve(configuredPrice, session == null ? null : session.GameMode);
        }
    }
}
