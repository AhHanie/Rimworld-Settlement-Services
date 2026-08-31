using System.Linq;
using LudeonTK;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Events;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Debug
{
    public static class ServiceEventDebugAction
    {
        [DebugAction("Settlement Services", "Force-roll service event...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void ForceRollServiceEvent()
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            var candidates = domain.AllJobs.Where(j => j.status == ServiceJobStatus.Active || j.status == ServiceJobStatus.AwaitingCollection).ToList();
            if (candidates.Count == 0) { Logger.Message("No Active/AwaitingCollection jobs to target."); return; }

            Dialog_DebugOptionListLister.ShowSimpleDebugMenu(candidates,
                job => $"#{job.jobId} {job.serviceDefName} ({job.status})",
                job => ChooseEvent(domain, job));
        }

        private static void ChooseEvent(SettlementServicesWorldComponent domain, ServiceJobRecord job)
        {
            SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
            if (def == null) { Logger.Error("Job's def no longer resolves."); return; }

            var ctx = new ServiceJobContext(domain, job);
            var options = ServiceEventRegistry.EligibleEvents(def, ctx).ToList();
            if (options.Count == 0) { Logger.Message("No eligible ServiceEventDefs for this job right now."); return; }

            Dialog_DebugOptionListLister.ShowSimpleDebugMenu(options,
                e => e.defName,
                chosen =>
                {
                    job.eventOutcome = new ServiceEventOutcomeRecord
                    {
                        eventDefName = chosen.defName,
                        triggerPhase = chosen.triggerPhase,
                        rolledTick = Find.TickManager.TicksGame,
                        applied = false,
                    };
                    ServiceEventEffectApplier.Present(chosen, job, ctx);
                    Logger.Message($"Forced event {chosen.defName} on job #{job.jobId}.");
                });
        }
    }
}
