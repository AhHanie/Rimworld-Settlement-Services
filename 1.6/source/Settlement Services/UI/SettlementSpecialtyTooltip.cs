using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Framework.Validation;
using Settlement_Services.Services.Hospitality;

namespace Settlement_Services.UI
{
    internal static class SettlementSpecialtyTooltip
    {
        public static string Build(Settlement settlement, SettlementSpecialtyDef specialty)
        {
            var sb = new StringBuilder();
            sb.Append(specialty.LabelCap.Colorize(ColoredText.TipSectionTitleColor));

            if (!specialty.description.NullOrEmpty())
                sb.Append("\n").Append(specialty.description);

            AppendSection(sb, "SettlementServices.SpecialtyTooltip.ServicesUnlocked".Translate(), BuildServicesUnlocked(settlement, specialty));
            AppendSection(sb, "SettlementServices.SpecialtyTooltip.HospitalityPackagesUnlocked".Translate(), BuildHospitalityPackagesUnlocked(settlement, specialty));
            AppendRaw(sb, BuildCategoryModifiers(specialty));
            AppendRaw(sb, BuildStockModifiers(specialty));
            AppendRaw(sb, BuildEffectKeys(specialty));

            return sb.ToString();
        }

        private static string BuildServicesUnlocked(Settlement settlement, SettlementSpecialtyDef specialty)
        {
            List<string> labels = SettlementServiceRegistry.AllValid
                .Where(d => !d.requiredCapabilityTags.NullOrEmpty()
                    && d.requiredCapabilityTags.All(specialty.modifiers.capabilityTags.Contains)
                    && SettlementServiceValidator.StructuralEligibility(d, settlement, out _))
                .Select(d => (string)d.LabelCap)
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();

            return labels.Count == 0 ? null : string.Join("\n", labels.Select(l => "- " + l));
        }

        private static string BuildHospitalityPackagesUnlocked(Settlement settlement, SettlementSpecialtyDef specialty)
        {
            List<string> labels = HospitalityPackageRegistry.AllValid
                .Where(pkg => !pkg.requiredCapabilityTags.NullOrEmpty()
                    && pkg.requiredCapabilityTags.All(specialty.modifiers.capabilityTags.Contains)
                    && HospitalityPackageUnlockedByDirectTags(pkg, settlement))
                .Select(pkg => (string)pkg.LabelCap)
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();

            return labels.Count == 0 ? null : string.Join("\n", labels.Select(l => "- " + l));
        }

        private static bool HospitalityPackageUnlockedByDirectTags(HospitalityPackageDef pkg, Settlement settlement)
        {
            if (settlement?.Faction?.def != null && (int)pkg.requiredTechLevel > (int)settlement.Faction.def.techLevel) return false;

            return true;
        }

        private static string BuildCategoryModifiers(SettlementSpecialtyDef specialty)
        {
            SettlementCapabilityModifiers mods = specialty.modifiers;
            if (mods.qualityOffset == 0f && mods.priceModifierPct == 0f && mods.durationMultiplierOffset == 0f)
                return null;

            var sb = new StringBuilder();
            foreach (string categoryDefName in mods.relevantCategoryDefNames)
            {
                ServiceCategoryDef category = DefDatabase<ServiceCategoryDef>.GetNamedSilentFail(categoryDefName);
                if (category == null) continue;

                AppendModifierBlock(sb, category.LabelCap, mods);
            }
            foreach (string serviceDefName in mods.relevantServiceDefNames)
            {
                SettlementServiceDef service = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(serviceDefName);
                if (service == null) continue;

                AppendModifierBlock(sb, service.LabelCap, mods);
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        private static void AppendModifierBlock(StringBuilder sb, string heading, SettlementCapabilityModifiers mods)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(heading.Colorize(ColoredText.TipSectionTitleColor));
            if (mods.qualityOffset != 0f)
                sb.Append("\n").Append("SettlementServices.SpecialtyTooltip.QualityModifier".Translate(mods.qualityOffset.ToStringPercentSigned()));
            if (mods.priceModifierPct != 0f)
                sb.Append("\n").Append("SettlementServices.SpecialtyTooltip.PriceModifier".Translate(mods.priceModifierPct.ToStringPercentSigned()));
            if (mods.durationMultiplierOffset != 0f)
                sb.Append("\n").Append("SettlementServices.SpecialtyTooltip.DurationModifier".Translate(mods.durationMultiplierOffset.ToStringPercentSigned()));
        }

        private static string BuildStockModifiers(SettlementSpecialtyDef specialty)
        {
            var sb = new StringBuilder();
            foreach (SpecialtyStockModifier mod in specialty.stockModifiers)
            {
                SettlementStockCategoryDef category = DefDatabase<SettlementStockCategoryDef>.GetNamedSilentFail(mod.stockCategoryDefName);
                if (category == null) continue;

                if (mod.capacityMultiplier != 1f)
                {
                    if (sb.Length > 0) sb.Append("\n");
                    sb.Append("SettlementServices.SpecialtyTooltip.StockCapacity".Translate(category.LabelCap, (mod.capacityMultiplier - 1f).ToStringPercentSigned()));
                }
                if (mod.refreshRateMultiplier != 1f)
                {
                    if (sb.Length > 0) sb.Append("\n");
                    sb.Append("SettlementServices.SpecialtyTooltip.StockRefresh".Translate(category.LabelCap, (mod.refreshRateMultiplier - 1f).ToStringPercentSigned()));
                }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        private static string BuildEffectKeys(SettlementSpecialtyDef specialty) =>
            specialty.tooltipEffectKeys.NullOrEmpty()
                ? null
                : string.Join("\n", specialty.tooltipEffectKeys.Select(k => "- " + k.Translate()));

        private static void AppendSection(StringBuilder sb, string heading, string body)
        {
            if (body.NullOrEmpty()) return;
            sb.Append("\n\n").Append(heading.Colorize(ColoredText.TipSectionTitleColor)).Append("\n").Append(body);
        }

        private static void AppendRaw(StringBuilder sb, string body)
        {
            if (body.NullOrEmpty()) return;
            sb.Append("\n\n").Append(body);
        }
    }
}
