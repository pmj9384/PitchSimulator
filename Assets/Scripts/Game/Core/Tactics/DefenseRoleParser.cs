using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Tactics
{
    // DefenseRoles.csv → DefenseRole 목록. CsvHelper + ClassMap(프로젝트 관례). id 중복·기본 역할 없음·범위 밖은 던진다(파서가 데이터를 믿지 않는다)
    public static class DefenseRoleParser
    {
        public static List<DefenseRole> Parse(string csvText)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { TrimOptions = TrimOptions.Trim };
            using var reader = new StringReader(csvText);
            using var csv = new CsvReader(reader, config);
            csv.Context.RegisterClassMap<RowMap>();

            var roles = new List<DefenseRole>();
            var ids = new HashSet<string>();
            int line = 1;
            foreach (Row r in csv.GetRecords<Row>())
            {
                line++;
                if (!ids.Add(r.DefenseRoleId)) { throw new FormatException($"DefenseRoles {line}행: id 중복 ({r.DefenseRoleId})"); }
                roles.Add(new DefenseRole(r.DefenseRoleId, r.DisplayName, r.Description, r.PressRangeScale, r.LineHeightOffset, r.RoamRadiusScale));
            }
            if (!ids.Contains(DefenseRole.DefaultId)) { throw new FormatException($"DefenseRoles: 기본 역할 '{DefenseRole.DefaultId}' 행이 없다"); }
            return roles;
        }

        public static DefenseRole? Find(IReadOnlyList<DefenseRole> roles, string id)
        {
            for (int i = 0; i < roles.Count; i++)
            {
                if (string.Equals(roles[i].Id, id, StringComparison.OrdinalIgnoreCase)) { return roles[i]; }
            }
            return null;
        }

        private sealed class Row
        {
            public string DefenseRoleId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public float PressRangeScale { get; set; }
            public float LineHeightOffset { get; set; }
            public float RoamRadiusScale { get; set; }
        }

        private sealed class RowMap : ClassMap<Row>
        {
            public RowMap()
            {
                Map(r => r.DefenseRoleId).Name("defenseRoleId");
                Map(r => r.DisplayName).Name("displayName");
                Map(r => r.Description).Name("description");
                Map(r => r.PressRangeScale).Name("pressRangeScale");
                Map(r => r.LineHeightOffset).Name("lineHeightOffset");
                Map(r => r.RoamRadiusScale).Name("roamRadiusScale");
            }
        }
    }
}
