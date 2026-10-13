using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class ServiceUnitQuote : IExposable
    {
        public int unitIndex;
        public string targetLabel;
        public int totalCost;
        public List<ServiceLineItem> lineItems = new List<ServiceLineItem>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref unitIndex, "unitIndex");
            Scribe_Values.Look(ref targetLabel, "targetLabel");
            Scribe_Values.Look(ref totalCost, "totalCost");
            Scribe_Collections.Look(ref lineItems, "lineItems", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && lineItems == null) lineItems = new List<ServiceLineItem>();
        }
    }
}
