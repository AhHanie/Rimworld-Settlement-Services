using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework;

namespace Settlement_Services.Framework.Events
{
    internal static class ServiceEventEffectApplier
    {
        private const float MaxRefundFraction = 0.25f;

        public static void Present(ServiceEventDef eventDef, ServiceJobRecord job, ServiceJobContext ctx)
        {
            job.eventOutcome.presented = true;

            if (!eventDef.choices.NullOrEmpty())
            {
                ChoiceLetter_ServiceEvent.Send(job, eventDef, ctx);
                return;
            }

            Apply(eventDef, eventDef.effects, job, ctx);
            job.eventOutcome.applied = true;
            SettlementServiceNotifier.NotifyEvent(job, eventDef);
        }

        public static void Apply(ServiceEventDef eventDef, ServiceEventEffects effects, ServiceJobRecord job, ServiceJobContext ctx)
        {
            if (effects != null) ApplyEffects(effects, job, ctx);
            eventDef.Worker?.Apply(ctx);
        }

        private static void ApplyEffects(ServiceEventEffects effects, ServiceJobRecord job, ServiceJobContext ctx)
        {
            Pawn pawn = ctx.ResolvePrimaryPawn();

            if (effects.experienceSkillDefName != null && pawn?.skills != null)
            {
                SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(effects.experienceSkillDefName);
                if (skill != null) pawn.skills.Learn(skill, effects.experienceAmount);
            }

            if (effects.thoughtDefName != null && pawn?.needs?.mood != null)
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(effects.thoughtDefName);
                if (thought != null) pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
            }

            if (effects.goodwillDelta != 0)
            {
                Faction faction = ctx.ResolveSettlement()?.Faction;
                faction?.TryAffectGoodwillWith(Faction.OfPlayer, effects.goodwillDelta, canSendMessage: false, canSendHostilityLetter: false);
            }

            if (effects.refundAmount > 0 && job.acceptedQuote != null)
            {
                int amount = Mathf.Min(effects.refundAmount, Mathf.RoundToInt(job.acceptedQuote.totalCost * MaxRefundFraction));
                if (amount > 0) SettlementServiceOrchestrator.ResolvePaymentProvider(job.requestChannel).Refund(amount, ctx);
            }

            if ((effects.durationDeltaTicks != 0 || effects.durationDeltaPct != 0f) && job.status == Domain.ServiceJobStatus.Active)
            {
                int originalDurationTicks = job.acceptedQuote != null && job.acceptedQuote.expectedDurationTicks > 0
                    ? job.acceptedQuote.expectedDurationTicks
                    : Mathf.Max(0, job.expectedCompletionTick - job.statusChangedTick);
                int deltaTicks = effects.durationDeltaTicks + Mathf.RoundToInt(originalDurationTicks * effects.durationDeltaPct);
                job.expectedCompletionTick = Mathf.Max(Find.TickManager.TicksGame + 1, job.expectedCompletionTick + deltaTicks);
            }

            if (effects.hediffDefName != null && pawn != null)
            {
                HediffDef hediff = DefDatabase<HediffDef>.GetNamedSilentFail(effects.hediffDefName);
                if (hediff != null)
                {
                    Hediff instance = HediffMaker.MakeHediff(hediff, pawn);
                    if (effects.hediffSeverity > 0f) instance.Severity = effects.hediffSeverity;
                    pawn.health.AddHediff(instance);
                }
            }

            if (effects.referralCategoryDefName != null)
                ServiceReferralResolver.TryRevealReferral(ctx, effects.referralCategoryDefName);

            if (effects.questHookDefName != null)
                ServiceQuestHookEffect.TryFire(effects.questHookDefName, ctx);

        }
    }
}
