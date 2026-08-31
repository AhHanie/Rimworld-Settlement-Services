using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Stock
{
    public class StockAllocationLedger
    {
        internal readonly Dictionary<ThingDef, int> Settlement = new Dictionary<ThingDef, int>();
    }
}
