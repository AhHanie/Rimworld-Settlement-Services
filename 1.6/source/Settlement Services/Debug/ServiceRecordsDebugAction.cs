using System.Linq;
using LudeonTK;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Debug
{
    public static class ServiceRecordsDebugAction
    {
        [DebugAction("Settlement Services", "Log all service records", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void LogAllServiceRecords()
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) { Logger.Message("No active world component."); return; }

            Logger.Message($"{domain.AllJobs.Count} job record(s):");
            foreach (ServiceJobRecord job in domain.AllJobs)
            {
                Settlement settlement = WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId);
                int? targetCaravanId = (job.target?.liveThing as Pawn)?.GetCaravan()?.ID;
                string targetsLabel = job.targets.Count == 0
                    ? "none"
                    : $"{job.targets.Count}x[{string.Join(", ", job.targets.Select(t => t?.snapshotLabel ?? "?"))}]";
                Logger.Message($"  #{job.jobId} {job.serviceDefName} @ {settlement?.Name ?? "?"} "
                    + $"status={job.status} channel={job.requestChannel} targets={targetsLabel} quantity={job.quantity} "
                    + $"quote={job.acceptedQuote?.totalCost.ToString() ?? "none"} "
                    + $"requesterCaravanId={job.requesterCaravanId} targetInCustody={job.targetInCustody} "
                    + $"targetCaravanId={(targetCaravanId.HasValue ? targetCaravanId.Value.ToString() : "none")}");
            }

            Logger.Message($"{domain.SettlementRecordsRaw.Count} settlement record(s):");
            foreach (SettlementRecord record in domain.SettlementRecordsRaw)
            {
                string specialties = record.capability != null ? string.Join(", ", record.capability.specialtyDefNames) : "none";
                string stock = record.stock.Count == 0 ? "none" : string.Join(", ", record.stock.Select(s => $"{s.stockThingDefName}={s.currentAmount}"));
                Logger.Message($"  settlement {record.settlementWorldObjectId}: "
                    + $"discoveries={record.discoveries.Count}, specialties=[{specialties}], stock=[{stock}]");
            }
        }
    }
}
