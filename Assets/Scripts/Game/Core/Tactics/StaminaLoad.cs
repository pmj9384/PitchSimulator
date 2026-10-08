using System;
using System.Collections.Generic;

namespace Game.Core.Tactics
{
    // 체력 부담 표 한 칸: 이 압박 시작·전진 정도로 한 경기를 뛰면 필드 선수가 지쳐서 느려져 있는 시간이 몇 %인가(러너 실측)
    public sealed class StaminaLoadEntry
    {
        public int PressStartOwn { get; set; }
        public int PressStartMid { get; set; }
        public int PressStartOpp { get; set; }
        public int Mentality { get; set; }
        public float SlowedPercent { get; set; }
    }

    public enum StaminaLoadLevel { Low, Medium, High }

    // 체력 부담 게이지의 순수 판정(09-30, 유저 제안: "압박 강도가 너무 높아지면 그만큼 체력 부하가 걸리는 게이지". FM26 전술 화면의 Intensity 막대가 레퍼런스).
    // 값은 식이 아니라 실측 표(StaminaLoadTable.csv)에서 읽는다(유저 승인 방식 A). 체력 규칙(FatigueRules)이 선수 행동에 걸려 있어
    // 전술 값에서 식으로 뽑으면 실제와 어긋난다. 표는 기본 4-4-2·균형 카드에서 압박 시작 3구역과 전진 정도만 바꿔 칸마다 100판을 잰 것이다.
    // 다른 전술 항목(속도·패스 방식·폭·역습·역압박)은 체력에 거의 영향이 없어(09-30 계측 6.1~7.5%) 표에 없다.
    // 표가 엔진과 맞는지는 테스트가 다시 재서 본다(StaminaLoadTests)
    public static class StaminaLoad
    {
        public const int Segments = 8;          // 막대 칸 수. FM26 Intensity 막대와 같은 8칸
        // 단계 경계는 출처 없는 출발값이다. 3·6으로 두면 카드 4장 중 빌드업·균형·역습(6칸)이 "보통", 압박 축구(8칸)가 "높음"이고
        // 81칸이 낮음 25·보통 32·높음 24로 갈린다(09-30 표 기준). 3·5였을 땐 기본 카드 3장이 전부 "높음"이었다
        private const int LowMaxSegments = 3;
        private const int MediumMaxSegments = 6;

        // 내 전술에 해당하는 칸의 값. 표는 81칸 전부 있어야 한다(파서가 확인)
        public static float Find(IReadOnlyList<StaminaLoadEntry> table, TeamTactics tactics)
        {
            for (int i = 0; i < table.Count; i++)
            {
                StaminaLoadEntry e = table[i];
                if (e.PressStartOwn != tactics.PressStart[(int)Third.Own]) { continue; }
                if (e.PressStartMid != tactics.PressStart[(int)Third.Middle]) { continue; }
                if (e.PressStartOpp != tactics.PressStart[(int)Third.Opponent]) { continue; }
                if (e.Mentality != tactics.Mentality) { continue; }
                return e.SlowedPercent;
            }
            throw new InvalidOperationException($"[StaminaLoad] 표에 없는 조합: 압박 시작 {string.Join(",", tactics.PressStart)} · 전진 정도 {tactics.Mentality}");
        }

        // 표의 최솟값·최댓값. 막대의 양 끝이다
        public static (float min, float max) Range(IReadOnlyList<StaminaLoadEntry> table)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i].SlowedPercent < min) { min = table[i].SlowedPercent; }
                if (table[i].SlowedPercent > max) { max = table[i].SlowedPercent; }
            }
            return (min, max);
        }

        // 채울 칸 수(1~Segments). 표의 최소가 1칸, 최대가 8칸이고 그 사이는 고르게 나눈다
        public static int FilledSegments(float percent, float min, float max)
        {
            if (max <= min) { return 1; }
            float t = (percent - min) / (max - min);
            if (t < 0f) { t = 0f; }
            if (t > 1f) { t = 1f; }
            return 1 + (int)Math.Floor(t * (Segments - 1) + 0.5f);   // 반올림. Math.Round는 딱 절반에서 짝수 쪽으로 가서 칸이 들쭉날쭉해진다
        }

        // 내 전술의 칸 수. 표에서 값을 찾아 표의 범위에 놓는다(화면이 부르는 입구)
        public static int FilledSegments(IReadOnlyList<StaminaLoadEntry> table, TeamTactics tactics)
        {
            (float min, float max) range = Range(table);
            return FilledSegments(Find(table, tactics), range.min, range.max);
        }

        public static StaminaLoadLevel LevelOf(int filledSegments)
        {
            if (filledSegments <= LowMaxSegments) { return StaminaLoadLevel.Low; }
            if (filledSegments <= MediumMaxSegments) { return StaminaLoadLevel.Medium; }
            return StaminaLoadLevel.High;
        }
    }
}
