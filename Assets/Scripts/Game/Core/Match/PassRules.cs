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
                // 킥 원점에서 발 뻗는 범위 안에 붙은 상대는 방향과 무관하게 확실히 닿는다(09-21 3분 계측: 붙은 압박 선수를 뒤라고 무시하고
                // 찼더니 릴리스 지점에서 96%가 끊겼다). Simple Soccer엔 이 경우가 없다(그 게임은 태클로 뺏어서 붙은 채로 차는 상황이 안 남)
                if (ox * ox + oz * oz <= MatchTuning.InterceptReach * MatchTuning.InterceptReach) { return 1f; }
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

        // 킥 초속(09-23 Play "공이 너무 쉽게 멈춤"): 거리와 무관하게 서드 템포로 차니 먼 패스는 목표에 못 미쳐 멈추고 리시버는 리드 점으로 뛰어
        // 공을 지나쳤다. 등감속 운동의 역산: 목표점에 도착 속도 arrival로 닿으려면 v0 = √(arrival² + 2·a·d). 상한을 넘으면 상한으로 차고
        // 못 미친다(느린 템포로 롱볼을 시키면 결과에 보인다: 세팅의 차이가 결과에 보이는 게 이 게임의 축)
        public static float KickSpeed(float distance, float arrivalSpeed, float deceleration, float maxSpeed)
        {
            float v0 = (float)Math.Sqrt(arrivalSpeed * arrivalSpeed + 2f * deceleration * distance);
            return v0 > maxSpeed ? maxSpeed : v0;
        }

        // 등감속 비행의 평균 속도 = (초속 + 도착 속도) ÷ 2. 비행 시간 = 거리 ÷ 평균 속도가 정확히 성립해서 리드·가로채기 판정이
        // "일정 속도"로 쓰는 값이다. 상한에 걸려 못 미치면 도착 속도 0으로 본다
        public static float AverageSpeed(float kickSpeed, float distance, float deceleration)
        {
            float remaining = kickSpeed * kickSpeed - 2f * deceleration * distance;
            float arrival = remaining > 0f ? (float)Math.Sqrt(remaining) : 0f;
            return (kickSpeed + arrival) * 0.5f;
        }

        // 리스크 허용치 이하면 안전. 경계 포함(허용치와 같으면 통과). 위험도 1은 허용치가 1이 아닌 한 안 함
        public static bool IsPassSafe(float interceptRisk, float riskAllow)
        {
            return interceptRisk <= riskAllow;
        }

        // 리시버 후보 점수. 앞선(공격 방향) 아군은 전진 거리 + 선호 거리 근접 + 측면 가중.
        // 옆·뒤 아군(09-21, C)은 같은 근접·측면 항에 배율 0.3을 곱하고 뒤 거리만큼 깎는다: 앞 후보를 절대 못 이기므로 앞이 전부
        // 막혔을 때만 돌릴 곳이 되고, 깊은 백패스는 0 이하로 떨어져 후보에서 빠진다(호출자는 0 초과만 본다).
        // passStyle(짧게 0·직접 1·롱볼 2)이 "먼 후보를 얼마나 선호하나"를, widthLevel이 "측면 후보를 얼마나 선호하나"를 정한다
        public static float ScoreReceiver(float passerX, float passerZ, float candX, float candZ, int attackSign, int passStyle, float passLength, int widthLevel)
        {
            float forward = (candX - passerX) * attackSign;
            float dx = candX - passerX;
            float dz = candZ - passerZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);

            float preferred = passLength * MatchTuning.PassStyleLengthScale[passStyle];   // 짧게 ×0.6 · 직접 ×1.0 · 롱볼 ×1.8
            float distFit = 1f - Math.Abs(dist - preferred) / (preferred + dist);         // 0~1, 선호 거리에 가까울수록 1
            float lateral = Math.Abs(candZ) * MatchTuning.WidthScale[widthLevel] * MatchTuning.LateralScoreScale; // 측면 가중(폭 넓게일수록). 크기는 MatchTuning 주석

            if (forward > 0f) { return forward * 0.05f + distFit + lateral; }

            return (distFit + lateral) * MatchTuning.BackPassScale + forward * MatchTuning.BackPassDepthPenalty;   // forward ≤ 0이라 감점
        }

        // 리드 패스 목표(09-18 Play 진단): 리시버의 지금 위치로 차면 리시버는 이미 움직여 공이 뒤에 떨어진다.
        // 공이 도착하는 시간 동안 리시버가 앞(공격 방향)으로 갈 수 있는 거리만큼 앞선 점을 목표로 한다. 상한은 리드 최대치.
        // 리드는 앞선 리시버(패서보다 공격 방향)에게만(09-21): 옆·뒤 아군에게 앞으로 8m 리드하면 착지점이 패서 앞이 되어
        // 판정은 짧은 앞 패스로 늘 안전, 실제 공은 리시버가 없는 곳으로 가서 먹혔다(3분 계측: 드리블 0·완성률 50%).
        // Simple Soccer의 "리시버 도달 원" 판정의 단순형. 결과는 필드 안으로 클램프
        public static (float x, float z) LeadTarget(float passerX, float receiverX, float receiverZ, int attackSign, float passDistance, float ballSpeed, float receiverSpeed)
        {
            if ((receiverX - passerX) * attackSign <= 0f) { return (receiverX, receiverZ); }   // 옆·뒤 리시버는 지금 위치로. 어차피 마중 나온다

            float travel = passDistance / ballSpeed;                       // 공 도착 시간
            float lead = Math.Min(receiverSpeed * travel, MatchTuning.PassLeadMax);
            float x = receiverX + lead * attackSign;
            float limit = Placement.FieldBounds.HalfLength - Placement.FieldBounds.EdgeMargin;
            if (x > limit) { x = limit; }
            if (x < -limit) { x = -limit; }
            return (x, receiverZ);
        }

        // 역습 리시버: 가장 앞선 아군 1명(자기 자신 제외). 없으면 -1
        // 09-21 리뷰: 자기만 빼면 최전방 선수가 공을 가졌을 때 뒤 선수에게 주고, 그 선수가 다시 앞으로 주는 핑퐁(3분에 158·157)이 됐다.
        // 패서보다 CounterForwardMargin 이상 앞선 아군만 후보. 없으면 -1(트리는 ④·⑤로 떨어진다)
        // onsideLine(공격 방향 좌표): 그보다 골 쪽인 아군은 오프사이드 위치라 제외(09-23). OffsideRules.NoLine이면 제한 없음(GK 골킥 등)
        public static int CounterReceiver(float passerX, IReadOnlyList<TargetInfo> teammates, int attackSign, int passerId, float onsideLine)
        {
            int best = -1;
            float bestForward = float.MinValue;
            float minForward = passerX * attackSign + MatchTuning.CounterForwardMargin;
            for (int i = 0; i < teammates.Count; i++)
            {
                if (teammates[i].PlayerId == passerId) { continue; }
                float forward = teammates[i].X * attackSign;
                if (forward < minForward || forward > onsideLine) { continue; }
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
            if (goLong) { return CounterReceiver(gkX, teammates, attackSign, keeperId, OffsideRules.NoLine); }   // GK가 가장 뒤라 앞선 아군 중 최전방. 골킥은 오프사이드 없음

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
