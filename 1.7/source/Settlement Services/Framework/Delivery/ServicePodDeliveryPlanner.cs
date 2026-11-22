using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Delivery
{
    internal sealed class ServicePodSpecs
    {
        public float massCapacity;
        public float fuelCapacity;
        public float fuelPerTile;
        public float minFuelCost;
        public int fixedLaunchDistanceMax;
        public float podMarketValue;
        public float fuelMarketValue;
    }

    internal sealed class ServicePodDeliveryPlan
    {
        public int podCount;
        public int baseFeePerPod;
        public int materialFeePerPod;
        public int fuelFeePerPod;
        public float massBound;
        public int maxRange;
        public int distance;
        public float massCapacityPerPod;
        public MapParent destination;
        public IntVec3 cell;

        public int BaseFeeTotal => podCount * baseFeePerPod;
        public int MaterialFeeTotal => podCount * materialFeePerPod;
        public int FuelFeeTotal => podCount * fuelFeePerPod;
        public int TotalFee => BaseFeeTotal + MaterialFeeTotal + FuelFeeTotal;
    }

    internal static class ServicePodDeliveryPlanner
    {
        internal const string TechTooLowKey = "SettlementServices.Error.PodDeliveryTechTooLow";
        internal const string NoHomeMapKey = "SettlementServices.Error.PodDeliveryNoHomeMap";
        internal const string NoDestinationKey = "SettlementServices.Error.PodDeliveryNoDestination";
        internal const string DestinationInvalidKey = "SettlementServices.Error.PodDeliveryDestinationInvalid";
        internal const string OutOfRangeKey = "SettlementServices.Error.PodDeliveryOutOfRange";
        internal const string UnboundedKey = "SettlementServices.Error.PodDeliveryManifestUnbounded";
        internal const string TooHeavyKey = "SettlementServices.Error.PodDeliveryItemTooHeavy";
        internal const string UnavailableKey = "SettlementServices.Error.PodDeliveryUnavailable";
        internal const string RequiresSilverKey = "SettlementServices.Error.PodDeliveryRequiresSilver";
        internal const string QuoteChangedKey = "SettlementServices.Error.PodDeliveryQuoteChanged";
        internal const string LineItemPodKey = "SettlementServices.LineItem.PodDelivery";
        internal const string LineItemMaterialsKey = "SettlementServices.LineItem.PodDeliveryMaterials";
        internal const string LineItemFuelKey = "SettlementServices.LineItem.PodDeliveryFuel";

        private const float PodFeeFractionOfMarketValue = 0.2f;
        private const float MassTolerance = 0.001f;
        private const int MaxPodsPerContract = 24;

        internal static bool TryGetSpecs(out ServicePodSpecs specs)
        {
            specs = null;
            ThingDef podDef = ThingDefOf.TransportPod;
            CompProperties_Transporter transporter = podDef?.GetCompProperties<CompProperties_Transporter>();
            CompProperties_Launchable launchable = podDef?.GetCompProperties<CompProperties_Launchable>();
            if (transporter == null || launchable == null) return false;

            ThingDef port = DefDatabase<ThingDef>.GetNamedSilentFail("FuelingPort")
                ?? DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.building != null && d.building.hasFuelingPort).OrderBy(d => d.defName).FirstOrDefault();
            float fuelCapacity = port?.GetCompProperties<CompProperties_Refuelable>()?.fuelCapacity ?? 0f;
            if (fuelCapacity <= 0f) return false;

            specs = new ServicePodSpecs
            {
                massCapacity = transporter.massCapacity,
                fuelCapacity = fuelCapacity,
                fuelPerTile = launchable.fuelPerTile,
                minFuelCost = launchable.minFuelCost,
                fixedLaunchDistanceMax = launchable.fixedLaunchDistanceMax,
                podMarketValue = podDef.BaseMarketValue,
                fuelMarketValue = ThingDefOf.Chemfuel != null ? ThingDefOf.Chemfuel.BaseMarketValue : 0f,
            };
            return true;
        }

        internal static int MaxLaunchDistance(ServicePodSpecs specs, PlanetLayer layer)
        {
            float factor = layer.Def.rangeDistanceFactor;
            if (specs.fuelCapacity < specs.minFuelCost) return 0;

            int range = Mathf.FloorToInt(specs.fuelCapacity / (specs.fuelPerTile * factor));
            if (range <= 0) range = int.MaxValue;
            if (specs.fixedLaunchDistanceMax >= 0) range = Mathf.Min(range, Mathf.RoundToInt(specs.fixedLaunchDistanceMax / factor));
            return range;
        }

        internal static float FuelNeededForDistance(ServicePodSpecs specs, int distance, PlanetLayer layer)
        {
            float needed = distance * (specs.fuelPerTile * layer.Def.rangeDistanceFactor);
            if (specs.minFuelCost > 0f && needed < specs.minFuelCost) needed = specs.minFuelCost;
            return Mathf.Max(needed, 1f);
        }

        internal static bool IsTechEligible(Settlement settlement) =>
            settlement?.Faction != null && settlement.Faction.def.techLevel >= TechLevel.Industrial;

        internal static List<Map> PlayerHomeMaps() => Find.Maps.Where(m => m.IsPlayerHome && m.Parent != null).ToList();

        internal static MapParent ResolveMapParent(int mapParentId)
        {
            if (mapParentId < 0) return null;
            foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects)
            {
                if (worldObject.ID == mapParentId) return worldObject as MapParent;
            }
            return null;
        }

        internal static bool TryResolveDestination(int mapParentId, IntVec3 cell, out MapParent parent, out string errorKey)
        {
            parent = ResolveMapParent(mapParentId);
            if (parent == null || !parent.Spawned || !parent.HasMap || !parent.Map.IsPlayerHome || !cell.IsValid || !cell.InBounds(parent.Map))
            {
                parent = null;
                errorKey = DestinationInvalidKey;
                return false;
            }

            errorKey = null;
            return true;
        }

        internal static bool TryBuildPlan(SettlementServiceDef def, SettlementServiceRequest request, List<SettlementServiceRequest> units, out ServicePodDeliveryPlan plan, out string errorKey)
        {
            plan = null;
            ServicePodDeliverySelection selection = request.podDelivery;

            if (!IsTechEligible(request.settlement)) { errorKey = TechTooLowKey; return false; }
            if (PlayerHomeMaps().Count == 0) { errorKey = NoHomeMapKey; return false; }
            if (selection == null || !selection.HasDestination) { errorKey = NoDestinationKey; return false; }
            if (!TryResolveDestination(selection.mapParentId, selection.cell, out MapParent destination, out errorKey)) return false;
            if (!TryGetSpecs(out ServicePodSpecs specs)) { errorKey = UnavailableKey; return false; }

            ServicePodManifestContext manifestContext = ServicePodManifestContext.For(request);
            var total = new ServicePodManifestEstimate();
            foreach (SettlementServiceRequest unit in units)
            {
                ServicePodManifestEstimate estimate = def.Worker.EstimatePodManifest(unit, manifestContext);
                if (estimate == null) { errorKey = UnboundedKey; return false; }
                total.Add(estimate);
            }

            if (!TryCountPods(total.unsplittableMasses, total.splittableMass, specs.massCapacity, out int podCount) || podCount > MaxPodsPerContract)
            { errorKey = TooHeavyKey; return false; }

            int distance = Find.WorldGrid.TraversalDistanceBetween(request.settlement.Tile, destination.Tile, passImpassable: true, int.MaxValue, canTraverseLayers: true);
            int maxRange = MaxLaunchDistance(specs, destination.Tile.Layer);
            if (distance < 0 || distance == int.MaxValue || distance > maxRange) { errorKey = OutOfRangeKey; return false; }

            if (!TransportPodPricing.TryGetMaterialCost(out int materialFee)) { errorKey = UnavailableKey; return false; }
            float fuel = FuelNeededForDistance(specs, distance, destination.Tile.Layer);
            double baseFee = Math.Max(1d, Math.Round(specs.podMarketValue * PodFeeFractionOfMarketValue));
            double fuelFee = Math.Max(0d, Math.Round(fuel * specs.fuelMarketValue));
            double totalFee = podCount * (baseFee + materialFee + fuelFee);
            if (!double.IsFinite(totalFee) || totalFee > int.MaxValue) { errorKey = UnavailableKey; return false; }
            plan = new ServicePodDeliveryPlan
            {
                podCount = podCount,
                baseFeePerPod = (int)baseFee,
                materialFeePerPod = materialFee,
                fuelFeePerPod = (int)fuelFee,
                massBound = total.TotalMass,
                maxRange = maxRange,
                distance = distance,
                massCapacityPerPod = specs.massCapacity,
                destination = destination,
                cell = selection.cell,
            };
            errorKey = null;
            return true;
        }

        internal static bool TryApplyToQuote(SettlementServiceQuote quote, ServicePodDeliveryPlan plan)
        {
            if (plan.TotalFee > int.MaxValue - quote.totalCost) return false;
            quote.lineItems.Add(new ServiceLineItem(LineItemPodKey, plan.BaseFeeTotal, false, plan.podCount.ToString()));
            if (plan.MaterialFeeTotal > 0) quote.lineItems.Add(new ServiceLineItem(LineItemMaterialsKey, plan.MaterialFeeTotal));
            if (plan.FuelFeeTotal > 0) quote.lineItems.Add(new ServiceLineItem(LineItemFuelKey, plan.FuelFeeTotal));
            quote.totalCost += plan.TotalFee;
            quote.podCount = plan.podCount;
            quote.podDeliveryCost = plan.TotalFee;
            quote.podMassBound = plan.massBound;
            quote.podMaxRange = plan.maxRange;
            quote.podDistance = plan.distance;
            return true;
        }

        internal static bool TryCountPods(List<float> unsplittable, float splittableMass, float capacity, out int podCount)
        {
            podCount = 0;
            if (capacity <= 0f) return false;

            var remaining = new List<float>();
            foreach (float mass in unsplittable.OrderByDescending(m => m))
            {
                if (mass > capacity + MassTolerance) return false;

                int index = -1;
                for (int i = 0; i < remaining.Count; i++)
                {
                    if (remaining[i] + MassTolerance >= mass && (index < 0 || remaining[i] < remaining[index])) index = i;
                }

                if (index >= 0) remaining[index] -= mass;
                else remaining.Add(capacity - mass);
            }

            float splittableLeft = splittableMass;
            for (int i = 0; i < remaining.Count && splittableLeft > 0f; i++)
            {
                float used = Mathf.Min(remaining[i], splittableLeft);
                remaining[i] -= used;
                splittableLeft -= used;
            }

            int extraPods = splittableLeft > MassTolerance ? Mathf.CeilToInt((splittableLeft - MassTolerance) / capacity) : 0;
            podCount = remaining.Count + extraPods;
            if (podCount == 0 && (unsplittable.Count > 0 || splittableMass > 0f)) podCount = 1;
            return podCount > 0;
        }

        internal struct PodLoad
        {
            public Thing thing;
            public int count;
        }

        internal static float MassOf(Thing thing, int count)
        {
            if (thing is Pawn pawn)
                return CollectionsMassCalculator.MassUsage(new List<Thing> { pawn }, IgnorePawnsInventoryMode.DontIgnore, includePawnsMass: true);
            return thing.GetStatValue(StatDefOf.Mass) * count;
        }

        internal static bool TryPartition(List<Thing> things, int podCount, float capacity, out List<List<PodLoad>> pods)
        {
            pods = new List<List<PodLoad>>();
            for (int i = 0; i < podCount; i++) pods.Add(new List<PodLoad>());
            var loads = new float[podCount];

            var unsplittable = new List<Thing>();
            var splittable = new List<Thing>();
            foreach (Thing thing in things)
            {
                if (thing is Pawn || thing.def.stackLimit <= 1 || thing.stackCount <= 1) unsplittable.Add(thing);
                else splittable.Add(thing);
            }

            foreach (Thing thing in unsplittable.OrderByDescending(t => MassOf(t, t.stackCount)))
            {
                float mass = MassOf(thing, thing.stackCount);
                int target = -1;
                for (int i = 0; i < podCount; i++)
                {
                    if (loads[i] + mass <= capacity + MassTolerance && (target < 0 || capacity - loads[i] < capacity - loads[target])) target = i;
                }
                if (target < 0) return false;

                loads[target] += mass;
                pods[target].Add(new PodLoad { thing = thing, count = thing.stackCount });
            }

            foreach (Thing stack in splittable)
            {
                float unitMass = stack.GetStatValue(StatDefOf.Mass);
                int left = stack.stackCount;
                for (int i = 0; i < podCount && left > 0; i++)
                {
                    int fit = unitMass <= 0f ? left : Mathf.FloorToInt((capacity - loads[i] + MassTolerance) / unitMass);
                    int take = Mathf.Min(left, Mathf.Max(0, fit));
                    if (take <= 0) continue;

                    loads[i] += unitMass * take;
                    pods[i].Add(new PodLoad { thing = stack, count = take });
                    left -= take;
                }
                if (left > 0) return false;
            }

            return true;
        }
    }
}
