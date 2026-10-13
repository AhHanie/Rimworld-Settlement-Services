using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class BoardRolePattern
    {
        public BoardRoleKind kind = BoardRoleKind.Crew;
        public float weight = 1f;
        public int minPartySize = 1;
        public int maxPartySize = 4;
        public List<BoardSkillRoleTemplate> skills = new List<BoardSkillRoleTemplate>();

        public IEnumerable<string> ConfigErrors(string owner)
        {
            if (weight < 0f) yield return $"{owner}: pattern weight must be >= 0.";
            if (minPartySize < 1) yield return $"{owner}: pattern minPartySize must be >= 1.";
            if (maxPartySize < minPartySize) yield return $"{owner}: pattern maxPartySize must be >= minPartySize.";
            if (skills.NullOrEmpty())
            {
                yield return $"{owner}: pattern has no skills.";
                yield break;
            }

            foreach (BoardSkillRoleTemplate template in skills)
            {
                if (template?.skill == null) yield return $"{owner}: pattern has a skill entry that does not resolve to a SkillDef.";
                else if (template.weight <= 0f) yield return $"{owner}: skill '{template.skill.defName}' has a non-positive weight.";
            }

            if (kind == BoardRoleKind.Mixed && skills.Count < 2)
                yield return $"{owner}: a Mixed pattern needs at least two skills.";
        }
    }
}
