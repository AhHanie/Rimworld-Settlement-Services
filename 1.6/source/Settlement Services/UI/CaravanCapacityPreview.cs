using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Services.Animals;
using Settlement_Services.Services.Market;

namespace Settlement_Services.UI
{
    public class CaravanCapacityPreview
    {
        public float currentUsed;
        public float currentCapacity;
        public bool hasProjection;
        public bool projectionIsEstimate;
        public float projectedUsed;
        public float projectedCapacity;

        public static bool CanShowFor(ServiceRequestSession session) =>
            session != null
            && session.channel == RequestChannel.InPerson
            && session.caravan != null
            && !session.caravan.Destroyed;

        public static CaravanCapacityPreview ForCurrent(ServiceRequestSession session)
        {
            if (!CanShowFor(session)) return null;

            return new CaravanCapacityPreview
            {
                currentUsed = session.caravan.MassUsage,
                currentCapacity = session.caravan.MassCapacity,
            };
        }

        public static CaravanCapacityPreview ForPurchase(ServiceRequestSession session, SettlementServiceQuote quote, IReadOnlyList<ServiceDisplayOption> displayedOptions)
        {
            CaravanCapacityPreview preview = ForCurrent(session);
            if (preview == null || quote == null || !quote.IsValid) return preview;

            SettlementServiceWorkerKind kind = ClassifyWorker(session);
            if (kind == SettlementServiceWorkerKind.Market)
                preview.TryProjectMarket(session, quote);
            else if (kind == SettlementServiceWorkerKind.AnimalVendor)
                preview.TryProjectAnimals(session, quote, displayedOptions);

            return preview;
        }

        private enum SettlementServiceWorkerKind
        {
            Other,
            Market,
            AnimalVendor
        }

        private static SettlementServiceWorkerKind ClassifyWorker(ServiceRequestSession session)
        {
            if (session.def?.Worker is LocalMarketServiceWorker) return SettlementServiceWorkerKind.Market;
            if (session.def?.Worker is AnimalVendorServiceWorker) return SettlementServiceWorkerKind.AnimalVendor;
            return SettlementServiceWorkerKind.Other;
        }

        private void TryProjectMarket(ServiceRequestSession session, SettlementServiceQuote quote)
        {
            if (session.marketCartLines.Count == 0) return;

            float purchasedMass = 0f;
            foreach (MarketCartLine line in session.marketCartLines)
            {
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(line.thingDefName);
                if (thingDef == null || thingDef.MadeFromStuff || line.count <= 0) return;
                purchasedMass += line.count * thingDef.GetStatValueAbstract(StatDefOf.Mass);
            }

            SetProjection(SilverMassRemoved(session.caravan, quote), purchasedMass, 0f, estimate: false);
        }

        private void TryProjectAnimals(ServiceRequestSession session, SettlementServiceQuote quote, IReadOnlyList<ServiceDisplayOption> displayedOptions)
        {
            if (session.selectedOptionKeys.Count == 0 || displayedOptions == null) return;
            if (session.selectedOptionKeys.Distinct().Count() != session.selectedOptionKeys.Count) return;

            float addedUsed = 0f;
            float addedCapacity = 0f;
            foreach (string key in session.selectedOptionKeys)
            {
                ServiceDisplayOption option = displayedOptions.FirstOrDefault(o => o.key == key);
                Pawn pawn = option?.pawnPreview;
                if (pawn == null || pawn.Destroyed) return;

                addedUsed += MassUtility.GearAndInventoryMass(pawn);
                addedCapacity += MassUtility.Capacity(pawn);
            }

            SetProjection(SilverMassRemoved(session.caravan, quote), addedUsed, addedCapacity, estimate: true);
        }

        private void SetProjection(float silverMassRemoved, float massAdded, float capacityAdded, bool estimate)
        {
            hasProjection = true;
            projectionIsEstimate = estimate;
            projectedUsed = System.Math.Max(0f, currentUsed - silverMassRemoved) + massAdded;
            projectedCapacity = currentCapacity + capacityAdded;
        }

        private static float SilverMassRemoved(Caravan caravan, SettlementServiceQuote quote)
        {
            int carriedSilver = CaravanInventoryUtility.AllInventoryItems(caravan)
                .Where(t => t.def == ThingDefOf.Silver)
                .Sum(t => t.stackCount);
            int removed = System.Math.Min(quote.totalCost, carriedSilver);
            return removed <= 0 ? 0f : removed * ThingDefOf.Silver.GetStatValueAbstract(StatDefOf.Mass);
        }
    }
}
