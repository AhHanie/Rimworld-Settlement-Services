using System.Collections.Generic;
using Verse;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceInputPlan
    {
        public static readonly ServiceInputPlan None = new ServiceInputPlan();

        public List<ThingDefCountClass> stockConsumed = new List<ThingDefCountClass>();
        public List<ThingDefCountClass> playerSuppliedConsumed = new List<ThingDefCountClass>();
        public List<ServiceStockReservation> specialStockReservations = new List<ServiceStockReservation>();
    }
}
