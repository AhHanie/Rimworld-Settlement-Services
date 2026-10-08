using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Services.Animals
{
    internal static class AnimalVendorOfferGenerator
    {
        private const string TraderKindDefName = "SettlementServices_AnimalVendorStock";

        public static List<Pawn> Generate(Settlement settlement)
        {
            var animals = new List<Pawn>();

            TraderKindDef traderDef = DefDatabase<TraderKindDef>.GetNamedSilentFail(TraderKindDefName);
            if (traderDef == null)
            {
                Settlement_Services.SupportLog.Error($"Animal vendor generation skipped for {settlement.LabelCap}: TraderKindDef '{TraderKindDefName}' is missing.");
                return animals;
            }

            var parms = new ThingSetMakerParams
            {
                traderDef = traderDef,
                tile = settlement.Tile,
                makingFaction = settlement.Faction,
            };

            List<Thing> generated = ThingSetMakerDefOf.TraderStock.root.Generate(parms);
            foreach (Thing thing in generated)
            {
                if (thing is Pawn pawn && pawn.RaceProps.Animal && !pawn.Destroyed && !pawn.Dead)
                {
                    animals.Add(pawn);
                    continue;
                }

                Settlement_Services.SupportLog.Warning($"Animal vendor generation for {settlement.LabelCap} produced an unexpected thing '{thing?.LabelCap ?? "null"}'; discarding it.");
                if (thing != null && !thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
            }

            return animals;
        }
    }
}
