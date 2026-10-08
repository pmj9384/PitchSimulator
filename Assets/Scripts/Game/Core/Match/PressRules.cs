using System.Collections.Generic;

namespace Game.Core.Match
{
    // 압박 판정 순수 함수(09-18 확정 스펙 §6). "누가 압박하나"는 팀 항목이 아니라 역할별 개인 압박 거리가 정한다(ST 12·CB 3).
    // 팀 전술은 서드별 "압박 시작"(안 감·표준·적극)으로 켜고 끄며 배율만 준다. 역압박 중엔 배율이 더 커진다.
    public static class PressRules
    {
        // 센터백 전진 구역(09-29 수비 D1): 공이 이 팀의 골라인에서 depth 안이고 박스 폭 안인가
        public static bool IsInStepOutZone(float ballX, float ballZ, int attackSign, float depth)
        {
            float fromOwnGoalLine = Placement.FieldBounds.HalfLength + ballX * attackSign;
            if (fromOwnGoalLine > depth) { return false; }
            return System.Math.Abs(ballZ) <= Placement.FieldBounds.PenaltyBoxHalfWidth;
        }

        // 공까지 거리가 개인 압박 거리 × 배율 안이면 압박. 경계 포함. 압박 시작이 "안 감"이면 역압박 중이어도 안 간다.
        // 역압박 중엔 단계 배율에 역압박 배율을 곱한다(10-08 결정 ⑥). 2로 덮어쓰던 땐 뺏긴 직후 6초가 대부분인 상대 진영 압박에서 표준과 적극이 같은 거리를 봤다(09-30 스윕)
        public static bool ShouldPress(float distToBall, float pressRange, int pressStartLevel, bool counterPressing)
        {
            if (pressStartLevel == 0) { return false; }

            float scale = MatchTuning.PressStartScale[pressStartLevel];
            if (counterPressing) { scale *= MatchTuning.CounterPressScale; }
            return distToBall <= pressRange * scale;
        }

        // 압박 순위(09-23): 압박 거리 안인 우리 팀 선수(eligible, 나 포함) 중 공에 더 가까운 사람 수. 0이면 내가 첫 압박자.
        // 동률은 PlayerId 작은 쪽이 앞(잡기 타이브레이크와 같은 규칙). 내가 목록에 없으면(압박 거리 밖) int.MaxValue.
        // 트리는 이 값이 MatchTuning.MaxPressers 미만일 때만 ⑧로 간다. 시뮬이 틱마다 팀별로 계산해 스냅샷에 넣는다.
        // 루즈볼 추격자(⑩)도 같은 함수의 0순위다(09-28 F1b).
        // 히스테리시스(09-28 F1b): 지금 맡고 있는 선수(incumbentId)는 거리를 margin만큼 짧게 쳐서, 도전자가 margin 넘게 더 가까워야 순위가 뒤집힌다.
        // 틱마다 처음부터 최근접을 뽑으면 거의 같은 거리의 두 동료가 번갈아 붙었다 떨어졌다(09-28 유저 Play, 20판 계측 루즈볼 추격자 교체 0.63회/초).
        // RoboCup 역할 배정의 표준 처방: 역할 전환 비용을 판정에 넣는다(Gerkey & Matarić, SPL 드롭인 전략 2016)
        public static int PressRank(int playerId, IReadOnlyList<TargetInfo> eligible, float ballX, float ballZ, int incumbentId = -1, float margin = 0f)
        {
            float myDist = -1f;
            for (int i = 0; i < eligible.Count; i++)
            {
                if (eligible[i].PlayerId != playerId) { continue; }
                myDist = EffectiveDistance(eligible[i], ballX, ballZ, incumbentId, margin);
                break;
            }
            if (myDist < 0f) { return int.MaxValue; }

            int closer = 0;
            for (int i = 0; i < eligible.Count; i++)
            {
                if (eligible[i].PlayerId == playerId) { continue; }
                float d = EffectiveDistance(eligible[i], ballX, ballZ, incumbentId, margin);
                if (d < myDist || (d == myDist && eligible[i].PlayerId < playerId)) { closer++; }
            }
            return closer;
        }

        // 0순위 선수 id. 후보가 없으면 -1. 시뮬이 팀별 "지금 맡은 선수"를 갱신할 때 쓴다
        public static int FirstInRank(IReadOnlyList<TargetInfo> eligible, float ballX, float ballZ, int incumbentId, float margin)
        {
            for (int i = 0; i < eligible.Count; i++)
            {
                if (PressRank(eligible[i].PlayerId, eligible, ballX, ballZ, incumbentId, margin) == 0) { return eligible[i].PlayerId; }
            }
            return -1;
        }

        // 공까지 거리. 지금 맡은 선수는 margin만큼 짧게 친다(음수가 되지 않게 0에서 자른다)
        private static float EffectiveDistance(TargetInfo t, float ballX, float ballZ, int incumbentId, float margin)
        {
            float dx = t.X - ballX;
            float dz = t.Z - ballZ;
            float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (t.PlayerId != incumbentId) { return d; }
            return System.Math.Max(0f, d - margin);
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
