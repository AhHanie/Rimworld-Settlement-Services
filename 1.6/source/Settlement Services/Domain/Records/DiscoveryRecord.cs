using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Domain.Records
{
    public class DiscoveryRecord : IExposable
    {
        public string serviceDefName;
        public RequestChannel discoveredVia;
        public int discoveredTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref serviceDefName, "serviceDefName");
            Scribe_Values.Look(ref discoveredVia, "discoveredVia");
            Scribe_Values.Look(ref discoveredTick, "discoveredTick");
        }

        public SettlementServiceDef ResolveDef()
        {
            return DefDatabase<SettlementServiceDef>.GetNamedSilentFail(serviceDefName);
        }
    }
}
