using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardJobCoordinator
    {
        internal const string PartyLostKey = "SettlementServices.JobBoard.Fail.PartyLost";
        internal const string ProviderHostileKey = "SettlementServices.JobBoard.Fail.ProviderHostile";
        internal const string ProviderLostKey = "SettlementServices.JobBoard.Fail.ProviderLost";
        internal const string DefMissingKey = "SettlementServices.JobBoard.Fail.DefMissing";
        internal const string AbortedKey = "SettlementServices.JobBoard.Fail.Aborted";

        private const int RetentionTicks = 900000;

        internal static string GetStartBlockKey(BoardJobRecord job, Settlement settlement, Caravan caravan, IReadOnlyList<Pawn> selected)
        {
            if (job == null || job.status != BoardJobStatus.Available) return "SettlementServices.JobBoard.Error.NoLongerAvailable";
            if (Find.TickManager.TicksGame >= job.expiryTick) return "SettlementServices.JobBoard.Error.Expired";

            string providerKey = GetProviderBlockKey(job, settlement);
            if (providerKey != null) return providerKey;

            if (caravan == null || caravan.Destroyed || !caravan.Spawned || CaravanVisitUtility.SettlementVisitedNow(caravan) != settlement)
                return "SettlementServices.JobBoard.Error.CaravanNotAtSettlement";

            if (!job.reward.settled && (job.reward.items.Count == 0 || job.reward.items.Any(i => i == null || i.Destroyed)))
                return "SettlementServices.JobBoard.Error.RewardLost";

            if (selected.Count != job.partySize) return "SettlementServices.JobBoard.Error.PartySize";

            foreach (Pawn pawn in selected)
            {
                string reason = BoardPawnEligibility.GetBlockReasonKey(pawn, caravan, job);
                if (reason != null) return reason;
            }

            if (!BoardRequirementMatcher.IsSatisfied(job.requirements, job.partySize, selected)) return "SettlementServices.JobBoard.Error.RequirementsNotMet";
            return null;
        }

        internal static bool TryStart(SettlementServicesWorldComponent domain, BoardJobRecord job, Settlement settlement, Caravan caravan, IReadOnlyList<Pawn> selected, out string errorKey)
        {
            errorKey = GetStartBlockKey(job, settlement, caravan, selected);
            if (errorKey != null) return false;

            int sourceCaravanId = caravan.ID;
            PlanetTile originTile = caravan.Tile;
            List<Pawn> party = selected.ToList();

            if (!BoardPawnCustodyService.TryDispatch(domain, caravan, party, out errorKey)) return false;

            int now = Find.TickManager.TicksGame;
            job.assignedPawns = party;
            job.sourceCaravanId = sourceCaravanId;
            job.originTile = originTile;
            job.pawnsReturned = false;
            job.startTick = now;
            job.completionTick = now + job.workTicks;
            job.settlementTile = settlement.Tile;
            SetStatus(job, BoardJobStatus.Active);

            BoardJobNotifier.NotifyStarted(job, settlement);
            return true;
        }

        internal static void TryCancelActive(SettlementServicesWorldComponent domain, BoardJobRecord job)
        {
            if (job == null || job.status != BoardJobStatus.Active) return;
            BeginReturn(domain, job, BoardJobStatus.Cancelled, AbortedKey);
        }

        internal static bool TryForceComplete(SettlementServicesWorldComponent domain, int boardJobId)
        {
            BoardJobRecord job = domain?.GetBoardJob(boardJobId);
            if (job == null || job.status != BoardJobStatus.Active) return false;

            BeginReturn(domain, job, BoardJobStatus.Completed, null);
            return true;
        }

        internal static void ExpireOffer(SettlementServicesWorldComponent domain, BoardJobRecord job, string reason)
        {
            if (job == null || job.status != BoardJobStatus.Available) return;

            SupportLog.Info($"Job board: offer #{job.boardJobId} ({job.defName}) at settlement {job.settlementWorldObjectId} removed at tick {Find.TickManager.TicksGame}: {reason}.");

            BoardRewardService.Discard(domain, job);
            SetStatus(job, BoardJobStatus.Expired);
        }

        internal static void TickDue(SettlementServicesWorldComponent domain)
        {
            int now = Find.TickManager.TicksGame;
            foreach (BoardJobRecord job in domain.BoardJobsRaw.ToList())
            {
                switch (job.status)
                {
                    case BoardJobStatus.Available:
                        TickOffer(domain, job, now);
                        break;
                    case BoardJobStatus.Active:
                        TickActive(domain, job, now);
                        break;
                    case BoardJobStatus.ReturnPending:
                        AdvanceReturn(domain, job);
                        break;
                    default:
                        TryPrune(domain, job, now);
                        break;
                }
            }
        }

        internal static void HandleSettlementDestroyed(SettlementServicesWorldComponent domain, int settlementWorldObjectId, PlanetTile tile)
        {
            foreach (BoardJobRecord job in domain.BoardJobsForSettlement(settlementWorldObjectId))
            {
                if (tile.Valid && !job.IsTerminal) job.settlementTile = tile;

                if (job.status == BoardJobStatus.Available) ExpireOffer(domain, job, "its settlement was destroyed");
                else if (job.status == BoardJobStatus.Active) BeginReturn(domain, job, BoardJobStatus.Failed, ProviderLostKey);
            }
        }

        internal static void Reconcile(SettlementServicesWorldComponent domain)
        {
            SupportLog.Info($"Job board reconcile: {domain.BoardJobsRaw.Count} board job(s) loaded ({domain.BoardJobsRaw.Count(j => j.status == BoardJobStatus.Available)} available, {domain.BoardJobsRaw.Count(j => j.HoldsWorkers)} with workers) at tick {Find.TickManager.TicksGame}; settlements known: {Find.WorldObjects?.Settlements?.Count ?? -1}.");
            var seenPawns = new HashSet<Pawn>();
            foreach (BoardJobRecord job in domain.BoardJobsRaw.ToList())
            {
                if (job.IsTerminal) continue;

                if (job.status == BoardJobStatus.Available)
                {
                    string brokenReason = null;
                    if (DefDatabase<SettlementBoardJobDef>.GetNamedSilentFail(job.defName) == null) brokenReason = "its job Def no longer exists";
                    else if (job.requirements.Any(r => r.skill == null)) brokenReason = "a requirement's skill no longer resolves";
                    else if (!job.reward.settled && (job.reward.items.Count == 0 || job.reward.items.Any(i => i == null || i.Destroyed)))
                        brokenReason = $"its banked reward is missing ({job.reward.items.Count(i => i != null && !i.Destroyed)} usable item(s) after load)";
                    if (brokenReason != null) ExpireOffer(domain, job, "load check: " + brokenReason);
                    continue;
                }

                if (!job.HoldsWorkers || job.pawnsReturned) continue;

                if (job.status == BoardJobStatus.Active) DropPawnsNoLongerInCustody(job);

                bool sharesWorker = false;
                for (int i = 0; i < job.assignedPawns.Count; i++)
                {
                    Pawn pawn = job.assignedPawns[i];
                    if (pawn == null || seenPawns.Add(pawn)) continue;

                    job.assignedPawns[i] = null;
                    sharesWorker = true;
                }

                if (sharesWorker)
                {
                    SupportLog.Error($"Board job {job.boardJobId} shares a worker with another board job; failing it so nobody is dispatched twice.");
                    if (job.status == BoardJobStatus.Active) BeginReturn(domain, job, BoardJobStatus.Failed, PartyLostKey);
                    continue;
                }

                if (job.status == BoardJobStatus.Active && DefDatabase<SettlementBoardJobDef>.GetNamedSilentFail(job.defName) == null)
                    BeginReturn(domain, job, BoardJobStatus.Failed, DefMissingKey);
            }
        }

        private static void DropPawnsNoLongerInCustody(BoardJobRecord job)
        {
            for (int i = 0; i < job.assignedPawns.Count; i++)
            {
                Pawn pawn = job.assignedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (!pawn.Spawned && pawn.holdingOwner == null) continue;

                SupportLog.Warning($"Board job {job.boardJobId}: worker {pawn.LabelShort} is no longer in custody; releasing them from the job.");
                job.assignedPawns[i] = null;
            }
        }

        private static void TickOffer(SettlementServicesWorldComponent domain, BoardJobRecord job, int now)
        {
            if (now >= job.expiryTick)
            {
                ExpireOffer(domain, job, $"expiry tick {job.expiryTick} reached");
                return;
            }

            Settlement settlement = WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId);
            if (settlement == null)
            {
                ExpireOffer(domain, job, "its settlement could not be found");
                return;
            }

            string providerKey = GetProviderBlockKey(job, settlement);
            if (providerKey != null) ExpireOffer(domain, job, $"provider check failed ({providerKey}; saved faction {job.providerFactionLoadId}, current faction {settlement.Faction?.GetUniqueLoadID() ?? "none"}, spawned {settlement.Spawned})");
        }

        private static void TickActive(SettlementServicesWorldComponent domain, BoardJobRecord job, int now)
        {
            if (job.assignedPawns.Any(p => p == null || p.Destroyed || p.Dead))
            {
                BeginReturn(domain, job, BoardJobStatus.Failed, PartyLostKey);
                return;
            }

            Settlement settlement = WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId);
            if (settlement == null)
            {
                BeginReturn(domain, job, BoardJobStatus.Failed, ProviderLostKey);
                return;
            }

            job.settlementTile = settlement.Tile;
            string providerKey = GetProviderBlockKey(job, settlement);
            if (providerKey != null)
            {
                BeginReturn(domain, job, BoardJobStatus.Failed, providerKey == "SettlementServices.JobBoard.Error.ProviderHostile" ? ProviderHostileKey : ProviderLostKey);
                return;
            }

            if (DefDatabase<SettlementBoardJobDef>.GetNamedSilentFail(job.defName) == null)
            {
                BeginReturn(domain, job, BoardJobStatus.Failed, DefMissingKey);
                return;
            }

            if (now >= job.completionTick) BeginReturn(domain, job, BoardJobStatus.Completed, null);
        }

        private static string GetProviderBlockKey(BoardJobRecord job, Settlement settlement)
        {
            if (settlement == null || !settlement.Spawned) return "SettlementServices.JobBoard.Error.ProviderLost";

            Faction faction = settlement.Faction;
            if (faction == null || faction.GetUniqueLoadID() != job.providerFactionLoadId) return "SettlementServices.JobBoard.Error.ProviderLost";
            if (faction.HostileTo(Faction.OfPlayer)) return "SettlementServices.JobBoard.Error.ProviderHostile";
            return null;
        }

        private static void BeginReturn(SettlementServicesWorldComponent domain, BoardJobRecord job, BoardJobStatus target, string failureKey)
        {
            job.returnTarget = target;
            job.failureKey = failureKey;
            SetStatus(job, BoardJobStatus.ReturnPending);
            AdvanceReturn(domain, job);
        }

        private static void AdvanceReturn(SettlementServicesWorldComponent domain, BoardJobRecord job)
        {
            Caravan receiver = null;
            if (!job.pawnsReturned && !BoardPawnCustodyService.TryReturn(domain, job, out receiver)) return;

            if (job.returnTarget == BoardJobStatus.Completed)
            {
                if (!job.reward.settled)
                {
                    receiver = receiver ?? BoardPawnCustodyService.FindHoldingCaravan(job);
                    if (receiver != null)
                    {
                        if (!BoardRewardService.TryDeliver(domain, job, receiver)) return;
                    }
                    else
                    {
                        BoardRewardService.QueueHomeDelivery(domain, job);
                    }
                }
            }
            else
            {
                BoardRewardService.Discard(domain, job);
            }

            SetStatus(job, job.returnTarget);
            BoardJobNotifier.NotifyFinished(job);
        }

        private static void TryPrune(SettlementServicesWorldComponent domain, BoardJobRecord job, int now)
        {
            if (now - job.statusChangedTick < RetentionTicks) return;
            if (job.HoldsWorkers || !job.reward.settled) return;

            domain.RemoveBoardJob(job);
        }

        private static void SetStatus(BoardJobRecord job, BoardJobStatus status)
        {
            job.status = status;
            job.statusChangedTick = Find.TickManager.TicksGame;
        }
    }
}
