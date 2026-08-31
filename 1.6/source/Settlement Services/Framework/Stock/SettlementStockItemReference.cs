using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Stock
{
    public class SettlementStockItemReference
    {
        public readonly ThingDef thing;
        public readonly SettlementStockCategoryDef category;
        public readonly SettlementStockItem item;

        public SettlementStockItemReference(ThingDef thing, SettlementStockCategoryDef category, SettlementStockItem item)
        {
            this.thing = thing;
            this.category = category;
            this.item = item;
        }
    }
}
