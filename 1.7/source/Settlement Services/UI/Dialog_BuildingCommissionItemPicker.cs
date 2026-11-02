using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.UI
{
    public class Dialog_BuildingCommissionItemPicker : Dialog_CommissionPickerBase<ThingDef>
    {
        private readonly Action<BuildingCommissionLine> onAdd;

        public Dialog_BuildingCommissionItemPicker(ServiceRequestSession session, List<ThingDef> eligibleBuildings, Action<BuildingCommissionLine> onAdd)
            : base(session, CommissionPickerKind.Construction, BuildEntries(eligibleBuildings), TypeLabel, null)
        {
            this.onAdd = onAdd;
        }

        protected override string Title => "SettlementServices.Label.AddConstructionItemTitle".Translate();

        protected override string ItemLabel(ThingDef item) => item.LabelCap;

        protected override ThingDef InfoCardDef(ThingDef item) => item;

        protected override bool PossibleWithAnyStuff(ThingDef item) => availability.BuildingPossibleWithAnyStuff(item, 1);

        protected override bool StuffRequirementMet => !selected.MadeFromStuff || selectedStuff != null;

        protected override bool SelectionPossible() =>
            selected != null && availability.BuildingPossible(selected, selectedStuff, quantity);

        protected override void DrawRowIcon(Rect rect, ThingDef item) => Widgets.DefIcon(rect, item, drawPlaceholder: true);

        protected override void OnItemSelected(ThingDef building)
        {
            if (!building.MadeFromStuff)
            {
                selectedStuff = null;
                return;
            }
            List<ThingDef> stuffs = availability.BuildingStuffs(building);
            selectedStuff = (materialFilter ? stuffs.FirstOrDefault(s => availability.BuildingPossible(building, s, 1)) : null) ?? stuffs.FirstOrDefault();
        }

        protected override void DrawDetailBody(Listing_Standard listing)
        {
            if (!selected.description.NullOrEmpty()) listing.Label(selected.description);

            bool needsStuff = selected.MadeFromStuff;
            if (needsStuff) DrawMaterialSelector(listing);

            if (needsStuff && selectedStuff == null)
                listing.Label("SettlementServices.Label.NoEligibleConstructionMaterial".Translate());
            else
                DrawBillPreview(listing);

            if (materialFilter && !SelectionPossible())
            {
                GUI.color = ColorLibrary.RedReadable;
                listing.Label("SettlementServices.Label.CommissionFilterUnavailable".Translate());
                GUI.color = Color.white;
            }
        }

        protected override void CommitAdd() =>
            onAdd(new BuildingCommissionLine(selected.defName, quantity, selectedStuff?.defName));

        private void DrawMaterialSelector(Listing_Standard listing)
        {
            listing.Label("SettlementServices.Label.CraftingStuffChoice".Translate());
            Rect row = listing.GetRect(28f);
            string label = selectedStuff != null ? selectedStuff.LabelCap : "SettlementServices.Label.NoTargetSelected".Translate();
            if (Widgets.ButtonText(row, label))
            {
                IEnumerable<ThingDef> options = availability.BuildingStuffs(selected);
                if (materialFilter) options = options.Where(s => availability.BuildingPossible(selected, s, quantity));
                var floatOptions = options.Select(s => new FloatMenuOption(s.LabelCap, () => selectedStuff = s)).ToList();
                if (floatOptions.Count > 0) Find.WindowStack.Add(new FloatMenu(floatOptions));
            }
            listing.Gap(4f);
        }

        private void DrawBillPreview(Listing_Standard listing)
        {
            List<ThingDefCountClass> cost = selected.CostListAdjusted(selectedStuff);
            foreach (ThingDefCountClass item in cost)
                listing.Label("SettlementServices.Label.RawMaterialLine".Translate(item.thingDef.LabelCap, item.count * quantity));

            int workTicks = Mathf.CeilToInt(selected.GetStatValueAbstract(StatDefOf.WorkToBuild, selectedStuff) * quantity * 2.5f);
            listing.Label("SettlementServices.Label.ConstructionWorkload".Translate(workTicks.ToStringTicksToPeriod()));
            listing.Gap(4f);
        }

        private static string TypeLabel(string key)
        {
            if (key == CommissionPickerKeys.None) return "SettlementServices.Label.CommissionTypeOtherBuildings".Translate();
            DesignationCategoryDef def = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(key);
            return def != null ? def.LabelCap.ToString() : key;
        }

        private static List<CommissionPickerEntry<ThingDef>> BuildEntries(List<ThingDef> buildings)
        {
            var entries = new List<CommissionPickerEntry<ThingDef>>(buildings.Count);
            foreach (ThingDef building in buildings)
            {
                string typeKey = building.designationCategory?.defName ?? CommissionPickerKeys.None;
                entries.Add(CommissionPickerEntry<ThingDef>.Create(
                    building, building, CommissionOrigin.IsOfficial(building), typeKey, false, null,
                    building.LabelCap.ToString(), building.description, building.defName));
            }
            return entries;
        }
    }
}
