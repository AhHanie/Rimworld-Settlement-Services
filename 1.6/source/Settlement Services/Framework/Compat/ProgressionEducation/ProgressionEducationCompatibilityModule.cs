using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.ProgressionEducation
{
    internal sealed class ProgressionEducationCompatibilityModule : ISettlementServicesCompatibilityModule
    {
        internal const string Id = "progressionEducation";

        internal static IProgressionEducationProficiencyGateway Gateway { get; private set; } = NullProgressionEducationProficiencyGateway.Instance;

        public string ModuleId => Id;

        public ICompatibilityAvailabilityRule AvailabilityRule => null;
        public ICompatibilityQuoteModifier QuoteModifier => null;
        public ICompatibilityCompletionObserver CompletionObserver { get; private set; }
        public ICompatibilitySettingsSection SettingsSection => null;
        public ICompatibilityCustodyLifecycle CustodyLifecycle => null;

        public bool TryInitialize()
        {
            ProgressionEducationAdapter.Initialize();
            if (!ProgressionEducationAdapter.IsReady) return false;

            Gateway = ProgressionEducationAdapter.Instance;
            CompletionObserver = new ProgressionEducationCompletionObserver();
            return true;
        }
    }
}
