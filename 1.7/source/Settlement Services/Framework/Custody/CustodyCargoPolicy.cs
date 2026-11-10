using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Custody
{
    public static class CustodyCargoPolicy
    {
        private static readonly List<Func<Thing, Pawn, bool>> retainWithPawnPredicates = new List<Func<Thing, Pawn, bool>>();

        public static void RegisterRetainWithPawn(Func<Thing, Pawn, bool> predicate)
        {
            if (predicate != null && !retainWithPawnPredicates.Contains(predicate)) retainWithPawnPredicates.Add(predicate);
        }

        internal static bool ShouldRetainWithPawn(Thing thing, Pawn owner)
        {
            if (thing == null) return true;
            if (thing is Pawn) return true;
            if (thing.def.IsWeapon) return true;
            if (thing is ThingWithComps withComps && withComps.GetComp<CompEquippable>() != null) return true;

            for (int i = 0; i < retainWithPawnPredicates.Count; i++)
            {
                try
                {
                    if (retainWithPawnPredicates[i](thing, owner)) return true;
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "A registered custody cargo retention predicate threw; treating the item as ordinary cargo");
                }
            }
            return false;
        }
    }
}
