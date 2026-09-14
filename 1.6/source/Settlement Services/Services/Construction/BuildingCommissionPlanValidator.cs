using Verse;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Services.Construction
{
    internal static class BuildingCommissionPlanValidator
    {
        private const string UnavailableKey = "SettlementServices.Error.BuildingNoLongerAvailable";

        public static bool Validate(BuildingCommissionPlan plan, out string errorKey)
        {
            if (plan == null)
            {
                errorKey = UnavailableKey;
                return false;
            }

            foreach (BuildingCommissionLine line in plan.lines)
            {
                if (IsLineValid(line)) continue;
                errorKey = UnavailableKey;
                return false;
            }

            errorKey = null;
            return true;
        }

        private static bool IsLineValid(BuildingCommissionLine line)
        {
            ThingDef building = DefDatabase<ThingDef>.GetNamedSilentFail(line.buildingDefName);
            if (!BuildingCommissionCatalog.IsEligibleBuildingDef(building)) return false;

            if (!building.MadeFromStuff) return line.stuffDefName == null;

            return line.stuffDefName != null && DefDatabase<ThingDef>.GetNamedSilentFail(line.stuffDefName) != null;
        }
    }
}
