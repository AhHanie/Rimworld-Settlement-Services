using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;

namespace Settlement_Services.Framework.Dto
{
    public class SettlementServiceRequest
    {
        public RequestChannel channel;
        public Settlement settlement;
        public List<ServiceTargetSelection> targets = new List<ServiceTargetSelection>();
        public int quantity = 1;
        public List<ThingDefCountClass> playerSuppliedInputs = new List<ThingDefCountClass>();

        public string selectedTierKey;

        public List<string> selectedOptionKeys = new List<string>();

        public List<ServiceTargetOptionSelection> targetOptionSelections = new List<ServiceTargetOptionSelection>();

        public List<CraftingCommissionLine> craftingCommissionLines = new List<CraftingCommissionLine>();

        public int craftingCrafterCount = 1;

        public Pawn negotiator;

        public int bookingTick = -1;

        public ServiceTargetSelection target
        {
            get => targets.Count > 0 ? targets[0] : ServiceTargetSelection.None;
            set
            {
                targets.Clear();
                if (value != null && value.kind != TargetKind.None) targets.Add(value);
            }
        }

        public int TargetCount => targets.Count;

        public int UnitCount => targets.Count > 0 ? targets.Count : Mathf.Max(1, quantity);

        public List<string> OptionKeysForTarget(int targetIndex)
        {
            ServiceTargetOptionSelection match = targetOptionSelections?.Find(s => s.targetIndex == targetIndex);
            return match != null ? match.optionKeys : selectedOptionKeys;
        }

        public SettlementServiceRequest ForTarget(ServiceTargetSelection single) => new SettlementServiceRequest
        {
            channel = channel,
            settlement = settlement,
            targets = new List<ServiceTargetSelection> { single },
            quantity = 1,
            playerSuppliedInputs = playerSuppliedInputs,
            selectedTierKey = selectedTierKey,
            selectedOptionKeys = selectedOptionKeys,
            craftingCommissionLines = CloneLines(craftingCommissionLines),
            craftingCrafterCount = craftingCrafterCount,
            negotiator = negotiator,
            bookingTick = bookingTick,
        };

        public SettlementServiceRequest ForTarget(ServiceTargetSelection target, int targetIndex) => new SettlementServiceRequest
        {
            channel = channel,
            settlement = settlement,
            targets = new List<ServiceTargetSelection> { target },
            quantity = 1,
            playerSuppliedInputs = playerSuppliedInputs,
            selectedTierKey = selectedTierKey,
            selectedOptionKeys = new List<string>(OptionKeysForTarget(targetIndex)),
            craftingCommissionLines = CloneLines(craftingCommissionLines),
            craftingCrafterCount = craftingCrafterCount,
            negotiator = negotiator,
            bookingTick = bookingTick,
        };

        public SettlementServiceRequest ForIteration() => new SettlementServiceRequest
        {
            channel = channel,
            settlement = settlement,
            targets = new List<ServiceTargetSelection>(),
            quantity = 1,
            playerSuppliedInputs = playerSuppliedInputs,
            selectedTierKey = selectedTierKey,
            selectedOptionKeys = selectedOptionKeys,
            craftingCommissionLines = CloneLines(craftingCommissionLines),
            craftingCrafterCount = craftingCrafterCount,
            negotiator = negotiator,
            bookingTick = bookingTick,
        };

        public SettlementServiceRequest Clone() => new SettlementServiceRequest
        {
            channel = channel,
            settlement = settlement,
            targets = new List<ServiceTargetSelection>(targets),
            quantity = quantity,
            playerSuppliedInputs = new List<ThingDefCountClass>(playerSuppliedInputs),
            selectedTierKey = selectedTierKey,
            selectedOptionKeys = new List<string>(selectedOptionKeys),
            targetOptionSelections = targetOptionSelections?.Select(s => s.Clone()).ToList() ?? new List<ServiceTargetOptionSelection>(),
            craftingCommissionLines = CloneLines(craftingCommissionLines),
            craftingCrafterCount = craftingCrafterCount,
            negotiator = negotiator,
            bookingTick = bookingTick,
        };

        private static List<CraftingCommissionLine> CloneLines(List<CraftingCommissionLine> source) =>
            source?.Select(l => l.Clone()).ToList() ?? new List<CraftingCommissionLine>();
    }
}
