using System;
using Game.Core.Placement;

namespace Game.Core.Match
{
    // 경기 판정 순수 함수 모음. 상태 없음, 엔진 없음, 난수 없음.
    // 확률만 계산해 돌려주고, 그 확률로 결과를 정하는 주사위(System.Random)는 경기(MatchSimulation)가 든다(09-16 결정).
    // 그래야 같은 시드가 같은 경기를 만들고, 테스트가 확률 계산과 결과 판정을 따로 검증한다.
    public static class MatchRules
    {
        // ── 이동
        // speed 스탯 → m/s. 50 = 7m/s 선형, 여기에 템포 배율. 드리블은 호출자가 DribbleFactor를 곱한다
        public static float SpeedMps(int speedStat)
        {
            return MatchTuning.SpeedMpsAt50 * (speedStat / 50f) * MatchTuning.Tempo;
        }

        // ── 슛 기하. attackSign = +1이면 +X 골(팀 0), -1이면 -X 골(팀 1)
        // 거리 = 골 중심까지, 각도 = 슛 지점에서 양 골포스트로 향하는 두 벡터 사이 각(라디안). xG 식의 두 입력
        public static float ShotDistance(float x, float z, int attackSign)
        {
            float dx = FieldBounds.HalfLength - x * attackSign;
            return (float)Math.Sqrt(dx * dx + z * z);
        }

        public static float ShotAngle(float x, float z, int attackSign)
        {
            float dx = FieldBounds.HalfLength - x * attackSign;
            if (dx <= 0f) { return 0f; }   // 골라인 위나 뒤에선 골문이 안 보인다

            float a1 = (float)Math.Atan2(FieldBounds.GoalHalfWidth - z, dx);
            float a2 = (float)Math.Atan2(-FieldBounds.GoalHalfWidth - z, dx);
            return Math.Abs(a1 - a2);
        }

        // ── 슛 확률(09-16 확정: xG 바탕 + 능력치 보정 1단). GK 세이브 확률을 따로 곱하지 않는다(xG에 이미 들어 있다).
        // 보정은 로짓에 더한다: 슈터 shot이 올리고 GK reflexes·diving 평균이 내린다. 로짓이라 결과가 저절로 0~1에 머문다
        public static float ShotProbability(float x, float z, int attackSign, int shooterShot, int keeperReflexes, int keeperDiving)
        {
            if (ShotDistance(x, z, attackSign) > MatchTuning.MaxShotRange) { return 0f; }   // 모델 학습 범위 밖. 다항식이 멀수록 다시 오르는 걸 막는다

            float xToGoalLine = FieldBounds.HalfLength - x * attackSign;
            float logit = XgModel.Logit(Math.Max(xToGoalLine, 0f), Math.Abs(z));
            float keeperBlock = (keeperReflexes + keeperDiving) * 0.5f;
            logit += (shooterShot - 50) / 50f * MatchTuning.StatLogitScale;
            logit -= (keeperBlock - 50) / 50f * MatchTuning.StatLogitScale;
            return Logistic(logit);
        }

        // 내 골 앞 페널티 박스 안인가(GK 출격 한계). attackSign이 +1이면 내 골은 -X 쪽
        public static bool IsInOwnPenaltyBox(float x, float z, int attackSign)
        {
            float depthFromGoalLine = FieldBounds.HalfLength + x * attackSign;   // 내 골라인에서 필드 안쪽으로 잰 거리
            return depthFromGoalLine >= 0f
                && depthFromGoalLine <= FieldBounds.PenaltyBoxDepth
                && Math.Abs(z) <= FieldBounds.PenaltyBoxHalfWidth;
        }

        public static float Logistic(float logit)
        {
            return 1f / (1f + (float)Math.Exp(-logit));
        }

        // ── 세이브 뒤 캐치 확률. 기본 0.65에 handling이 ±0.2. 캐치면 GK 소유, 아니면 앞으로 튕겨 자유 공(스펙 §5)
        public static float CatchProbability(int handling)
        {
            float p = MatchTuning.CatchBase + (handling - 50) / 50f * MatchTuning.CatchHandlingScale;
            return Clamp01(p);
        }

        // ── 확률 → 결과. roll은 [0,1) 난수. roll < p면 성공. p = 0이면 절대 성공 안 하고 p = 1이면 항상 성공
        public static bool Resolve(float probability, float roll)
        {
            return roll < probability;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) { return 0f; }
            if (v > 1f) { return 1f; }
            return v;
        }
    }
}
