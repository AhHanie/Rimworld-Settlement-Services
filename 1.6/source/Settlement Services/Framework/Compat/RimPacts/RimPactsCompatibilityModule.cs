using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.RimPacts
{
    internal sealed class RimPactsCompatibilityModule : ISettlementServicesCompatibilityModule
    {
        internal const string Id = "rimpacts";

        public string ModuleId => Id;

        public ICompatibilityAvailabilityRule AvailabilityRule { get; private set; }
        public ICompatibilityQuoteModifier QuoteModifier { get; private set; }
        public ICompatibilityCompletionObserver CompletionObserver { get; private set; }
        public ICompatibilitySettingsSection SettingsSection { get; private set; }

        public bool TryInitialize()
        {
            RimPactsAdapter.Initialize();
            if (!RimPactsAdapter.IsReady) return false;

            AvailabilityRule = new RimPactsAvailabilityRule();
            QuoteModifier = new RimPactsQuoteModifier();
            CompletionObserver = new RimPactsCompletionObserver();
            SettingsSection = new RimPactsSettingsSection();
            return true;
        }
    }
}
