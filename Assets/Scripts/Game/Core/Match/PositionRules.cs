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

        // 아군 소유 때 서는 자리. 배치(공격 시 자리) + X 전진(팀 전진 정도 + 개인 전진 폭) + Z 폭(서드별 팀 폭 배율 × 개인 측면 쏠림 × 다이얼 배율).
        // 다이얼 배율(PositionDialScale 0.3, 09-26)은 측면 쏠림·라인 높이에만: 선수표 값을 미터로 그대로 더하면 폭이 필드 밖(FB 42·W 49)으로 나가고 수비 라인이 겹친다
        // 폭은 선수가 선 쪽(z 부호)으로 벌린다. 중앙(z=0)은 안 벌린다. 필드 안으로만 자른다.
        // 이탈 반경(roamRadius)으로는 안 자른다(09-21): 전진 폭은 공격 자리를 정의하는 값이고 이탈 반경은 그 자리에서 벗어나는 허용치라 다른 축이다.
        // 09-18 첫 구현이 둘을 묶어 ST(전진 30, 이탈 3)가 3m만 올라가 공격 형태가 자기 진영에 갇혔고 3분 동안 슛이 0이었다
        public static (float x, float z) AttackHome(float baseX, float baseZ, int attackSign, int mentality, float pushUp, int widthLevel, float width)
        {
            float dx = (MatchTuning.MentalityOffset[mentality] + pushUp) * attackSign;   // 전진 폭은 미터 그대로: 편성 posX가 이 전제로 맞춰져 있다(09-21). 0.3을 걸면 공격이 상대 진영에 못 간다(09-26 100판 슛 0.1)
            float dz = width * MatchTuning.PositionDialScale * MatchTuning.WidthScale[widthLevel] * Math.Sign(baseZ);
            return Offset(baseX, baseZ, dx, dz);
        }

        // 상대 소유 때 서는 자리. 배치(수비 시 자리)에서 개인 라인 높이 × 다이얼 배율만큼 앞으로. 라인 간격 자체는 편성 posX2가 정한다(스펙 §6)
        public static (float x, float z) DefendHome(float baseX, float baseZ, int attackSign, float lineHeight)
        {
            return Offset(baseX, baseZ, lineHeight * MatchTuning.PositionDialScale * attackSign, 0f);
        }

        // 공 지향 슬라이드: 자리(공격 시/수비 시 계산 결과)에 공 좌표 × 계수를 더한다. 공 좌표를 그대로 쓰므로 팀 부호가 필요 없다:
        // 공이 상대 진영이면 양 팀 다 그쪽으로(공격 팀은 침투, 수비 팀은 라인 상승), 공이 왼쪽이면 전원 왼쪽으로. 세로는 상한으로 자른다.
        // GK는 가로만 조금(SlideLateralKeeper), 세로 0. 결과는 필드 안으로 클램프
        public static (float x, float z) SlideTowardBall(float homeX, float homeZ, float ballX, float ballZ, bool defending, bool goalkeeper)
        {
            if (goalkeeper)
            {
                return Offset(homeX, homeZ, 0f, ballZ * MatchTuning.SlideLateralKeeper);
            }

            float lateral = defending ? MatchTuning.SlideLateralDefend : MatchTuning.SlideLateralAttack;
            float vertical = defending ? MatchTuning.SlideVerticalDefend : MatchTuning.SlideVerticalAttack;
            float verticalMax = defending ? MatchTuning.SlideVerticalMaxDefend : MatchTuning.SlideVerticalMaxAttack;

            float dx = ballX * vertical;
            if (dx > verticalMax) { dx = verticalMax; }
            if (dx < -verticalMax) { dx = -verticalMax; }
            return Offset(homeX, homeZ, dx, ballZ * lateral);
        }

        private static (float x, float z) Offset(float baseX, float baseZ, float dx, float dz)
        {
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
