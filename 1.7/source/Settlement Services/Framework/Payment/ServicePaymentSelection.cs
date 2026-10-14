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

        public ServicePaymentEntry(Thing thing, int count)
        {
            this.thing = thing;
            this.count = count;
        }
    }

    public class ServicePaymentSelection
    {
        public readonly ServicePaymentKind kind;
        public readonly List<ServicePaymentEntry> entries;

        private ServicePaymentSelection(ServicePaymentKind kind, List<ServicePaymentEntry> entries)
        {
            this.kind = kind;
            this.entries = entries;
        }

        public bool IsBarter => kind == ServicePaymentKind.Barter;

        public static ServicePaymentSelection Silver() => new ServicePaymentSelection(ServicePaymentKind.Silver, new List<ServicePaymentEntry>());

        public static ServicePaymentSelection Barter(IEnumerable<ServicePaymentEntry> entries) =>
            new ServicePaymentSelection(ServicePaymentKind.Barter, new List<ServicePaymentEntry>(entries));
    }
}
