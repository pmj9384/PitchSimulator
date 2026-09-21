using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Tactics
{
    // TacticPresets.csv 텍스트 → 스타일 카드 목록. PlayerTableParser와 같은 틀(CsvHelper 위임, 규칙 검증만 여기서).
    // 서드별 값은 CSV 열 3개(passStyleOwn·passStyleMid·passStyleOpp)를 배열 하나로 접는다. 기획이 표로 읽기 쉽게.
    public static class TeamTacticsParser
    {
        public static List<TeamTactics> Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                throw new FormatException("TacticPresets: 내용이 비어 있다");
            }

            var presets = new List<TeamTactics>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<RowMap>();

                foreach (Row row in csv.GetRecords<Row>())
                {
                    int line = csv.Parser.Row;
                    if (string.IsNullOrWhiteSpace(row.PresetId))
                    {
                        throw new FormatException($"TacticPresets {line}행: presetId가 비어 있다");
                    }
                    if (!seen.Add(row.PresetId))
                    {
                        throw new FormatException($"TacticPresets {line}행: presetId 중복 ({row.PresetId})");
                    }

                    presets.Add(ToTactics(row, line));
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("TacticPresets: 헤더가 스키마와 다르다. " + Data.CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"TacticPresets {line}행: 값 형식 오류. " + Data.CsvParseHelper.FirstLine(ex.Message));
            }

            if (presets.Count == 0)
            {
                throw new FormatException("TacticPresets: 데이터 행이 없다 (헤더만 있음)");
            }

            return presets;
        }

        private static TeamTactics ToTactics(Row r, int line)
        {
            var t = new TeamTactics
            {
                PresetId = r.PresetId,
                DisplayName = r.DisplayName,
                Description = r.Description,
                PassStyle = Triple(r.PassStyleOwn, r.PassStyleMid, r.PassStyleOpp, "passStyle", line),
                PassRisk = Triple(r.PassRiskOwn, r.PassRiskMid, r.PassRiskOpp, "passRisk", line),
                Tempo = Triple(r.TempoOwn, r.TempoMid, r.TempoOpp, "tempo", line),
                Width = Triple(r.WidthOwn, r.WidthMid, r.WidthOpp, "width", line),
                PressStart = Triple(r.PressStartOwn, r.PressStartMid, r.PressStartOpp, "pressStart", line),
                Counter = Level(r.Counter, "counter", line),
                CounterPress = Level(r.CounterPress, "counterPress", line),
                GkDistribution = Level(r.GkDistribution, "gkDistribution", line),
                Mentality = Level(r.Mentality, "mentality", line),
            };
            return t;
        }

        private static int[] Triple(int own, int mid, int opp, string field, int line)
        {
            return new[] { Level(own, field + "Own", line), Level(mid, field + "Mid", line), Level(opp, field + "Opp", line) };
        }

        private static int Level(int v, string field, int line)
        {
            if (v < 0 || v >= TeamTactics.Levels)
            {
                throw new FormatException($"TacticPresets {line}행: {field}는 0~{TeamTactics.Levels - 1} ({v})");
            }
            return v;
        }

        // CSV 한 행의 평면 스키마. TeamTactics의 배열을 열 3개로 편 것
        private sealed class Row
        {
            public string PresetId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int PassStyleOwn { get; set; } public int PassStyleMid { get; set; } public int PassStyleOpp { get; set; }
            public int PassRiskOwn { get; set; } public int PassRiskMid { get; set; } public int PassRiskOpp { get; set; }
            public int TempoOwn { get; set; } public int TempoMid { get; set; } public int TempoOpp { get; set; }
            public int WidthOwn { get; set; } public int WidthMid { get; set; } public int WidthOpp { get; set; }
            public int PressStartOwn { get; set; } public int PressStartMid { get; set; } public int PressStartOpp { get; set; }
            public int Counter { get; set; } public int CounterPress { get; set; } public int GkDistribution { get; set; } public int Mentality { get; set; }
        }

        private sealed class RowMap : ClassMap<Row>
        {
            public RowMap()
            {
                Map(r => r.PresetId).Name("presetId");
                Map(r => r.DisplayName).Name("displayName");
                Map(r => r.Description).Name("description");
                Map(r => r.PassStyleOwn).Name("passStyleOwn"); Map(r => r.PassStyleMid).Name("passStyleMid"); Map(r => r.PassStyleOpp).Name("passStyleOpp");
                Map(r => r.PassRiskOwn).Name("passRiskOwn"); Map(r => r.PassRiskMid).Name("passRiskMid"); Map(r => r.PassRiskOpp).Name("passRiskOpp");
                Map(r => r.TempoOwn).Name("tempoOwn"); Map(r => r.TempoMid).Name("tempoMid"); Map(r => r.TempoOpp).Name("tempoOpp");
                Map(r => r.WidthOwn).Name("widthOwn"); Map(r => r.WidthMid).Name("widthMid"); Map(r => r.WidthOpp).Name("widthOpp");
                Map(r => r.PressStartOwn).Name("pressStartOwn"); Map(r => r.PressStartMid).Name("pressStartMid"); Map(r => r.PressStartOpp).Name("pressStartOpp");
                Map(r => r.Counter).Name("counter"); Map(r => r.CounterPress).Name("counterPress"); Map(r => r.GkDistribution).Name("gkDistribution"); Map(r => r.Mentality).Name("mentality");
            }
        }
    }
}
