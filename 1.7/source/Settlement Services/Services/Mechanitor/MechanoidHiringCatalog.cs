using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Mechanitor
{
    public class MechanoidHiringEntry
    {
        public const string OptionKeyPrefix = "MechHire:";

        public readonly ThingDef raceDef;
        public readonly PawnKindDef kindDef;
        public readonly string optionKey;
        public readonly float baseMarketValue;
        public readonly float bandwidthCost;

        public MechanoidHiringEntry(ThingDef raceDef, PawnKindDef kindDef)
        {
            this.raceDef = raceDef;
            this.kindDef = kindDef;
            optionKey = OptionKeyPrefix + raceDef.defName;
            baseMarketValue = raceDef.BaseMarketValue;
            bandwidthCost = raceDef.GetStatValueAbstract(StatDefOf.BandwidthCost);
        }
    }

    public static class MechanoidHiringCatalog
    {
        private static List<MechanoidHiringEntry> cachedEntries;

        public static IReadOnlyList<MechanoidHiringEntry> AllEntries
        {
            get
            {
                if (cachedEntries == null) Build();
                return cachedEntries;
            }
        }

        public static MechanoidHiringEntry ResolveOption(string optionKey) =>
            AllEntries.FirstOrDefault(e => e.optionKey == optionKey);

        private static void Build()
        {
            var entries = new List<MechanoidHiringEntry>();
            var seenRaceDefs = new HashSet<ThingDef>();

            foreach (RecipeDef recipe in MechanitorUtility.MechRecipes)
            {
                ThingDef raceDef = recipe.ProducedThingDef;
                if (raceDef == null || !seenRaceDefs.Add(raceDef)) continue;
                if (raceDef.race?.IsMechanoid != true) continue;
                if (raceDef.comps.NullOrEmpty() || !raceDef.comps.Any(c => c is CompProperties_OverseerSubject)) continue;

                PawnKindDef kindDef = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(k => k.race == raceDef);
                if (kindDef == null)
                {
                    SupportLog.Warning($"Mech hiring: no PawnKindDef found for mechanoid race {raceDef.defName}; skipping.");
                    continue;
                }

                entries.Add(new MechanoidHiringEntry(raceDef, kindDef));
            }

            entries.Sort((a, b) =>
            {
                int cmp = string.Compare(a.raceDef.label, b.raceDef.label, StringComparison.Ordinal);
                return cmp != 0 ? cmp : string.Compare(a.raceDef.defName, b.raceDef.defName, StringComparison.Ordinal);
            });

            cachedEntries = entries;
        }
    }
}
