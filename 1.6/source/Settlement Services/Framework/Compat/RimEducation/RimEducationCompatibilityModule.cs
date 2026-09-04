using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.RimEducation
{
    internal sealed class RimEducationCompatibilityModule : ISettlementServicesCompatibilityModule
    {
        internal const string Id = "rimEducation";

        public string ModuleId => Id;

        public ICompatibilityAvailabilityRule AvailabilityRule => null;
        public ICompatibilityQuoteModifier QuoteModifier => null;
        public ICompatibilityCompletionObserver CompletionObserver { get; private set; }
        public ICompatibilitySettingsSection SettingsSection => null;

        public bool TryInitialize()
        {
            RimEducationAdapter.Initialize();
            if (!RimEducationAdapter.IsReady) return false;

            CompletionObserver = new RimEducationCompletionObserver();
            return true;
        }
    }
}
