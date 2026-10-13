using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events
{
    public class ServiceEventWorker
    {
        public ServiceEventDef def;

        public virtual bool CanApply(ServiceJobContext ctx) => true;

        public virtual void Apply(ServiceJobContext ctx) { }
    }
}
