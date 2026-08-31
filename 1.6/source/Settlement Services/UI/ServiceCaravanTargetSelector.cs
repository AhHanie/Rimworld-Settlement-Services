using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Compat;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.UI
{
    public static class ServiceCaravanTargetSelector
    {
        public static IEnumerable<Thing> EligibleTargets(Caravan caravan, ServiceTargetRule rule)
        {
            if (caravan == null || rule == ServiceTargetRule.None) yield break;
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;

            IEnumerable<Thing> candidates;
            switch (rule)
            {
                case ServiceTargetRule.Pawn:
                    candidates = caravan.PawnsListForReading.Where(p => p.RaceProps.Humanlike);
                    break;
                case ServiceTargetRule.Animal:
                    candidates = caravan.PawnsListForReading.Where(p => p.RaceProps.Animal);
                    break;
                case ServiceTargetRule.Mech:
                    candidates = caravan.PawnsListForReading.Where(p => p.RaceProps.IsMechanoid);
                    break;
                case ServiceTargetRule.Vehicle:
                    candidates = VehicleCandidates(caravan);
                    break;
                case ServiceTargetRule.Android:
                    candidates = caravan.PawnsListForReading.Where(p => AndroidsAdapter.IsAndroid(p));
                    break;
                case ServiceTargetRule.Item:
                    candidates = CaravanInventoryUtility.AllInventoryItems(caravan);
                    break;
                default:
                    candidates = Enumerable.Empty<Thing>();
                    break;
            }

            foreach (Thing thing in candidates)
                if (!domain.IsTargetReserved(thing)) yield return thing;
        }

        private static IEnumerable<Thing> VehicleCandidates(Caravan caravan)
        {
            Type vehicleType = VehicleFrameworkAdapter.VehiclePawnType;
            if (vehicleType == null) yield break;
            foreach (Pawn pawn in caravan.PawnsListForReading)
                if (vehicleType.IsInstanceOfType(pawn)) yield return pawn;
        }

        public static void OpenTargetPicker(ServiceRequestSession session, Action<Thing> onChosen)
        {
            var excluded = new HashSet<Thing>(session.targets);
            List<Thing> options = EligibleTargets(session.caravan, session.def.targetRule).Where(t => !excluded.Contains(t)).ToList();
            if (options.Count == 0)
            {
                Messages.Message("SettlementServices.Message.NoEligibleTargets".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            var floatOptions = options.Select(t => new FloatMenuOption(t.LabelCap, () => onChosen(t))).ToList();
            Find.WindowStack.Add(new FloatMenu(floatOptions));
        }

        public static bool RevalidateStillEligible(ServiceRequestSession session)
        {
            if (session.def.targetRule == ServiceTargetRule.None) return true;

            int before = session.targets.Count;
            for (int i = session.targets.Count - 1; i >= 0; i--)
                if (!IsStillEligible(session, session.targets[i])) session.RemoveTargetAt(i);
            return session.targets.Count == before;
        }

        private static bool IsStillEligible(ServiceRequestSession session, Thing thing)
        {
            if (thing == null || thing.Destroyed) return false;
            if (SettlementServicesWorldComponent.Current.IsTargetReserved(thing)) return false;
            if (session.caravan != null && thing is Pawn p && !session.caravan.PawnsListForReading.Contains(p)) return false;
            if (session.caravan != null && !(thing is Pawn) && !CaravanInventoryUtility.AllInventoryItems(session.caravan).Contains(thing)) return false;
            return true;
        }
    }
}
