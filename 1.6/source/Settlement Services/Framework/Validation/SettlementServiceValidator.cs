using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Specialty;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Framework.Validation
{
    public static class SettlementServiceValidator
    {
        public static bool Validate(SettlementServiceDef def, SettlementServiceRequest request, out string errorKey)
        {
            Settlement settlement = request.settlement;
            Faction faction = settlement?.Faction;

            if (faction != null && faction.HostileTo(Faction.OfPlayer))
            { errorKey = "SettlementServices.Error.FactionHostile"; return false; }

            if (!TechFactionEligibility(def, settlement, out errorKey)) return false;
            if (!GoodwillMet(def, settlement, out errorKey)) return false;
            if (!CapabilityEligibility(def, settlement, out errorKey)) return false;

            if (!ValidateRemoteChannel(def, request, out errorKey)) return false;

            errorKey = null;
            return true;
        }

        internal static bool StructuralEligibility(SettlementServiceDef def, Settlement settlement, out string errorKey)
        {
            if (!TechFactionEligibility(def, settlement, out errorKey)) return false;
            return CapabilityEligibility(def, settlement, out errorKey);
        }

        private static bool TechFactionEligibility(SettlementServiceDef def, Settlement settlement, out string errorKey)
        {
            Faction faction = settlement?.Faction;
            if (faction != null)
            {
                if (def.requiredTechLevel != TechLevel.Undefined && faction.def.techLevel < def.requiredTechLevel)
                { errorKey = "SettlementServices.Error.TechLevelTooLow"; return false; }

                if (!def.requiredFactionTags.NullOrEmpty() && !def.requiredFactionTags.Contains(faction.def.categoryTag))
                { errorKey = "SettlementServices.Error.FactionTypeNotEligible"; return false; }
            }

            errorKey = null;
            return true;
        }

        private static bool CapabilityEligibility(SettlementServiceDef def, Settlement settlement, out string errorKey)
        {
            if (!def.requiredCapabilityTags.NullOrEmpty()
                && settlement != null
                && !def.requiredCapabilityTags.All(tag => SettlementSpecialtyService.HasCapabilityTag(settlement, tag)))
            { errorKey = "SettlementServices.Error.SpecialtyRequired"; return false; }

            errorKey = null;
            return true;
        }

        internal static bool RemoteRequiresIndustrialTech(Settlement settlement)
        {
            Faction faction = settlement?.Faction;
            return faction == null || faction.def.techLevel >= TechLevel.Industrial;
        }

        internal static bool GoodwillMet(SettlementServiceDef def, Settlement settlement, out string errorKey)
        {
            Faction faction = settlement?.Faction;
            if (faction != null && def.minimumGoodwill != int.MinValue && faction.PlayerGoodwill < def.minimumGoodwill)
            { errorKey = "SettlementServices.Error.InsufficientGoodwill"; return false; }

            errorKey = null;
            return true;
        }

        internal static bool MatchesTargetRule(TargetKind kind, ServiceTargetRule rule) => kind.ToString() == rule.ToString();

        private static bool ValidateRemoteChannel(SettlementServiceDef def, SettlementServiceRequest request, out string errorKey)
        {
            if (request.channel != RequestChannel.Remote) { errorKey = null; return true; }

            if (!def.allowRemoteRequest) { errorKey = "SettlementServices.Error.RemoteNotSupported"; return false; }

            if (!RemoteRequiresIndustrialTech(request.settlement))
            { errorKey = "SettlementServices.Error.RemoteRequiresIndustrialSettlement"; return false; }

            if (!CommsConsoleUtility.PlayerHasPoweredCommsConsole())
            { errorKey = "SettlementServices.Error.RemoteRequiresCommsConsole"; return false; }

            if (def.targetRule != ServiceTargetRule.None || !request.playerSuppliedInputs.NullOrEmpty())
            { errorKey = "SettlementServices.Error.RemoteRequiresPhysicalPresence"; return false; }

            errorKey = null;
            return true;
        }
    }
}
