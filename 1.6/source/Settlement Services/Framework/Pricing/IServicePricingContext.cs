using RimWorld;

namespace Settlement_Services.Framework.Pricing
{
    public interface IServicePricingContext
    {
        float TotalPlayerWealth { get; }
        float DifficultyMultiplier { get; }
        float WealthPriceScalePct { get; }
        float ReputationModifierPct(Faction faction);
    }
}
