using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework;

namespace Settlement_Services.Framework.Events
{
    internal static class ServiceEventEffectApplier
    {
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

            if (effects.experienceSkillDefName != null)
            {
                SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(effects.experienceSkillDefName);
                if (skill != null)
                {
                    if (effects.grantExperienceToAllParticipants)
                        GrantExperienceToAllParticipants(skill, effects.experienceAmount, job);
                    else if (pawn?.skills != null)
                        pawn.skills.Learn(skill, effects.experienceAmount);
                }
            }

            if (effects.thoughtDefName != null)
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(effects.thoughtDefName);
                if (thought != null)
                {
                    if (effects.grantThoughtToAllParticipants)
                        GrantThoughtToAllParticipants(thought, job);
                    else if (pawn?.needs?.mood != null)
                        pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
                }
            }

            if (effects.goodwillDelta != 0)
            {
                Faction faction = ctx.ResolveSettlement()?.Faction;
                faction?.TryAffectGoodwillWith(Faction.OfPlayer, effects.goodwillDelta, canSendMessage: false, canSendHostilityLetter: false);
            }

            if (job.acceptedQuote != null)
            {
                int amount = 0;
                if (effects.refundAmount > 0)
                    amount = Mathf.Min(effects.refundAmount, Mathf.RoundToInt(job.acceptedQuote.totalCost * ServiceEventEffects.MaxRefundFraction));
                else if (effects.refundFraction > 0f)
                    amount = Mathf.RoundToInt(job.acceptedQuote.totalCost * Mathf.Min(effects.refundFraction, ServiceEventEffects.MaxRefundFraction));

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

            if (effects.hediffDefName != null)
            {
                HediffDef hediff = DefDatabase<HediffDef>.GetNamedSilentFail(effects.hediffDefName);
                if (hediff != null)
                {
                    if (effects.grantHediffToAllParticipants)
                        GrantHediffToAllParticipants(hediff, effects.hediffSeverity, job);
                    else if (pawn != null)
                        AddHediff(hediff, effects.hediffSeverity, pawn);
                }
            }

            if (effects.referralCategoryDefName != null)
                ServiceReferralResolver.TryRevealReferral(ctx, effects.referralCategoryDefName);

            if (effects.questHookDefName != null)
                ServiceQuestHookEffect.TryFire(effects.questHookDefName, ctx);

        }

        private static void GrantThoughtToAllParticipants(ThoughtDef thought, ServiceJobRecord job)
        {
            foreach (TargetSnapshot target in job.Targets)
            {
                if (target?.liveThing is Pawn targetPawn && !targetPawn.Destroyed && targetPawn.needs?.mood != null)
                    targetPawn.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
        }

        private static void GrantHediffToAllParticipants(HediffDef hediff, float severity, ServiceJobRecord job)
        {
            var seen = new HashSet<Pawn>();
            foreach (TargetSnapshot target in job.Targets)
            {
                if (!(target?.liveThing is Pawn targetPawn) || targetPawn.Destroyed || targetPawn.health == null) continue;
                if (seen.Add(targetPawn)) AddHediff(hediff, severity, targetPawn);
            }
        }

        private static void AddHediff(HediffDef hediff, float severity, Pawn pawn)
        {
            Hediff instance = HediffMaker.MakeHediff(hediff, pawn);
            if (severity > 0f) instance.Severity = severity;
            pawn.health.AddHediff(instance);
        }

        private static void GrantExperienceToAllParticipants(SkillDef skill, float amount, ServiceJobRecord job)
        {
            var seen = new HashSet<Pawn>();
            foreach (TargetSnapshot target in job.Targets)
            {
                if (!(target?.liveThing is Pawn targetPawn)) continue;
                if (targetPawn.Destroyed || targetPawn.Dead || targetPawn.skills == null) continue;
                if (seen.Add(targetPawn)) targetPawn.skills.Learn(skill, amount);
            }
        }
    }
}
