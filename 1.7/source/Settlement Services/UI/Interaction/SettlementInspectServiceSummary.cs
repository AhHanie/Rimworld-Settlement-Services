using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.UI.Interaction
{
    internal static class SettlementInspectServiceSummary
    {
        public static List<string> BuildLines(IReadOnlyList<SettlementSpecialtyDef> specialties, IReadOnlyList<DiscoveryRecord> discoveries)
        {
            var lines = new List<string>();
            if (specialties != null) lines.Add(BuildSpecialtyLine(specialties));

            List<SettlementServiceDef> inPerson = ResolveDistinct(discoveries, RequestChannel.InPerson);
            List<SettlementServiceDef> remote = ResolveDistinct(discoveries, RequestChannel.Remote);

            if (inPerson.Count == 0 && remote.Count == 0)
            {
                if (specialties != null) lines.Add("SettlementServices.Label.InspectNoDiscoveries".Translate());
                return lines;
            }

            AppendChannel(lines, specialties, inPerson,
                "SettlementServices.Label.InspectSpecialtyServicesInPerson",
                "SettlementServices.Label.InspectOtherAreasInPerson");
            AppendChannel(lines, specialties, remote,
                "SettlementServices.Label.InspectSpecialtyServicesRemote",
                "SettlementServices.Label.InspectOtherAreasRemote");
            return lines;
        }

        private static string BuildSpecialtyLine(IReadOnlyList<SettlementSpecialtyDef> specialties)
        {
            if (specialties.Count == 0) return "SettlementServices.Header.NoSpecialties".Translate();

            string names = string.Join(", ", specialties
                .Distinct()
                .Select(s => s.label.NullOrEmpty() ? s.defName : s.label)
                .OrderBy(l => l));
            return "SettlementServices.Label.InspectSpecialties".Translate(names);
        }

        private static List<SettlementServiceDef> ResolveDistinct(IReadOnlyList<DiscoveryRecord> discoveries, RequestChannel channel)
        {
            var result = new List<SettlementServiceDef>();
            if (discoveries == null) return result;

            var seen = new HashSet<SettlementServiceDef>();
            foreach (DiscoveryRecord record in discoveries)
            {
                if (record == null || record.discoveredVia != channel) continue;

                SettlementServiceDef def = record.ResolveDef();
                if (def != null && seen.Add(def)) result.Add(def);
            }
            return result;
        }

        private static void AppendChannel(List<string> lines, IReadOnlyList<SettlementSpecialtyDef> specialties,
            List<SettlementServiceDef> services, string specialtyHeadingKey, string otherAreasKey)
        {
            if (services.Count == 0) return;

            List<SettlementServiceDef> specialtyServices = services.Where(s => IsSpecialtyRelated(s, specialties)).ToList();
            List<SettlementServiceDef> otherServices = services.Except(specialtyServices).ToList();

            if (specialtyServices.Count > 0)
            {
                lines.Add(specialtyHeadingKey.Translate());
                foreach (string label in specialtyServices.Select(s => (string)s.LabelCap).OrderBy(l => l))
                    lines.Add("- " + label);
            }

            string areas = string.Join(", ", otherServices
                .Select(s => s.category)
                .Where(c => c != null)
                .Distinct()
                .OrderBy(c => c.order)
                .ThenBy(c => (string)c.LabelCap)
                .Select(c => (string)c.LabelCap));
            if (!areas.NullOrEmpty()) lines.Add(otherAreasKey.Translate(areas));
        }

        private static bool IsSpecialtyRelated(SettlementServiceDef service, IReadOnlyList<SettlementSpecialtyDef> specialties)
        {
            if (specialties == null) return false;

            foreach (SettlementSpecialtyDef specialty in specialties)
            {
                SettlementCapabilityModifiers mods = specialty.modifiers;
                if (mods != null)
                {
                    if (service.category != null && !mods.relevantCategoryDefNames.NullOrEmpty()
                        && mods.relevantCategoryDefNames.Contains(service.category.defName)) return true;
                    if (!mods.relevantServiceDefNames.NullOrEmpty() && mods.relevantServiceDefNames.Contains(service.defName)) return true;
                    if (!service.requiredCapabilityTags.NullOrEmpty() && !mods.capabilityTags.NullOrEmpty()
                        && service.requiredCapabilityTags.Any(mods.capabilityTags.Contains)) return true;
                }

                if (!service.requiredSpecialtyDefNames.NullOrEmpty() && service.requiredSpecialtyDefNames.Contains(specialty.defName)) return true;
            }
            return false;
        }
    }
}
