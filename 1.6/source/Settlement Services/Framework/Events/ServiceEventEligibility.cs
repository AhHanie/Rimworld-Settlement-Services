using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events
{
    internal static class ServiceEventEligibility
    {
        public static bool IsEligible(ServiceEventDef eventDef, SettlementServiceDef serviceDef, ServiceJobContext ctx)
        {
            if (!CategoryMatches(eventDef, serviceDef)) return false;
            if (!eventDef.excludedServiceDefNames.NullOrEmpty() && eventDef.excludedServiceDefNames.Contains(serviceDef.defName))
                return false;

            if (eventDef.triggerPhase == ServiceEventTriggerPhase.DuringService
                && ctx.Job.expectedCompletionTick - Find.TickManager.TicksGame <= SettlementServicesWorldComponent.TickInterval)
                return false;

            Settlement settlement = ctx.ResolveSettlement();
            if (eventDef.minTechLevel != TechLevel.Undefined
                && (settlement?.Faction == null || settlement.Faction.def.techLevel < eventDef.minTechLevel))
                return false;

            if (eventDef.requiresPawnTarget && ctx.ResolvePrimaryPawn() == null) return false;

            if (!eventDef.requiredTraitDefNames.NullOrEmpty())
            {
                Pawn pawn = ctx.ResolvePrimaryPawn();
                if (pawn?.story?.traits == null
                    || !eventDef.requiredTraitDefNames.Any(t => pawn.story.traits.HasTrait(DefDatabase<TraitDef>.GetNamedSilentFail(t))))
                    return false;
            }

            if (!eventDef.requiredMemeDefNames.NullOrEmpty())
            {
                Ideo ideo = settlement?.Faction?.ideos?.PrimaryIdeo;
                if (ideo == null || !eventDef.requiredMemeDefNames.Any(m => ideo.HasMeme(DefDatabase<MemeDef>.GetNamedSilentFail(m))))
                    return false;
            }

            if (SettlementServicesWorldComponent.Current.IsServiceEventOnCooldown(ctx.Job.settlementWorldObjectId, eventDef.defName, eventDef.cooldownTicks))
                return false;

            return true;
        }

        private static bool CategoryMatches(ServiceEventDef eventDef, SettlementServiceDef serviceDef)
        {
            if (eventDef.appliesToAllCategories) return true;
            return !eventDef.eligibleCategoryDefNames.NullOrEmpty()
                && serviceDef.category != null
                && eventDef.eligibleCategoryDefNames.Contains(serviceDef.category.defName);
        }
    }
}
