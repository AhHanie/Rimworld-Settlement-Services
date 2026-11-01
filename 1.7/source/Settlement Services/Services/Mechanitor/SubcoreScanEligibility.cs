using RimWorld;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Mechanitor
{
    internal static class SubcoreScanEligibility
    {
        public static bool IsValidDonor(Pawn pawn, out string errorKey)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) { errorKey = "SettlementServices.Error.TargetNoLongerExists"; return false; }
            if (!pawn.RaceProps.Humanlike) { errorKey = "SettlementServices.Error.SubcoreScanDonorNotHumanlike"; return false; }
            if (!pawn.IsColonist && !pawn.IsPrisonerOfColony && !pawn.IsSlaveOfColony) { errorKey = "SettlementServices.Error.SubcoreScanDonorNotOwned"; return false; }
            if (pawn.IsQuestLodger()) { errorKey = "SettlementServices.Error.SubcoreScanDonorQuestLodger"; return false; }
            if (pawn.DevelopmentalStage.Baby()) { errorKey = "SettlementServices.Error.SubcoreScanDonorBaby"; return false; }
            if (pawn.health.hediffSet.HasHediff(HediffDefOf.ScanningSickness)) { errorKey = "SettlementServices.Error.SubcoreScanDonorSick"; return false; }

            errorKey = null;
            return true;
        }

        public static ServiceInputPlan PlanInputs(SettlementServiceDef def, SettlementServiceRequest request)
        {
            var plan = new ServiceInputPlan();
            if (def.stockRequirements.NullOrEmpty()) return plan;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, def.stockRequirements, request.playerSuppliedInputs);
            if (!allocation.Success) return plan;

            plan.stockConsumed.AddRange(allocation.SettlementSupplied);
            plan.playerSuppliedConsumed.AddRange(allocation.PlayerSupplied);
            return plan;
        }

        public static Thing MakeSubcore(string thingDefName)
        {
            ThingDef subcoreDef = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
            return subcoreDef == null ? null : ThingMaker.MakeThing(subcoreDef);
        }

        public static bool SubcoreAvailable(string thingDefName) => DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName) != null;
    }
}
