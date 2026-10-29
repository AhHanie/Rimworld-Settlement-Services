using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Genetics
{
    public class DeathrestServiceWorker : SettlementServiceWorker
    {
        private const int CycleTicks = Gene_Deathrest.BaseDeathrestTicksWithoutInterruptedHediff;
        private const float NeedTolerance = 0.001f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return TryValidate(pawn, requireNeedBelowFull: true, requireNotDeathresting: true, out string reasonKey)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable(reasonKey);
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target?.thing is Pawn pawn)) return null;
            return TryValidate(pawn, requireNeedBelowFull: true, requireNotDeathresting: true, out string reasonKey) ? null : reasonKey;
        }

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn) || !TryGetDeathrest(pawn, out _, out Need_Deathrest need)) yield break;
            float projected = Mathf.Min(need.MaxLevel, need.CurLevel + CycleGain);
            yield return "SettlementServices.Label.DeathrestProjectedNeed".Translate((projected / need.MaxLevel).ToStringPercent());
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override int? ExpectedDurationTicksFor(SettlementServiceRequest request) => CycleTicks;

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceStartResult.Ok;
            return TryValidate(pawn, requireNeedBelowFull: true, requireNotDeathresting: true, out string reasonKey)
                ? ServiceStartResult.Ok
                : ServiceStartResult.Fail(reasonKey);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceCompletionResult.Ok();
            if (!TryValidate(pawn, requireNeedBelowFull: false, requireNotDeathresting: false, out string reasonKey))
                return ServiceCompletionResult.Fail(reasonKey);

            TryGetDeathrest(pawn, out Gene_Deathrest gene, out Need_Deathrest need);
            ApplyUnassistedOffMapCycle(pawn, gene, need);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static float CycleGain => Need_Deathrest.GainPerDayDeathresting * (CycleTicks / 60000f);

        private static void ApplyUnassistedOffMapCycle(Pawn pawn, Gene_Deathrest gene, Need_Deathrest need)
        {
            gene.RemoveOldDeathrestBonuses();

            need.CurLevel += CycleGain;

            if (need.CurLevel > 0f)
                RemoveHediff(pawn, HediffDefOf.DeathrestExhaustion);
            RemoveHediff(pawn, HediffDefOf.InterruptedDeathrest);

            gene.deathrestTicks = 0;
            gene.adjustedDeathrestTicks = 0f;
        }

        private static void RemoveHediff(Pawn pawn, HediffDef def)
        {
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null) pawn.health.RemoveHediff(hediff);
        }

        private static bool TryGetDeathrest(Pawn pawn, out Gene_Deathrest gene, out Need_Deathrest need)
        {
            gene = null;
            need = null;
            if (!ModsConfig.BiotechActive || pawn?.genes == null || pawn.needs == null) return false;
            gene = pawn.genes.GetFirstGeneOfType<Gene_Deathrest>();
            if (gene == null) return false;
            return pawn.needs.TryGetNeed(out need);
        }

        private static bool TryValidate(Pawn pawn, bool requireNeedBelowFull, bool requireNotDeathresting, out string reasonKey)
        {
            bool validMember = pawn != null && !pawn.Dead && pawn.RaceProps.Humanlike && !pawn.IsQuestLodger()
                && (pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony);
            if (!validMember) { reasonKey = "SettlementServices.Error.InvalidDeathrestTarget"; return false; }
            if (!TryGetDeathrest(pawn, out _, out Need_Deathrest need))
            { reasonKey = "SettlementServices.Error.NoDeathrestGene"; return false; }
            if (requireNotDeathresting && pawn.Deathresting)
            { reasonKey = "SettlementServices.Error.AlreadyDeathresting"; return false; }
            if (SanguophageUtility.ShouldBeDeathrestingOrInComaInsteadOfDead(pawn))
            { reasonKey = "SettlementServices.Error.DeathrestLethalCondition"; return false; }
            if (requireNeedBelowFull && need.CurLevel >= need.MaxLevel - NeedTolerance)
            { reasonKey = "SettlementServices.Error.DeathrestNotNeeded"; return false; }

            reasonKey = null;
            return true;
        }
    }
}
