using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class ServiceTargetOptionSelection : IExposable
    {
        public int targetIndex;
        public List<string> optionKeys = new List<string>();

        public ServiceTargetOptionSelection()
        {
        }

        public ServiceTargetOptionSelection(int targetIndex, IEnumerable<string> optionKeys)
        {
            this.targetIndex = targetIndex;
            this.optionKeys = Normalize(optionKeys);
        }

        public ServiceTargetOptionSelection Clone() => new ServiceTargetOptionSelection(targetIndex, optionKeys);

        public void ExposeData()
        {
            Scribe_Values.Look(ref targetIndex, "targetIndex");
            Scribe_Collections.Look(ref optionKeys, "optionKeys", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit) optionKeys = Normalize(optionKeys);
        }

        private static List<string> Normalize(IEnumerable<string> keys)
        {
            var result = new List<string>();
            if (keys == null) return result;

            var seen = new HashSet<string>();
            foreach (string key in keys)
                if (key != null && seen.Add(key)) result.Add(key);
            return result;
        }
    }
}
