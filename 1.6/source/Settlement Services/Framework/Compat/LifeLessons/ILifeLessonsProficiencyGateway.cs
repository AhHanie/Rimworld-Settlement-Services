using System;
using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Compat.LifeLessons
{
    internal readonly struct LifeLessonsProficiencyOption
    {
        public readonly string optionKey;
        public readonly string proficiencyLabelCap;
        public readonly string categoryLabelCap;
        public readonly int totalLearningCost;

        public LifeLessonsProficiencyOption(string optionKey, string proficiencyLabelCap, string categoryLabelCap, int totalLearningCost)
        {
            this.optionKey = optionKey;
            this.proficiencyLabelCap = proficiencyLabelCap;
            this.categoryLabelCap = categoryLabelCap;
            this.totalLearningCost = totalLearningCost;
        }
    }

    internal readonly struct LifeLessonsAwardResult
    {
        public readonly bool success;
        public readonly string diagnosticReason;

        private LifeLessonsAwardResult(bool success, string diagnosticReason)
        {
            this.success = success;
            this.diagnosticReason = diagnosticReason;
        }

        public static LifeLessonsAwardResult Ok() => new LifeLessonsAwardResult(true, null);
        public static LifeLessonsAwardResult Fail(string diagnosticReason) => new LifeLessonsAwardResult(false, diagnosticReason);
    }

    internal interface ILifeLessonsProficiencyGateway
    {
        bool IsReady { get; }

        IEnumerable<LifeLessonsProficiencyOption> GetEligibleProficiencies(Pawn pawn);

        bool TryResolveProficiency(Pawn pawn, string optionKey, out LifeLessonsProficiencyOption option);

        LifeLessonsAwardResult TryGrantProficiency(Pawn pawn, string optionKey);
    }

    internal sealed class NullLifeLessonsProficiencyGateway : ILifeLessonsProficiencyGateway
    {
        internal static readonly NullLifeLessonsProficiencyGateway Instance = new NullLifeLessonsProficiencyGateway();

        public bool IsReady => false;

        public IEnumerable<LifeLessonsProficiencyOption> GetEligibleProficiencies(Pawn pawn) => Array.Empty<LifeLessonsProficiencyOption>();

        public bool TryResolveProficiency(Pawn pawn, string optionKey, out LifeLessonsProficiencyOption option)
        {
            option = default;
            return false;
        }

        public LifeLessonsAwardResult TryGrantProficiency(Pawn pawn, string optionKey) =>
            LifeLessonsAwardResult.Fail("Life Lessons integration is unavailable");
    }
}
