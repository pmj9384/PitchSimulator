using System;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using Game.Core.Match;

namespace Game.Core.Data
{
    // ThreatTable.csv 텍스트 → xT 격자(11-03 ⑤ xT). 순수 함수. 파일/Resources 접근은 호출측 몫. 파싱은 CsvHelper + ClassMap에 위임하고 규칙 검증만 여기서 한다.
    // CSV 맨 위 '#' 줄은 생성 방법·판 수·커밋 해시 설명이다(AllowComments). 96칸(12×8)이 빠짐·중복 없이 다 있어야 한다
    public static class ThreatTableParser
    {
        public sealed class ThreatCell
        {
            public int Column { get; set; }
            public int Row { get; set; }
            public float Threat { get; set; }
        }

        public static ThreatGrid Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                throw new FormatException("ThreatTable: 내용이 비어 있다");
            }

            var cells = new float[ThreatGrid.Rows, ThreatGrid.Columns];
            var seen = new bool[ThreatGrid.Rows, ThreatGrid.Columns];
            int count = 0;
            try
            {
                using var reader = new StringReader(csvText);
                var config = new CsvConfiguration(CultureInfo.InvariantCulture) { AllowComments = true };
                using var csv = new CsvReader(reader, config);
                csv.Context.RegisterClassMap<CellMap>();

                foreach (ThreatCell cell in csv.GetRecords<ThreatCell>())
                {
                    int line = csv.Parser.Row;
                    if (cell.Column < 0 || cell.Column >= ThreatGrid.Columns) { throw new FormatException($"ThreatTable {line}행: column은 0~{ThreatGrid.Columns - 1} ({cell.Column})"); }
                    if (cell.Row < 0 || cell.Row >= ThreatGrid.Rows) { throw new FormatException($"ThreatTable {line}행: row는 0~{ThreatGrid.Rows - 1} ({cell.Row})"); }
                    if (float.IsNaN(cell.Threat) || cell.Threat < 0f || cell.Threat > 1f) { throw new FormatException($"ThreatTable {line}행: threat는 0~1 ({cell.Threat})"); }
                    if (seen[cell.Row, cell.Column]) { throw new FormatException($"ThreatTable {line}행: 같은 칸이 두 번 ({cell.Column},{cell.Row})"); }
                    seen[cell.Row, cell.Column] = true;
                    cells[cell.Row, cell.Column] = cell.Threat;
                    count++;
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("ThreatTable: 헤더가 스키마와 다르다. " + CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"ThreatTable {line}행: 값 형식 오류. " + CsvParseHelper.FirstLine(ex.Message));
            }

            if (count != ThreatGrid.Rows * ThreatGrid.Columns)
            {
                throw new FormatException($"ThreatTable: {ThreatGrid.Rows * ThreatGrid.Columns}칸이어야 한다 ({count}칸)");
            }
            return new ThreatGrid(cells);
        }

        private sealed class CellMap : ClassMap<ThreatCell>
        {
            public CellMap()
            {
                Map(c => c.Column).Name("column");
                Map(c => c.Row).Name("row");
                Map(c => c.Threat).Name("threat");
            }
        }
    }
}
