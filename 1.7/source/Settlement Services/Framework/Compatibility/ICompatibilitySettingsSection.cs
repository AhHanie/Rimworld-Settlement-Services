using Verse;

namespace Settlement_Services.Framework.Compatibility
{
    internal interface ICompatibilitySettingsSection
    {
        string ModuleId { get; }
        void Draw(Listing_Standard listing, CompatibilitySettingsStore settings);
    }
}
