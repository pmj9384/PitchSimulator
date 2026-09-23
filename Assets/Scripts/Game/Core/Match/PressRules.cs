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

        // 추격 예측(09-23, Simple Soccer pursuit): 공의 지금 위치가 아니라 "공 + 공 속도 × 예측 시간"을 향해 달린다.
        // 예측 시간 = 거리 ÷ (내 속도 + 공 속도), 상한 PursuitMaxLookahead. 공이 서 있으면 지금 위치. 09-21 3분 계측에서 압박이 늘 공의 뒤를 쫓아
        // 소유자와 2.3m 안으로 한 번도 못 들어갔다(드리블러 6.3 vs CB 5.6m/s). 결과는 필드 안으로 클램프
        public static (float x, float z) PursuitPoint(float myX, float myZ, float mySpeed, float ballX, float ballZ, float ballVelX, float ballVelZ)
        {
            float ballSpeed = (float)System.Math.Sqrt(ballVelX * ballVelX + ballVelZ * ballVelZ);
            if (ballSpeed < 0.01f) { return (ballX, ballZ); }

            float dx = ballX - myX;
            float dz = ballZ - myZ;
            float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
            float t = dist / (mySpeed + ballSpeed);
            if (t > MatchTuning.PursuitMaxLookahead) { t = MatchTuning.PursuitMaxLookahead; }

            float limitX = Placement.FieldBounds.HalfLength - Placement.FieldBounds.EdgeMargin;
            float limitZ = Placement.FieldBounds.HalfWidth - Placement.FieldBounds.EdgeMargin;
            float x = System.Math.Max(-limitX, System.Math.Min(limitX, ballX + ballVelX * t));
            float z = System.Math.Max(-limitZ, System.Math.Min(limitZ, ballZ + ballVelZ * t));
            return (x, z);
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
