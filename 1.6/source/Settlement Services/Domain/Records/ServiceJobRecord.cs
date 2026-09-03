using System.Collections.Generic;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Domain.Records
{
    public class ServiceJobRecord : IExposable
    {
        public int jobId;
        public int schemaVersion;
        public int settlementWorldObjectId = -1;
        public PlanetTile settlementTile = PlanetTile.Invalid;

        public ServiceJobStatus status;
        public int createdTick;
        public int statusChangedTick;
        public int expectedCompletionTick = -1;

        public RequestChannel requestChannel;

        public string requesterFactionLoadId;
        public string requesterCaravanSnapshotLabel;
        public int requesterCaravanId = -1;

        public string serviceDefName;
        public TargetSnapshot target;
        public List<TargetSnapshot> targets = new List<TargetSnapshot>();
        public int quantity = 1;
        public int eventTargetIndex = -1;

        public SettlementServiceQuote acceptedQuote;

        public string lastErrorKey;

        public bool targetInCustody;

        public List<TargetSnapshot> results = new List<TargetSnapshot>();

        public ServiceEventOutcomeRecord eventOutcome;

        public List<string> selectedOptionKeys = new List<string>();

        public List<ServiceTargetOptionSelection> targetOptionSelections = new List<ServiceTargetOptionSelection>();

        public List<CraftingCommissionLine> craftingCommissionLines = new List<CraftingCommissionLine>();

        public CraftingProductionPlan craftingProductionPlan;

        public List<ThingDefCountClass> consumedPlayerSuppliedInputs = new List<ThingDefCountClass>();
        public bool playerSuppliedInputsRefunded;

        public string providerFactionLoadId;
        public List<string> processedCompatibilityCompletionModuleIds = new List<string>();

        public IReadOnlyList<TargetSnapshot> Targets => targets;

        public List<string> OptionKeysForTarget(int targetIndex)
        {
            ServiceTargetOptionSelection match = targetOptionSelections?.Find(s => s.targetIndex == targetIndex);
            return match != null ? match.optionKeys : selectedOptionKeys;
        }

        public bool TryMarkCompatibilityCompletionProcessed(string moduleId)
        {
            if (processedCompatibilityCompletionModuleIds.Contains(moduleId)) return false;
            processedCompatibilityCompletionModuleIds.Add(moduleId);
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref jobId, "jobId");
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 1);
            Scribe_Values.Look(ref settlementWorldObjectId, "settlementWorldObjectId", -1);
            Scribe_Values.Look(ref settlementTile, "settlementTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref status, "status");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref statusChangedTick, "statusChangedTick");
            Scribe_Values.Look(ref expectedCompletionTick, "expectedCompletionTick", -1);
            Scribe_Values.Look(ref requestChannel, "requestChannel");
            Scribe_Values.Look(ref requesterFactionLoadId, "requesterFactionLoadId");
            Scribe_Values.Look(ref requesterCaravanSnapshotLabel, "requesterCaravanSnapshotLabel");
            Scribe_Values.Look(ref requesterCaravanId, "requesterCaravanId", -1);
            Scribe_Values.Look(ref serviceDefName, "serviceDefName");
            Scribe_Deep.Look(ref target, "target");
            Scribe_Collections.Look(ref targets, "targets", LookMode.Deep);
            Scribe_Values.Look(ref quantity, "quantity", 1);
            Scribe_Values.Look(ref eventTargetIndex, "eventTargetIndex", -1);
            Scribe_Deep.Look(ref acceptedQuote, "acceptedQuote");
            Scribe_Values.Look(ref lastErrorKey, "lastErrorKey");
            Scribe_Values.Look(ref targetInCustody, "targetInCustody");
            Scribe_Collections.Look(ref results, "results", LookMode.Deep);
            Scribe_Deep.Look(ref eventOutcome, "eventOutcome");
            Scribe_Collections.Look(ref selectedOptionKeys, "selectedOptionKeys", LookMode.Value);
            Scribe_Collections.Look(ref targetOptionSelections, "targetOptionSelections", LookMode.Deep);
            Scribe_Collections.Look(ref craftingCommissionLines, "craftingCommissionLines", LookMode.Deep);
            Scribe_Deep.Look(ref craftingProductionPlan, "craftingProductionPlan");
            Scribe_Collections.Look(ref consumedPlayerSuppliedInputs, "consumedPlayerSuppliedInputs", LookMode.Deep);
            Scribe_Values.Look(ref playerSuppliedInputsRefunded, "playerSuppliedInputsRefunded");
            Scribe_Values.Look(ref providerFactionLoadId, "providerFactionLoadId");
            Scribe_Collections.Look(ref processedCompatibilityCompletionModuleIds, "processedCompatibilityCompletionModuleIds", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && results == null)
            {
                results = new List<TargetSnapshot>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && selectedOptionKeys == null)
            {
                selectedOptionKeys = new List<string>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && targetOptionSelections == null)
            {
                targetOptionSelections = new List<ServiceTargetOptionSelection>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && craftingCommissionLines == null)
            {
                craftingCommissionLines = new List<CraftingCommissionLine>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && consumedPlayerSuppliedInputs == null)
            {
                consumedPlayerSuppliedInputs = new List<ThingDefCountClass>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && targets == null)
            {
                targets = new List<TargetSnapshot>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && processedCompatibilityCompletionModuleIds == null)
            {
                processedCompatibilityCompletionModuleIds = new List<string>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                schemaVersion = SchemaVersion.Current;
            }
        }
    }
}
