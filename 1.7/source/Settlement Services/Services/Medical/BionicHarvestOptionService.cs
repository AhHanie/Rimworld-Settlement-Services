using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Services.Medical
{
    internal static class BionicHarvestOptionService
    {
        internal readonly struct Option
        {
            internal readonly Hediff_AddedPart hediff;
            internal readonly BodyPartRecord part;
            internal readonly ThingDef item;
            internal readonly int ordinal;

            internal Option(Hediff_AddedPart hediff, int ordinal)
            {
                this.hediff = hediff;
                part = hediff.Part;
                item = hediff.def.spawnThingOnRemoved;
                this.ordinal = ordinal;
            }

            internal Option(BodyPartRecord part)
            {
                hediff = null;
                this.part = part;
                item = part.def.spawnThingOnRemoved;
                ordinal = 0;
            }

            internal bool IsNatural => hediff == null;
            internal string Key => IsNatural
                ? $"organ|{part.Index}|{item.defName}"
                : $"{part.Index}|{hediff.def.defName}|{item.defName}|{ordinal}";
            internal string Label => $"{item.LabelCap} ({part.LabelCap})";

            internal bool IsAvailable(Pawn pawn)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.health.hediffSet.PartIsMissing(part)) return false;
                if (IsNatural)
                    return part.def.spawnThingOnRemoved == item && MedicalRecipesUtility.IsCleanAndDroppable(pawn, part);
                return pawn.health.hediffSet.hediffs.Contains(hediff) && hediff.Part == part && hediff.def.spawnThingOnRemoved == item;
            }
        }

        internal static List<Option> Find(Pawn pawn)
        {
            var result = new List<Option>();
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.RaceProps.Humanlike) return result;
            ThingCategoryDef bionic = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("BodyPartsBionic");
            ThingCategoryDef archotech = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("BodyPartsArchotech");
            ThingCategoryDef natural = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("BodyPartsNatural");
            var ordinals = new Dictionary<string, int>();
            var present = new HashSet<BodyPartRecord>(pawn.health.hediffSet.GetNotMissingParts());
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (!(hediff is Hediff_AddedPart added) || !hediff.Visible || hediff.Part == null || !present.Contains(hediff.Part)) continue;
                ThingDef item = hediff.def.spawnThingOnRemoved;
                if (item == null || !(bionic != null && item.IsWithinCategory(bionic)) && !(archotech != null && item.IsWithinCategory(archotech))) continue;
                string identity = $"{hediff.Part.Index}|{hediff.def.defName}|{item.defName}";
                ordinals.TryGetValue(identity, out int ordinal);
                ordinals[identity] = ordinal + 1;
                result.Add(new Option(added, ordinal));
            }
            foreach (BodyPartRecord part in present)
            {
                ThingDef item = part.def.spawnThingOnRemoved;
                if (item != null && natural != null && item.IsWithinCategory(natural)
                    && MedicalRecipesUtility.IsCleanAndDroppable(pawn, part))
                    result.Add(new Option(part));
            }
            result.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
            return result;
        }

        internal static bool Conflicts(Option a, Option b)
        {
            if (a.part == b.part) return true;
            for (BodyPartRecord p = a.part.parent; p != null; p = p.parent)
                if (p == b.part) return true;
            for (BodyPartRecord p = b.part.parent; p != null; p = p.parent)
                if (p == a.part) return true;
            return false;
        }

        internal static IEnumerable<ServiceDisplayOption> Display(Pawn pawn)
        {
            List<Option> options = Find(pawn);
            foreach (Option option in options)
                yield return new ServiceDisplayOption
                {
                    key = option.Key,
                    label = option.Label,
                    groupKey = "SettlementServices.Label.OrganBionicHarvestChoice",
                    allowMultipleSelectionInGroup = true,
                    conflictingOptionKeys = options.Where(o => o.Key != option.Key && Conflicts(o, option)).Select(o => o.Key).ToList(),
                };
        }

        internal static bool TryResolve(Pawn pawn, IReadOnlyList<string> keys, out List<Option> selected, out string error)
        {
            selected = new List<Option>();
            error = null;
            if (keys == null || keys.Count == 0) { error = "SettlementServices.Error.OrganBionicHarvestSelectPart"; return false; }
            Dictionary<string, Option> options = Find(pawn).ToDictionary(o => o.Key);
            var seen = new HashSet<string>();
            foreach (string key in keys)
            {
                if (!seen.Add(key)) { error = "SettlementServices.Error.OrganBionicHarvestConflict"; return false; }
                if (key == null || !options.TryGetValue(key, out Option option)) { error = "SettlementServices.Error.OrganBionicHarvestStaleSelection"; return false; }
                selected.Add(option);
            }
            for (int i = 0; i < selected.Count; i++)
                for (int j = i + 1; j < selected.Count; j++)
                    if (Conflicts(selected[i], selected[j])) { error = "SettlementServices.Error.OrganBionicHarvestConflict"; return false; }
            return true;
        }
    }
}
