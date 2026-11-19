using System;
using System.Collections.Generic;
using Game.Core.Match;

namespace Game.Core.Viewing
{
    // 사건 목록 + 보기 모드 → 보여 줄 구간 목록(11-06 S4 설계 4절). 순수 함수. 새 판정은 없고 시뮬이 낸 슛·소유 사건만 읽는다.
    // 구간은 정렬돼 있고 겹치지 않는다. 전체 경기 모드는 [0, totalTicks) 하나
    public static class HighlightRules
    {
        // 화면 1초(×1 배속)가 몇 틱인가 = 고정 스텝의 역수(50). ViewingTuning의 초를 이 값으로 바꾼다.
        // 경기 시계 기준(TotalTicks ÷ 표시 초)이던 것을 11-19에 바꿨다: 3분 압축에서 45초가 화면 1.5초로 줄었다. 실제 90분에서는 두 값이 같다
        public static float ScreenTicksPerSecond => 1f / MatchTuning.FixedStep;

        public static IReadOnlyList<TickRange> Build(IReadOnlyList<MatchEvent> events, ViewingMode mode, int totalTicks, float ticksPerSecond)
        {
            var ranges = new List<TickRange>();
            if (mode == ViewingMode.Full)
            {
                ranges.Add(new TickRange(0, totalTicks));
                return ranges;
            }

            List<int> chosen = ChooseShots(events, mode);
            if (chosen.Count == 0)
            {
                AddEmptyMatchRanges(ranges, totalTicks, ticksPerSecond);
                return ranges;
            }

            var raw = new List<TickRange>();
            for (int i = 0; i < chosen.Count; i++)
            {
                raw.Add(RangeOf(events, chosen[i], totalTicks, ticksPerSecond));
            }
            raw.Sort(CompareByStart);
            Merge(raw, ranges, SecondsToTicks(ViewingTuning.MergeGapSec, ticksPerSecond));
            return ranges;
        }

        // 보여 줄 슛의 인덱스(사건 목록 기준). 모드별 기준은 설계 4-2
        public static List<int> ChooseShots(IReadOnlyList<MatchEvent> events, ViewingMode mode)
        {
            var chosen = new List<int>();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind != MatchEventKind.Shot) { continue; }
                if (IsKeyShot(events[i]))
                {
                    chosen.Add(i);
                }
            }
            FillKeyMinimum(events, chosen);
            if (mode == ViewingMode.Key) { return chosen; }

            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind != MatchEventKind.Shot) { continue; }
                if (chosen.Contains(i)) { continue; }
                if (IsExtendedShot(events[i]))
                {
                    chosen.Add(i);
                }
            }
            return chosen;
        }

        private static bool IsKeyShot(MatchEvent shot)
        {
            if (shot.IsGoal) { return true; }
            return shot.Probability >= ViewingTuning.BigChanceP;
        }

        private static bool IsExtendedShot(MatchEvent shot)
        {
            if (shot.IsOnTarget) { return true; }
            if (shot.Outcome != ShotOutcome.Blocked) { return false; }
            return shot.Probability >= ViewingTuning.DangerousBlockP;
        }

        // 주요 장면이 하한보다 적으면 남은 슛을 p 높은 순으로 채운다. p가 같으면 먼저 찬 슛(정렬을 결정적으로)
        private static void FillKeyMinimum(IReadOnlyList<MatchEvent> events, List<int> chosen)
        {
            while (chosen.Count < ViewingTuning.KeyMinimumShots)
            {
                int best = -1;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Kind != MatchEventKind.Shot) { continue; }
                    if (chosen.Contains(i)) { continue; }
                    if (best < 0 || events[i].Probability > events[best].Probability)
                    {
                        best = i;
                    }
                }
                if (best < 0) { return; }
                chosen.Add(best);
            }
        }

        // 슛 하나의 구간: 시작 = 그 팀이 공을 얻은 마지막 틱 − 여유(찬 순간 기준 8~45초로 자름), 끝 = 결과 뒤 결과별 여유
        private static TickRange RangeOf(IReadOnlyList<MatchEvent> events, int shotIndex, int totalTicks, float ticksPerSecond)
        {
            MatchEvent shot = events[shotIndex];
            int latestStart = shot.Tick - SecondsToTicks(ViewingTuning.MinLeadSec, ticksPerSecond);
            int earliestStart = shot.Tick - SecondsToTicks(ViewingTuning.MaxLeadSec, ticksPerSecond);
            int start = earliestStart;
            int wonTick = BuildUpStartTick(events, shotIndex);
            if (wonTick >= 0)
            {
                start = wonTick - SecondsToTicks(ViewingTuning.BuildUpLeadSec, ticksPerSecond);
            }
            start = Math.Clamp(start, earliestStart, latestStart);
            int end = shot.ResolveTick + SecondsToTicks(AfterSeconds(shot.Outcome), ticksPerSecond);
            start = Math.Clamp(start, 0, totalTicks);
            end = Math.Clamp(end, start, totalTicks);
            return new TickRange(start, end);
        }

        // 슛 직전, 슛 팀이 패스 받기가 아닌 방법으로 공을 얻은 마지막 틱. 앞 슛(어느 팀이든)보다 이전이면 그 공격의 시작이 아니라고 보고 -1.
        // 킥오프·골킥처럼 소유 사건 없이 시작한 공격도 -1이 되어 최대 여유(화면 45초)로 시작한다
        private static int BuildUpStartTick(IReadOnlyList<MatchEvent> events, int shotIndex)
        {
            MatchEvent shot = events[shotIndex];
            for (int i = shotIndex - 1; i >= 0; i--)
            {
                MatchEvent candidate = events[i];
                if (candidate.Kind == MatchEventKind.Shot) { return -1; }
                if (candidate.Team == shot.Team) { return candidate.Tick; }
            }
            return -1;
        }

        private static float AfterSeconds(ShotOutcome outcome)
        {
            switch (outcome)
            {
                case ShotOutcome.Goal:
                    return ViewingTuning.AfterGoalSec;
                case ShotOutcome.Caught:
                case ShotOutcome.Parried:
                    return ViewingTuning.AfterSaveSec;
                case ShotOutcome.Blocked:
                    return ViewingTuning.AfterBlockSec;
                default:
                    return ViewingTuning.AfterMissSec;
            }
        }

        private static void Merge(List<TickRange> sorted, List<TickRange> result, int mergeGapTicks)
        {
            TickRange current = sorted[0];
            for (int i = 1; i < sorted.Count; i++)
            {
                TickRange next = sorted[i];
                if (next.Start - current.End <= mergeGapTicks)
                {
                    current = new TickRange(current.Start, Math.Max(current.End, next.End));
                    continue;
                }
                result.Add(current);
                current = next;
            }
            result.Add(current);
        }

        // 고를 슛이 없는 경기(0:0, 슛 0)도 빈 화면이 아니라 킥오프와 종료 장면을 보여 준다 [가정]
        private static void AddEmptyMatchRanges(List<TickRange> ranges, int totalTicks, float ticksPerSecond)
        {
            int length = Math.Min(SecondsToTicks(ViewingTuning.EmptyMatchSec, ticksPerSecond), totalTicks);
            int endStart = totalTicks - length;
            if (endStart <= length)
            {
                ranges.Add(new TickRange(0, totalTicks));
                return;
            }
            ranges.Add(new TickRange(0, length));
            ranges.Add(new TickRange(endStart, totalTicks));
        }

        private static int CompareByStart(TickRange a, TickRange b)
        {
            return a.Start.CompareTo(b.Start);
        }

        // 1틱 미만으로 줄지 않게 올림(테스트처럼 1초가 몇 틱 안 되는 값을 넘겨도 여유가 0이 되지 않는다)
        private static int SecondsToTicks(float seconds, float ticksPerSecond)
        {
            return (int)Math.Ceiling(seconds * ticksPerSecond);
        }
    }
}
