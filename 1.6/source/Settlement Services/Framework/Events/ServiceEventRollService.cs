using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;
using Settlement_Services;

namespace Settlement_Services.Framework.Events
{
    internal static class ServiceEventRollService
    {
        private const int ChanceSalt = 401;
        private const int PickSalt = 402;
        private const int TimingSalt = 403;

        public static void RollAndPresent(SettlementServicesWorldComponent domain, SettlementServiceDef def, ServiceJobRecord job, ServiceJobContext ctx)
        {
            float chance = Mathf.Clamp01(def.eventChancePct * ModSettings.Current.serviceEventFrequencyPct * def.Worker.EventChanceMultiplierFor(ctx));

            if (Rand.ValueSeeded(Gen.HashCombineInt(job.jobId, ChanceSalt)) > chance)
            {
                job.eventOutcome = ServiceEventOutcomeRecord.None(Find.TickManager.TicksGame);
                return;
            }

            List<ServiceEventDef> eligible = ServiceEventRegistry.EligibleEvents(def, ctx).ToList();
            if (eligible.Count == 0)
            {
                job.eventOutcome = ServiceEventOutcomeRecord.None(Find.TickManager.TicksGame);
                return;
            }

            ServiceEventDef chosen = WeightedPick(eligible, Gen.HashCombineInt(job.jobId, PickSalt));

            int scheduledTick = -1;
            if (chosen.triggerPhase == ServiceEventTriggerPhase.DuringService)
            {
                float fraction = Mathf.Lerp(0.25f, 0.75f, Rand.ValueSeeded(Gen.HashCombineInt(job.jobId, TimingSalt)));
                scheduledTick = job.createdTick + Mathf.RoundToInt(fraction * (job.expectedCompletionTick - job.createdTick));
            }

            job.eventOutcome = new ServiceEventOutcomeRecord
            {
                eventDefName = chosen.defName,
                triggerPhase = chosen.triggerPhase,
                scheduledTick = scheduledTick,
                rolledTick = Find.TickManager.TicksGame,
                applied = false,
            };

            domain.RecordServiceEventOccurrence(job.settlementWorldObjectId, chosen.defName);

            if (chosen.triggerPhase == ServiceEventTriggerPhase.OnStart)
                ServiceEventEffectApplier.Present(chosen, job, ctx);
        }

        private static ServiceEventDef WeightedPick(List<ServiceEventDef> pool, int seed)
        {
            float total = pool.Sum(e => e.selectionWeight);
            if (total <= 0f) return pool[Rand.RangeSeeded(0, pool.Count, seed)];

            float roll = Rand.RangeSeeded(0f, total, seed);
            float cumulative = 0f;
            foreach (ServiceEventDef e in pool)
            {
                cumulative += e.selectionWeight;
                if (roll <= cumulative) return e;
            }
            return pool[pool.Count - 1];
        }
    }
}
