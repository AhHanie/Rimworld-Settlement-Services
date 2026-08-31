using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceInputPlan
    {
        public static readonly ServiceInputPlan None = new ServiceInputPlan();

        public List<ThingDefCountClass> stockConsumed = new List<ThingDefCountClass>();
        public List<ThingDefCountClass> playerSuppliedConsumed = new List<ThingDefCountClass>();
    }
}
