using System;
using System.Collections.Generic;

namespace Game.Core.Tactics
{
    // 슬라이더 3개. 각자 맡은 칸이 있다(목업 힌트 그대로): 앞으로 = 전진 정도 + 폭 + 패스 리스크, 빨리 = 속도, 압박 = 압박 시작
    public enum TacticSlider { Forward, Speed, Press }

    // 팀 전술 설정창(09-30)의 순수 판정. 입구 3단 = 카드 → 슬라이더 → 세부 표(스펙 §4-2, 목업 "전술 지도와 설정창" 탭 2).
    // 슬라이더는 칸 값을 한 값으로 덮지 않고 카드 값에 오프셋을 더한다(09-30 유저 결정): 카드가 가진 서드별 모양(예: 빌드업의 우리 진영 짧게·상대 진영 직접)을 지킨다.
    // "얼마나 압박"의 "역할별 압박 거리"(목업)는 개인 다이얼이라 개인 전술창 몫
    public static class TacticsEditing
    {
        public const int SliderMin = -2;
        public const int SliderMax = 2;   // 5칸. 폭은 문서에 수치가 없어 09-30 확정 스펙 표의 값. 칸 값 정의역(0~2) 전체를 한쪽 끝까지 밀 수 있는 폭

        // 슬라이더 하나가 자기 담당 칸만 다시 쓴다: 담당 칸 = 기준 카드 값 + 오프셋, 나머지는 지금 값(current) 그대로.
        // 카드를 통째로 복사해 다시 계산하면 슬라이더와 상관없는 칸(패스 방식·전환)과 다른 슬라이더의 칸까지 표에서 바꾼 값이 사라진다(09-30 재검증)
        public static TeamTactics ApplySlider(TeamTactics current, TeamTactics card, TacticSlider slider, int offset)
        {
            CheckSlider(offset, nameof(offset));

            TeamTactics t = Copy(current);
            switch (slider)
            {
                case TacticSlider.Forward:
                    t.Mentality = Shift(card.Mentality, offset);
                    ShiftFrom(card.Width, t.Width, offset);
                    ShiftFrom(card.PassRisk, t.PassRisk, offset);
                    break;
                case TacticSlider.Speed:
                    ShiftFrom(card.Tempo, t.Tempo, offset);
                    break;
                case TacticSlider.Press:
                    ShiftFrom(card.PressStart, t.PressStart, offset);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slider), slider, "없는 슬라이더");
            }
            return t;
        }

        // 카드 값과 한 칸도 다르지 않으면 그 카드. 아니면 null = "사용자 설정"(목업 규칙)
        public static string? MatchingPreset(TeamTactics tactics, IReadOnlyList<TeamTactics> presets)
        {
            for (int i = 0; i < presets.Count; i++)
            {
                if (SameValues(tactics, presets[i])) { return presets[i].PresetId; }
            }
            return null;
        }

        // 판정에 쓰이는 값만 비교한다(이름·설명은 보지 않는다)
        public static bool SameValues(TeamTactics a, TeamTactics b)
        {
            if (!SameThirds(a.PassStyle, b.PassStyle)) { return false; }
            if (!SameThirds(a.PassRisk, b.PassRisk)) { return false; }
            if (!SameThirds(a.Tempo, b.Tempo)) { return false; }
            if (!SameThirds(a.Width, b.Width)) { return false; }
            if (!SameThirds(a.PressStart, b.PressStart)) { return false; }
            if (a.Counter != b.Counter) { return false; }
            if (a.CounterPress != b.CounterPress) { return false; }
            if (a.GkDistribution != b.GkDistribution) { return false; }
            return a.Mentality == b.Mentality;
        }

        // 세이브·표 입력의 관문. 파서(TeamTacticsParser)와 같은 정의역(0~2)과 서드 3칸
        public static void Validate(TeamTactics t)
        {
            CheckThirds(t.PassStyle, "passStyle");
            CheckThirds(t.PassRisk, "passRisk");
            CheckThirds(t.Tempo, "tempo");
            CheckThirds(t.Width, "width");
            CheckThirds(t.PressStart, "pressStart");
            CheckLevel(t.Counter, "counter");
            CheckLevel(t.CounterPress, "counterPress");
            CheckLevel(t.GkDistribution, "gkDistribution");
            CheckLevel(t.Mentality, "mentality");
        }

        public static TeamTactics Copy(TeamTactics src)
        {
            return new TeamTactics
            {
                PresetId = src.PresetId,
                DisplayName = src.DisplayName,
                Description = src.Description,
                PassStyle = (int[])src.PassStyle.Clone(),
                PassRisk = (int[])src.PassRisk.Clone(),
                Tempo = (int[])src.Tempo.Clone(),
                Width = (int[])src.Width.Clone(),
                PressStart = (int[])src.PressStart.Clone(),
                Counter = src.Counter,
                CounterPress = src.CounterPress,
                GkDistribution = src.GkDistribution,
                Mentality = src.Mentality,
            };
        }

        private static int Shift(int level, int offset)
        {
            return Math.Clamp(level + offset, 0, TeamTactics.Levels - 1);
        }

        private static void ShiftFrom(int[] cardThirds, int[] target, int offset)
        {
            for (int i = 0; i < target.Length; i++) { target[i] = Shift(cardThirds[i], offset); }
        }

        private static bool SameThirds(int[] a, int[] b)
        {
            if (a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) { return false; }
            }
            return true;
        }

        private static void CheckSlider(int value, string name)
        {
            if (value < SliderMin || value > SliderMax) { throw new ArgumentOutOfRangeException(name, value, $"슬라이더는 {SliderMin}~{SliderMax}"); }
        }

        private static void CheckThirds(int[] thirds, string field)
        {
            if (thirds == null || thirds.Length != 3) { throw new InvalidOperationException($"[TacticsEditing] {field}는 서드 3칸이어야 한다"); }
            for (int i = 0; i < thirds.Length; i++) { CheckLevel(thirds[i], field); }
        }

        private static void CheckLevel(int value, string field)
        {
            if (value < 0 || value >= TeamTactics.Levels) { throw new InvalidOperationException($"[TacticsEditing] {field}는 0~{TeamTactics.Levels - 1} ({value})"); }
        }
    }
}
