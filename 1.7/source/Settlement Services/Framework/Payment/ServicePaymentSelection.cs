using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Payment
{
    public enum ServicePaymentKind
    {
        Silver,
        Barter
    }

    public class ServicePaymentEntry
    {
        public Thing thing;
        public int count;
        public decimal? expectedUnitValue;

        public ServicePaymentEntry(Thing thing, int count, decimal? expectedUnitValue = null)
        {
            this.thing = thing;
            this.count = count;
            this.expectedUnitValue = expectedUnitValue;
        }
    }

    public class ServicePaymentSelection
    {
        public readonly ServicePaymentKind kind;
        public readonly List<ServicePaymentEntry> entries;
        public readonly List<ServicePaymentEntry> goods;

        private ServicePaymentSelection(ServicePaymentKind kind, List<ServicePaymentEntry> entries, List<ServicePaymentEntry> goods)
        {
            this.kind = kind;
            this.entries = entries;
            this.goods = goods;
        }

        public bool IsBarter => kind == ServicePaymentKind.Barter;

        public bool HasGoods => goods.Count > 0;

        public static ServicePaymentSelection Silver() =>
            new ServicePaymentSelection(ServicePaymentKind.Silver, new List<ServicePaymentEntry>(), new List<ServicePaymentEntry>());

        public static ServicePaymentSelection Barter(IEnumerable<ServicePaymentEntry> entries) => Barter(entries, null);

        public static ServicePaymentSelection Barter(IEnumerable<ServicePaymentEntry> entries, IEnumerable<ServicePaymentEntry> goods) =>
            new ServicePaymentSelection(
                ServicePaymentKind.Barter,
                new List<ServicePaymentEntry>(entries),
                goods == null ? new List<ServicePaymentEntry>() : new List<ServicePaymentEntry>(goods));
    }
}
