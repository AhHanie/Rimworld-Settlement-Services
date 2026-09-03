using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal static class BiosculpterCycleService
    {
        public const string PodDefName = "BiosculpterPod";
        public const string BioregenerationKey = "bioregeneration";
        public const string AgeReversalKey = "ageReversal";
        public const float NutritionRequired = 5f;

        public static bool PodDefExists() => DefDatabase<ThingDef>.GetNamedSilentFail(PodDefName) != null;

        public static bool CanAgeReverse(Pawn pawn) => pawn.ageTracker.Adult;

        public static CompProperties_BiosculpterPod_BaseCycle ResolveCycleProps(string cycleKey)
        {
            if (cycleKey == null) return null;

            ThingDef podDef = DefDatabase<ThingDef>.GetNamedSilentFail(PodDefName);
            return podDef?.comps?
                .OfType<CompProperties_BiosculpterPod_BaseCycle>()
                .FirstOrDefault(p => p.key == cycleKey);
        }

        public static int? BaseTicksFor(string cycleKey)
        {
            CompProperties_BiosculpterPod_BaseCycle props = ResolveCycleProps(cycleKey);
            return props != null ? Mathf.RoundToInt(props.durationDays * GenDate.TicksPerDay) : (int?)null;
        }

        public static CompBiosculpterPod_Cycle ResolveCycle(string cycleKey)
        {
            CompProperties_BiosculpterPod_BaseCycle props = ResolveCycleProps(cycleKey);
            if (props == null) return null;

            var comp = (CompBiosculpterPod_Cycle)Activator.CreateInstance(props.compClass);
            comp.props = props;
            return comp;
        }
    }
}
