namespace Settlement_Services.Framework.Compatibility
{
    internal interface ISettlementServicesCompatibilityModule
    {
        string ModuleId { get; }

        bool TryInitialize();

        ICompatibilityAvailabilityRule AvailabilityRule { get; }
        ICompatibilityQuoteModifier QuoteModifier { get; }
        ICompatibilityCompletionObserver CompletionObserver { get; }
        ICompatibilitySettingsSection SettingsSection { get; }
    }
}
