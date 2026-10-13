using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Events;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Services.Education
{
    internal static class EducationEventEffectResolver
    {
        public static float ExperienceMultiplierFor(ServiceJobContext ctx)
        {
            ServiceEventEffects effects = ResolveScheduledAutomaticEffects(ctx.Job);
            return effects?.educationExperienceMultiplier ?? 1f;
        }

        public static float ResearchProgressMultiplierFor(ServiceJobContext ctx)
        {
            ServiceEventEffects effects = ResolveScheduledAutomaticEffects(ctx.Job);
            return effects?.researchProgressMultiplier ?? 1f;
        }

        private static ServiceEventEffects ResolveScheduledAutomaticEffects(ServiceJobRecord job)
        {
            ServiceEventOutcomeRecord outcome = job.eventOutcome;
            if (outcome == null || outcome.eventDefName == null || outcome.triggerPhase != ServiceEventTriggerPhase.OnComplete)
                return null;

            ServiceEventDef eventDef = DefDatabase<ServiceEventDef>.GetNamedSilentFail(outcome.eventDefName);
            if (eventDef == null || !eventDef.choices.NullOrEmpty()) return null;

            return eventDef.effects;
        }
    }
}
