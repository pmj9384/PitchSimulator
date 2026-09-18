using System;
using System.Collections.Generic;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    // 자리·구역 판정 순수 함수(09-18 확정 스펙 §6). 상태 없음, 엔진 없음.
    // 서드는 콜라이더가 아니라 공 X 비교 한 줄이다(잡기 판정과 같은 이유: PhysX 콜백 순서·EditMode 불가).
    // 공은 하나라 서드도 팀당 하나. MatchSimulation이 틱마다 한 번 계산해 컨텍스트에 넣는다.
    public static class PositionRules
    {
        // 공이 있는 세로 구역. attackSign = +1(팀 0, +X 공격)이면 x ≤ −17.5가 우리 진영. 경계는 진영 쪽에 포함
        public static Third ThirdOf(float ballX, int attackSign)
        {
            float forward = ballX * attackSign;   // 우리 골에서 상대 골 쪽으로 잰 값
            if (forward <= -MatchTuning.ThirdBoundary) { return Third.Own; }
            if (forward >= MatchTuning.ThirdBoundary) { return Third.Opponent; }
            return Third.Middle;
        }

        // 역습 판정의 재료: 공보다 상대 골 쪽에 있는 상대 필드 플레이어 수. GK는 뺀다(늘 뒤에 있어 조직 붕괴와 무관)
        public static int CountDefendersAhead(float ballX, IReadOnlyList<TargetInfo> opponents, int attackSign, int keeperId)
        {
            int count = 0;
            float ballForward = ballX * attackSign;
            for (int i = 0; i < opponents.Count; i++)
            {
                if (opponents[i].PlayerId == keeperId) { continue; }
                if (opponents[i].X * attackSign > ballForward) { count++; }
            }
            return count;
        }

        // 아군 소유 때 서는 자리. 배치(공격 시 자리) + X 전진(팀 전진 정도 + 개인 전진 폭) + Z 폭(서드별 팀 폭 배율 × 개인 측면 쏠림).
        // 폭은 선수가 선 쪽(z 부호)으로 벌린다. 중앙(z=0)은 안 벌린다. 오프셋 벡터 길이는 자리 이탈 반경으로 자른다
        public static (float x, float z) AttackHome(float baseX, float baseZ, int attackSign, int mentality, float pushUp, int widthLevel, float width, float roamRadius)
        {
            float dx = (MatchTuning.MentalityOffset[mentality] + pushUp) * attackSign;
            float dz = width * MatchTuning.WidthScale[widthLevel] * Math.Sign(baseZ);
            return Offset(baseX, baseZ, dx, dz, roamRadius);
        }

        // 상대 소유 때 서는 자리. 배치(수비 시 자리)에서 개인 라인 높이만큼 앞으로
        public static (float x, float z) DefendHome(float baseX, float baseZ, int attackSign, float lineHeight)
        {
            return Offset(baseX, baseZ, lineHeight * attackSign, 0f, float.MaxValue);
        }

        private static (float x, float z) Offset(float baseX, float baseZ, float dx, float dz, float roamRadius)
        {
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len > roamRadius && len > 0f)
            {
                float scale = roamRadius / len;
                dx *= scale;
                dz *= scale;
            }
            return (Clamp(baseX + dx, FieldBounds.HalfLength), Clamp(baseZ + dz, FieldBounds.HalfWidth));
        }

        private static float Clamp(float v, float half)
        {
            float limit = half - FieldBounds.EdgeMargin;
            if (v > limit) { return limit; }
            if (v < -limit) { return -limit; }
            return v;
        }
    }
}
