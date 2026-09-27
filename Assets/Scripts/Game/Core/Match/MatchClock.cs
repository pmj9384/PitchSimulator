using System;

namespace Game.Core.Match
{
    // 경기 시계 순수 함수(09-27 HUD): 틱 → 표시 분·초·전후반. 실시간 3분 = 게임 내 90분(스펙 §0). 종료 틱(MatchTicks)에서 정확히 90:00
    public static class MatchClock
    {
        public const int DisplayMinutes = 90;
        public const int DisplaySeconds = DisplayMinutes * 60;   // 5,400초. 틱 하나 = 0.6초(중계 시계처럼 초가 오른다)

        public static int TotalSecondsOf(int ticks)
        {
            int clamped = Math.Clamp(ticks, 0, MatchTuning.MatchTicks);
            return clamped * DisplaySeconds / MatchTuning.MatchTicks;   // 정수 나눗셈: 8999틱 = 5399초, 9000틱 = 5400초
        }

        public static int MinuteOf(int ticks)
        {
            return TotalSecondsOf(ticks) / 60;
        }

        public static int SecondOf(int ticks)
        {
            return TotalSecondsOf(ticks) % 60;
        }

        public static bool IsSecondHalf(int ticks)
        {
            return ticks >= MatchTuning.HalfTimeTick;
        }
    }
}
