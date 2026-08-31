using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Ideology
{
    public class IdeologyConversionServiceWorker : SettlementServiceWorker
    {
        private const string IdeoGroupKey = "SettlementServices.Label.IdeologyChoice";

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (Find.IdeoManager.classicMode)
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.IdeoClassicModeActive");
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            if (pawn.DevelopmentalStage.Baby() || pawn.ideo?.Ideo == null)
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.InvalidConversionTarget");
            return EligibleIdeos(ctx.Settlement, pawn).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoIdeologyOffered");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            Pawn pawn = ctx.SelectedTarget as Pawn;
            foreach (Ideo ideo in EligibleIdeos(ctx.Settlement, pawn))
                yield return new ServiceDisplayOption
                {
                    key = ideo.GetUniqueLoadID(),
                    label = ideo.name,
                    description = ideo.description,
                    groupKey = IdeoGroupKey,
                };
        }

        private static IEnumerable<Ideo> EligibleIdeos(Settlement settlement, Pawn pawn)
        {
            FactionIdeosTracker ideos = settlement?.Faction?.ideos;
            if (ideos == null) yield break;

            if (ideos.PrimaryIdeo != null && Eligible(ideos.PrimaryIdeo, pawn)) yield return ideos.PrimaryIdeo;
            foreach (Ideo minor in ideos.IdeosMinorListForReading)
                if (Eligible(minor, pawn)) yield return minor;
        }

        private static bool Eligible(Ideo ideo, Pawn pawn) => !ideo.hidden && (pawn == null || pawn.Ideo != ideo);

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) =>
            Enumerable.Empty<ServiceLineItem>();

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            string ideoKey = request.selectedOptionKeys.FirstOrDefault();
            if (ideoKey == null) return null;
            return EligibleIdeos(request.settlement, pawn).Any(i => i.GetUniqueLoadID() == ideoKey)
                ? null
                : "SettlementServices.Error.NoIdeologyOffered";
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn) || pawn.ideo?.Ideo == null)
                return ServiceStartResult.Fail("SettlementServices.Error.InvalidConversionTarget");
            return ResolveTargetIdeo(ctx.Job) != null ? ServiceStartResult.Ok : ServiceStartResult.Fail("SettlementServices.Error.TargetIdeologyNoLongerExists");
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn) || pawn.ideo?.Ideo == null) return ServiceCompletionResult.Ok();

            Ideo targetIdeo = ResolveTargetIdeo(ctx.Job);
            if (targetIdeo == null) return ServiceCompletionResult.Fail("SettlementServices.Error.TargetIdeologyNoLongerExists");

            if (pawn.ideo.Ideo != targetIdeo)
            {
                if (pawn.DevelopmentalStage.Baby() || !Eligible(targetIdeo, pawn))
                    return ServiceCompletionResult.Fail("SettlementServices.Error.InvalidConversionTarget");
                if (!pawn.ideo.IdeoConversionAttempt(1f, targetIdeo, applyCertaintyFactor: false))
                    return ServiceCompletionResult.Fail("SettlementServices.Error.InvalidConversionTarget");
            }

            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("SettlementServiceEvent_PositiveExperience");
            if (thought != null && pawn.needs?.mood != null) pawn.needs.mood.thoughts.memories.TryGainMemory(thought);

            Messages.Message("SettlementServices.Message.ConversionSucceeded"
                .Translate(pawn.LabelShortCap, pawn.ideo.Ideo.name, pawn.ideo.Certainty.ToStringPercent()),
                pawn, MessageTypeDefOf.NeutralEvent);

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static Ideo ResolveTargetIdeo(ServiceJobRecord job) => IdeoLookup.ResolveIdeo(job.selectedOptionKeys.FirstOrDefault());
    }
}
