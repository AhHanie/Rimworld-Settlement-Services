using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Delivery
{
    public sealed class ServicePodManifestEstimate
    {
        public readonly List<float> unsplittableMasses = new List<float>();
        public float splittableMass;

        public void Add(ServicePodManifestEstimate other)
        {
            if (other == null) return;
            unsplittableMasses.AddRange(other.unsplittableMasses);
            splittableMass += other.splittableMass;
        }

        public float TotalMass => unsplittableMasses.Sum() + splittableMass;

        public static ServicePodManifestEstimate ForTargets(SettlementServiceRequest unit, ServicePodManifestContext context)
        {
            var estimate = new ServicePodManifestEstimate();
            if (unit.targets.NullOrEmpty()) return null;

            foreach (ServiceTargetSelection selection in unit.targets)
            {
                Thing thing = selection?.thing;
                if (thing == null) return null;

                if (thing is Pawn pawn)
                {
                    estimate.unsplittableMasses.Add(PawnReturnMass(pawn, context));
                }
                else
                {
                    float unitMass = thing.GetStatValue(StatDefOf.Mass);
                    if (thing.def.stackLimit > 1 && thing.stackCount > 1) estimate.splittableMass += unitMass * thing.stackCount;
                    else estimate.unsplittableMasses.Add(unitMass * thing.stackCount);
                }
            }
            return estimate;
        }

        private static float PawnReturnMass(Pawn pawn, ServicePodManifestContext context)
        {
            float mass = pawn.GetStatValue(StatDefOf.Mass) + MassUtility.GearMass(pawn);
            if (pawn.inventory?.innerContainer == null) return mass;

            bool allStays = context == null || context.InventoryStaysWithPawns;
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (allStays || CustodyCargoPolicy.ShouldRetainWithPawn(item, pawn)) mass += item.GetStatValue(StatDefOf.Mass) * item.stackCount;
            }
            return mass;
        }
    }

    public sealed class ServicePodManifestContext
    {
        public Caravan Caravan;
        public HashSet<Pawn> Departing = new HashSet<Pawn>();
        public bool InventoryStaysWithPawns;

        public static ServicePodManifestContext For(SettlementServiceRequest request)
        {
            Caravan caravan = request.negotiator?.GetCaravan();
            var departing = new HashSet<Pawn>(request.targets.Select(t => t?.thing as Pawn).Where(p => p != null));
            bool anyRemaining = caravan != null && caravan.PawnsListForReading.Any(p => !departing.Contains(p));
            return new ServicePodManifestContext { Caravan = caravan, Departing = departing, InventoryStaysWithPawns = !anyRemaining };
        }
    }
}
