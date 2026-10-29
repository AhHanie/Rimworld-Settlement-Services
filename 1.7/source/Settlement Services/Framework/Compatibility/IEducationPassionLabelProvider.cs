using RimWorld;

namespace Settlement_Services.Framework.Compatibility
{
    internal interface IEducationPassionLabelProvider
    {
        bool TryGetLabel(SkillRecord record, out string label);
    }

    internal interface IEducationPassionLabelProviderModule
    {
        IEducationPassionLabelProvider EducationPassionLabelProvider { get; }
    }
}
