using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class MarketPurchasePlan : IExposable
    {
        public List<MarketCartLine> lines = new List<MarketCartLine>();

        public MarketPurchasePlan Clone() => new MarketPurchasePlan
        {
            lines = lines.Select(l => l.Clone()).ToList(),
        };

        public void ExposeData()
        {
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && lines == null) lines = new List<MarketCartLine>();
        }
    }
}
