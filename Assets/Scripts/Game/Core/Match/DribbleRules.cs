using System;
using System.Collections.Generic;

namespace Game.Core.Match
{
    // 드리블 돌파 판정 순수 함수(09-29 유저 Play: "드리블로 상대를 뚫으려는 게 하나도 없다. 윙어·공격수는 역할에 따라 뚫어야").
    // 방향 고르기는 RoboCup 2D HELIOS(agent2d) 드리블 생성기를 옮긴 것: 방향 후보를 일정 각도로 만들고, 그 지점에 상대가 나보다 먼저
    // 닿는 후보를 버린 뒤 남은 것 중 고른다(arXiv 2401.03406 2절: "-180~+180, 30° 간격", "상대가 먼저 닿거나 도중에 끊을 수 있으면 제거").
    // 돌파는 앞으로 제치는 동작이라 후보를 앞쪽 ±90°로 줄였다. 상태 없음, 엔진 없음, 난수 없음
    public static class DribbleRules
    {
        private const float Epsilon = 1e-4f;   // 후보 점수 동률 판정 폭(부동소수 오차)

        // 돌파 상대가 있나: 공격 방향 앞쪽이면서 engageRange 안인 상대
        public static bool HasDefenderAhead(float x, float z, int attackSign, IReadOnlyList<TargetInfo> opponents, float engageRange)
        {
            float r2 = engageRange * engageRange;
            for (int i = 0; i < opponents.Count; i++)
            {
                float dx = opponents[i].X - x;
                float dz = opponents[i].Z - z;
                if (dx * attackSign <= 0f) { continue; }
                if (dx * dx + dz * dz <= r2) { return true; }
            }
            return false;
        }

        // 돌파 목표점. 후보 = 정면 기준 -90°~+90°를 stepDeg 간격, 거리 lookahead. 후보마다 "내가 닿는 시간"과 "가장 빠른 상대가 닿는 시간"의 차(여유)를 잰다.
        // 여유가 양수인(상대보다 먼저 닿는) 후보 중 가장 앞으로 가는 것, 없으면 여유가 가장 큰 것. 동률은 정면에 가까운 각. 필드 밖 후보는 버린다
        public static (float x, float z) TakeOnTarget(float x, float z, int attackSign, IReadOnlyList<TargetInfo> opponents,
            float carrySpeed, float opponentSpeed, float opponentReach, float lookahead, float stepDeg, float halfLength, float halfWidthInside)
        {
            float myTime = lookahead / Math.Max(carrySpeed, 0.1f);
            bool bestSafe = false;
            float bestScore = float.MinValue;
            float bestAbsAngle = float.MaxValue;
            float bx = x + lookahead * attackSign;
            float bz = z;

            for (float deg = -90f; deg <= 90f + 1e-3f; deg += stepDeg)
            {
                double rad = deg * Math.PI / 180.0;
                float px = x + (float)Math.Cos(rad) * lookahead * attackSign;
                float pz = z + (float)Math.Sin(rad) * lookahead;
                if (Math.Abs(px) > halfLength || Math.Abs(pz) > halfWidthInside) { continue; }

                float oppTime = float.MaxValue;
                for (int i = 0; i < opponents.Count; i++)
                {
                    float dx = opponents[i].X - px;
                    float dz = opponents[i].Z - pz;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz) - opponentReach;
                    float t = Math.Max(d, 0f) / Math.Max(opponentSpeed, 0.1f);
                    if (t < oppTime) { oppTime = t; }
                }
                float margin = oppTime - myTime;
                bool safe = margin > 0f;
                float score = safe ? (px - x) * attackSign : margin;   // 안전하면 얼마나 앞으로 가나, 아니면 얼마나 덜 늦나
                float absAngle = Math.Abs(deg);
                if (!IsBetterCandidate(safe, score, absAngle, bestSafe, bestScore, bestAbsAngle)) { continue; }

                bestSafe = safe;
                bestScore = score;
                bestAbsAngle = absAngle;
                bx = px;
                bz = pz;
            }
            return (bx, bz);
        }

        // 안전한 후보가 안전하지 않은 후보보다 먼저. 같은 부류면 점수가 큰 쪽, 비기면 정면에 가까운 쪽
        private static bool IsBetterCandidate(bool safe, float score, float absAngle, bool bestSafe, float bestScore, float bestAbsAngle)
        {
            if (safe != bestSafe) { return safe; }
            if (score > bestScore + Epsilon) { return true; }
            if (score < bestScore - Epsilon) { return false; }
            return absAngle < bestAbsAngle;
        }

        // 돌파 의도를 가질 확률. 개인 드리블 성향(0~1) × 배율. 역할 차이는 선수 표 dribble 열이 이미 담고 있다(윙어 0.9·타깃맨 0.2)
        public static float TakeOnChance(float dribbleDial, float scale)
        {
            return Math.Max(0f, Math.Min(1f, dribbleDial * scale));
        }
    }
}
