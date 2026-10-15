using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    internal static class VehicleShopCatalog
    {
        public const float RetailMarkupFactor = 1.4f;
        public const float TierTwoMarketValueFloor = 3000f;
        public const float TierThreeMarketValueFloor = 7000f;

        private const string TierThreePackageId = "oskarpotocki.vanillavehiclesexpandedtier3";
        private const string VveStatExtensionTypeName = "VanillaVehiclesExpanded.StatExtension";
        private const string VveStatValuesFieldName = "statValues";
        private const string VveTierValuePrefix = "VVE_Tier";

        private static Dictionary<string, int> overridesByDefName;
        private static FieldInfo vveStatValuesField;
        private static bool vveStatValuesFieldResolved;

        public static int PriceOf(ThingDef def) => Mathf.Max(1, Mathf.RoundToInt(def.BaseMarketValue * RetailMarkupFactor));

        public static List<ThingDef> EligibleDefs()
        {
            if (!VehicleShopAdapter.Available) return new List<ThingDef>();

            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(VehicleShopAdapter.IsPlayerPurchasable)
                .OrderBy(d => d.defName, StringComparer.Ordinal)
                .ToList();
        }

        public static int TierOf(ThingDef def)
        {
            if (def == null) return VehicleShopDraw.MinTier;

            if (OverrideTiers().TryGetValue(def.defName, out int overrideTier)) return Clamp(overrideTier);
            if (string.Equals(def.modContentPack?.PackageId, TierThreePackageId, StringComparison.OrdinalIgnoreCase)) return VehicleShopDraw.MaxTier;
            if (TryGetVveTier(def, out int vveTier)) return Clamp(vveTier);

            if (def.BaseMarketValue >= TierThreeMarketValueFloor) return 3;
            return def.BaseMarketValue >= TierTwoMarketValueFloor ? 2 : 1;
        }

        private static int Clamp(int tier) => Mathf.Clamp(tier, VehicleShopDraw.MinTier, VehicleShopDraw.MaxTier);

        private static Dictionary<string, int> OverrideTiers()
        {
            if (overridesByDefName != null) return overridesByDefName;

            var result = new Dictionary<string, int>();
            foreach (VehicleShopTierOverridesDef overridesDef in DefDatabase<VehicleShopTierOverridesDef>.AllDefs.OrderBy(d => d.defName, StringComparer.Ordinal))
            {
                foreach (VehicleShopTierOverride entry in overridesDef.overrides)
                {
                    if (entry == null || entry.vehicleDefName.NullOrEmpty()) continue;
                    if (!result.TryAdd(entry.vehicleDefName, entry.tier))
                        SupportLog.Warning($"Vehicle shop tier override for '{entry.vehicleDefName}' in '{overridesDef.defName}' was ignored because another override already set it.");
                }
            }
            overridesByDefName = result;
            return result;
        }

        private static bool TryGetVveTier(ThingDef def, out int tier)
        {
            tier = 0;
            if (def.modExtensions == null) return false;

            foreach (DefModExtension extension in def.modExtensions)
            {
                if (extension == null || extension.GetType().FullName != VveStatExtensionTypeName) continue;

                if (!vveStatValuesFieldResolved)
                {
                    vveStatValuesFieldResolved = true;
                    vveStatValuesField = extension.GetType().GetField(VveStatValuesFieldName, BindingFlags.Public | BindingFlags.Instance);
                }
                if (vveStatValuesField == null || !(vveStatValuesField.GetValue(extension) is IEnumerable values)) continue;

                foreach (object value in values)
                {
                    string text = value?.ToString();
                    if (text != null && text.StartsWith(VveTierValuePrefix, StringComparison.Ordinal)
                        && int.TryParse(text.Substring(VveTierValuePrefix.Length), out int parsed))
                    {
                        tier = parsed;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
