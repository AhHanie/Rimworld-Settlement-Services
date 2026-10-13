using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class CaravanAnimalJoinServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) =>
            IsEligibleRecipient(ResolveRequesterCaravan(ctx)) && EligibleAnimalKinds().Any();

        public override void Apply(ServiceJobContext ctx)
        {
            Caravan caravan = ResolveRequesterCaravan(ctx);
            if (!IsEligibleRecipient(caravan)) return;

            if (!EligibleAnimalKinds().TryRandomElement(out PawnKindDef kind)) return;

            Pawn animal = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, caravan.Tile));
            caravan.AddPawn(animal, addCarriedPawnToWorldPawnsIfAny: true);
        }

        private static Caravan ResolveRequesterCaravan(ServiceJobContext ctx)
        {
            int requesterCaravanId = ctx.Job.requesterCaravanId;
            if (requesterCaravanId < 0) return null;
            return Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == requesterCaravanId);
        }

        private static bool IsEligibleRecipient(Caravan caravan) =>
            caravan != null && !caravan.Destroyed && caravan.Spawned && caravan.IsPlayerControlled && caravan.Faction == Faction.OfPlayer;

        private static IEnumerable<PawnKindDef> EligibleAnimalKinds() =>
            DefDatabase<PawnKindDef>.AllDefsListForReading.Where(kind =>
                kind.race?.race != null
                && kind.RaceProps.Animal
                && kind.race.GetStatValueAbstract(StatDefOf.Wildness) < 1f
                && kind.RaceProps.animalType != AnimalType.Dryad
                && kind.RaceProps.allowedOnCaravan
                && !kind.RaceProps.neverIncludeInQuests);
    }
}
