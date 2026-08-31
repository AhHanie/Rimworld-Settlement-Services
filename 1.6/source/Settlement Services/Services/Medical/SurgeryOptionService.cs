using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Medical
{
    internal static class SurgeryOptionService
    {
        public const string ProstheticsCategoryDefName = "SettlementStock_ProstheticsAndBionics";

        public readonly struct ImplantOption
        {
            public readonly RecipeDef recipe;
            public readonly BodyPartRecord part;
            public readonly ThingDef stockThingDef;

            public ImplantOption(RecipeDef recipe, BodyPartRecord part, ThingDef stockThingDef)
            {
                this.recipe = recipe;
                this.part = part;
                this.stockThingDef = stockThingDef;
            }

            public string Key => $"{recipe.defName}|{part.Index}";
            public string Label => $"{recipe.LabelCap} ({part.LabelCap})";
        }

        private static SettlementStockCategoryDef ProstheticsCategory =>
            DefDatabase<SettlementStockCategoryDef>.GetNamedSilentFail(ProstheticsCategoryDefName);

        public static IEnumerable<ThingDef> ConfiguredThingDefs()
        {
            SettlementStockCategoryDef category = ProstheticsCategory;
            if (category == null) yield break;

            foreach (SettlementStockItemReference reference in SettlementStockCatalog.ItemsFor(category))
                yield return reference.thing;
        }

        public static IEnumerable<ImplantOption> FindOptions(Pawn pawn)
        {
            if (pawn == null) yield break;

            var seenKeys = new HashSet<string>();
            foreach (ThingDef thingDef in ConfiguredThingDefs())
            {
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("Install" + thingDef.defName);
                if (recipe == null || !recipe.Worker.AvailableOnNow(pawn)) continue;

                foreach (BodyPartRecord part in recipe.Worker.GetPartsToApplyOn(pawn, recipe))
                {
                    var option = new ImplantOption(recipe, part, thingDef);
                    if (seenKeys.Add(option.Key)) yield return option;
                }
            }
        }

        public static List<ImplantOption> FindOfferedOptions(Pawn pawn, Settlement settlement, Caravan caravan)
        {
            var result = new List<ImplantOption>();
            if (pawn == null) return result;

            var offeredThingDefs = new HashSet<ThingDef>();
            SettlementStockCategoryDef category = ProstheticsCategory;
            if (category != null)
                foreach (SettlementStockItemReference reference in SettlementStockService.ItemsFor(settlement, category))
                    offeredThingDefs.Add(reference.thing);

            if (caravan != null)
            {
                var configured = new HashSet<ThingDef>(ConfiguredThingDefs());
                foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
                    if (configured.Contains(thing.def)) offeredThingDefs.Add(thing.def);
            }

            result.AddRange(FindOptions(pawn).Where(o => offeredThingDefs.Contains(o.stockThingDef)));
            result.Sort((a, b) =>
            {
                int labelCompare = string.Compare(a.Label, b.Label, StringComparison.Ordinal);
                return labelCompare != 0 ? labelCompare : string.Compare(a.stockThingDef.defName, b.stockThingDef.defName, StringComparison.Ordinal);
            });
            return result;
        }

        public static ImplantOption? FindByKey(Pawn pawn, string key)
        {
            foreach (ImplantOption option in FindOptions(pawn))
                if (option.Key == key) return option;
            return null;
        }

        public static bool ConflictsWith(ImplantOption a, ImplantOption b)
        {
            if (a.part.Index == b.part.Index) return true;
            return IsAncestorOf(a.part, b.part) || IsAncestorOf(b.part, a.part);
        }

        private static bool IsAncestorOf(BodyPartRecord candidateAncestor, BodyPartRecord part)
        {
            for (BodyPartRecord current = part.parent; current != null; current = current.parent)
                if (current.Index == candidateAncestor.Index) return true;
            return false;
        }

        public static List<string> ConflictingKeysFor(ImplantOption option, List<ImplantOption> allOptions) =>
            allOptions.Where(o => o.Key != option.Key && ConflictsWith(option, o)).Select(o => o.Key).ToList();

        public static List<ImplantOption> ResolveAvailable(Pawn pawn, IReadOnlyList<string> keys)
        {
            var result = new List<ImplantOption>();
            if (pawn == null || keys == null || keys.Count == 0) return result;

            List<ImplantOption> available = FindOptions(pawn).ToList();
            foreach (string key in keys)
                foreach (ImplantOption candidate in available)
                    if (candidate.Key == key) { result.Add(candidate); break; }

            return result;
        }

        public static bool TryResolveSelected(Pawn pawn, IReadOnlyList<string> keys, out List<ImplantOption> resolved, out string errorKey)
        {
            resolved = new List<ImplantOption>();
            errorKey = null;
            if (pawn == null || keys == null || keys.Count == 0) return true;

            List<ImplantOption> available = FindOptions(pawn).ToList();
            var seenKeys = new HashSet<string>();

            foreach (string key in keys)
            {
                if (!seenKeys.Add(key)) { errorKey = "SettlementServices.Error.ConflictingSurgeries"; return false; }

                ImplantOption? match = null;
                foreach (ImplantOption candidate in available)
                    if (candidate.Key == key) { match = candidate; break; }

                if (match == null) { errorKey = "SettlementServices.Error.NoCompatibleImplants"; return false; }
                resolved.Add(match.Value);
            }

            for (int i = 0; i < resolved.Count; i++)
                for (int j = i + 1; j < resolved.Count; j++)
                    if (ConflictsWith(resolved[i], resolved[j])) { errorKey = "SettlementServices.Error.ConflictingSurgeries"; return false; }

            return true;
        }
    }
}
