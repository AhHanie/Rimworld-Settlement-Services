using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class SettlementServiceQuote : IExposable
    {
        public List<ServiceLineItem> lineItems = new List<ServiceLineItem>();
        public int totalCost;
        public string selectedTierKey;
        public int expectedDurationTicks;
        public int unitCount = 1;
        public int unitPrice;

        public List<ServiceUnitQuote> perUnitQuotes = new List<ServiceUnitQuote>();

        public List<string> validationErrors = new List<string>();

        public bool IsValid => validationErrors.Count == 0;

        public static SettlementServiceQuote Invalid(string errorKey)
        {
            return new SettlementServiceQuote { validationErrors = { errorKey } };
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref lineItems, "lineItems", LookMode.Deep);
            Scribe_Values.Look(ref totalCost, "totalCost");
            Scribe_Values.Look(ref selectedTierKey, "selectedTierKey");
            Scribe_Values.Look(ref expectedDurationTicks, "expectedDurationTicks");
            Scribe_Values.Look(ref unitCount, "unitCount", 1);
            Scribe_Values.Look(ref unitPrice, "unitPrice");
            Scribe_Collections.Look(ref perUnitQuotes, "perUnitQuotes", LookMode.Deep);
            Scribe_Collections.Look(ref validationErrors, "validationErrors", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (lineItems == null) lineItems = new List<ServiceLineItem>();
                if (perUnitQuotes == null) perUnitQuotes = new List<ServiceUnitQuote>();
                if (validationErrors == null) validationErrors = new List<string>();
            }
        }
    }
}
