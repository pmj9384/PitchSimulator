using System;
using System.Collections.Generic;

namespace Game.Core.Match
{
    // 패스 판정 순수 함수(09-18 확정 스펙 §6). 상태 없음, 엔진 없음, 난수 없음.
    // 안전 판정은 Simple Soccer(Buckland) isPassSafeFromOpponent를 옮긴 것: 패스 방향을 축으로 한 로컬 좌표에서
    // 상대가 뒤면 안전, 앞이면 공이 상대의 수직 발치를 지나는 시각 t에 상대가 (속도×t + 잡기 반경)만큼 움직여 닿는가.
    // 확률이 아니라 위험도(0~1)를 돌려주고, 팀 서드별 리스크 허용(안전 0.2·균형 0.5·모험 0.8, 개인이 덮음)이 판정을 가른다.
    public static class PassRules
    {
        // 위험도 0(아무도 못 닿음)~1(확실히 닿음). 여러 상대 중 최대값.
        // 상대 속도는 스탯을 모르므로 평균 달리기(7m/s)로 본다. 정밀하게 하려면 호출자가 상대별 속도를 주면 된다(2차)
        public static float InterceptRisk(float fromX, float fromZ, float toX, float toZ, IReadOnlyList<TargetInfo> opponents, float opponentSpeed, float ballSpeed)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len <= 0f) { return 1f; }   // 제자리 패스는 의미 없음 = 위험

            float ux = dx / len;
            float uz = dz / len;
            float worst = 0f;

            for (int i = 0; i < opponents.Count; i++)
            {
                float ox = opponents[i].X - fromX;
                float oz = opponents[i].Z - fromZ;
                float along = ox * ux + oz * uz;              // 패스 축 위 투영(앞뒤)
                if (along <= 0f || along > len) { continue; } // 뒤에 있거나 리시버보다 멀면 무관

                float perp = Math.Abs(ox * uz - oz * ux);    // 축에서 수직 거리
                float t = along / ballSpeed;                  // 공이 그 지점을 지나는 시각
                float reach = opponentSpeed * t;              // 그 시각까지 상대가 달릴 수 있는 거리

                // Simple Soccer의 이진 판정(닿으면 위험)을 여유폭으로 편다: 잡기 반경 안에 들어오면 1(확실),
                // 달려서 겨우 닿는 범위 끝에서 0. 그 사이는 선형. 리스크 허용(0.2/0.5/0.8)이 이 여유폭 어디까지 감수하나를 정한다
                float margin = perp - MatchTuning.InterceptReach;  // 발 뻗는 범위를 뺀 "달려야 하는" 거리
                float risk;
                if (margin <= 0f) { risk = 1f; }
                else if (reach <= 0f) { risk = 0f; }
                else { risk = 1f - margin / reach; }
                if (risk > worst) { worst = risk; }
            }

            return worst < 0f ? 0f : (worst > 1f ? 1f : worst);
        }

        // 리스크 허용치 이하면 안전. 경계 포함(허용치와 같으면 통과). 위험도 1은 허용치가 1이 아닌 한 안 함
        public static bool IsPassSafe(float interceptRisk, float riskAllow)
        {
            return interceptRisk <= riskAllow;
        }

        // 리시버 후보 점수. 앞선(공격 방향) 아군만 양수. 전진 거리 + 선호 거리 근접 + 측면 가중.
        // passStyle(짧게 0·직접 1·롱볼 2)이 "먼 후보를 얼마나 선호하나"를, widthLevel이 "측면 후보를 얼마나 선호하나"를 정한다
        public static float ScoreReceiver(float passerX, float passerZ, float candX, float candZ, int attackSign, int passStyle, float passLength, int widthLevel)
        {
            float forward = (candX - passerX) * attackSign;
            if (forward <= 0f) { return -1f; }   // 뒤나 옆은 후보 아님(1주차. 백패스는 2차)

            float dx = candX - passerX;
            float dz = candZ - passerZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);

            float preferred = passLength * MatchTuning.PassStyleLengthScale[passStyle];   // 짧게 ×0.6 · 직접 ×1.0 · 롱볼 ×1.8
            float distFit = 1f - Math.Abs(dist - preferred) / (preferred + dist);         // 0~1, 선호 거리에 가까울수록 1
            float lateral = Math.Abs(candZ) * MatchTuning.WidthScale[widthLevel] * 0.02f; // 측면 가중(폭 넓게일수록)

            return forward * 0.05f + distFit + lateral;
        }

        // 리드 패스 목표(09-18 Play 진단): 리시버의 지금 위치로 차면 리시버는 이미 움직여 공이 뒤에 떨어진다.
        // 공이 도착하는 시간 동안 리시버가 앞(공격 방향)으로 갈 수 있는 거리만큼 앞선 점을 목표로 한다. 상한은 리드 최대치.
        // Simple Soccer의 "리시버 도달 원" 판정의 단순형. 결과는 필드 안으로 클램프
        public static (float x, float z) LeadTarget(float receiverX, float receiverZ, int attackSign, float passDistance, float ballSpeed, float receiverSpeed)
        {
            float travel = passDistance / ballSpeed;                       // 공 도착 시간
            float lead = Math.Min(receiverSpeed * travel, MatchTuning.PassLeadMax);
            float x = receiverX + lead * attackSign;
            float limit = Placement.FieldBounds.HalfLength - Placement.FieldBounds.EdgeMargin;
            if (x > limit) { x = limit; }
            if (x < -limit) { x = -limit; }
            return (x, receiverZ);
        }

        // 역습 리시버: 가장 앞선 아군 1명(자기 자신 제외). 없으면 -1
        public static int CounterReceiver(IReadOnlyList<TargetInfo> teammates, int attackSign, int passerId)
        {
            int best = -1;
            float bestForward = float.MinValue;
            for (int i = 0; i < teammates.Count; i++)
            {
                if (teammates[i].PlayerId == passerId) { continue; }
                float forward = teammates[i].X * attackSign;
                if (forward > bestForward || (forward == bestForward && teammates[i].PlayerId < best))
                {
                    bestForward = forward;
                    best = teammates[i].PlayerId;
                }
            }
            return best;
        }

        // GK 배급 대상. 짧게(0) = 가장 가까운 아군, 길게(2) = 가장 앞선(멀리 있는) 아군, 섞어(1) = alternate로 번갈아
        public static int KeeperDistributionTarget(float gkX, float gkZ, IReadOnlyList<TargetInfo> teammates, int attackSign, int level, int keeperId, bool alternate)
        {
            bool goLong = level == 2 || (level == 1 && alternate);
            if (goLong) { return CounterReceiver(teammates, attackSign, keeperId); }

            int best = -1;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < teammates.Count; i++)
            {
                if (teammates[i].PlayerId == keeperId) { continue; }
                float dx = teammates[i].X - gkX;
                float dz = teammates[i].Z - gkZ;
                float d = dx * dx + dz * dz;
                if (d < bestDistSq || (d == bestDistSq && teammates[i].PlayerId < best))
                {
                    bestDistSq = d;
                    best = teammates[i].PlayerId;
                }
            }
            return best;
        }
    }
}
