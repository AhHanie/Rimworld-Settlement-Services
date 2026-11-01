using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Services.Genetics
{
    internal enum GrowthVatAdvanceOutcome
    {
        Running,
        TargetReached,
        Stillborn,
        NutritionShortfall,
        BirthFailed,
        TargetLost,
    }

    internal static class GrowthVatSimulation
    {
        private const int ChunkTicks = 250;
        private const int MaxCatchUpTicks = GrowthVatMath.TicksPerDay * 4;
        private const float NutritionTolerance = 0.001f;
        private const int NeedIntervalTicks = 150;
        private const float GrowthPointsPerNeedInterval = 0.0025f;

        public static GrowthVatAdvanceOutcome Advance(SettlementServicesWorldComponent domain, ServiceJobRecord job, GrowthVatJobState state)
        {
            int now = Find.TickManager.TicksGame;
            int remaining = Mathf.Clamp(now - state.lastProcessedTick, 0, MaxCatchUpTicks);
            int gestationLeft = state.gestationTicksLeft;
            float consumed = 0f;
            GrowthVatAdvanceOutcome outcome = GrowthVatAdvanceOutcome.Running;

            try
            {
                if (state.InGestation)
                {
                    int used = Mathf.Min(remaining, gestationLeft);
                    gestationLeft -= used;
                    remaining -= used;
                    consumed += GrowthVatMath.NutritionForEmbryoTicks(used);

                    if (gestationLeft > 0) return outcome;

                    domain.RecordGrowthVatProgress(job.jobId, now, 0, consumed);
                    consumed = 0f;

                    outcome = RunBirth(domain, job, state);
                    if (outcome != GrowthVatAdvanceOutcome.Running) return outcome;
                }

                outcome = GrowOccupant(domain, job, state, remaining, ref consumed);
                return outcome;
            }
            finally
            {
                domain.RecordGrowthVatProgress(job.jobId, now, gestationLeft, consumed);
                if (outcome == GrowthVatAdvanceOutcome.Running) RefreshExpectedCompletion(domain, job, state, now);
            }
        }

        private static GrowthVatAdvanceOutcome RunBirth(SettlementServicesWorldComponent domain, ServiceJobRecord job, GrowthVatJobState state)
        {
            HumanEmbryo embryo = job.Targets.Select(t => t?.liveThing).OfType<HumanEmbryo>().FirstOrDefault();
            if (embryo == null || embryo.Destroyed) return GrowthVatAdvanceOutcome.TargetLost;

            GrowthVatMilestone milestone = GrowthVatMilestone.FromKey(state.milestoneKey) ?? GrowthVatMilestone.Birth;
            try
            {
                if (!GrowthVatBirthService.TryBirth(domain, job, embryo, milestone)) return GrowthVatAdvanceOutcome.BirthFailed;
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"Job {job.jobId}: growth vat birth failed; the embryo stays in custody.");
                return GrowthVatAdvanceOutcome.BirthFailed;
            }

            if (state.stillborn)
            {
                domain.MarkGrowthVatTargetReached(job.jobId);
                return GrowthVatAdvanceOutcome.Stillborn;
            }

            if (milestone.IsBirth)
            {
                domain.MarkGrowthVatTargetReached(job.jobId);
                return GrowthVatAdvanceOutcome.TargetReached;
            }

            return GrowthVatAdvanceOutcome.Running;
        }

        private static GrowthVatAdvanceOutcome GrowOccupant(SettlementServicesWorldComponent domain, ServiceJobRecord job, GrowthVatJobState state, int remaining, ref float consumed)
        {
            Pawn pawn = domain.GrowthVatOccupant(job.jobId);
            if (pawn == null || pawn.Destroyed || pawn.Dead) return GrowthVatAdvanceOutcome.TargetLost;

            while (remaining > 0)
            {
                int agePerTick = GrowthVatMath.AgeTicksPerGameTick(GrowthVatMath.EffectiveSpeed(pawn));
                long need = state.targetAgeTicks - pawn.ageTracker.AgeBiologicalTicks;
                if (need <= 0) return Reached(domain, job);

                int step = (int)Math.Min(Math.Min(remaining, ChunkTicks), GrowthVatMath.GameTicksToReach(need, agePerTick));
                StepOccupant(pawn, step, agePerTick);
                consumed += GrowthVatMath.NutritionForPawnTicks(step);
                remaining -= step;

                if (pawn.Destroyed || pawn.Dead) return GrowthVatAdvanceOutcome.TargetLost;

                long needAfter = state.targetAgeTicks - pawn.ageTracker.AgeBiologicalTicks;
                if (needAfter <= 0) return Reached(domain, job);

                int nextAgePerTick = GrowthVatMath.AgeTicksPerGameTick(GrowthVatMath.EffectiveSpeed(pawn));
                float projected = state.consumedNutrition + consumed + GrowthVatMath.NutritionForPawnTicks(GrowthVatMath.GameTicksToReach(needAfter, nextAgePerTick));
                if (projected > state.prepaidNutrition + NutritionTolerance) return GrowthVatAdvanceOutcome.NutritionShortfall;
            }

            return GrowthVatAdvanceOutcome.Running;
        }

        private static GrowthVatAdvanceOutcome Reached(SettlementServicesWorldComponent domain, ServiceJobRecord job)
        {
            domain.MarkGrowthVatTargetReached(job.jobId);
            return GrowthVatAdvanceOutcome.TargetReached;
        }

        private static void StepOccupant(Pawn pawn, int gameTicks, int agePerTick)
        {
            Pawn_AgeTracker age = pawn.ageTracker;

            if (age.canGainGrowthPoints && age.growthPoints >= 0f)
                age.growthPoints += age.GrowthPointsPerDay * GrowthPointsPerNeedInterval * gameTicks / NeedIntervalTicks;

            age.vatGrowTicks += gameTicks - 1;
            age.Notify_TickedInGrowthVat(agePerTick * gameTicks);

            if (pawn.Destroyed || pawn.Dead) return;

            Hediff learning = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.VatLearning) ?? pawn.health.AddHediff(HediffDefOf.VatLearning);
            learning.TickInterval(gameTicks);
            learning.PostTickInterval(gameTicks);
        }

        private static void RefreshExpectedCompletion(SettlementServicesWorldComponent domain, ServiceJobRecord job, GrowthVatJobState state, int now)
        {
            long ticksLeft;
            if (state.InGestation)
            {
                GrowthVatMilestone milestone = GrowthVatMilestone.FromKey(state.milestoneKey) ?? GrowthVatMilestone.Birth;
                ticksLeft = state.gestationTicksLeft + (milestone.IsBirth ? 0 : GrowthVatMath.ProjectEmbryo(milestone).GrowthTicks);
            }
            else
            {
                Pawn pawn = domain.GrowthVatOccupant(job.jobId);
                if (pawn == null) return;

                long need = state.targetAgeTicks - pawn.ageTracker.AgeBiologicalTicks;
                ticksLeft = GrowthVatMath.GameTicksToReach(need, GrowthVatMath.AgeTicksPerGameTick(GrowthVatMath.EffectiveSpeed(pawn)));
            }

            long eta = now + Math.Max(1L, ticksLeft);
            domain.SetJobExpectedCompletionTick(job.jobId, (int)Math.Min(int.MaxValue, eta));
        }
    }
}
