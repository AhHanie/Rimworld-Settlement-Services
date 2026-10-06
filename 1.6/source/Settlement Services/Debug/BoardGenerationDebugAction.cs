using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Board;

namespace Settlement_Services.Debug
{
    public static class BoardGenerationDebugAction
    {
        [DebugAction("Settlement Services", "Job board: force offer refresh", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void ForceOfferRefresh()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Logger.Message("No settlement under mouse.");
                return;
            }

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            BoardOfferScheduler.EnsureInitialized(settlement);
            JobBoardRecord board = domain.GetOrCreateJobBoard(settlement.ID);
            board.nextOfferTick = Find.TickManager.TicksGame;
            BoardOfferScheduler.Refresh(settlement);

            Logger.Message($"{settlement.Name}: board refreshed.");
            foreach (BoardJobRecord job in domain.BoardJobsForSettlement(settlement.ID).Where(j => j.status == BoardJobStatus.Available))
                Logger.Message($"  #{job.boardJobId} {job.defName}: {job.partySize} worker(s), {job.workTicks.ToStringTicksToPeriod()}, reward {job.reward.marketValue.ToStringMoney()} (budget {job.rewardBudget:F0}); {string.Join("; ", job.requirements.Select(r => $"{r.roleKind} {r.skill?.defName} {r.minLevel}+ x{r.requiredCount}"))}");
        }

        [DebugAction("Settlement Services", "Job board: generation report", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void GenerationReport()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Logger.Message("No settlement under mouse.");
                return;
            }

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            const int samples = 200;
            var byDef = new Dictionary<string, int>();
            var bySize = new Dictionary<int, int>();
            float rewardTotal = 0f;
            int generated = 0;

            for (int i = 0; i < samples; i++)
            {
                BoardJobRecord offer = BoardJobGenerator.TryGenerate(settlement, null);
                if (offer == null) continue;

                generated++;
                byDef[offer.defName] = byDef.TryGetValue(offer.defName, out int d) ? d + 1 : 1;
                bySize[offer.partySize] = bySize.TryGetValue(offer.partySize, out int s) ? s + 1 : 1;
                rewardTotal += offer.reward.marketValue;

                BoardRewardService.Discard(domain, offer);
            }

            Logger.Message($"Generated {generated}/{samples} offers; mean reward {(generated == 0 ? 0f : rewardTotal / generated):F0}.");
            foreach (KeyValuePair<string, int> pair in byDef.OrderBy(p => p.Key)) Logger.Message($"  {pair.Key}: {pair.Value}");
            foreach (KeyValuePair<int, int> pair in bySize.OrderBy(p => p.Key)) Logger.Message($"  party of {pair.Key}: {pair.Value}");
        }
    }
}
