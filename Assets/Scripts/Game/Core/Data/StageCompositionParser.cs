using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Data
{
    // StageComposition.csv 텍스트 ↔ 행 목록. PlayerTableParser와 같은 틀(파싱은 CsvHelper, 규칙 검증만 여기서).
    // 쓰기(Serialize)도 여기 두는 이유: 플레이어 세팅 저장이 같은 형식이라 읽기·쓰기가 한 스키마를 봐야 어긋나지 않는다.
    public static class StageCompositionParser
    {
        public static List<StageEntry> Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                throw new FormatException("StageComposition: 내용이 비어 있다");
            }

            var entries = new List<StageEntry>();

            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<StageEntryMap>();

                foreach (StageEntry row in csv.GetRecords<StageEntry>())
                {
                    int line = csv.Parser.Row;
                    ValidateRow(row, line);
                    entries.Add(row);
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("StageComposition: 헤더가 스키마와 다르다. " + CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"StageComposition {line}행: 값 형식 오류. " + CsvParseHelper.FirstLine(ex.Message));
            }

            return entries;   // 빈 파일(헤더만)은 허용. "아직 세팅 없음"이 정상 상태다(세팅 저장 초기값)
        }

        // 행 목록 → CSV 텍스트. 헤더 포함, 줄바꿈은 \n 고정(플랫폼 따라 파일이 달라지면 저장본 비교가 깨진다).
        public static string Serialize(IEnumerable<StageEntry> entries)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using var writer = new StringWriter();
            using var csv = new CsvWriter(writer, config);
            csv.Context.RegisterClassMap<StageEntryMap>();
            csv.WriteRecords(entries);
            return writer.ToString();
        }

        private static void ValidateRow(StageEntry e, int line)
        {
            if (e.Stage < 1)
            {
                throw new FormatException($"StageComposition {line}행: stage는 1 이상 ({e.Stage})");
            }
            if (e.Side != StageEntry.SidePlayer && e.Side != StageEntry.SideEnemy)
            {
                throw new FormatException($"StageComposition {line}행: side는 player/enemy ({e.Side})");
            }
            if (e.Kind != StageEntry.KindPlayer)
            {
                throw new FormatException($"StageComposition {line}행: kind는 player ({e.Kind})");
            }
            if (string.IsNullOrWhiteSpace(e.Id))
            {
                throw new FormatException($"StageComposition {line}행: id가 비어 있다");
            }
            if (e.Count < 1)
            {
                throw new FormatException($"StageComposition {line}행: count는 1 이상 ({e.Count})");
            }
        }


        private sealed class StageEntryMap : ClassMap<StageEntry>
        {
            public StageEntryMap()
            {
                Map(e => e.Stage).Name("stage");
                Map(e => e.Side).Name("side");
                Map(e => e.Kind).Name("kind");
                Map(e => e.Id).Name("id");
                Map(e => e.Count).Name("count");
                Map(e => e.PosX).Name("posX");
                Map(e => e.PosZ).Name("posZ");
            }
        }
    }
}
