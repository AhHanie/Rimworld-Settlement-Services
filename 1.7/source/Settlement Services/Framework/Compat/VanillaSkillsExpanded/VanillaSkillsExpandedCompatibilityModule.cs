using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.VanillaSkillsExpanded
{
    internal sealed class VanillaSkillsExpandedCompatibilityModule : ISettlementServicesCompatibilityModule, IEducationPassionLabelProviderModule
    {
        internal const string Id = "vanillaSkillsExpanded";

        public string ModuleId => Id;

        public ICompatibilityAvailabilityRule AvailabilityRule => null;
        public ICompatibilityQuoteModifier QuoteModifier => null;
        public ICompatibilityCompletionObserver CompletionObserver => null;
        public ICompatibilitySettingsSection SettingsSection => null;
        public ICompatibilityCustodyLifecycle CustodyLifecycle => null;

        public IEducationPassionLabelProvider EducationPassionLabelProvider { get; private set; }

        public bool TryInitialize()
        {
            VanillaSkillsExpandedPassionAdapter.Initialize();
            if (!VanillaSkillsExpandedPassionAdapter.IsReady) return false;

            EducationPassionLabelProvider = VanillaSkillsExpandedPassionAdapter.Instance;
            return true;
        }
    }
}
