using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Specialty
{
    public static class SettlementSpecialtyService
    {
        public static IReadOnlyList<SettlementSpecialtyDef> GetSpecialties(Settlement settlement)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            IReadOnlyList<string> defNames = domain.GetOrGenerateSpecialtyDefNames(
                settlement.ID, () => SettlementSpecialtyGenerator.Generate(settlement).Select(d => d.defName).ToList());

            var resolved = new List<SettlementSpecialtyDef>();
            foreach (string defName in defNames)
            {
                SettlementSpecialtyDef def = DefDatabase<SettlementSpecialtyDef>.GetNamedSilentFail(defName);
                if (def != null) resolved.Add(def);
            }
            return resolved;
        }

        public static bool TryAddSpecialty(Settlement settlement, SettlementSpecialtyDef specialty)
        {
            if (settlement == null || specialty == null) return false;

            GetSpecialties(settlement);
            return SettlementServicesWorldComponent.Current.TryAddSpecialty(settlement.ID, specialty.defName);
        }

        public static bool HasCapabilityTag(Settlement settlement, string tag) =>
            GetSpecialties(settlement).Any(d => d.modifiers.capabilityTags.Contains(tag));

        public static float TotalPriceModifierPct(Settlement settlement, ServiceCategoryDef category) =>
            Relevant(settlement, category).Sum(d => d.modifiers.priceModifierPct);

        public static float TotalDurationMultiplierOffset(Settlement settlement, ServiceCategoryDef category) =>
            Relevant(settlement, category).Sum(d => d.modifiers.durationMultiplierOffset);

        public static float TotalQualityOffset(Settlement settlement, ServiceCategoryDef category) =>
            Relevant(settlement, category).Sum(d => d.modifiers.qualityOffset);

        private static IEnumerable<SettlementSpecialtyDef> Relevant(Settlement settlement, ServiceCategoryDef category) =>
            category == null
                ? Enumerable.Empty<SettlementSpecialtyDef>()
                : GetSpecialties(settlement).Where(d => d.modifiers.relevantCategoryDefNames.Contains(category.defName));
    }
}
