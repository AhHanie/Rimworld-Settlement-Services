using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Services.Market
{
    public class MercerServiceWorker : LocalMarketServiceWorker
    {
        public override string GoodsHeadingKey => "SettlementServices.Label.MercerGoods";
        public override string NoGoodsLabelKey => "SettlementServices.Label.NoMercerGoodsAvailable";
        protected override string NoGoodsErrorKey => "SettlementServices.Error.NoMercerGoodsAvailable";

        public override List<MarketCatalogRow> GetSortedRows(Settlement settlement) =>
            MercerCatalog.SortedEligibleRows(settlement);

        protected override bool IsInAssortment(ThingDef thingDef) => MercerCatalog.GetGroup(thingDef) != null;
    }
}
