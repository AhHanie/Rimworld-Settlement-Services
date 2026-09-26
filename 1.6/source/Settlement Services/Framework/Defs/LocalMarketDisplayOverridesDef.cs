using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class LocalMarketDisplayOverridesDef : Def
    {
        public List<string> drinkDefNames = new List<string>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            var seenNames = new HashSet<string>();
            foreach (string name in drinkDefNames)
            {
                if (name.NullOrEmpty())
                {
                    yield return "drinkDefNames has an empty entry.";
                    continue;
                }
                if (!seenNames.Add(name))
                    yield return $"drinkDefNames has a duplicate entry {name}.";
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name) == null)
                    yield return $"drinkDefNames references unknown ThingDef {name}.";
            }
        }
    }
}
