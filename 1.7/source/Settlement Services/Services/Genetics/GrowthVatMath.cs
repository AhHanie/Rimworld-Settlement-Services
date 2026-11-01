using RimWorld;
using UnityEngine;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal struct GrowthVatProjection
    {
        public int GestationTicks;
        public long GrowthTicks;
        public float Nutrition;

        public long TotalTicks => GestationTicks + GrowthTicks;
    }

    internal static class GrowthVatMath
    {
        public const int GestationTicks = Building_GrowthVat.EmbryoGestationTicks;
        public const float EmbryoNutritionPerDay = 6f;
        public const float PawnNutritionPerDay = 3f;
        public const int TicksPerDay = 60000;
        public const float AdultAge = 18f;

        public static float EffectiveSpeed(Pawn pawn)
        {
            float speed = pawn == null ? 1f : pawn.GetStatValue(StatDefOf.GrowthVatOccupantSpeed);
            return float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f ? 1f : speed;
        }

        public static int AgeTicksPerGameTick(float speed) =>
            Mathf.Max(1, Mathf.RoundToInt(Building_GrowthVat.AgeTicksPerTickInGrowthVat * speed));

        public static long GameTicksToReach(long remainingAgeTicks, int ageTicksPerGameTick)
        {
            if (remainingAgeTicks <= 0) return 0;
            return (remainingAgeTicks + ageTicksPerGameTick - 1) / ageTicksPerGameTick;
        }

        public static float NutritionForPawnTicks(long ticks) => PawnNutritionPerDay * ticks / TicksPerDay;

        public static float NutritionForEmbryoTicks(long ticks) => EmbryoNutritionPerDay * ticks / TicksPerDay;

        public static GrowthVatProjection ProjectEmbryo(GrowthVatMilestone milestone, float newbornSpeed = 1f)
        {
            long growthTicks = milestone.IsBirth ? 0 : GameTicksToReach(milestone.TargetAgeTicks, AgeTicksPerGameTick(newbornSpeed));
            return new GrowthVatProjection
            {
                GestationTicks = GestationTicks,
                GrowthTicks = growthTicks,
                Nutrition = NutritionForEmbryoTicks(GestationTicks) + NutritionForPawnTicks(growthTicks),
            };
        }

        public static GrowthVatProjection ProjectChild(Pawn pawn, GrowthVatMilestone milestone)
        {
            long remaining = milestone.TargetAgeTicks - pawn.ageTracker.AgeBiologicalTicks;
            long growthTicks = GameTicksToReach(remaining, AgeTicksPerGameTick(EffectiveSpeed(pawn)));
            return new GrowthVatProjection
            {
                GestationTicks = 0,
                GrowthTicks = growthTicks,
                Nutrition = NutritionForPawnTicks(growthTicks),
            };
        }

        public static int ClampTicks(long ticks) => (int)System.Math.Min(int.MaxValue, System.Math.Max(1L, ticks));
    }
}
