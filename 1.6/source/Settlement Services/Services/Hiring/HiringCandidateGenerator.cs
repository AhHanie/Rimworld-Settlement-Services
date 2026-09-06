using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Services.Hiring
{
    internal static class HiringCandidateGenerator
    {
        public static HiringCandidateRecord Generate(Settlement settlement, int candidateId)
        {
            PawnKindDef kindDef = settlement.Faction?.RandomPawnKind();
            if (kindDef?.race == null || !kindDef.RaceProps.Humanlike)
            {
                Settlement_Services.SupportLog.Warning($"Hiring candidate rejected for {settlement.LabelCap} ({settlement.Faction?.def?.defName ?? "no faction"}): no humanlike pawn kind available (RandomPawnKind returned {(kindDef == null ? "null" : $"'{kindDef.defName}'")}).");
                return null;
            }

            var request = new PawnGenerationRequest(kindDef, settlement.Faction, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                Settlement_Services.SupportLog.Warning($"Hiring candidate rejected for {settlement.LabelCap}: PawnGenerator.GeneratePawn returned null for kind '{kindDef.defName}'.");
                return null;
            }

            if (pawn.DevelopmentalStage != DevelopmentalStage.Adult)
            {
                Settlement_Services.SupportLog.Warning($"Hiring candidate rejected for {settlement.LabelCap}: generated pawn '{pawn.LabelShortCap}' (kind '{kindDef.defName}') was {pawn.DevelopmentalStage}, not Adult.");
                pawn.Destroy(DestroyMode.Vanish);
                return null;
            }

            return new HiringCandidateRecord
            {
                candidateId = candidateId,
                pawn = pawn,
            };
        }
    }
}
