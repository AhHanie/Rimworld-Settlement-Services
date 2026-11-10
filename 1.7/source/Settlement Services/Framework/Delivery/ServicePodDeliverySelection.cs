using Verse;

namespace Settlement_Services.Framework.Delivery
{
    public sealed class ServicePodDeliverySelection
    {
        public bool enabled;
        public int mapParentId = -1;
        public IntVec3 cell = IntVec3.Invalid;

        public bool HasDestination => mapParentId >= 0 && cell.IsValid;

        public ServicePodDeliverySelection Clone() => new ServicePodDeliverySelection
        {
            enabled = enabled,
            mapParentId = mapParentId,
            cell = cell,
        };
    }
}
