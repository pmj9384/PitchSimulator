using System;

namespace Game.Core.Match
{
    // 경기 시계 순수 함수(09-27 HUD): 틱 → 표시 분·초·전후반·추가시간. 실시간 3분 = 9,000틱 = 게임 내 90분 + 추가시간(스펙 §7).
    // 경기 길이는 늘 MatchTicks. 추가시간은 표시 시계의 흐름 속도만 바꾼다(AddedTime 주석)
    public static class MatchClock
    {
        public const int DisplayMinutes = 90;
        public const int DisplaySeconds = DisplayMinutes * 60;   // 5,400초. 추가시간 없으면 틱 하나 = 0.6초
        public const int HalfSeconds = DisplaySeconds / 2;       // 2,700초 = 45:00

        public static int TotalDisplaySeconds(AddedTime added)
        {
            return DisplaySeconds + 60 * (added.FirstHalfMinutes + added.SecondHalfMinutes);
        }

        public static int TotalSecondsOf(int ticks, AddedTime added)
        {
            int clamped = Math.Clamp(ticks, 0, MatchTuning.MatchTicks);
            return clamped * TotalDisplaySeconds(added) / MatchTuning.MatchTicks;   // 정수 나눗셈. 종료 틱에서 정확히 전체 초
        }

        // 하프타임 틱 = 표시 시계가 45:00 + 전반 추가시간에 처음 닿는 틱. 추가시간 없으면 4,500
        public static int HalfTimeTick(AddedTime added)
        {
            int firstHalfEnd = HalfSeconds + 60 * added.FirstHalfMinutes;
            int total = TotalDisplaySeconds(added);
            return (firstHalfEnd * MatchTuning.MatchTicks + total - 1) / total;
        }

        public static bool IsSecondHalf(int ticks, AddedTime added)
        {
            return ticks >= HalfTimeTick(added);
        }

        public static ClockReading Describe(int ticks, AddedTime added)
        {
            int total = TotalSecondsOf(ticks, added);
            if (!IsSecondHalf(ticks, added))
            {
                if (total < HalfSeconds) { return new ClockReading(false, total / 60, total % 60, false, 0, 0); }
                int over = total - HalfSeconds;
                return new ClockReading(false, 45, 0, true, over / 60, over % 60);
            }

            int intoSecond = total - (HalfSeconds + 60 * added.FirstHalfMinutes);   // 후반은 45:00부터 다시 센다
            int shown = HalfSeconds + Math.Max(intoSecond, 0);
            if (shown < DisplaySeconds) { return new ClockReading(true, shown / 60, shown % 60, false, 0, 0); }
            int over2 = shown - DisplaySeconds;
            if (over2 == 0) { return new ClockReading(true, DisplayMinutes, 0, false, 0, 0); }
            return new ClockReading(true, DisplayMinutes, 0, true, over2 / 60, over2 % 60);
        }

        // ── 추가시간 없는 단축형(테스트·로그용)
        public static int MinuteOf(int ticks) => TotalSecondsOf(ticks, AddedTime.None) / 60;
        public static int SecondOf(int ticks) => TotalSecondsOf(ticks, AddedTime.None) % 60;
        public static int TotalSecondsOf(int ticks) => TotalSecondsOf(ticks, AddedTime.None);
        public static bool IsSecondHalf(int ticks) => IsSecondHalf(ticks, AddedTime.None);
    }
}
