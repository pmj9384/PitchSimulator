using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.Data
{
    // PlayerTable.csv 텍스트 → 역할 프리셋 목록. 순수 함수. 파일/Resources 접근은 호출측(PlayerTableRepository) 몫.
    // 파싱은 CsvHelper에 위임하고, 스키마 매핑과 게임 규칙 검증만 여기서 통제한다.
    // 실패는 전부 FormatException으로 감싼다. 호출측과 테스트가 CsvHelper 타입을 몰라도 되게.
    public static class PlayerTableParser
    {
        public static List<PlayerStats> Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                throw new FormatException("PlayerTable: 내용이 비어 있다");
            }

            var roles = new List<PlayerStats>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // 사람이 손으로 치는 값. ST/st를 같은 id로 본다

            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<PlayerStatsMap>();

                foreach (PlayerStats row in csv.GetRecords<PlayerStats>())
                {
                    // Parser.Row = 원본 파일 기준 행 번호(헤더 = 1행). 기획이 CSV에서 바로 찾아갈 수 있는 값
                    int line = csv.Parser.Row;

                    if (string.IsNullOrWhiteSpace(row.RoleId))
                    {
                        throw new FormatException($"PlayerTable {line}행: roleId가 비어 있다");
                    }
                    if (!seenIds.Add(row.RoleId))
                    {
                        throw new FormatException($"PlayerTable {line}행: roleId 중복 ({row.RoleId})");
                    }
                    ValidateRanges(row, line);

                    roles.Add(row);
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("PlayerTable: 헤더가 스키마와 다르다. " + CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"PlayerTable {line}행: 값 형식 오류. " + CsvParseHelper.FirstLine(ex.Message));
            }

            if (roles.Count == 0)
            {
                throw new FormatException("PlayerTable: 데이터 행이 없다 (헤더만 있음)");
            }

            return roles;
        }

        // 게임 규칙 검증. 총점이 어긋나면 "막히면 강화가 아니라 재세팅" 축이 무너지므로 로드 시점에 막는다
        private static void ValidateRanges(PlayerStats p, int line)
        {
            RequirePositive(p.Speed, "speed", line);
            RequirePositive(p.Stamina, "stamina", line);
            RequirePositive(p.Pass, "pass", line);
            RequirePositive(p.Shot, "shot", line);
            RequirePositive(p.Tackle, "tackle", line);
            RequirePositive(p.Positioning, "positioning", line);

            if (p.BuildTotal != PlayerStats.TotalPoints)
            {
                throw new FormatException($"PlayerTable {line}행: 빌드 합계는 {PlayerStats.TotalPoints} ({p.BuildTotal})");
            }
            if (p.PressRange <= 0f)
            {
                throw new FormatException($"PlayerTable {line}행: pressRange는 양수 ({p.PressRange})");
            }
            if (p.ShotBias < 0f || p.ShotBias > 1f)
            {
                throw new FormatException($"PlayerTable {line}행: shotBias는 0~1 ({p.ShotBias})");
            }
            if (p.PassLength <= 0f)
            {
                throw new FormatException($"PlayerTable {line}행: passLength는 양수 ({p.PassLength})");
            }
            if (p.PushUp < 0f || p.Width < 0f || p.LineHeight < 0f)
            {
                throw new FormatException($"PlayerTable {line}행: pushUp·width·lineHeight는 0 이상");
            }
        }

        private static void RequirePositive(int value, string field, int line)
        {
            if (value < 1)
            {
                throw new FormatException($"PlayerTable {line}행: {field}는 1 이상 ({value})");
            }
        }


        // CSV 헤더(camelCase) ↔ C# 프로퍼티(PascalCase) 명시 매핑. 자동 추론에 안 맡긴다
        private sealed class PlayerStatsMap : ClassMap<PlayerStats>
        {
            public PlayerStatsMap()
            {
                Map(p => p.RoleId).Name("roleId");
                Map(p => p.Speed).Name("speed");
                Map(p => p.Stamina).Name("stamina");
                Map(p => p.Pass).Name("pass");
                Map(p => p.Shot).Name("shot");
                Map(p => p.Tackle).Name("tackle");
                Map(p => p.Positioning).Name("positioning");
                Map(p => p.PushUp).Name("pushUp");
                Map(p => p.PressRange).Name("pressRange");
                Map(p => p.ShotBias).Name("shotBias");
                Map(p => p.PassLength).Name("passLength");
                Map(p => p.Width).Name("width");
                Map(p => p.LineHeight).Name("lineHeight");
                Map(p => p.DisplayName).Name("displayName");
                Map(p => p.Description).Name("description");
                Map(p => p.Icon).Name("icon");
            }
        }
    }
}
