using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardOfferScheduler
    {
        internal const int InitialOffers = 3;
        internal const int MaxVisibleOffers = 6;

        private const int CadenceMinTicks = 90000;
        private const int CadenceMaxTicks = 150000;
        private const int RetryAfterFailureTicks = 60000;
        private const int MaxCatchUpOffers = 4;

        internal static void EnsureInitialized(Settlement settlement)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null || settlement == null) return;

            domain.EnsureBoardReconciled();

            JobBoardRecord board = domain.GetOrCreateJobBoard(settlement.ID);
            if (board.initialized)
            {
                Refresh(settlement);
                return;
            }

            board.initialized = true;

            bool failed = false;
            for (int i = 0; i < InitialOffers && !failed; i++) failed = !GenerateOne(domain, settlement, board);

            board.nextOfferTick = Find.TickManager.TicksGame + (failed ? RetryAfterFailureTicks : RollCadence(settlement, board));
        }

        internal static void Refresh(Settlement settlement)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null || settlement == null) return;

            domain.EnsureBoardReconciled();

            JobBoardRecord board = domain.TryGetJobBoard(settlement.ID);
            if (board == null || !board.initialized) return;

            int now = Find.TickManager.TicksGame;
            foreach (BoardJobRecord offer in domain.BoardJobsForSettlement(settlement.ID))
            {
                if (offer.status == BoardJobStatus.Available && now >= offer.expiryTick) BoardJobCoordinator.ExpireOffer(domain, offer, $"expiry tick {offer.expiryTick} reached (refresh at tick {now})");
            }

            if (now < board.nextOfferTick) return;

            bool failed = false;
            int missedSlots = 1 + (now - board.nextOfferTick) / CadenceMinTicks;
            int toAdd = Math.Min(missedSlots, MaxCatchUpOffers);
            for (int i = 0; i < toAdd && !failed && AvailableCount(domain, settlement) < MaxVisibleOffers; i++)
                failed = !GenerateOne(domain, settlement, board);

            board.nextOfferTick = now + (failed ? RetryAfterFailureTicks : RollCadence(settlement, board));
        }

        internal static void TickDue(SettlementServicesWorldComponent domain)
        {
            int now = Find.TickManager.TicksGame;
            foreach (SettlementRecord record in domain.SettlementRecordsRaw.ToList())
            {
                JobBoardRecord board = record.jobBoard;
                if (board == null || !board.initialized) continue;

                bool due = now >= board.nextOfferTick
                    || domain.BoardJobsForSettlement(record.settlementWorldObjectId).Any(j => j.status == BoardJobStatus.Available && now >= j.expiryTick);
                if (!due) continue;

                Settlement settlement = WorldObjectLookup.ResolveSettlement(record.settlementWorldObjectId);
                if (settlement == null) continue;

                Refresh(settlement);
            }
        }

        private static int AvailableCount(SettlementServicesWorldComponent domain, Settlement settlement) =>
            domain.BoardJobsForSettlement(settlement.ID).Count(j => j.status == BoardJobStatus.Available);

        private static bool GenerateOne(SettlementServicesWorldComponent domain, Settlement settlement, JobBoardRecord board) =>
            WithSeed(settlement, board, () => TryAddOffer(domain, settlement));

        private static int RollCadence(Settlement settlement, JobBoardRecord board) =>
            WithSeedValue(settlement, board, () => Rand.RangeInclusive(CadenceMinTicks, CadenceMaxTicks));

        private static bool TryAddOffer(SettlementServicesWorldComponent domain, Settlement settlement)
        {
            List<BoardJobRecord> available = domain.BoardJobsForSettlement(settlement.ID)
                .Where(j => j.status == BoardJobStatus.Available)
                .ToList();
            if (available.Count >= MaxVisibleOffers) return false;
            if (settlement.Faction == null || settlement.Faction.HostileTo(Faction.OfPlayer)) return false;

            BoardJobRecord offer = BoardJobGenerator.TryGenerate(settlement, available.Select(j => j.defName).ToList());
            if (offer == null) return false;

            domain.AddBoardJob(offer);
            return true;
        }

        private static bool WithSeed(Settlement settlement, JobBoardRecord board, Func<bool> action) =>
            WithSeedValue(settlement, board, action);

        private static T WithSeedValue<T>(Settlement settlement, JobBoardRecord board, Func<T> action)
        {
            board.generationSerial++;
            int seed = Gen.HashCombineInt(Gen.HashCombineInt(Find.World.info.Seed, settlement.ID), board.generationSerial);

            Rand.PushState(seed);
            try
            {
                return action();
            }
            finally
            {
                Rand.PopState();
            }
        }
    }
}
