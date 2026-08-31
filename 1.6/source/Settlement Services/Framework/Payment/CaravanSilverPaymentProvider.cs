using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Payment
{
    public class CaravanSilverPaymentProvider : IServicePaymentProvider
    {
        public static readonly CaravanSilverPaymentProvider Instance = new CaravanSilverPaymentProvider();

        private static Caravan ResolveCaravan(SettlementServiceRequest request) => request.negotiator?.GetCaravan();

        private static IEnumerable<Thing> CaravanSilver(Caravan caravan) =>
            caravan == null ? Enumerable.Empty<Thing>() : CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => t.def == ThingDefOf.Silver);

        public bool HasEnough(int amount, SettlementServiceRequest request) =>
            CaravanSilver(ResolveCaravan(request)).Sum(t => t.stackCount) >= amount;

        public bool TryDebit(int amount, SettlementServiceRequest request, out string errorKey)
        {
            Caravan caravan = ResolveCaravan(request);
            if (caravan == null || !HasEnough(amount, request))
            {
                errorKey = "SettlementServices.Error.InsufficientCaravanSilver";
                return false;
            }

            int remaining = amount;
            foreach (Thing thing in CaravanSilver(caravan).ToList())
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, thing.stackCount);
                thing.SplitOff(take).Destroy();
                remaining -= take;
            }

            errorKey = null;
            return true;
        }

        public void Refund(int amount, ServiceJobContext ctx)
        {
            Caravan caravan = ResolveRequesterCaravan(ctx);
            if (caravan == null) return;

            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = amount;
            CaravanInventoryUtility.GiveThing(caravan, silver);
        }

        private static Caravan ResolveRequesterCaravan(ServiceJobContext ctx)
        {
            if (ctx.Job.requesterCaravanId < 0) return null;
            return Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == ctx.Job.requesterCaravanId);
        }
    }
}
