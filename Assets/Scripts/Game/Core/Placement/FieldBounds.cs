namespace Game.Core.Placement
{
    // 필드 치수와 진영 축의 단일 정의처(스펙 §3: FIFA 표준 105m × 68m, 중앙 원점).
    // 진영 축은 X다. 팀0(플레이어)은 x<0에서 시작해 +X 골을 노리고, 팀1(상대)은 그 반대.
    // 축을 바꾸려면 이 파일의 두 상수와 PlacementRules(2차 이식)의 축 판정 한 줄만 바꾼다.
    public static class FieldBounds
    {
        public const float HalfLength = 52.5f;   // 진영 축(X) 방향 절반
        public const float HalfWidth = 34f;      // 폭(Z) 방향 절반

        // 골대·페널티 박스(FIFA 규격). 골대는 골라인(x = ±HalfLength) 위 z = ±GoalHalfWidth
        public const float GoalHalfWidth = 3.66f;         // 골대 폭 7.32m의 절반. xG 각도 계산의 두 포스트
        public const float PenaltyBoxDepth = 16.5f;       // 골라인에서 필드 안쪽으로
        public const float PenaltyBoxHalfWidth = 20.15f;  // 박스 폭 40.3m의 절반. GK 출격 한계(스펙 §5)

        // 선수 몸이 라인 밖으로 걸치지 않게 두는 여백
        public const float EdgeMargin = 0.5f;

        // 선수 사이 최소 간격(스펙 §8 "격자 없음, 최소 간격 검사"). 같은 행 count>1 스폰도 이 간격으로 벌린다
        public const float MinSpacing = 1.2f;
    }
}
