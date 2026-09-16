using System.Collections.Generic;

namespace Game.Core.Match
{
    // "주어진 점에 가장 가까운 선수 하나"를 고르는 눈. 공 소유 판정(스펙 §5 "공에 가장 가까운 선수가 잡는다")과
    // 압박 판정(상대 소유 때 공에 가장 가까운 1~2명)이 같은 함수를 쓴다.
    // 누가 후보 목록에 드는가(정의역)는 호출자(MatchManager) 책임. 여기는 받은 목록에서 고르기만 한다.
    public static class TargetSelector
    {
        // 반환 = 고른 선수의 PlayerId, 후보가 없으면 -1.
        // 동률이면 PlayerId 작은 쪽. 목록 순서와 무관하게 항상 같은 답이어야 재현성(고정 스텝·멀티)이 선다.
        public static int SelectNearest(float pointX, float pointZ, IReadOnlyList<TargetInfo>? candidates)
        {
            if (candidates == null || candidates.Count == 0) { return -1; }

            // 첫 후보로 시딩한다. -1·MaxValue로 시작하면 거리가 무한대·NaN인 후보에서 후보가 있는데도 -1이 나온다
            int bestId = candidates[0].PlayerId;
            float bestDistSq = DistSq(candidates[0], pointX, pointZ);

            for (int i = 1; i < candidates.Count; i++)
            {
                float distSq = DistSq(candidates[i], pointX, pointZ);

                // 더 가까우면 교체, 똑같이 가까우면 스폰 순번 작은 쪽으로 교체
                if (distSq < bestDistSq ||
                    (distSq == bestDistSq && candidates[i].PlayerId < bestId))
                {
                    bestDistSq = distSq;
                    bestId = candidates[i].PlayerId;
                }
            }

            return bestId;
        }

        // 대소 비교만 하므로 √ 생략. 22명이 매 틱 도는 계산
        private static float DistSq(TargetInfo c, float pointX, float pointZ)
        {
            float dx = c.X - pointX;
            float dz = c.Z - pointZ;
            return dx * dx + dz * dz;
        }
    }
}
