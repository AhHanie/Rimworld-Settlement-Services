using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal static class BiosculpterCycleService
    {
        public const string PodDefName = "BiosculpterPod";

        public static bool PodDefExists() => DefDatabase<ThingDef>.GetNamedSilentFail(PodDefName) != null;

        public static bool CanAgeReverse(Pawn pawn) => pawn.ageTracker.Adult;

        public static T ResolveCycle<T>() where T : CompBiosculpterPod_Cycle
        {
            ThingDef podDef = DefDatabase<ThingDef>.GetNamedSilentFail(PodDefName);
            CompProperties props = podDef?.comps?.FirstOrDefault(p => typeof(T).IsAssignableFrom(p.compClass));
            if (props == null) return null;

            var comp = (T)Activator.CreateInstance(props.compClass);
            comp.props = props;
            return comp;
        }
    }
}
