using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class LocalMarketDisplayOverridesDef : Def
    {
        public List<string> drinkDefNames = new List<string>();
        public List<string> playerSellOnlyDefNames = new List<string>();
        public List<string> pantryIngredientDefNames = new List<string>();
        public List<string> meatDisplayDefNames = new List<string>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            foreach (string e in NameListErrors(nameof(drinkDefNames), drinkDefNames)) yield return e;
            foreach (string e in NameListErrors(nameof(playerSellOnlyDefNames), playerSellOnlyDefNames)) yield return e;
            foreach (string e in NameListErrors(nameof(pantryIngredientDefNames), pantryIngredientDefNames)) yield return e;
            foreach (string e in NameListErrors(nameof(meatDisplayDefNames), meatDisplayDefNames)) yield return e;
        }

        private static IEnumerable<string> NameListErrors(string fieldName, List<string> names)
        {
            if (names == null) yield break;

            var seenNames = new HashSet<string>();
            foreach (string name in names)
            {
                if (name.NullOrEmpty())
                {
                    yield return $"{fieldName} has an empty entry.";
                    continue;
                }
                if (!seenNames.Add(name))
                    yield return $"{fieldName} has a duplicate entry {name}.";
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name) == null)
                    yield return $"{fieldName} references unknown ThingDef {name}.";
            }
        }
    }
}
