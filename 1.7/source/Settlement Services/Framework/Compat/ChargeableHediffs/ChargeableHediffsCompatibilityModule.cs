using Verse;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.ChargeableHediffs
{
    internal sealed class ChargeableHediffsCompatibilityModule : ISettlementServicesCompatibilityModule, IRechargeableHediffServiceProvider
    {
        internal const string Id = "chargeableHediffs";

        public string ModuleId => Id;

        public ICompatibilityAvailabilityRule AvailabilityRule => null;
        public ICompatibilityQuoteModifier QuoteModifier => null;
        public ICompatibilityCompletionObserver CompletionObserver => null;
        public ICompatibilitySettingsSection SettingsSection => null;
        public ICompatibilityCustodyLifecycle CustodyLifecycle => null;

        public IRechargeableHediffService RechargeableHediffService { get; private set; }

        public bool TryInitialize()
        {
            ChargeableHediffsAdapter.Initialize();
            if (!ChargeableHediffsAdapter.IsReady) return false;

            RechargeableHediffService = new ChargeableHediffsRechargeableService();
            return true;
        }

        private sealed class ChargeableHediffsRechargeableService : IRechargeableHediffService
        {
            public bool IsAvailable => ChargeableHediffsAdapter.IsReady;

            public RechargeableHediffStatus Inspect(Pawn pawn) => ChargeableHediffsAdapter.Inspect(pawn);

            public bool TryRechargeFully(Pawn pawn, out string errorKey) => ChargeableHediffsAdapter.TryRechargeFully(pawn, out errorKey);
        }
    }
}
