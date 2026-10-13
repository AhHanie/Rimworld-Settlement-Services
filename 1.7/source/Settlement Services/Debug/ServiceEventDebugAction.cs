using System;
using System.Collections.Generic;
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
        [DebugAction("Settlement Services", "Force-roll service event...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceRollServiceEvent()
        {
            WithActiveJob(job =>
            {
                if (!TryResolveJobContext(job, out SettlementServicesWorldComponent domain, out SettlementServiceDef def, out ServiceJobContext ctx)) return;

                if (ServiceEventRollService.ForceRandom(domain, def, job, ctx))
                    Logger.Message($"Forced random event {job.eventOutcome?.eventDefName} on job #{job.jobId}.");
                else
                    Logger.Message("No eligible ServiceEventDefs for this job right now.");
            });
        }

        [DebugAction("Settlement Services", "Force-select service event...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceSelectServiceEvent()
        {
            WithActiveJob(job =>
            {
                if (!TryResolveJobContext(job, out SettlementServicesWorldComponent domain, out SettlementServiceDef def, out ServiceJobContext ctx)) return;

                List<ServiceEventDef> options = ServiceEventRegistry.EligibleEvents(def, ctx).OrderBy(e => e.defName).ToList();
                if (options.Count == 0) { Logger.Message("No eligible ServiceEventDefs for this job right now."); return; }

                Dialog_DebugOptionListLister.ShowSimpleDebugMenu(options,
                    e => $"{e.defName} ({e.triggerPhase})",
                    chosen =>
                    {
                        ServiceEventRollService.ForceSpecific(domain, chosen, job, ctx);
                        Logger.Message($"Forced event {chosen.defName} on job #{job.jobId}.");
                    });
            });
        }

        private static void WithActiveJob(Action<ServiceJobRecord> onChosen)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) { Logger.Message("No Settlement Services world component."); return; }

            var candidates = domain.AllJobs.Where(j => j.status == ServiceJobStatus.Active).ToList();
            if (candidates.Count == 0) { Logger.Message("No Active jobs to target."); return; }

            Dialog_DebugOptionListLister.ShowSimpleDebugMenu(candidates,
                job => $"#{job.jobId} {job.serviceDefName} ({job.status})",
                onChosen);
        }

        private static bool TryResolveJobContext(ServiceJobRecord job, out SettlementServicesWorldComponent domain, out SettlementServiceDef def, out ServiceJobContext ctx)
        {
            domain = SettlementServicesWorldComponent.Current;
            def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
            ctx = default;
            if (def == null) { Logger.Error($"Job #{job.jobId}'s service def '{job.serviceDefName}' no longer resolves."); return false; }

            ctx = new ServiceJobContext(domain, job).ForUnitIndex(job.eventTargetIndex);
            return true;
        }
    }
}
