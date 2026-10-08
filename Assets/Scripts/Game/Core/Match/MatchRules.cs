using System;
using System.Collections.Generic;
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

        // 태클 성공 확률(09-21): 기본 확률 × 로지스틱((tackle − 50)/50 × 보정 폭). tackle 80 → 0.19, 50 → 0.15, 20 → 0.11
        public static float TackleProbability(int tackle)
        {
            float logit = (tackle - 50) / 50f * MatchTuning.StatLogitScale;
            return MatchTuning.TackleBaseChance * Logistic(logit);
        }

        // 압박받는 슛(09-29 수비 D3): 슈터 발 뻗는 범위(PressedRadius) 안에 상대가 있으면 로짓에서 penalty를 뺀다.
        // StatsBomb "Closing down": 슛의 약 65%가 압박 속에서 나오고 무압박 슛이 더 잘 들어간다. 슛 경로 수비수 예시 xG 0.25 → 0.18은 로짓 약 -0.4
        public static float PressuredShotProbability(float probability, float logitPenalty)
        {
            if (probability <= 0f) { return 0f; }
            float p = Math.Min(probability, 0.9999f);
            float logit = (float)Math.Log(p / (1f - p));
            return Logistic(logit - logitPenalty);
        }

        // 슛 블록 후보(09-29 수비 D3): 슈터에서 range 안이고 슛 방향 ±coneDeg 안인 상대 필드 선수 중 가장 가까운 선수. 없으면 -1.
        // Opta는 슛을 막은 최종 수비수를 블록으로 센다. 각도 ±20°는 Wharton xG 논문의 각도 압박 기준
        public static int ShotBlocker(float shooterX, float shooterZ, float aimX, float aimZ, IReadOnlyList<TargetInfo> opponents, int keeperId, float range, float coneDeg)
        {
            float ax = aimX - shooterX;
            float az = aimZ - shooterZ;
            float aimLen = (float)Math.Sqrt(ax * ax + az * az);
            if (aimLen <= 0f) { return -1; }

            float cosLimit = (float)Math.Cos(coneDeg * Math.PI / 180.0);
            int best = -1;
            float bestD2 = range * range;
            for (int i = 0; i < opponents.Count; i++)
            {
                TargetInfo o = opponents[i];
                if (o.PlayerId == keeperId) { continue; }
                float ox = o.X - shooterX;
                float oz = o.Z - shooterZ;
                float d2 = ox * ox + oz * oz;
                if (d2 > bestD2 || d2 <= 0f) { continue; }
                float cos = (ox * ax + oz * az) / ((float)Math.Sqrt(d2) * aimLen);
                if (cos < cosLimit) { continue; }
                bestD2 = d2;
                best = o.PlayerId;
            }
            return best;
        }

        // 막힌 공이 튕기는 방향(단위 벡터): 슛 반대 방향을 turnDeg만큼 비튼다. 블로커가 슛 라인의 왼쪽이면 왼쪽으로, 오른쪽이면 오른쪽으로(결정적)
        public static (float x, float z) BlockReboundDirection(float shooterX, float shooterZ, float aimX, float aimZ, float blockerX, float blockerZ, float turnDeg)
        {
            float ax = aimX - shooterX;
            float az = aimZ - shooterZ;
            float len = (float)Math.Sqrt(ax * ax + az * az);
            float bx = -ax / len;
            float bz = -az / len;
            float side = ax * (blockerZ - shooterZ) - az * (blockerX - shooterX) >= 0f ? 1f : -1f;   // 슛 방향 기준 블로커가 어느 쪽인가(외적 부호)
            double rad = turnDeg * Math.PI / 180.0 * side;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            return (bx * cos - bz * sin, bx * sin + bz * cos);
        }

        public static float Logistic(float logit)
        {
            return 1f / (1f + (float)Math.Exp(-logit));
        }

        // ── 슛 방향 오차(09-18 확정: 빗나감). 골 확률(xG)과 별개로 "어디로 날아가나"를 정한다.
        // shot이 낮을수록 조준 반폭이 넓어 골문 밖(포스트 3.66m 밖)으로도 간다. Simple Soccer PlayerKickingAccuracy의 역할
        public static float ShotSpread(int shot)
        {
            float t = Clamp01(shot / 100f);
            return MatchTuning.ShotSpreadMax + (MatchTuning.ShotSpreadMin - MatchTuning.ShotSpreadMax) * t;
        }

        // roll [0,1] → 조준 Z(골라인 위 지점, 골 중심 기준). 0.5가 정중앙, 양 끝이 ±반폭
        public static float ShotAimZ(int shot, float roll)
        {
            return (roll * 2f - 1f) * ShotSpread(shot);
        }

        // 조준 Z가 두 포스트 사이면 골문 안(경계 포함). 밖이면 빗나감
        public static bool IsOnTarget(float aimZ)
        {
            return Math.Abs(aimZ) <= FieldBounds.GoalHalfWidth;
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
