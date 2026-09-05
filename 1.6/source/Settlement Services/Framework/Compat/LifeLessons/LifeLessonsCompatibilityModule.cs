using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.LifeLessons
{
    internal sealed class LifeLessonsCompatibilityModule : ISettlementServicesCompatibilityModule
    {
        internal const string Id = "lifeLessons";

        internal static ILifeLessonsProficiencyGateway Gateway { get; private set; } = NullLifeLessonsProficiencyGateway.Instance;

        public string ModuleId => Id;

        public ICompatibilityAvailabilityRule AvailabilityRule => null;
        public ICompatibilityQuoteModifier QuoteModifier => null;
        public ICompatibilityCompletionObserver CompletionObserver { get; private set; }
        public ICompatibilitySettingsSection SettingsSection => null;
        public ICompatibilityCustodyLifecycle CustodyLifecycle => null;

        public bool TryInitialize()
        {
            LifeLessonsAdapter.Initialize();
            if (!LifeLessonsAdapter.IsReady) return false;

            Gateway = LifeLessonsAdapter.Instance;
            CompletionObserver = new LifeLessonsCompletionObserver();
            return true;
        }
    }
}
