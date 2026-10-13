using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Hospitality
{
    public class HospitalityPackageDef : Def
    {
        public int minimumCost = 0;
        public float wealthScale = 0f;

        public TechLevel requiredTechLevel = TechLevel.Neolithic;

        public string thoughtDefName;
        public string hediffDefName;

        public List<string> restoredNeedDefNames;

        public List<string> requiredCapabilityTags;
        public bool requiresAdultPawn = false;

        public float eventChanceMultiplier = 1f;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            if (minimumCost < 0) yield return "minimumCost must be >= 0.";
            if (wealthScale < 0f) yield return "wealthScale must be >= 0.";
            if (float.IsNaN(eventChanceMultiplier) || float.IsInfinity(eventChanceMultiplier) || eventChanceMultiplier <= 0f)
                yield return "eventChanceMultiplier must be finite and > 0.";
            if (!hediffDefName.NullOrEmpty() && DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName) == null)
                yield return $"hediffDefName '{hediffDefName}' does not resolve to a HediffDef.";
        }
    }
}
