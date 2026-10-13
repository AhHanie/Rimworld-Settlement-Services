using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class CraftingCommissionRecipeDef : Def
    {
        public List<ThingDefCountClass> inputs = new List<ThingDefCountClass>();
        public ThingDefCountClass output;
        public TechLevel techLevel = TechLevel.Undefined;
        public float workAmount = -1f;
        public List<string> factionPrerequisiteTags;
        public bool commissionable = true;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            if (inputs.NullOrEmpty())
            {
                yield return "inputs must contain at least one entry.";
            }
            else
            {
                foreach (ThingDefCountClass input in inputs)
                {
                    if (input.thingDef == null) yield return "inputs has an entry with no thingDef.";
                    if (input.count <= 0) yield return $"inputs entry {input.thingDef?.defName ?? "<null>"} has a non-positive count.";
                }
            }

            if (output == null)
            {
                yield return "output must be set.";
                yield break;
            }
            if (output.thingDef == null)
            {
                yield return "output has no thingDef.";
                yield break;
            }
            if (output.count <= 0) yield return "output has a non-positive count.";
            if (output.chance.HasValue) yield return "output must not use chance; a commission quote must promise a determinate result.";

            if (workAmount < 0f) yield return "workAmount must be zero or greater.";

            if (techLevel == TechLevel.Undefined) yield return "techLevel must not be Undefined.";
            if (techLevel == TechLevel.Animal) yield return "techLevel must not be Animal.";

            ThingDef producedDef = output.thingDef;
            if (producedDef.race != null) yield return "output cannot be a race.";
            if (producedDef.category == ThingCategory.Building && !producedDef.Minifiable)
                yield return "output is a building that is not minifiable.";

            if (producedDef.MadeFromStuff)
            {
                if (output.stuff == null)
                    yield return "output is made from stuff but declares no output.stuff.";
                else if (!GenStuff.AllowedStuffsFor(producedDef, TechLevel.Undefined, checkAllowedInStuffGeneration: false).Contains(output.stuff))
                    yield return $"output.stuff {output.stuff.defName} is not an allowed stuff for {producedDef.defName}.";
            }
            else if (output.stuff != null)
            {
                yield return "output.stuff is set but the output is not made from stuff.";
            }
        }
    }
}
