namespace Game.Core.Match
{
    // 압박 판정 순수 함수(09-18 확정 스펙 §6). "누가 압박하나"는 팀 항목이 아니라 역할별 개인 압박 거리가 정한다(ST 12·CB 3).
    // 팀 전술은 서드별 "압박 시작"(안 감·표준·적극)으로 켜고 끄며 배율만 준다. 역압박 중엔 배율이 더 커진다.
    public static class PressRules
    {
        // 공까지 거리가 개인 압박 거리 × 배율 안이면 압박. 경계 포함. 압박 시작이 "안 감"이면 역압박 중이어도 안 간다
        public static bool ShouldPress(float distToBall, float pressRange, int pressStartLevel, bool counterPressing)
        {
            if (pressStartLevel == 0) { return false; }

            float scale = counterPressing ? MatchTuning.CounterPressScale : MatchTuning.PressStartScale[pressStartLevel];
            return distToBall <= pressRange * scale;
        }

        // 뺏긴 순간 우리 골 쪽에 남은 우리 수비 수가 문턱 이상이면(뒤가 비지 않았으면) 창 안에서 역압박. 뒤가 비었으면 재정비
        public static bool IsCounterPressing(int defendersBehind, int counterPressLevel, int ticksSinceTurnover)
        {
            if (counterPressLevel == 0) { return false; }
            if (ticksSinceTurnover >= MatchTuning.CounterPressWindowTicks) { return false; }

            return defendersBehind >= MatchTuning.CounterPressThreshold[counterPressLevel];
        }
    }
}
