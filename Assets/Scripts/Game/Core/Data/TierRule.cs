using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Data
{
    // TierRules.csv 한 행 = 부 하나의 생성 규칙(스펙 §10 표). 팀 수·경기 수·총점·허용 포메이션·허용 프리셋·다이얼 변주 폭·자리 2쌍·빌드 재분배
    public sealed class TierRule
    {
        public int Tier { get; set; }
        public int Teams { get; set; }
        public int Matches { get; set; }
        public int TotalPoints { get; set; }
        public IReadOnlyList<string> Formations { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Presets { get; set; } = Array.Empty<string>();
        public int DialVariance { get; set; }     // 0 기본 / 1 다이얼 1~2개 극단 / 2 넓게
        public bool DualPositions { get; set; }   // 공격 시/수비 시 자리를 다르게 쓰는가
        public bool Rebuild { get; set; }         // 빌드 재분배(피지컬 CB·기술 ST)를 쓰는가
    }

    public static class TierRuleParser
    {
        public const char ListSeparator = '|';

        // 부 번호 오름차순(1부 → 4부)으로 돌려준다. 1~4 전부 있어야 한다
        public static List<TierRule> Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText)) { throw new FormatException("TierRules: 내용이 비어 있다"); }

            var rules = new List<TierRule>();
            var seen = new HashSet<int>();
            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<RowMap>();
                foreach (Row row in csv.GetRecords<Row>())
                {
                    int line = csv.Parser.Row;
                    if (row.Tier < 1 || row.Tier > 4) { throw new FormatException($"TierRules {line}행: tier는 1~4 ({row.Tier})"); }
                    if (!seen.Add(row.Tier)) { throw new FormatException($"TierRules {line}행: tier 중복 ({row.Tier})"); }
                    if (row.Teams < 3) { throw new FormatException($"TierRules {line}행: teams는 3 이상 ({row.Teams})"); }
                    if (row.Matches != row.Teams - 1) { throw new FormatException($"TierRules {line}행: 단판 라운드로빈이면 matches = teams − 1 ({row.Matches} ≠ {row.Teams - 1})"); }
                    if (row.TotalPoints <= 0) { throw new FormatException($"TierRules {line}행: totalPoints는 양수"); }
                    List<string> formations = SplitList(row.Formations, "formations", line);
                    List<string> presets = SplitList(row.Presets, "presets", line);
                    if (row.DialVariance < 0 || row.DialVariance > 2) { throw new FormatException($"TierRules {line}행: dialVariance는 0~2"); }
                    rules.Add(new TierRule
                    {
                        Tier = row.Tier, Teams = row.Teams, Matches = row.Matches, TotalPoints = row.TotalPoints,
                        Formations = formations, Presets = presets, DialVariance = row.DialVariance,
                        DualPositions = row.DualPositions != 0, Rebuild = row.Rebuild != 0,
                    });
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("TierRules: 헤더가 스키마와 다르다. " + CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"TierRules {line}행: 값 형식 오류. " + CsvParseHelper.FirstLine(ex.Message));
            }
            if (rules.Count != 4) { throw new FormatException($"TierRules: 1~4부가 전부 있어야 한다 ({rules.Count}개)"); }

            rules.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            for (int i = 1; i < rules.Count; i++)
            {
                if (rules[i].TotalPoints >= rules[i - 1].TotalPoints) { throw new FormatException($"TierRules: 총점은 상위 부가 커야 한다 ({rules[i].Tier}부 {rules[i].TotalPoints} ≥ {rules[i - 1].Tier}부 {rules[i - 1].TotalPoints})"); }
            }
            return rules;
        }

        private static List<string> SplitList(string raw, string field, int line)
        {
            var items = new List<string>();
            foreach (string part in (raw ?? string.Empty).Split(ListSeparator))
            {
                string v = part.Trim();
                if (v.Length > 0) { items.Add(v); }
            }
            if (items.Count == 0) { throw new FormatException($"TierRules {line}행: {field}가 비어 있다"); }
            return items;
        }

        private sealed class Row
        {
            public int Tier { get; set; }
            public int Teams { get; set; }
            public int Matches { get; set; }
            public int TotalPoints { get; set; }
            public string Formations { get; set; } = string.Empty;
            public string Presets { get; set; } = string.Empty;
            public int DialVariance { get; set; }
            public int DualPositions { get; set; }
            public int Rebuild { get; set; }
        }

        private sealed class RowMap : ClassMap<Row>
        {
            public RowMap()
            {
                Map(r => r.Tier).Name("tier");
                Map(r => r.Teams).Name("teams");
                Map(r => r.Matches).Name("matches");
                Map(r => r.TotalPoints).Name("totalPoints");
                Map(r => r.Formations).Name("formations");
                Map(r => r.Presets).Name("presets");
                Map(r => r.DialVariance).Name("dialVariance");
                Map(r => r.DualPositions).Name("dualPositions");
                Map(r => r.Rebuild).Name("rebuild");
            }
        }
    }
}
