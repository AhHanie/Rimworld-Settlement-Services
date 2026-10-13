using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.UI
{
    internal static class ServiceDiscoveryFormatting
    {
        public static string JoinLabels(IEnumerable<DiscoveryRecord> discoveries)
        {
            IEnumerable<string> labels = discoveries
                .Select(d => d.ResolveDef())
                .Where(def => def != null)
                .Select(def => (string)def.LabelCap)
                .Distinct()
                .OrderBy(l => l);
            return string.Join(", ", labels);
        }

        public static (string inPerson, string remote) SplitByChannel(IReadOnlyList<DiscoveryRecord> discoveries)
        {
            string inPerson = JoinLabels(discoveries.Where(d => d.discoveredVia == RequestChannel.InPerson));
            string remote = JoinLabels(discoveries.Where(d => d.discoveredVia == RequestChannel.Remote));
            return (inPerson.NullOrEmpty() ? null : inPerson, remote.NullOrEmpty() ? null : remote);
        }
    }
}
