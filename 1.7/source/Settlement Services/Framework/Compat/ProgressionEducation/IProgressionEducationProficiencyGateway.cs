using System;
using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Compat.ProgressionEducation
{
    internal readonly struct ProficiencyPromotionOption
    {
        public readonly string optionKey;
        public readonly string trackLabelCap;
        public readonly string currentTierLabelCap;
        public readonly string nextTierLabelCap;
        public readonly int semesterGoal;

        public ProficiencyPromotionOption(string optionKey, string trackLabelCap, string currentTierLabelCap, string nextTierLabelCap, int semesterGoal)
        {
            this.optionKey = optionKey;
            this.trackLabelCap = trackLabelCap;
            this.currentTierLabelCap = currentTierLabelCap;
            this.nextTierLabelCap = nextTierLabelCap;
            this.semesterGoal = semesterGoal;
        }
    }

    internal readonly struct ProficiencyPromotionResult
    {
        public readonly bool success;
        public readonly string diagnosticReason;

        private ProficiencyPromotionResult(bool success, string diagnosticReason)
        {
            this.success = success;
            this.diagnosticReason = diagnosticReason;
        }

        public static ProficiencyPromotionResult Ok() => new ProficiencyPromotionResult(true, null);
        public static ProficiencyPromotionResult Fail(string diagnosticReason) => new ProficiencyPromotionResult(false, diagnosticReason);
    }

    internal interface IProgressionEducationProficiencyGateway
    {
        bool IsReady { get; }

        float ProficiencyClassSpeedModifier { get; }

        IEnumerable<ProficiencyPromotionOption> GetEligiblePromotions(Pawn pawn);

        bool TryResolvePromotion(Pawn pawn, string optionKey, out ProficiencyPromotionOption option);

        ProficiencyPromotionResult TryGrantPromotion(Pawn pawn, string optionKey);
    }

    internal sealed class NullProgressionEducationProficiencyGateway : IProgressionEducationProficiencyGateway
    {
        internal static readonly NullProgressionEducationProficiencyGateway Instance = new NullProgressionEducationProficiencyGateway();

        public bool IsReady => false;

        public float ProficiencyClassSpeedModifier => 1f;

        public IEnumerable<ProficiencyPromotionOption> GetEligiblePromotions(Pawn pawn) => Array.Empty<ProficiencyPromotionOption>();

        public bool TryResolvePromotion(Pawn pawn, string optionKey, out ProficiencyPromotionOption option)
        {
            option = default;
            return false;
        }

        public ProficiencyPromotionResult TryGrantPromotion(Pawn pawn, string optionKey) =>
            ProficiencyPromotionResult.Fail("Progression Education integration is unavailable");
    }
}
