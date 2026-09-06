using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Pricing;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Hiring
{
    public class HiringServiceWorker : SettlementServiceWorker
    {
        private const string CandidateGroupKey = "SettlementServices.Label.CandidateChoice";
        private const string CandidateKeyPrefix = "Candidate:";
        private const string ModeGroupKey = "SettlementServices.Label.HireModeChoice";
        private const string ModeJoinCaravanKey = "Mode:JoinCaravan";
        private const string ModeTravelHomeKey = "Mode:TravelHome";

        private const int BaselineContractTicks = 300000;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (ctx.Settlement?.Faction == null) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.SettlementNoLongerExists");
            if (Pool(ctx.Settlement).Count == 0) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCandidatesAvailable");

            if (ctx.SelectedOptionKeys.Count > 0)
            {
                if (!TryResolveSelectedCandidates(ctx.Settlement, ctx.SelectedOptionKeys, out List<HiringCandidateRecord> selected))
                    return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.CandidateNoLongerAvailable");
                if (selected.Count == 0)
                    return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCandidatesSelected");
            }

            if (ctx.SelectedOptionKeys.Contains(ModeJoinCaravanKey) && ctx.RequestingCaravan == null)
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoRequesterCaravan");

            if (ctx.SelectedOptionKeys.Contains(ModeTravelHomeKey))
            {
                HomeTravelPlan plan = PlanHomeTravel(ctx.Settlement, ctx.SelectedOptionKeys);
                if (!plan.Success) return ServiceAvailabilityReport.Unavailable(plan.ErrorKey);
            }

            return ServiceAvailabilityReport.Available;
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (ctx.Settlement?.Faction == null) yield break;

            foreach (HiringCandidateRecord candidate in Pool(ctx.Settlement))
            {
                yield return new ServiceDisplayOption
                {
                    key = CandidateKeyPrefix + candidate.candidateId,
                    label = candidate.pawn.LabelShortCap,
                    description = HighestSkillLabel(candidate.pawn),
                    groupKey = CandidateGroupKey,
                    allowMultipleSelectionInGroup = true,
                    pawnPreview = candidate.pawn,
                };
            }

            yield return new ServiceDisplayOption { key = ModeJoinCaravanKey, label = "SettlementServices.Label.HireModeJoinCaravan".Translate(), groupKey = ModeGroupKey };
            yield return new ServiceDisplayOption { key = ModeTravelHomeKey, label = "SettlementServices.Label.HireModeTravelHome".Translate(), groupKey = ModeGroupKey };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => Array.Empty<ServiceLineItem>();

        public override int? ExpectedDurationTicksFor(SettlementServiceRequest request)
        {
            if (!request.selectedOptionKeys.Contains(ModeTravelHomeKey)) return null;

            HomeTravelPlan plan = PlanHomeTravel(request.settlement, request.selectedOptionKeys);
            return plan.Success ? plan.EtaTicks : 0;
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return ServiceStartResult.Fail("SettlementServices.Error.SettlementNoLongerExists");
            if (!TryResolveSelectedCandidates(settlement, ctx.Job.selectedOptionKeys, out List<HiringCandidateRecord> selected) || selected.Count == 0)
                return ServiceStartResult.Fail("SettlementServices.Error.CandidateNoLongerAvailable");

            if (ctx.Job.selectedOptionKeys.Contains(ModeJoinCaravanKey))
            {
                if (ResolveRequesterCaravan(ctx.Job) == null) return ServiceStartResult.Fail("SettlementServices.Error.NoRequesterCaravan");
                return ServiceStartResult.Ok;
            }

            HomeTravelPlan plan = PlanHomeTravel(settlement, ctx.Job.selectedOptionKeys);
            if (!plan.Success) return ServiceStartResult.Fail(plan.ErrorKey);

            int etaTicks = ctx.Job.acceptedQuote.expectedDurationTicks;
            int arrivalTick = Find.TickManager.TicksGame + etaTicks;
            bool began = ctx.Domain.BeginHiringTransit(ctx.Job.jobId, settlement.ID, settlement.Tile, settlement.Faction.GetUniqueLoadID(), selected, plan.DestinationMap, arrivalTick);
            if (!began) return ServiceStartResult.Fail("SettlementServices.Error.CandidateNoLongerAvailable");

            AnnounceDeparture(selected.Select(c => c.pawn).ToList(), plan.DestinationMap, etaTicks);
            return ServiceStartResult.Ok;
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx) =>
            ctx.Job.selectedOptionKeys.Contains(ModeJoinCaravanKey) ? CompleteCaravanHire(ctx) : CompleteHomeTravelHire(ctx);

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated)
        {
            if (!ctx.Job.selectedOptionKeys.Contains(ModeJoinCaravanKey)) ctx.Domain.AbortHiringTransit(ctx.Job.jobId);
            return ServiceCancelResult.Ok();
        }

        private static ServiceCompletionResult CompleteCaravanHire(ServiceJobContext ctx)
        {
            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return ServiceCompletionResult.Fail("SettlementServices.Error.SettlementNoLongerExists");
            if (!TryResolveSelectedCandidates(settlement, ctx.Job.selectedOptionKeys, out List<HiringCandidateRecord> selected) || selected.Count == 0)
                return ServiceCompletionResult.Fail("SettlementServices.Error.CandidateNoLongerAvailable");

            Caravan caravan = ResolveRequesterCaravan(ctx.Job);
            if (caravan == null) return ServiceCompletionResult.Fail("SettlementServices.Error.NoRequesterCaravan");

            ctx.Domain.ClaimHiringCandidates(settlement.ID, selected.Select(c => c.candidateId));

            var hiredPawns = new List<Pawn>();
            foreach (HiringCandidateRecord candidate in selected)
            {
                Pawn pawn = candidate.pawn;
                PrepareContractor(pawn, settlement, ctx.Job);
                HandToCaravan(pawn, caravan);
                hiredPawns.Add(pawn);
            }

            AnnounceHired(hiredPawns);
            return ServiceCompletionResult.Ok();
        }

        private static ServiceCompletionResult CompleteHomeTravelHire(ServiceJobContext ctx)
        {
            if (!ctx.Domain.TryGetHiringTransit(ctx.Job.jobId, out HiringTransitRecord transit))
                return ServiceCompletionResult.Fail("SettlementServices.Error.CandidateNoLongerAvailable");

            Settlement originSettlement = WorldObjectLookup.ResolveSettlement(transit.originSettlementWorldObjectId);
            Map destinationMap = Find.Maps.FirstOrDefault(m => m.uniqueID == transit.destinationMapId);

            if (originSettlement == null || destinationMap == null || !destinationMap.IsPlayerHome)
            {
                ctx.Domain.AbortHiringTransit(ctx.Job.jobId);
                return ServiceCompletionResult.Fail("SettlementServices.Error.HomeDeliveryFailed");
            }

            List<Pawn> pawns = ctx.Domain.ReleaseHiringTransitForArrival(ctx.Job.jobId);
            if (pawns.Count == 0) return ServiceCompletionResult.Fail("SettlementServices.Error.CandidateNoLongerAvailable");

            foreach (Pawn pawn in pawns)
            {
                PrepareContractor(pawn, originSettlement, ctx.Job);
                GenSpawn.Spawn(pawn, DropCellFinder.TradeDropSpot(destinationMap), destinationMap);
            }

            AnnounceArrived(pawns);
            return ServiceCompletionResult.Ok();
        }

        private static void PrepareContractor(Pawn pawn, Settlement settlement, ServiceJobRecord job)
        {
            pawn.SetFaction(Faction.OfPlayer);

            pawn.workSettings.EnableAndInitialize();
            EnableWorkIfPermitted(pawn, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling"));
            EnableWorkIfPermitted(pawn, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cleaning"));

            AttachContract(pawn, settlement, job);
        }

        private static void EnableWorkIfPermitted(Pawn pawn, WorkTypeDef workType)
        {
            if (workType != null && !pawn.WorkTypeIsDisabled(workType)) pawn.workSettings.SetPriority(workType, 3);
        }

        private static void AttachContract(Pawn pawn, Settlement settlement, ServiceJobRecord job)
        {
            HediffDef contractDef = DefDatabase<HediffDef>.GetNamedSilentFail("SettlementService_TemporaryContract");
            if (contractDef == null) return;

            var hediff = (Hediff_TemporaryContract)HediffMaker.MakeHediff(contractDef, pawn);
            SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
            ServicePriorityTierDef tier = def != null ? ServicePricingEngine.ResolveTier(def, job.acceptedQuote?.selectedTierKey) : null;
            float multiplier = tier?.durationMultiplier ?? 1f;

            hediff.contractExpiryTick = Find.TickManager.TicksGame + Mathf.RoundToInt(BaselineContractTicks * multiplier);
            hediff.originSettlementWorldObjectId = settlement.ID;
            hediff.originFactionLoadId = settlement.Faction.GetUniqueLoadID();
            pawn.health.AddHediff(hediff);
        }

        private static void HandToCaravan(Pawn pawn, Caravan caravan)
        {
            Find.WorldPawns.PassToWorld(pawn);
            caravan.AddPawn(pawn, true);
        }

        private static void AnnounceHired(List<Pawn> hiredPawns)
        {
            if (hiredPawns.Count == 1)
                Messages.Message("SettlementServices.Message.ContractorHired".Translate(hiredPawns[0].LabelShortCap), hiredPawns[0], MessageTypeDefOf.PositiveEvent);
            else
                Messages.Message("SettlementServices.Message.ContractorsHired".Translate(hiredPawns.Count), MessageTypeDefOf.PositiveEvent);
        }

        private static void AnnounceArrived(List<Pawn> arrivedPawns)
        {
            if (arrivedPawns.Count == 1)
                Messages.Message("SettlementServices.Message.ContractorArrived".Translate(arrivedPawns[0].LabelShortCap), arrivedPawns[0], MessageTypeDefOf.PositiveEvent);
            else
                Messages.Message("SettlementServices.Message.ContractorsArrived".Translate(arrivedPawns.Count), MessageTypeDefOf.PositiveEvent);
        }

        private static void AnnounceDeparture(List<Pawn> pawns, Map destinationMap, int etaTicks)
        {
            if (etaTicks <= 0 || pawns.Count == 0) return;

            string destinationLabel = destinationMap.Parent.LabelCap;
            string etaLabel = etaTicks.ToStringTicksToPeriod();

            if (pawns.Count == 1)
                Messages.Message("SettlementServices.Message.ContractorEnRoute".Translate(pawns[0].LabelShortCap, destinationLabel, etaLabel), MessageTypeDefOf.NeutralEvent);
            else
                Messages.Message("SettlementServices.Message.ContractorsEnRoute".Translate(pawns.Count, destinationLabel, etaLabel), MessageTypeDefOf.NeutralEvent);
        }

        private readonly struct HomeTravelPlan
        {
            public bool Success { get; }
            public string ErrorKey { get; }
            public Map DestinationMap { get; }
            public int EtaTicks { get; }

            private HomeTravelPlan(bool success, string errorKey, Map destinationMap, int etaTicks)
            {
                Success = success;
                ErrorKey = errorKey;
                DestinationMap = destinationMap;
                EtaTicks = etaTicks;
            }

            public static readonly HomeTravelPlan NotApplicable = new HomeTravelPlan(true, null, null, 0);
            public static HomeTravelPlan Fail(string errorKey) => new HomeTravelPlan(false, errorKey, null, 0);
            public static HomeTravelPlan Ok(Map map, int etaTicks) => new HomeTravelPlan(true, null, map, etaTicks);
        }

        private static HomeTravelPlan PlanHomeTravel(Settlement settlement, IReadOnlyList<string> selectedOptionKeys)
        {
            if (!selectedOptionKeys.Contains(ModeTravelHomeKey)) return HomeTravelPlan.NotApplicable;

            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (homeMap == null) return HomeTravelPlan.Fail("SettlementServices.Error.NoHomeMapForTravel");

            PlanetTile originTile = settlement.Tile;
            PlanetTile destinationTile = homeMap.Tile;
            if (!originTile.Valid || !destinationTile.Valid) return HomeTravelPlan.Fail("SettlementServices.Error.NoReachableHomeForTravel");
            if (originTile == destinationTile) return HomeTravelPlan.Ok(homeMap, 0);

            using (WorldPath path = originTile.Layer.Pather.FindPath(originTile, destinationTile, null))
            {
                if (!path.Found) return HomeTravelPlan.Fail("SettlementServices.Error.NoReachableHomeForTravel");
            }

            int eta = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(originTile, destinationTile, null);
            return HomeTravelPlan.Ok(homeMap, Mathf.Max(0, eta));
        }

        private static bool TryResolveSelectedCandidates(Settlement settlement, IReadOnlyList<string> selectedOptionKeys, out List<HiringCandidateRecord> candidates)
        {
            var ids = new HashSet<int>();
            foreach (string key in selectedOptionKeys)
            {
                if (!key.StartsWith(CandidateKeyPrefix)) continue;
                if (!int.TryParse(key.Substring(CandidateKeyPrefix.Length), out int id) || !ids.Add(id))
                {
                    candidates = null;
                    return false;
                }
            }

            candidates = Pool(settlement).Where(c => ids.Contains(c.candidateId)).ToList();
            return candidates.Count == ids.Count;
        }

        private static IReadOnlyList<HiringCandidateRecord> Pool(Settlement settlement) =>
            SettlementServicesWorldComponent.Current.GetOrRefreshHiringCandidates(settlement, id => HiringCandidateGenerator.Generate(settlement, id));

        private static Caravan ResolveRequesterCaravan(ServiceJobRecord job) =>
            job.requesterCaravanId < 0 ? null : Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == job.requesterCaravanId);

        private static string HighestSkillLabel(Pawn pawn)
        {
            SkillRecord best = pawn.skills?.skills
                .Where(s => s != null && !s.TotallyDisabled)
                .OrderByDescending(s => s.Level)
                .ThenBy(s => s.def.defName, StringComparer.Ordinal)
                .FirstOrDefault();

            return best == null
                ? "SettlementServices.Label.CandidateNoUsableSkill".Translate()
                : "SettlementServices.Label.CandidateSkillSummary".Translate(best.def.LabelCap, best.Level);
        }
    }
}
