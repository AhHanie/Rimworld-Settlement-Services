using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Framework.Specialty;
using Settlement_Services.Framework.Validation;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events
{
    internal static class ServiceReferralResolver
    {
        public static void TryRevealReferral(ServiceJobContext ctx, string categoryDefName)
        {
            Settlement origin = ctx.ResolveSettlement();
            if (origin == null) return;

            ServiceCategoryDef category = DefDatabase<ServiceCategoryDef>.GetNamedSilentFail(categoryDefName);
            var candidates = category != null ? SettlementServiceRegistry.ForCategory(category) : SettlementServiceRegistry.AllValid;

            Settlement best = Find.WorldObjects.Settlements
                .Where(s => s != origin && s.Faction != null && !s.Faction.HostileTo(Faction.OfPlayer))
                .Where(s => candidates.Any(d => SettlementServiceValidator.StructuralEligibility(d, s, out _)))
                .OrderBy(s => Find.WorldGrid.TraversalDistanceBetween(origin.Tile, s.Tile))
                .FirstOrDefault();

            if (best == null) return;

            foreach (SettlementServiceDef def in candidates.Where(d => SettlementServiceValidator.StructuralEligibility(d, best, out _)))
                SettlementServicesWorldComponent.Current.RecordDiscovery(best.ID, def.defName, ctx.Job.requestChannel);
        }

        public static bool HasUndiscoveredSpecialtyReferral(ServiceJobContext ctx, string specialtyDefName, string categoryDefName)
        {
            Settlement origin = ctx.ResolveSettlement();
            return origin != null && FindNearestSpecialtySettlement(origin, specialtyDefName, categoryDefName) != null;
        }

        public static void TryRevealSpecialtyReferral(ServiceJobContext ctx, string specialtyDefName, string categoryDefName)
        {
            Settlement origin = ctx.ResolveSettlement();
            if (origin == null) return;

            Settlement best = FindNearestSpecialtySettlement(origin, specialtyDefName, categoryDefName);
            if (best == null) return;

            ServiceCategoryDef category = DefDatabase<ServiceCategoryDef>.GetNamedSilentFail(categoryDefName);
            var candidates = category != null ? SettlementServiceRegistry.ForCategory(category) : SettlementServiceRegistry.AllValid;

            foreach (SettlementServiceDef def in candidates.Where(d => SettlementServiceValidator.StructuralEligibility(d, best, out _)))
                SettlementServicesWorldComponent.Current.RecordDiscovery(best.ID, def.defName, ctx.Job.requestChannel);
        }

        private static Settlement FindNearestSpecialtySettlement(Settlement origin, string specialtyDefName, string categoryDefName)
        {
            ServiceCategoryDef category = DefDatabase<ServiceCategoryDef>.GetNamedSilentFail(categoryDefName);
            var candidates = category != null ? SettlementServiceRegistry.ForCategory(category) : SettlementServiceRegistry.AllValid;

            return Find.WorldObjects.Settlements
                .Where(s => s != origin && s.Faction != null && !s.Faction.HostileTo(Faction.OfPlayer))
                .Where(s => SettlementSpecialtyService.PreviewSpecialties(s).Any(sp => sp.defName == specialtyDefName))
                .Where(s => candidates.Any(d => SettlementServiceValidator.StructuralEligibility(d, s, out _)
                    && !SettlementServicesWorldComponent.Current.HasDiscovered(s.ID, d.defName)))
                .OrderBy(s => Find.WorldGrid.TraversalDistanceBetween(origin.Tile, s.Tile))
                .ThenBy(s => s.ID)
                .FirstOrDefault();
        }
    }
}
