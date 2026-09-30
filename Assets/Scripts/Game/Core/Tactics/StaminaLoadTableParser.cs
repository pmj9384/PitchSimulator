using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Tactics
{
    // StaminaLoadTable.csv 텍스트 → 체력 부담 표. TeamTacticsParser와 같은 틀(CsvHelper 위임, 규칙 검증만 여기서).
    // 표는 조합(압박 시작 3구역 × 전진 정도)마다 정확히 한 칸이어야 한다. 빠진 칸은 화면에서 그 전술을 고르는 순간에야 드러나므로 읽을 때 막는다
    public static class StaminaLoadTableParser
    {
        private const int Combinations = TeamTactics.Levels * TeamTactics.Levels * TeamTactics.Levels * TeamTactics.Levels;

        public static List<StaminaLoadEntry> Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                throw new FormatException("StaminaLoadTable: 내용이 비어 있다");
            }

            var entries = new List<StaminaLoadEntry>();
            var seen = new HashSet<int>();

            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<EntryMap>();

                foreach (StaminaLoadEntry entry in csv.GetRecords<StaminaLoadEntry>())
                {
                    int line = csv.Parser.Row;
                    Level(entry.PressStartOwn, "pressStartOwn", line);
                    Level(entry.PressStartMid, "pressStartMid", line);
                    Level(entry.PressStartOpp, "pressStartOpp", line);
                    Level(entry.Mentality, "mentality", line);
                    if (entry.SlowedPercent < 0f)
                    {
                        throw new FormatException($"StaminaLoadTable {line}행: slowedPercent는 0 이상 ({entry.SlowedPercent})");
                    }
                    if (!seen.Add(Key(entry)))
                    {
                        throw new FormatException($"StaminaLoadTable {line}행: 같은 조합이 두 번 ({entry.PressStartOwn},{entry.PressStartMid},{entry.PressStartOpp},{entry.Mentality})");
                    }

                    entries.Add(entry);
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("StaminaLoadTable: 헤더가 스키마와 다르다. " + Data.CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"StaminaLoadTable {line}행: 값 형식 오류. " + Data.CsvParseHelper.FirstLine(ex.Message));
            }

            if (entries.Count != Combinations)
            {
                throw new FormatException($"StaminaLoadTable: {Combinations}칸이어야 한다 ({entries.Count}칸)");
            }

            return entries;
        }

        private static void Level(int v, string field, int line)
        {
            if (v < 0 || v >= TeamTactics.Levels)
            {
                throw new FormatException($"StaminaLoadTable {line}행: {field}는 0~{TeamTactics.Levels - 1} ({v})");
            }
        }

        // 조합 하나를 가리키는 번호(3진수 4자리)
        private static int Key(StaminaLoadEntry e)
        {
            int n = TeamTactics.Levels;
            return ((e.PressStartOwn * n + e.PressStartMid) * n + e.PressStartOpp) * n + e.Mentality;
        }

        private sealed class EntryMap : ClassMap<StaminaLoadEntry>
        {
            public EntryMap()
            {
                Map(e => e.PressStartOwn).Name("pressStartOwn");
                Map(e => e.PressStartMid).Name("pressStartMid");
                Map(e => e.PressStartOpp).Name("pressStartOpp");
                Map(e => e.Mentality).Name("mentality");
                Map(e => e.SlowedPercent).Name("slowedPercent");
            }
        }
    }
}
