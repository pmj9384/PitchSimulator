using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Data
{
    // StageTable.csv 텍스트 → 스테이지 목록. PlayerTableParser와 같은 틀(CsvHelper 위임, 규칙 검증만 여기서).
    public static class StageTableParser
    {
        public static List<StageInfo> Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                throw new FormatException("StageTable: 내용이 비어 있다");
            }

            var stages = new List<StageInfo>();
            var seen = new HashSet<int>();

            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<StageInfoMap>();

                foreach (StageInfo row in csv.GetRecords<StageInfo>())
                {
                    int line = csv.Parser.Row;

                    if (row.Stage < 1)
                    {
                        throw new FormatException($"StageTable {line}행: stage는 1 이상 ({row.Stage})");
                    }
                    if (!seen.Add(row.Stage))
                    {
                        throw new FormatException($"StageTable {line}행: stage 중복 ({row.Stage})");
                    }

                    stages.Add(row);
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("StageTable: 헤더가 스키마와 다르다. " + CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"StageTable {line}행: 값 형식 오류. " + CsvParseHelper.FirstLine(ex.Message));
            }

            if (stages.Count == 0)
            {
                throw new FormatException("StageTable: 데이터 행이 없다 (헤더만 있음)");
            }

            return stages;
        }


        private sealed class StageInfoMap : ClassMap<StageInfo>
        {
            public StageInfoMap()
            {
                Map(s => s.Stage).Name("stage");
                Map(s => s.DisplayName).Name("displayName");
                Map(s => s.OpponentName).Name("opponentName");
            }
        }
    }
}
