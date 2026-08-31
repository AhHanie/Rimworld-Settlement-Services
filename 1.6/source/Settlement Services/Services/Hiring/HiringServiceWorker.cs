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
        private const string HazardousWorkKey = "AllowHazardousWork";

        private const int PoolCapacity = 2;
        private const int PoolRefreshIntervalTicks = 180000;
        private const int CandidateExpiryTicks = 360000;

        private const int BaselineContractTicks = 300000;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (ctx.Settlement?.Faction == null) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.SettlementNoLongerExists");
            if (Pool(ctx.Settlement).Count == 0) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCandidatesAvailable");

            HiringCandidateRecord candidate = ResolveCandidate(ctx.Settlement, ctx.SelectedOptionKeys);
            if (candidate != null && candidate.refusesHazardousWork && ctx.SelectedOptionKeys.Contains(HazardousWorkKey))
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.CandidateRefusesHazardousWork");

            if (ctx.SelectedOptionKeys.Contains(ModeJoinCaravanKey) && ctx.RequestingCaravan == null)
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoRequesterCaravan");

            return ServiceAvailabilityReport.Available;
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (ctx.Settlement?.Faction == null) yield break;

            foreach (HiringCandidateRecord candidate in Pool(ctx.Settlement))
                yield return new ServiceDisplayOption
                {
                    key = CandidateKeyPrefix + candidate.candidateId,
                    label = CandidateLabel(candidate),
                    groupKey = CandidateGroupKey,
                };

            yield return new ServiceDisplayOption { key = ModeJoinCaravanKey, label = "SettlementServices.Label.HireModeJoinCaravan".Translate(), groupKey = ModeGroupKey };
            yield return new ServiceDisplayOption { key = ModeTravelHomeKey, label = "SettlementServices.Label.HireModeTravelHome".Translate(), groupKey = ModeGroupKey };

            yield return new ServiceDisplayOption { key = HazardousWorkKey, label = "SettlementServices.Label.AllowHazardousWork".Translate() };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            HiringCandidateRecord candidate = ResolveCandidate(request.settlement, request.selectedOptionKeys);
            if (candidate != null) items.Add(new ServiceLineItem("SettlementServices.LineItem.ContractorWage", candidate.wage));
            return items;
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return ServiceStartResult.Fail("SettlementServices.Error.SettlementNoLongerExists");
            if (ResolveCandidate(settlement, ctx.Job.selectedOptionKeys) == null) return ServiceStartResult.Fail("SettlementServices.Error.CandidateNoLongerAvailable");

            if (ctx.Job.selectedOptionKeys.Contains(ModeJoinCaravanKey) && ResolveRequesterCaravan(ctx.Job) == null)
                return ServiceStartResult.Fail("SettlementServices.Error.NoRequesterCaravan");
            if (!ctx.Job.selectedOptionKeys.Contains(ModeJoinCaravanKey) && !Find.Maps.Any(m => m.IsPlayerHome))
                return ServiceStartResult.Fail("SettlementServices.Error.NoHomeMapForTravel");

            return ServiceStartResult.Ok;
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            Settlement settlement = ctx.ResolveSettlement();
            HiringCandidateRecord candidate = settlement == null ? null : ResolveCandidate(settlement, ctx.Job.selectedOptionKeys);
            if (settlement == null || candidate == null) return ServiceCompletionResult.Fail("SettlementServices.Error.CandidateNoLongerAvailable");

            ctx.Domain.RemoveHiringCandidate(settlement.ID, candidate.candidateId);

            Pawn pawn = GenerateContractor(settlement, candidate);
            AttachContract(pawn, settlement, ctx.Job);

            if (ctx.Job.selectedOptionKeys.Contains(ModeJoinCaravanKey)) HandToCaravan(pawn, ResolveRequesterCaravan(ctx.Job));
            else HandToHomeMap(pawn);

            Messages.Message("SettlementServices.Message.ContractorHired".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static Pawn GenerateContractor(Settlement settlement, HiringCandidateRecord candidate)
        {
            var request = new PawnGenerationRequest(settlement.Faction.def.basicMemberKind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false);
            Pawn pawn = PawnGenerator.GeneratePawn(request);

            WorkTypeDef specialty = DefDatabase<WorkTypeDef>.GetNamedSilentFail(candidate.specialtyWorkTypeDefName);
            SkillDef relevantSkill = specialty?.relevantSkills.FirstOrDefault();
            if (relevantSkill != null)
            {
                SkillRecord skill = pawn.skills.GetSkill(relevantSkill);
                skill.Level = candidate.skillLevel;
                skill.passion = Passion.Major;
            }

            pawn.workSettings.EnableAndInitialize();
            pawn.workSettings.DisableAll();
            EnableWorkIfPermitted(pawn, specialty);
            EnableWorkIfPermitted(pawn, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling"));
            EnableWorkIfPermitted(pawn, DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cleaning"));

            return pawn;
        }

        private static void EnableHazardousWork(Pawn pawn)
        {
            foreach (string defName in new[] { "Firefighting", "Hunting", "Construction" })
                EnableWorkIfPermitted(pawn, DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName));
        }

        private static void EnableWorkIfPermitted(Pawn pawn, WorkTypeDef workType)
        {
            if (workType != null && !pawn.WorkTypeIsDisabled(workType)) pawn.workSettings.SetPriority(workType, 3);
        }

        private static void AttachContract(Pawn pawn, Settlement settlement, ServiceJobRecord job)
        {
            if (job.selectedOptionKeys.Contains(HazardousWorkKey)) EnableHazardousWork(pawn);

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

        private static void HandToHomeMap(Pawn pawn)
        {
            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            GenSpawn.Spawn(pawn, DropCellFinder.TradeDropSpot(homeMap), homeMap);
        }

        private static HiringCandidateRecord ResolveCandidate(Settlement settlement, IReadOnlyList<string> selectedOptionKeys)
        {
            string key = selectedOptionKeys?.FirstOrDefault(k => k.StartsWith(CandidateKeyPrefix));
            if (key == null || !int.TryParse(key.Substring(CandidateKeyPrefix.Length), out int candidateId)) return null;
            return Pool(settlement).FirstOrDefault(c => c.candidateId == candidateId);
        }

        private static IReadOnlyList<HiringCandidateRecord> Pool(Settlement settlement) =>
            SettlementServicesWorldComponent.Current.GetOrRefreshHiringCandidates(settlement.ID, PoolCapacity, PoolRefreshIntervalTicks, id => HiringCandidateGenerator.Generate(settlement, id, CandidateExpiryTicks));

        private static Caravan ResolveRequesterCaravan(ServiceJobRecord job) =>
            job.requesterCaravanId < 0 ? null : Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == job.requesterCaravanId);

        private static string CandidateLabel(HiringCandidateRecord candidate)
        {
            string tierKey = candidate.qualityTierKey == "Expert" ? "SettlementServices.Label.HiringTierExpert"
                : candidate.qualityTierKey == "Skilled" ? "SettlementServices.Label.HiringTierSkilled"
                : "SettlementServices.Label.HiringTierStandard";
            WorkTypeDef specialty = DefDatabase<WorkTypeDef>.GetNamedSilentFail(candidate.specialtyWorkTypeDefName);
            string workLabel = specialty?.pawnLabel ?? candidate.specialtyWorkTypeDefName;
            return "SettlementServices.Label.CandidateOption".Translate(tierKey.Translate(), workLabel, candidate.wage);
        }
    }
}
