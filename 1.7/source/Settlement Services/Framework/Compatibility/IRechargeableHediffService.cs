using Verse;

namespace Settlement_Services.Framework.Compatibility
{
    internal readonly struct RechargeableHediffStatus
    {
        public static readonly RechargeableHediffStatus None = new RechargeableHediffStatus(false, false, 0f);

        public bool HasRechargeableHediffs { get; }
        public bool NeedsCharge { get; }
        public float DeficitFraction { get; }

        public RechargeableHediffStatus(bool hasRechargeableHediffs, bool needsCharge, float deficitFraction)
        {
            HasRechargeableHediffs = hasRechargeableHediffs;
            NeedsCharge = needsCharge;
            DeficitFraction = deficitFraction;
        }
    }

    internal interface IRechargeableHediffService
    {
        bool IsAvailable { get; }

        RechargeableHediffStatus Inspect(Pawn pawn);

        bool TryRechargeFully(Pawn pawn, out string errorKey);
    }

    internal sealed class NullRechargeableHediffService : IRechargeableHediffService
    {
        internal static readonly NullRechargeableHediffService Instance = new NullRechargeableHediffService();

        public bool IsAvailable => false;

        public RechargeableHediffStatus Inspect(Pawn pawn) => RechargeableHediffStatus.None;

        public bool TryRechargeFully(Pawn pawn, out string errorKey)
        {
            errorKey = "SettlementServices.Error.ChargeableHediffsUnavailable";
            return false;
        }
    }

    internal interface IRechargeableHediffServiceProvider
    {
        IRechargeableHediffService RechargeableHediffService { get; }
    }
}
