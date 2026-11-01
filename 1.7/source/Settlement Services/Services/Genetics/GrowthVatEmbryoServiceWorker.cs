using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Services.Genetics
{
    internal sealed class GrowthVatEmbryoServiceWorker : GrowthVatServiceWorkerBase
    {
        protected override GrowthVatKind Kind => GrowthVatKind.Embryo;

        protected override string TargetBlockKey(Thing target, Caravan caravan, bool atStart)
        {
            switch (EmbryoEligibility.Check(target, caravan, atStart))
            {
                case EmbryoBlock.NotEmbryo: return "SettlementServices.Error.GrowthVatNotEmbryo";
                case EmbryoBlock.Gone: return "SettlementServices.Error.TargetNoLongerExists";
                case EmbryoBlock.NoGenes: return "SettlementServices.Error.GrowthVatEmbryoNoGenes";
                case EmbryoBlock.ImplantPending: return "SettlementServices.Error.GrowthVatEmbryoImplantPending";
                case EmbryoBlock.PartialStack: return "SettlementServices.Error.GrowthVatEmbryoPartialStack";
                case EmbryoBlock.NotInCaravan: return "SettlementServices.Error.TargetNotInCaravan";
                default: return null;
            }
        }

        public override bool ShouldListTarget(Thing target) => target is HumanEmbryo;

        protected override GrowthVatProjection Project(Thing target, GrowthVatMilestone milestone) => GrowthVatMath.ProjectEmbryo(milestone);

        protected override IEnumerable<GrowthVatMilestone> OfferedMilestones(Thing target) => GrowthVatMilestone.All;

        protected override bool TryBeginOccupancy(ServiceJobContext ctx, Thing target) => true;
    }
}
