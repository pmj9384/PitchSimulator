using System.Collections.Generic;

namespace Game.Core.Placement
{
    // 배치 판정 순수 함수(WarTableSimulator 8절에서 이식, 10-08). 상태 없음, 엔진 없음. 조작(칩 드래그)은 자리와 목록만 넘기고 답만 받는다.
    // 스펙 §8: 격자 없음, 필요한 건 "내 진영 절반 판정"과 "최소 간격 검사" 둘뿐
    public static class PlacementRules
    {
        // 한 번에 답한다. 순서는 영역 → 간격: 영역 밖이면 간격을 볼 이유가 없다. UI는 첫 번째 이유만 보여 주면 된다
        public static PlacementVerdict Evaluate(float x, float z, int team, IReadOnlyList<(float X, float Z)> occupied, float minSpacing)
        {
            if (!IsInsideOwnHalf(x, z, team)) { return PlacementVerdict.OutsideOwnHalf; }
            if (!IsClearOf(x, z, occupied, minSpacing)) { return PlacementVerdict.TooClose; }
            return PlacementVerdict.Ok;
        }

        // 내 진영 절반 = 진영 축(X)에서 중앙선 기준 내 쪽(팀 0은 x < 0), 폭(Z)은 필드 안. 가장자리 여백만큼 안쪽으로 줄인다.
        // 중앙선(x = 0) 위는 어느 쪽도 아니다
        public static bool IsInsideOwnHalf(float x, float z, int team)
        {
            float inner = FieldBounds.HalfLength - FieldBounds.EdgeMargin;
            float sideCoord = team == 0 ? -x : x;

            if (sideCoord < FieldBounds.EdgeMargin) { return false; }
            if (sideCoord > inner) { return false; }
            if (z < -(FieldBounds.HalfWidth - FieldBounds.EdgeMargin)) { return false; }
            if (z > FieldBounds.HalfWidth - FieldBounds.EdgeMargin) { return false; }
            return true;
        }

        // 다른 선수 전부와 최소 간격 이상 떨어져 있는가. 경계 포함(정확히 간격만큼 = 허용). √ 없이 제곱 비교
        public static bool IsClearOf(float x, float z, IReadOnlyList<(float X, float Z)> occupied, float minSpacing)
        {
            if (occupied == null) { return true; }

            float minSq = minSpacing * minSpacing;
            for (int i = 0; i < occupied.Count; i++)
            {
                float dx = occupied[i].X - x;
                float dz = occupied[i].Z - z;
                if (dx * dx + dz * dz < minSq) { return false; }
            }
            return true;
        }
    }
}
