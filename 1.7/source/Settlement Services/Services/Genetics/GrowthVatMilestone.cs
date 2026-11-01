using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal sealed class GrowthVatMilestone
    {
        public const string BirthKey = "Birth";
        public const long TicksPerYear = 3600000L;

        public static readonly GrowthVatMilestone Birth = new GrowthVatMilestone(BirthKey, 0);
        public static readonly GrowthVatMilestone Age3 = new GrowthVatMilestone("Age3", 3);
        public static readonly GrowthVatMilestone Age7 = new GrowthVatMilestone("Age7", 7);
        public static readonly GrowthVatMilestone Age10 = new GrowthVatMilestone("Age10", 10);
        public static readonly GrowthVatMilestone Age13 = new GrowthVatMilestone("Age13", 13);
        public static readonly GrowthVatMilestone Age18 = new GrowthVatMilestone("Age18", 18);

        public static readonly IReadOnlyList<GrowthVatMilestone> All = new List<GrowthVatMilestone> { Birth, Age3, Age7, Age10, Age13, Age18 };

        public string Key { get; }
        public int Years { get; }
        public bool IsBirth => Years <= 0;
        public long TargetAgeTicks => Years * TicksPerYear;
        public bool IsGrowthMomentAge => Years == 7 || Years == 10 || Years == 13;

        private GrowthVatMilestone(string key, int years)
        {
            Key = key;
            Years = years;
        }

        public string Label => IsBirth
            ? "SettlementServices.Label.GrowthVatMilestoneBirth".Translate().ToString()
            : "SettlementServices.Label.GrowthVatMilestoneAge".Translate(Years).ToString();

        public string Description
        {
            get
            {
                if (IsBirth) return "SettlementServices.Label.GrowthVatMilestoneBirthDesc".Translate().ToString();
                if (IsGrowthMomentAge) return "SettlementServices.Label.GrowthVatMilestoneGrowthMomentDesc".Translate(Years).ToString();
                if (Years == 18) return "SettlementServices.Label.GrowthVatMilestoneAdultDesc".Translate().ToString();
                return "SettlementServices.Label.GrowthVatMilestoneAgeDesc".Translate(Years).ToString();
            }
        }

        public static GrowthVatMilestone FromKey(string key) => key == null ? null : All.FirstOrDefault(m => m.Key == key);

        public static GrowthVatMilestone FromSelection(IReadOnlyList<string> selectedKeys, out string errorKey)
        {
            if (selectedKeys == null || selectedKeys.Count == 0)
            {
                errorKey = "SettlementServices.Error.GrowthVatMilestoneRequired";
                return null;
            }

            if (selectedKeys.Count != 1 || selectedKeys.Distinct().Count() != 1)
            {
                errorKey = "SettlementServices.Error.GrowthVatMilestoneAmbiguous";
                return null;
            }

            GrowthVatMilestone milestone = FromKey(selectedKeys[0]);
            errorKey = milestone == null ? "SettlementServices.Error.GrowthVatMilestoneInvalid" : null;
            return milestone;
        }
    }
}
