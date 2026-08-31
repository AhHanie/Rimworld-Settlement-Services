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

            Apply(eventDef.effects, job, ctx);
            job.eventOutcome.applied = true;
            SettlementServiceNotifier.NotifyEvent(job, eventDef);
        }

        public static void Apply(ServiceEventEffects effects, ServiceJobRecord job, ServiceJobContext ctx)
        {
            if (effects == null) return;
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

            if (effects.durationDeltaTicks != 0 && job.status == Domain.ServiceJobStatus.Active)
                job.expectedCompletionTick = Mathf.Max(Find.TickManager.TicksGame + 1, job.expectedCompletionTick + effects.durationDeltaTicks);

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
