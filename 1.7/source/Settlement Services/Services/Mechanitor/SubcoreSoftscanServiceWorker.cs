using System.Collections.Generic;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Mechanitor
{
    public class SubcoreSoftscanServiceWorker : SubcoreScanServiceWorker
    {
        protected override string SubcoreDefName => "SubcoreRegular";

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (ctx.SelectedTarget is Pawn pawn)
                yield return "SettlementServices.Warning.SubcoreSoftscanSickness".Translate(pawn.LabelShortCap, HediffDefOf.ScanningSickness.label);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!TryMakeSubcore(ctx, out Pawn pawn, out Thing subcore, out string errorKey)) return ServiceCompletionResult.Fail(errorKey);

            if (!pawn.health.hediffSet.HasHediff(HediffDefOf.ScanningSickness)) pawn.health.AddHediff(HediffDefOf.ScanningSickness);

            return ServiceCompletionResult.Ok(requiresCollection: true, resultThings: new List<Thing> { subcore });
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }
}
