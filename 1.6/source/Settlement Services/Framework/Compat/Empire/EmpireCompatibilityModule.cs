using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.Empire
{
    internal sealed class EmpireCompatibilityModule : ISettlementServicesCompatibilityModule
    {
        internal const string Id = "empire";

        public string ModuleId => Id;

        public ICompatibilityAvailabilityRule AvailabilityRule { get; private set; }
        public ICompatibilityQuoteModifier QuoteModifier => null;
        public ICompatibilityCompletionObserver CompletionObserver => null;
        public ICompatibilitySettingsSection SettingsSection => null;
        public ICompatibilityCustodyLifecycle CustodyLifecycle => null;

        public bool TryInitialize()
        {
            EmpireAdapter.Initialize();
            if (!EmpireAdapter.IsReady) return false;

            AvailabilityRule = new EmpireAvailabilityRule();
            return true;
        }
    }
}
