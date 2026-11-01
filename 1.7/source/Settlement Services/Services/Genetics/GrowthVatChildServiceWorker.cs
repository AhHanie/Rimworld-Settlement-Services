using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Services.Genetics
{
    internal sealed class GrowthVatChildServiceWorker : GrowthVatServiceWorkerBase
    {
        protected override GrowthVatKind Kind => GrowthVatKind.Child;

        protected override string TargetBlockKey(Thing target, Caravan caravan, bool atStart)
        {
            if (!(target is Pawn pawn)) return "SettlementServices.Error.InvalidTarget";
            if (pawn.Destroyed || pawn.Dead) return "SettlementServices.Error.TargetNoLongerExists";
            if (!pawn.RaceProps.Humanlike || !pawn.IsColonist || pawn.IsQuestLodger()) return "SettlementServices.Error.GrowthVatChildNotColonist";
            if (pawn.ageTracker.AgeBiologicalYearsFloat >= GrowthVatMath.AdultAge) return "SettlementServices.Error.GrowthVatChildTooOld";
            if (pawn.health.hediffSet.HasHediff(HediffDefOf.BioStarvation)) return "SettlementServices.Error.GrowthVatChildBiostarving";
            if (!OfferedMilestones(pawn).Any()) return "SettlementServices.Error.GrowthVatNoMilestoneAhead";
            return null;
        }

        public override bool ShouldListTarget(Thing target) =>
            target is Pawn pawn && pawn.RaceProps.Humanlike && pawn.IsColonist && pawn.ageTracker.AgeBiologicalYearsFloat < GrowthVatMath.AdultAge;

        protected override GrowthVatProjection Project(Thing target, GrowthVatMilestone milestone) => GrowthVatMath.ProjectChild((Pawn)target, milestone);

        protected override IEnumerable<GrowthVatMilestone> OfferedMilestones(Thing target)
        {
            if (!(target is Pawn pawn) || pawn.ageTracker == null) return Enumerable.Empty<GrowthVatMilestone>();

            long ageTicks = pawn.ageTracker.AgeBiologicalTicks;
            return GrowthVatMilestone.All.Where(m => !m.IsBirth && m.TargetAgeTicks > ageTicks);
        }

        protected override bool TryBeginOccupancy(ServiceJobContext ctx, Thing target) =>
            target is Pawn pawn && ctx.Domain.TryEnterGrowthVat(ctx.Job.jobId, pawn);
    }
}
