using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Services.Construction
{
    public class BuildingCommissionExclusionsDef : Def
    {
        public List<string> excludedBuildingDefNames = new List<string>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            var seenNames = new HashSet<string>();
            foreach (string name in excludedBuildingDefNames)
            {
                if (name.NullOrEmpty())
                {
                    yield return "excludedBuildingDefNames has an empty entry.";
                    continue;
                }
                if (!seenNames.Add(name))
                    yield return $"excludedBuildingDefNames has a duplicate entry {name}.";
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name) == null)
                    yield return $"excludedBuildingDefNames references unknown ThingDef {name}.";
            }
        }
    }
}
