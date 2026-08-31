using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Payment
{
    public class HomeColonySilverPaymentProvider : IServicePaymentProvider
    {
        public static readonly HomeColonySilverPaymentProvider Instance = new HomeColonySilverPaymentProvider();

        private static IEnumerable<Thing> HomeStoredSilver() =>
            Find.Maps.Where(m => m.IsPlayerHome)
                     .SelectMany(m => m.listerThings.ThingsOfDef(ThingDefOf.Silver))
                     .Where(t => t.IsInValidStorage());

        public bool HasEnough(int amount, SettlementServiceRequest request) =>
            HomeStoredSilver().Sum(t => t.stackCount) >= amount;

        public bool TryDebit(int amount, SettlementServiceRequest request, out string errorKey)
        {
            if (!HasEnough(amount, request))
            {
                errorKey = "SettlementServices.Error.InsufficientHomeSilver";
                return false;
            }

            int remaining = amount;
            foreach (Thing thing in HomeStoredSilver().ToList())
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
            if (!TryPlaceRefund(amount)) ctx.Domain.QueuePendingHomeSilverRefund(amount);
        }

        public bool TryPlaceRefund(int amount)
        {
            Map targetMap = TradeUtility.PlayerHomeMapWithMostLaunchableSilver() ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (targetMap == null) return false;

            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = amount;
            GenPlace.TryPlaceThing(silver, targetMap.Center, targetMap, ThingPlaceMode.Near);
            return true;
        }
    }
}
