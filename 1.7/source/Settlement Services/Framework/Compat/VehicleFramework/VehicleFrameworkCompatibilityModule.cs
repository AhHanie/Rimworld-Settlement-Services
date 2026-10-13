using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    internal sealed class VehicleFrameworkCompatibilityModule : ISettlementServicesCompatibilityModule
    {
        internal const string Id = "vehicleframework";

        public string ModuleId => Id;

        public ICompatibilityAvailabilityRule AvailabilityRule => null;
        public ICompatibilityQuoteModifier QuoteModifier => null;
        public ICompatibilityCompletionObserver CompletionObserver => null;
        public ICompatibilitySettingsSection SettingsSection => null;
        public ICompatibilityCustodyLifecycle CustodyLifecycle { get; private set; }

        public bool TryInitialize()
        {
            if (VehicleFrameworkAdapter.VehiclePawnType == null) return false;

            CustodyLifecycle = new VehicleFrameworkCustodyLifecycle();
            return true;
        }
    }
}
