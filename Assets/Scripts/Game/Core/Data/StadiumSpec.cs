using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Data
{
    // 경기장 관중석 면(경기장 중앙 기준). 먼 쪽 = 중계 카메라 맞은편(+Z), 가까운 쪽 = 카메라 쪽(−Z), 골대 뒤 = ±X
    public enum StandSide { Far, Near, EndLeft, EndRight }

    // Stadiums.csv 한 행 = 부 하나의 경기장(11-06 경기장 4종 1단계, 설계 실험/2026-11-05-설계-경기장-4종.md §1).
    // 화면 전용이다. 시뮬은 경기장을 읽지 않는다. 좌석 슬롯 좌표는 StadiumLayout, 관중 채움은 StadiumRules
    public sealed class StadiumSpec
    {
        public string Id { get; set; } = string.Empty;
        public int Tier { get; set; }
        public IReadOnlyList<StandSide> Stands { get; set; } = Array.Empty<StandSide>();
        public int Rows { get; set; }                // 관중석 단 수
        public float RowDepth { get; set; }          // 단 깊이(m)
        public float RowHeight { get; set; }         // 단 높이(m)
        public float BaseHeight { get; set; }        // 첫 단 아래 기단 높이(m)
        public IReadOnlyList<StandSide> Roof { get; set; } = Array.Empty<StandSide>();       // 지붕을 덮는 면
        public int LightTowers { get; set; }         // 코너 조명탑 수
        public int Scoreboards { get; set; }         // 전광판 수
        public IReadOnlyList<StandSide> AdBoards { get; set; } = Array.Empty<StandSide>();   // 광고판을 두는 터치·골라인 쪽
        public int FillPercent { get; set; }         // 관중 채움 비율(%). 좌석 슬롯마다 시드 해시로 자른다
        public float Wear { get; set; }              // 잔디 마모 0(새 잔디)~1
        public float Stripe { get; set; }            // 잔디 깎기 줄무늬 세기 0(없음)~1(선명). 마모와 따로 둔다(설계 §1 표의 별도 행, 11-06 리뷰)
        public float Runoff { get; set; }            // 선 바깥 잔디 폭(m). 관중석은 그 바깥에서 시작한다
    }

    public static class StadiumSpecParser
    {
        // 부 번호 오름차순(1부 → 4부). 1~4가 전부 있어야 한다
        public static List<StadiumSpec> Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText)) { throw new FormatException("Stadiums: 내용이 비어 있다"); }

            var specs = new List<StadiumSpec>();
            var tiers = new HashSet<int>();
            var ids = new HashSet<string>();
            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<RowMap>();
                foreach (Row row in csv.GetRecords<Row>())
                {
                    int line = csv.Parser.Row;
                    specs.Add(ToSpec(row, line, tiers, ids));
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("Stadiums: 헤더가 스키마와 다르다. " + CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"Stadiums {line}행: 값 형식 오류. " + CsvParseHelper.FirstLine(ex.Message));
            }
            if (specs.Count != 4) { throw new FormatException($"Stadiums: 1~4부가 전부 있어야 한다 ({specs.Count}개)"); }
            specs.Sort(CompareByTier);
            return specs;
        }

        // id로 찾는다. TierRule.Stadium이 가리키는 행이 없으면 데이터 오류다
        public static StadiumSpec Find(IReadOnlyList<StadiumSpec> specs, string id)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Id == id) { return specs[i]; }
            }
            throw new InvalidOperationException($"[Stadiums] id {id} 없음");
        }

        private static int CompareByTier(StadiumSpec a, StadiumSpec b)
        {
            return a.Tier.CompareTo(b.Tier);
        }

        private static StadiumSpec ToSpec(Row row, int line, HashSet<int> tiers, HashSet<string> ids)
        {
            string id = (row.Id ?? string.Empty).Trim();
            if (id.Length == 0) { throw new FormatException($"Stadiums {line}행: id가 비어 있다"); }
            if (!ids.Add(id)) { throw new FormatException($"Stadiums {line}행: id 중복 ({id})"); }
            if (row.Tier < 1 || row.Tier > 4) { throw new FormatException($"Stadiums {line}행: tier는 1~4 ({row.Tier})"); }
            if (!tiers.Add(row.Tier)) { throw new FormatException($"Stadiums {line}행: tier 중복 ({row.Tier})"); }
            List<StandSide> stands = SplitSides(row.Stands, "stands", line);
            if (stands.Count == 0) { throw new FormatException($"Stadiums {line}행: stands가 비어 있다"); }
            if (row.Rows < 1) { throw new FormatException($"Stadiums {line}행: rows는 1 이상"); }
            if (row.RowDepth <= 0f) { throw new FormatException($"Stadiums {line}행: rowDepth는 양수"); }
            if (row.RowHeight <= 0f) { throw new FormatException($"Stadiums {line}행: rowHeight는 양수"); }
            if (row.BaseHeight < 0f) { throw new FormatException($"Stadiums {line}행: baseHeight는 0 이상"); }
            if (row.FillPercent < 0 || row.FillPercent > 100) { throw new FormatException($"Stadiums {line}행: fillPercent는 0~100"); }
            if (row.Wear < 0f || row.Wear > 1f) { throw new FormatException($"Stadiums {line}행: wear는 0~1"); }
            if (row.Stripe < 0f || row.Stripe > 1f) { throw new FormatException($"Stadiums {line}행: stripe는 0~1"); }
            if (row.Runoff < 0f) { throw new FormatException($"Stadiums {line}행: runoff는 0 이상"); }
            return new StadiumSpec
            {
                Id = id,
                Tier = row.Tier,
                Stands = stands,
                Rows = row.Rows,
                RowDepth = row.RowDepth,
                RowHeight = row.RowHeight,
                BaseHeight = row.BaseHeight,
                Roof = SplitSides(row.Roof, "roof", line),
                LightTowers = row.LightTowers,
                Scoreboards = row.Scoreboards,
                AdBoards = SplitSides(row.AdBoards, "adBoards", line),
                FillPercent = row.FillPercent,
                Wear = row.Wear,
                Stripe = row.Stripe,
                Runoff = row.Runoff,
            };
        }

        // "far|near" → 면 목록. 빈 칸은 빈 목록(지붕·광고판 없음). 같은 면 중복은 오류
        private static List<StandSide> SplitSides(string raw, string field, int line)
        {
            var sides = new List<StandSide>();
            foreach (string part in (raw ?? string.Empty).Split(TierRuleParser.ListSeparator))
            {
                string v = part.Trim();
                if (v.Length == 0) { continue; }
                StandSide side = ParseSide(v, field, line);
                if (sides.Contains(side)) { throw new FormatException($"Stadiums {line}행: {field}에 {v} 중복"); }
                sides.Add(side);
            }
            return sides;
        }

        private static StandSide ParseSide(string v, string field, int line)
        {
            switch (v)
            {
                case "far": return StandSide.Far;
                case "near": return StandSide.Near;
                case "endLeft": return StandSide.EndLeft;
                case "endRight": return StandSide.EndRight;
            }
            throw new FormatException($"Stadiums {line}행: {field}의 면 이름은 far·near·endLeft·endRight ({v})");
        }

        private sealed class Row
        {
            public string Id { get; set; } = string.Empty;
            public int Tier { get; set; }
            public string Stands { get; set; } = string.Empty;
            public int Rows { get; set; }
            public float RowDepth { get; set; }
            public float RowHeight { get; set; }
            public float BaseHeight { get; set; }
            public string Roof { get; set; } = string.Empty;
            public int LightTowers { get; set; }
            public int Scoreboards { get; set; }
            public string AdBoards { get; set; } = string.Empty;
            public int FillPercent { get; set; }
            public float Wear { get; set; }
            public float Stripe { get; set; }
            public float Runoff { get; set; }
        }

        private sealed class RowMap : ClassMap<Row>
        {
            public RowMap()
            {
                Map(r => r.Id).Name("id");
                Map(r => r.Tier).Name("tier");
                Map(r => r.Stands).Name("stands");
                Map(r => r.Rows).Name("rows");
                Map(r => r.RowDepth).Name("rowDepth");
                Map(r => r.RowHeight).Name("rowHeight");
                Map(r => r.BaseHeight).Name("baseHeight");
                Map(r => r.Roof).Name("roof");
                Map(r => r.LightTowers).Name("lightTowers");
                Map(r => r.Scoreboards).Name("scoreboards");
                Map(r => r.AdBoards).Name("adBoards");
                Map(r => r.FillPercent).Name("fillPercent");
                Map(r => r.Wear).Name("wear");
                Map(r => r.Stripe).Name("stripe");
                Map(r => r.Runoff).Name("runoff");
            }
        }
    }
}
