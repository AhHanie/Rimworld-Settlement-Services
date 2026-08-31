using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class ServiceLineItem : IExposable
    {
        public string labelKey;

        public string labelArgument;

        public int amount;

        public bool isModifier;

        public ServiceLineItem()
        {
        }

        public ServiceLineItem(string labelKey, int amount, bool isModifier = false, string labelArgument = null)
        {
            this.labelKey = labelKey;
            this.amount = amount;
            this.isModifier = isModifier;
            this.labelArgument = labelArgument;
        }

        public string DisplayLabel => labelArgument == null ? labelKey.Translate() : labelKey.Translate(labelArgument);

        public void ExposeData()
        {
            Scribe_Values.Look(ref labelKey, "labelKey");
            Scribe_Values.Look(ref labelArgument, "labelArgument");
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref isModifier, "isModifier");
        }
    }
}
