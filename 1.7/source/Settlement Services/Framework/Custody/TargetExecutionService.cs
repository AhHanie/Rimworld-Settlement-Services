using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Custody
{
    internal static class TargetExecutionService
    {
        internal static bool TryFinalize(SettlementServicesWorldComponent domain, ServiceJobRecord job)
        {
            if (domain == null || job == null) return true;
            if (!job.pendingExecution) return true;

            bool allDone = true;
            foreach (TargetSnapshot target in job.Targets)
            {
                if (!(target?.liveThing is Pawn pawn) || pawn.Destroyed) continue;

                if (!pawn.Dead && !TryExecute(pawn))
                {
                    allDone = false;
                    continue;
                }

                if (!BankCorpse(domain, job, pawn)) allDone = false;
            }

            if (!allDone)
            {
                SupportLog.Error($"Job {job.jobId}: a committed ripscan execution could not be completed; the job stays recoverable and will retry.");
                return false;
            }

            if (!job.Targets.Any(t => TargetCustodyService.RequiresReturn(t?.liveThing))) job.targetInCustody = false;
            domain.ClearPendingExecution(job.jobId);
            return true;
        }

        private static bool TryExecute(Pawn pawn)
        {
            try
            {
                Detach(pawn);

                var dinfo = new DamageInfo(DamageDefOf.ExecutionCut, 9999f, 999f, -1f, null, pawn.health.hediffSet.GetBrain());
                dinfo.SetIgnoreInstantKillProtection(ignore: true);
                dinfo.SetAllowDamagePropagation(val: false);

                pawn.forceNoDeathNotification = true;
                try
                {
                    pawn.TakeDamage(dinfo);
                    if (!pawn.Dead) pawn.Kill(dinfo);
                }
                finally
                {
                    pawn.forceNoDeathNotification = false;
                }

                if (!pawn.Dead) return false;

                ThoughtUtility.GiveThoughtsForPawnExecuted(pawn, null, PawnExecutionKind.Ripscanned);
                Messages.Message("MessagePawnKilledRipscanner".Translate(pawn.Named("PAWN")), MessageTypeDefOf.NegativeHealthEvent, historical: false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Ripscan execution failed");
                return pawn.Dead;
            }
        }

        private static void Detach(Pawn pawn)
        {
            if (pawn.Spawned) return;

            if (pawn.holdingOwner != null)
            {
                var caravan = pawn.holdingOwner.Owner as Caravan;
                pawn.holdingOwner.Remove(pawn);
                if (caravan != null && !caravan.Destroyed && caravan.PawnsListForReading.Count == 0) caravan.Destroy();
            }

            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
        }

        private static bool BankCorpse(SettlementServicesWorldComponent domain, ServiceJobRecord job, Pawn pawn)
        {
            Corpse corpse = pawn.Corpse;
            if (corpse == null || corpse.Destroyed || corpse.Spawned || corpse.holdingOwner != null) return true;

            return domain.TryAddRecoveredJobResult(job.jobId, corpse);
        }
    }
}
