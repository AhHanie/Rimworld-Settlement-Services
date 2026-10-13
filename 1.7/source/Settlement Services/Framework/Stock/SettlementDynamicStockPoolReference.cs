using System.Collections.Generic;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Stock
{
    public class SettlementDynamicStockPoolReference
    {
        public readonly SettlementStockCategoryDef category;
        public readonly SettlementDynamicStockPool pool;
        public readonly string key;
        public readonly IReadOnlyList<ThingDef> candidates;
        public readonly HashSet<ThingDef> candidateSet;

        public SettlementDynamicStockPoolReference(SettlementStockCategoryDef category, SettlementDynamicStockPool pool, string key, List<ThingDef> candidates)
        {
            this.category = category;
            this.pool = pool;
            this.key = key;
            this.candidates = candidates;
            candidateSet = new HashSet<ThingDef>(candidates);
        }
    }
}
