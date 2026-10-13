using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Pricing
{
    internal static class NegotiatorDiscount
    {
        private const int Tier1Level = 5;
        private const int Tier2Level = 10;
        private const int Tier3Level = 15;
        private const int Tier4Level = 20;

        private const float Tier1Pct = 0.02f;
        private const float Tier2Pct = 0.03f;
        private const float Tier3Pct = 0.04f;
        private const float Tier4Pct = 0.05f;

        public static float DiscountPctFor(Pawn negotiator)
        {
            if (!ModSettings.Current.negotiatorSocialDiscountEnabled) return 0f;

            int level = EffectiveSocialLevel(negotiator);
            if (level >= Tier4Level) return Tier4Pct;
            if (level >= Tier3Level) return Tier3Pct;
            if (level >= Tier2Level) return Tier2Pct;
            if (level >= Tier1Level) return Tier1Pct;
            return 0f;
        }

        public static int EffectiveSocialLevel(Pawn negotiator) =>
            negotiator?.skills == null ? 0 : negotiator.skills.GetSkill(SkillDefOf.Social).Level;
    }
}
