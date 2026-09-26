using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Data
{
    // TeamNames.csv(kind,value) → 도시 목록 + 접미 목록. 팀 이름 = 도시 + 접미(스펙 §10 "도시명 + 시민축구단/FC"). 생성기가 시드로 조합
    public sealed class TeamNameTable
    {
        public IReadOnlyList<string> Cities { get; }
        public IReadOnlyList<string> Suffixes { get; }

        private TeamNameTable(List<string> cities, List<string> suffixes)
        {
            Cities = cities;
            Suffixes = suffixes;
        }

        public string Compose(int cityIndex, int suffixIndex)
        {
            return Cities[cityIndex % Cities.Count] + " " + Suffixes[suffixIndex % Suffixes.Count];
        }

        public static TeamNameTable Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText)) { throw new FormatException("TeamNames: 내용이 비어 있다"); }

            var cities = new List<string>();
            var suffixes = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<RowMap>();
                foreach (Row row in csv.GetRecords<Row>())
                {
                    int line = csv.Parser.Row;
                    if (string.IsNullOrWhiteSpace(row.Value)) { throw new FormatException($"TeamNames {line}행: value가 비어 있다"); }
                    if (!seen.Add(row.Kind + ":" + row.Value)) { throw new FormatException($"TeamNames {line}행: 중복 ({row.Kind} {row.Value})"); }
                    if (string.Equals(row.Kind, "city", StringComparison.OrdinalIgnoreCase)) { cities.Add(row.Value); }
                    else if (string.Equals(row.Kind, "suffix", StringComparison.OrdinalIgnoreCase)) { suffixes.Add(row.Value); }
                    else { throw new FormatException($"TeamNames {line}행: kind는 city/suffix ({row.Kind})"); }
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("TeamNames: 헤더가 스키마와 다르다. " + CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"TeamNames {line}행: 값 형식 오류. " + CsvParseHelper.FirstLine(ex.Message));
            }
            if (cities.Count == 0 || suffixes.Count == 0) { throw new FormatException("TeamNames: city와 suffix가 1개 이상씩 있어야 한다"); }
            return new TeamNameTable(cities, suffixes);
        }

        private sealed class Row
        {
            public string Kind { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private sealed class RowMap : ClassMap<Row>
        {
            public RowMap()
            {
                Map(r => r.Kind).Name("kind");
                Map(r => r.Value).Name("value");
            }
        }
    }
}
