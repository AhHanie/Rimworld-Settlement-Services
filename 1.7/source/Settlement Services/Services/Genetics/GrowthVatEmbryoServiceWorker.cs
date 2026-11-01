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
            if (!(target is HumanEmbryo embryo) || embryo.def != ThingDefOf.HumanEmbryo) return "SettlementServices.Error.GrowthVatNotEmbryo";
            if (embryo.Destroyed) return "SettlementServices.Error.TargetNoLongerExists";
            if (embryo.GeneSet == null) return "SettlementServices.Error.GrowthVatEmbryoNoGenes";
            if (embryo.implantTarget != null) return "SettlementServices.Error.GrowthVatEmbryoImplantPending";
            if (embryo.stackCount != 1) return "SettlementServices.Error.GrowthVatEmbryoPartialStack";
            if (!atStart && caravan != null && !CaravanInventoryUtility.AllInventoryItems(caravan).Contains(embryo))
                return "SettlementServices.Error.TargetNotInCaravan";
            return null;
        }

        public override bool ShouldListTarget(Thing target) => target is HumanEmbryo;

        protected override GrowthVatProjection Project(Thing target, GrowthVatMilestone milestone) => GrowthVatMath.ProjectEmbryo(milestone);

        protected override IEnumerable<GrowthVatMilestone> OfferedMilestones(Thing target) => GrowthVatMilestone.All;

        protected override bool TryBeginOccupancy(ServiceJobContext ctx, Thing target) => true;
    }
}
