using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Services.Medical;

namespace Settlement_Services.Framework.Events.Workers
{
    public class SurgicalComplicationServiceEventWorker : ServiceEventWorker
    {
        private const int TotalDamage = 20;

        public override bool CanApply(ServiceJobContext ctx)
        {
            Pawn pawn = ctx.ResolvePrimaryPawn();
            return pawn != null && !pawn.Destroyed && pawn.health != null && ctx.SelectedOptionKeys.Count > 0;
        }

        public override void Apply(ServiceJobContext ctx)
        {
            Pawn pawn = ctx.ResolvePrimaryPawn();
            if (pawn == null || pawn.Destroyed || pawn.health == null) return;

            BodyPartRecord operatedPart = ResolveOperatedPart(pawn, ctx.SelectedOptionKeys);

            var preExisting = new HashSet<Hediff_Injury>(pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>());
            HealthUtility.GiveRandomSurgeryInjuries(pawn, TotalDamage, operatedPart);

            SettlementServiceDef serviceDef = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(ctx.Job.serviceDefName);
            float quality = MedicalQualityService.TreatmentQuality(ctx.ResolveSettlement(), serviceDef?.category);

            foreach (Hediff_Injury injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>())
            {
                if (preExisting.Contains(injury)) continue;
                if (injury.TendableNow()) injury.Tended(quality, 1f);
            }
        }

        private static BodyPartRecord ResolveOperatedPart(Pawn pawn, IReadOnlyList<string> selectedOptionKeys)
        {
            string key = selectedOptionKeys.FirstOrDefault();
            if (key == null) return null;

            int separatorIndex = key.LastIndexOf('|');
            if (separatorIndex < 0 || !int.TryParse(key.Substring(separatorIndex + 1), out int partIndex))
                return null;

            List<BodyPartRecord> allParts = pawn.RaceProps?.body?.AllParts;
            return allParts != null && partIndex >= 0 && partIndex < allParts.Count ? allParts[partIndex] : null;
        }
    }
}
