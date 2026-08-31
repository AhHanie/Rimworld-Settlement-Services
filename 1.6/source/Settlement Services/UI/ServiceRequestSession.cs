using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.UI
{
    public class ServiceRequestSession
    {
        public readonly RequestChannel channel;
        public readonly Settlement settlement;
        public readonly Caravan caravan;
        public readonly Pawn negotiator;

        public SettlementServiceDef def;
        public readonly List<Thing> targets = new List<Thing>();
        private readonly List<List<string>> targetOptionKeys = new List<List<string>>();
        public int quantity = 1;
        public string selectedTierKey;
        public readonly List<string> selectedOptionKeys = new List<string>();
        public readonly List<ThingDefCountClass> playerSuppliedInputs = new List<ThingDefCountClass>();
        public readonly List<CraftingCommissionLine> craftingCommissionLines = new List<CraftingCommissionLine>();
        public int craftingCrafterCount = 1;

        private ServiceRequestSession(RequestChannel channel, Settlement settlement, Caravan caravan, Pawn negotiator)
        {
            this.channel = channel;
            this.settlement = settlement;
            this.caravan = caravan;
            this.negotiator = negotiator;
        }

        public static ServiceRequestSession ForInPersonVisit(Settlement settlement, Caravan caravan) =>
            new ServiceRequestSession(RequestChannel.InPerson, settlement, caravan, BestCaravanPawnUtility.FindBestNegotiator(caravan));

        public static ServiceRequestSession ForRemote(Settlement settlement, Pawn negotiator) =>
            new ServiceRequestSession(RequestChannel.Remote, settlement, null, negotiator);

        public TargetKind TargetKindForDef => def == null || def.targetRule == ServiceTargetRule.None
            ? TargetKind.None
            : (TargetKind)Enum.Parse(typeof(TargetKind), def.targetRule.ToString());

        public void AddTarget(Thing thing)
        {
            targets.Add(thing);
            targetOptionKeys.Add(new List<string>());
        }

        public void RemoveTargetAt(int index)
        {
            if (index < 0 || index >= targets.Count) return;
            targets.RemoveAt(index);
            if (index < targetOptionKeys.Count) targetOptionKeys.RemoveAt(index);
        }

        public List<string> OptionsForTarget(int index)
        {
            while (targetOptionKeys.Count < targets.Count) targetOptionKeys.Add(new List<string>());
            return index >= 0 && index < targetOptionKeys.Count ? targetOptionKeys[index] : new List<string>();
        }

        public SettlementServiceRequest BuildRequest()
        {
            TargetKind kind = TargetKindForDef;
            bool perTargetOptions = def?.Worker != null && def.Worker.UsesPerTargetOptionSelections;

            return new SettlementServiceRequest
            {
                channel = channel,
                settlement = settlement,
                targets = targets.Select(t => new ServiceTargetSelection { kind = kind, thing = t }).ToList(),
                quantity = quantity,
                playerSuppliedInputs = new List<ThingDefCountClass>(playerSuppliedInputs),
                selectedTierKey = selectedTierKey,
                selectedOptionKeys = new List<string>(selectedOptionKeys),
                targetOptionSelections = perTargetOptions ? BuildTargetOptionSelections() : new List<ServiceTargetOptionSelection>(),
                craftingCommissionLines = craftingCommissionLines.Select(l => l.Clone()).ToList(),
                craftingCrafterCount = craftingCrafterCount,
                negotiator = negotiator,
            };
        }

        private List<ServiceTargetOptionSelection> BuildTargetOptionSelections()
        {
            var result = new List<ServiceTargetOptionSelection>();
            for (int i = 0; i < targets.Count; i++)
                result.Add(new ServiceTargetOptionSelection(i, OptionsForTarget(i)));
            return result;
        }
    }
}
