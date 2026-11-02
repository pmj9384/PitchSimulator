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
        // opponentReaction(09-29 F2b): 상대가 반응·가속으로 늦는 시간. 시뮬 이동에 가속이 생겨 판정도 같은 전제로(MatchTuning.ReactionTime).
        // 기본값을 두지 않는다: 빠뜨리면 "판정과 이동이 같은 전제"가 조용히 깨진다(10-29 리뷰)
        public static float InterceptRisk(float fromX, float fromZ, float toX, float toZ, IReadOnlyList<TargetInfo> opponents, float opponentSpeed, float ballSpeed, float opponentReaction)
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
                // 착지점에서 발 뻗는 범위 안에 선 상대도 확실히 닿는다. 착지점보다 먼 상대는 아래에서 "무관"으로 넘기는데,
                // 착지점이 받는 선수 발밑까지 내려오면서(PickLanding) 그 바로 뒤에 붙은 수비수까지 무관이 되는 것을 막는다
                float ex = opponents[i].X - toX;
                float ez = opponents[i].Z - toZ;
                if (ex * ex + ez * ez <= MatchTuning.InterceptReach * MatchTuning.InterceptReach) { return 1f; }
                float along = ox * ux + oz * uz;              // 패스 축 위 투영(앞뒤)
                if (along <= 0f || along > len) { continue; } // 뒤에 있거나 리시버보다 멀면 무관

                float perp = Math.Abs(ox * uz - oz * ux);    // 축에서 수직 거리
                float t = along / ballSpeed;                  // 공이 그 지점을 지나는 시각
                float reach = opponentSpeed * Math.Max(0f, t - opponentReaction);   // 그 시각까지 상대가 달릴 수 있는 거리

                // Simple Soccer의 이진 판정(닿으면 위험)을 여유폭으로 편다: 잡기 반경 안에 들어오면 1(확실),
                // 달려서 겨우 닿는 범위 끝에서 0. 그 사이는 선형. 리스크 허용(0.2/0.5/0.8)이 이 여유폭 어디까지 감수하나를 정한다
                float margin = perp - MatchTuning.InterceptReach;  // 발 뻗는 범위를 뺀 "달려야 하는" 거리
                float risk;
                if (margin <= 0f) { risk = 1f; }
                else if (reach <= 0f) { risk = 0f; }
                else { risk = 1f - margin / reach; }
                if (risk > worst) { worst = risk; }
            }

            return Math.Clamp(worst, 0f, 1f);
        }

        // 킥 초속: 거리 d를 평균 속도 v̄로 가게 찬다(10-25. 실측 Metrica 패스 평균 10~14.5m/s를 그대로 입력으로 쓴다).
        // 등감속에서 v̄ = (v0 + v도착)/2, v도착² = v0² − 2ad → v0 = v̄ + a·d/(2·v̄). 상한을 넘으면 상한으로 차고 못 미친다
        // (느린 템포로 롱볼을 시키면 결과에 보인다: 세팅의 차이가 결과에 보이는 게 이 게임의 축).
        // 09-23~10-24는 도착 속도가 입력(v0 = √(v도착² + 2ad))이라 15m 패스 평균이 8.5m/s로 실측(12.2)보다 30% 느렸다
        public static float KickSpeed(float distance, float averageSpeed, float deceleration, float maxSpeed)
        {
            float v0 = averageSpeed + deceleration * distance / (2f * averageSpeed);
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

        // 안전 가중(10-29 가속 E): 후보 점수에 곱한다. (1 − 위험도) × 경계 띠. 띠는 허용치 ± band에서 1 → 0으로 선형으로 줄어
        // 허용치 바로 위·아래 후보가 통과/탈락으로 튀지 않는다. 전엔 위험도 ≤ 허용치면 점수 그대로, 넘으면 탈락이라
        // 10-25 격자에서 슬라이드 계수 0.18/0.20/0.25에 4부 승률이 46/74/48%로 튀었다(실험/2026-10-25-슬라이드-수축-격자.md)
        public static float SafetyWeight(float interceptRisk, float riskAllow, float band)
        {
            if (band <= 0f)
            {
                return IsPassSafe(interceptRisk, riskAllow) ? 1f - interceptRisk : 0f;   // 띠 없음 = 옛 이진 판정 + (1 − 위험도)
            }
            float taper = (riskAllow + band - interceptRisk) / (2f * band);
            if (taper <= 0f) { return 0f; }
            if (taper > 1f) { taper = 1f; }
            return (1f - interceptRisk) * taper;
        }

        // 로빙의 위험(10-29 ④-1 3단계): 공이 머리 위로 가니 경로 옆 수비는 무관하고, 착지점에 누가 먼저 닿나로 정한다(HELIOS·Spearman의 도착 경쟁).
        // 도착 시간 = 발 뻗는 범위 밖이면 반응 + 달릴 거리 ÷ 속도(땅볼 판정 InterceptRisk와 같은 모양). 받는 선수와 가장 빠른 상대의 시간 차를
        // 로지스틱(폭 sigma, Spearman pitch control)으로 확률로 바꾼다. 동시 도착 = 0.5. 킥 지점에 붙은 상대는 뜨기 전에 닿으니 1.
        // 상대는 착지점 한 점이 아니라 하강 구간(공이 잡기 높이 아래로 내려온 뒤 착지까지) 위 가장 가까운 점까지 잰다. 그 구간에선 공이 낮아 잡힌다(11-02 리뷰).
        // 받는 선수는 킥 순간부터 착지점으로 마중 나가니 반응 지연이 없다. reaction은 상대에게만 준다(11-02 리뷰)
        // 하강 구간은 필드 선수 높이(FieldReachHeight)로 잰다. 상대 박스 안 GK는 2.6m까지 닿아 실제론 더 일찍 잡는다(④ GK 공중볼 단계에서 맞춘다)
        public static float LobRisk(float passerX, float passerZ, float landingX, float landingZ, float receiverX, float receiverZ,
            IReadOnlyList<TargetInfo> opponents, float runSpeed, float reaction, float sigma)
        {
            float distance = Distance(passerX, passerZ, landingX, landingZ);
            if (distance <= 0f) { return 1f; }

            // 포물선은 좌우 대칭이라 하강 구간 길이 = 발사 구간 길이
            float fall = BallRules.LobRiseDistance(distance, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity, MatchTuning.FieldReachHeight);
            float startX = landingX - (landingX - passerX) / distance * fall;
            float startZ = landingZ - (landingZ - passerZ) / distance * fall;

            float receiverTime = ArrivalTime(Distance(receiverX, receiverZ, landingX, landingZ), runSpeed, 0f);
            float defenderTime = float.MaxValue;
            for (int i = 0; i < opponents.Count; i++)
            {
                if (Distance(opponents[i].X, opponents[i].Z, passerX, passerZ) <= MatchTuning.InterceptReach) { return 1f; }
                float toSegment = DistanceToSegment(opponents[i].X, opponents[i].Z, startX, startZ, landingX, landingZ);
                float t = ArrivalTime(toSegment, runSpeed, reaction);
                if (t < defenderTime) { defenderTime = t; }
            }
            if (defenderTime == float.MaxValue) { return 0f; }
            // π/√3은 sigma를 로지스틱 분포의 표준편차로 읽는 변환이다(Spearman 2017 pitch control의 tti_sigma)
            double x = (receiverTime - defenderTime) / sigma * Math.PI / Math.Sqrt(3.0);
            return (float)(1.0 / (1.0 + Math.Exp(-x)));
        }

        private static float ArrivalTime(float distance, float runSpeed, float reaction)
        {
            float run = distance - MatchTuning.InterceptReach;
            if (run <= 0f) { return 0f; }
            return reaction + run / runSpeed;
        }

        // 점 (px, pz)에서 선분 (ax, az)–(bx, bz)까지 가장 짧은 거리
        private static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax;
            float dz = bz - az;
            float lengthSq = dx * dx + dz * dz;
            if (lengthSq <= 0f) { return Distance(px, pz, ax, az); }
            float t = ((px - ax) * dx + (pz - az) * dz) / lengthSq;
            t = Math.Clamp(t, 0f, 1f);
            return Distance(px, pz, ax + dx * t, az + dz * t);
        }

        // 로빙의 발사 구간 위험(10-29 ④-1, 진단: 로빙의 85%가 차자마자 0.5~1.5m 높이에서 킥 지점 근처 상대에게 끊겼다).
        // 공이 잡기 높이를 넘기 전까지의 짧은 경로를 땅볼과 같은 가로채기 판정(InterceptRisk)으로 잰다. 실행에서 공이 낮은 구간과 같은 구간이다
        public static float LobLaunchRisk(float passerX, float passerZ, float landingX, float landingZ, IReadOnlyList<TargetInfo> opponents)
        {
            float distance = Distance(passerX, passerZ, landingX, landingZ);
            if (distance <= 0f) { return 1f; }
            float rise = BallRules.LobRiseDistance(distance, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity, MatchTuning.FieldReachHeight);
            float endX = passerX + (landingX - passerX) / distance * rise;
            float endZ = passerZ + (landingZ - passerZ) / distance * rise;
            float horizontal = LobHorizontalSpeed(distance);
            return InterceptRisk(passerX, passerZ, endX, endZ, opponents, MatchTuning.InterceptRunSpeed, horizontal, MatchTuning.ReactionTime);
        }

        // 로빙 후보: 같은 리시버에게 띄워 주는 착지점(긴 리드)과 그 위험 = max(발사 구간, 착지 경쟁). 거리가 로빙 범위 밖이면 PassLanding.None.
        // 리드는 로빙의 수평 속도로 잰다(체공 시간 = 거리 ÷ 수평 속도). 지금 상수에선 로빙 범위(15m 이상)의 리드가 늘 상한 PassLeadMax 8m에 걸려 땅볼 속도로 재도 같지만, 상한이나 범위를 바꾸면 차이가 난다
        // 착지 뒤 굴러가는 공(착지 속도 × LobBounceSpeedRetention)을 리시버가 놓쳤을 때의 위험은 판정 밖이다 [가정]
        public static PassLanding LobLanding(float passerX, float passerZ, float receiverX, float receiverZ, int attackSign, IReadOnlyList<TargetInfo> opponents)
        {
            float toReceiver = Distance(passerX, passerZ, receiverX, receiverZ);
            if (toReceiver <= 0f) { return PassLanding.None; }
            float horizontal = LobHorizontalSpeed(toReceiver);
            (float x, float z) point = LeadTarget(passerX, receiverX, receiverZ, attackSign, toReceiver, horizontal, MatchTuning.SpeedMpsAt50, MatchTuning.PassLeadOptions[0]);

            float distance = Distance(passerX, passerZ, point.x, point.z);
            if (distance < MatchTuning.LobMinDistance) { return PassLanding.None; }
            if (distance > MatchTuning.LobMaxDistance) { return PassLanding.None; }

            float race = LobRisk(passerX, passerZ, point.x, point.z, receiverX, receiverZ, opponents, MatchTuning.InterceptRunSpeed, MatchTuning.ReactionTime, MatchTuning.ArrivalSigma);
            float launch = LobLaunchRisk(passerX, passerZ, point.x, point.z, opponents);
            return new PassLanding(point.x, point.z, Math.Max(race, launch));
        }

        private static float LobHorizontalSpeed(float distance)
        {
            (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(distance, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
            return v.horizontal;
        }

        // 되돌림 패스(방금 준 선수에게 곧바로 뒤로)는 안전한(허용치 이하) 다른 후보가 없을 때만 쓴다(W↔ST 측면 왕복 22회, 09-21).
        // 10-29 리뷰: 안전 가중 띠 안의 허용치 넘는 후보가 안전한 되돌림을 늘 이겼다(전엔 안전한 후보만 골랐다)
        public static bool PrefersOverReturn(bool bestFound, bool bestIsSafe, bool returnFound)
        {
            if (!bestFound) { return false; }
            if (!returnFound) { return true; }
            return bestIsSafe;
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
        // 공이 도착하는 시간 동안 리시버가 앞(공격 방향)으로 갈 수 있는 거리만큼 앞선 점을 목표로 한다. 상한은 leadMax(착지점 후보마다 다르다, 09-30).
        // 리드는 앞선 리시버(패서보다 공격 방향)에게만: 옆·뒤 아군에게 앞으로 8m 리드하면 착지점이 패서 앞이 되어
        // 판정은 짧은 앞 패스로 늘 안전, 실제 공은 리시버가 없는 곳으로 가서 먹혔다(3분 계측: 드리블 0·완성률 50%).
        // Simple Soccer의 "리시버 도달 원" 판정의 단순형. 결과는 필드 안으로 클램프
        public static (float x, float z) LeadTarget(float passerX, float receiverX, float receiverZ, int attackSign, float passDistance, float ballSpeed, float receiverSpeed, float leadMax)
        {
            if ((receiverX - passerX) * attackSign <= 0f) { return (receiverX, receiverZ); }   // 옆·뒤 리시버는 지금 위치로. 어차피 마중 나온다

            float travel = passDistance / ballSpeed;                       // 공 도착 시간
            float lead = Math.Min(receiverSpeed * travel, leadMax);
            float x = receiverX + lead * attackSign;
            float limit = Placement.FieldBounds.HalfLength - Placement.FieldBounds.EdgeMargin;
            if (x > limit) { x = limit; }
            if (x < -limit) { x = -limit; }
            return (x, receiverZ);
        }

        // 착지점 하나: 받는 선수 앞 leadMax 상한의 리드 점. 받는 선수 속도는 평균(speed 50)으로 본다.
        // 트리는 동료의 스탯을 모르고(TargetInfo), 고른 점을 시뮬이 그대로 차므로 판정과 실행이 같은 점을 본다.
        // 그전엔 트리가 평균 속도로 안전을 재고 시뮬이 실제 속도로 다시 계산해 찼다(09-28 재검증 "판정과 실행이 다른 전제")
        public static (float x, float z) LandingPoint(float passerX, float passerZ, float receiverX, float receiverZ, int attackSign, float averageSpeed, float leadMax)
        {
            float distance = Distance(passerX, passerZ, receiverX, receiverZ);
            return LeadTarget(passerX, receiverX, receiverZ, attackSign, distance, FlightAverageSpeed(distance, averageSpeed), MatchTuning.SpeedMpsAt50, leadMax);
        }

        // 그 착지점으로 찼을 때의 가로채기 위험. 공 속도는 착지점까지 거리로 역산한 평균(시뮬 Pass가 차는 속도와 같은 식).
        // 상대는 반응·가속만큼 늦게 출발한다(10-29 가속 B). 시뮬 이동에 가속이 있으니 판정도 같은 전제
        public static float LandingRisk(float passerX, float passerZ, float landingX, float landingZ, float averageSpeed, IReadOnlyList<TargetInfo> opponents)
        {
            float distance = Distance(passerX, passerZ, landingX, landingZ);
            return InterceptRisk(passerX, passerZ, landingX, landingZ, opponents, MatchTuning.InterceptRunSpeed, FlightAverageSpeed(distance, averageSpeed), MatchTuning.ReactionTime);
        }

        // 착지점 고르기(09-30 공격 칼날 A): 리드 후보(MatchTuning.PassLeadOptions)를 긴 것부터 보다가 위험이 허용치 이하인 첫 후보를 고른다.
        // 앞이 비면 공간으로, 수비 라인이 가까우면 짧게, 그래도 막히면 발밑으로. Simple Soccer GetBestPassToReceiver(후보 3점 중 안전하고 골에 가까운 것)와 같은 구조.
        // 전부 허용치를 넘으면 가장 덜 위험한 후보(동률은 긴 쪽)를 돌려준다. 호출자(트리)는 Risk로 안전 가중(SafetyWeight)을 매긴다.
        // 착지점은 이진 판정(IsPassSafe)으로 "안전한 가장 긴 리드"를 고르고, 리시버끼리 비교만 연속 가중으로 한다(10-29 가속 E).
        // 착지점까지 가중으로 고르면 위험 0인 발밑이 늘 이겨 공간 패스가 사라진다. 리드 길이의 가치는 ScoreReceiver에 없다
        public static PassLanding PickLanding(float passerX, float passerZ, float receiverX, float receiverZ, int attackSign, float averageSpeed, IReadOnlyList<TargetInfo> opponents, float riskAllow)
        {
            PassLanding leastRisky = PassLanding.None;
            for (int i = 0; i < MatchTuning.PassLeadOptions.Length; i++)
            {
                (float x, float z) point = LandingPoint(passerX, passerZ, receiverX, receiverZ, attackSign, averageSpeed, MatchTuning.PassLeadOptions[i]);
                float risk = LandingRisk(passerX, passerZ, point.x, point.z, averageSpeed, opponents);
                var candidate = new PassLanding(point.x, point.z, risk);
                if (IsPassSafe(risk, riskAllow)) { return candidate; }
                if (i == 0 || risk < leastRisky.Risk) { leastRisky = candidate; }
            }
            return leastRisky;
        }

        private static float Distance(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // 그 거리를 평균 속도에 맞춰 찼을 때 실제 평균 공 속도(초속 역산 → 평균. 상한에 걸리면 입력보다 낮아진다)
        private static float FlightAverageSpeed(float distance, float averageSpeed)
        {
            float kick = KickSpeed(distance, averageSpeed, MatchTuning.BallDeceleration, MatchTuning.PassSpeedMax);
            return AverageSpeed(kick, distance, MatchTuning.BallDeceleration);
        }

        // 역습 리시버: 가장 앞선 아군 1명(자기 자신 제외). 없으면 -1
        // 09-21 리뷰: 자기만 빼면 최전방 선수가 공을 가졌을 때 뒤 선수에게 주고, 그 선수가 다시 앞으로 주는 핑퐁(3분에 158·157)이 됐다.
        // 패서보다 CounterForwardMargin 이상 앞선 아군만 후보. 없으면 -1(트리는 ④·⑤로 떨어진다)
        // onsideLine(공격 방향 좌표): 그보다 골 쪽인 아군은 오프사이드 위치라 제외. OffsideRules.NoLine이면 제한 없음(GK 골킥 등)
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

        // 킥오프 첫 패스(09-29 유저 Play: "공을 사람한테 주고 뒤로 주면서 시작해야 하는데 바로 시작한다"). IFAB 8조는 방향을 묻지 않지만
        // 실제 킥오프는 거의 다 옆·뒤 동료에게 짧게 내준다. 키커보다 앞서지 않은 동료 중 가장 가까운 이, 동률은 PlayerId 작은 쪽. 없으면 -1
        public static int KickoffReceiver(float kickerX, float kickerZ, IReadOnlyList<TargetInfo> teammates, int attackSign)
        {
            int best = -1;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < teammates.Count; i++)
            {
                TargetInfo m = teammates[i];
                if ((m.X - kickerX) * attackSign > 0f) { continue; }
                float dx = m.X - kickerX;
                float dz = m.Z - kickerZ;
                float d = dx * dx + dz * dz;
                if (d < bestDistSq || (d == bestDistSq && m.PlayerId < best))
                {
                    bestDistSq = d;
                    best = m.PlayerId;
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

    // 착지점 하나와 그 점까지의 가로채기 위험(0~1)
    public readonly struct PassLanding
    {
        public static readonly PassLanding None = new PassLanding(0f, 0f, 1f, false);   // 후보가 없다(로빙 범위 밖 등). 위험 1과 구별한다

        public readonly float X;
        public readonly float Z;
        public readonly float Risk;
        public readonly bool Found;

        public PassLanding(float x, float z, float risk) : this(x, z, risk, true)
        {
        }

        private PassLanding(float x, float z, float risk, bool found)
        {
            X = x;
            Z = z;
            Risk = risk;
            Found = found;
        }
    }

    // 패스 한 번의 선택: 누구에게, 어느 점으로. 트리가 고르고 시뮬은 그 점으로 찬다
    public readonly struct PassChoice
    {
        public static readonly PassChoice None = new PassChoice(-1, 0f, 0f, false);

        public readonly int ReceiverId;
        public readonly float X;
        public readonly float Z;
        public readonly bool IsLob;   // 띄워 준다(10-29 ④-1)

        public PassChoice(int receiverId, float x, float z, bool isLob)
        {
            ReceiverId = receiverId;
            X = x;
            Z = z;
            IsLob = isLob;
        }

        public bool Found => ReceiverId != -1;
    }
}
