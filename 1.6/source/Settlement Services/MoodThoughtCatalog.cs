using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services
{
    public static class MoodThoughtCatalog
    {
        public const float MinMoodEffect = 0f;
        public const float MaxMoodEffect = 20f;

        public class Entry
        {
            public readonly string defName;
            public readonly float defaultMoodEffect;

            public Entry(string defName, float defaultMoodEffect)
            {
                this.defName = defName;
                this.defaultMoodEffect = defaultMoodEffect;
            }
        }

        public static readonly List<Entry> Entries = new List<Entry>
        {
            new Entry("SettlementServiceEvent_PositiveExperience", 3f),
            new Entry("SettlementServiceEvent_AdultRecreation_Positive", 6f),
            new Entry("SettlementServiceEvent_Hospitality_Spa", 5f),
            new Entry("SettlementServiceEvent_Hospitality_Tavern", 3f),
            new Entry("SettlementServiceEvent_Hospitality_ExtendedStay_ThreeNights", 5f),
            new Entry("SettlementServiceEvent_Hospitality_ExtendedStay_OneWeek", 10f),
            new Entry("SettlementServiceEvent_Hospitality_Meditation", 4f),
        };

        public static float GetValue(ModSettings settings, string defName)
        {
            if (settings.moodThoughtOverrides.TryGetValue(defName, out float overridden)) return overridden;

            foreach (Entry entry in Entries)
                if (entry.defName == defName) return entry.defaultMoodEffect;

            return 0f;
        }

        public static void Apply(ModSettings settings)
        {
            foreach (Entry entry in Entries)
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(entry.defName);
                if (thought == null || thought.stages.NullOrEmpty()) continue;

                thought.stages[0].baseMoodEffect = GetValue(settings, entry.defName);
            }
        }
    }
}
