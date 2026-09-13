using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class PreventativeTreatmentServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) =>
            EligibleRecipients(ctx).Any();

        public override void Apply(ServiceJobContext ctx)
        {
            foreach (Pawn pawn in EligibleRecipients(ctx))
            {
                Thing pill = ThingMaker.MakeThing(ThingDefOf.Penoxycyline);
                pill.Ingested(pawn, 0f);
            }
        }

        private static IEnumerable<Pawn> EligibleRecipients(ServiceJobContext ctx)
        {
            var seen = new HashSet<Pawn>();
            foreach (TargetSnapshot target in ctx.ResolveTargets())
            {
                if (!(target?.liveThing is Pawn pawn)) continue;
                if (pawn.Destroyed || pawn.Dead || pawn.health == null) continue;
                if (seen.Add(pawn)) yield return pawn;
            }
        }
    }
}
