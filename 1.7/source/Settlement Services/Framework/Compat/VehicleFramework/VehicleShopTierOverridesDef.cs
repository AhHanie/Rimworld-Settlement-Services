using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    public class VehicleShopTierOverride
    {
        public string vehicleDefName;
        public int tier;
    }

    public class VehicleShopTierOverridesDef : Def
    {
        public List<VehicleShopTierOverride> overrides = new List<VehicleShopTierOverride>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;

            var seen = new HashSet<string>();
            foreach (VehicleShopTierOverride entry in overrides)
            {
                if (entry == null || entry.vehicleDefName.NullOrEmpty())
                {
                    yield return "has an override without a vehicleDefName";
                    continue;
                }
                if (!seen.Add(entry.vehicleDefName)) yield return $"overrides vehicle '{entry.vehicleDefName}' more than once";
                if (entry.tier < VehicleShopDraw.MinTier || entry.tier > VehicleShopDraw.MaxTier)
                    yield return $"gives vehicle '{entry.vehicleDefName}' tier {entry.tier}, which is outside {VehicleShopDraw.MinTier}-{VehicleShopDraw.MaxTier}";
                if (DefDatabase<ThingDef>.GetNamedSilentFail(entry.vehicleDefName) == null)
                    yield return $"overrides unknown vehicle '{entry.vehicleDefName}'";
            }
        }
    }
}
