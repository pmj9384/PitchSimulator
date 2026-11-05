namespace Game.Core.Placement
{
    // 필드 치수와 진영 축의 한 곳에서 정의(스펙 §3: FIFA 표준 105m × 68m, 중앙 원점).
    // 진영 축은 X다. 팀0(플레이어)은 x<0에서 시작해 +X 골을 노리고, 팀1(상대)은 그 반대.
    // 축을 바꾸려면 이 파일의 두 상수와 PositionRules·PressRules·PassRules의 축 판정을 같이 바꾼다.
    public static class FieldBounds
    {
        public const float HalfLength = 52.5f;   // 진영 축(X) 방향 절반
        public const float HalfWidth = 34f;      // 폭(Z) 방향 절반

        // 골대·페널티 박스(FIFA 규격). 골대는 골라인(x = ±HalfLength) 위 z = ±GoalHalfWidth
        public const float GoalHalfWidth = 3.66f;         // 골대 폭 7.32m의 절반. xG 각도 계산의 두 포스트
        public const float PenaltyBoxDepth = 16.5f;       // 골라인에서 필드 안쪽으로
        public const float PenaltyBoxHalfWidth = 20.16f;  // 박스 폭 40.32m의 절반(포스트 안쪽에서 16.5m씩, IFAB 1조. 11-05 20.15 → 20.16). GK 출격 한계(스펙 §5)
        public const float CenterCircleRadius = 9.15f;    // 킥오프 때 상대가 떨어져 있어야 하는 거리(IFAB 1조 센터서클, 8조 킥오프)
        public const float GoalAreaDepth = 5.5f;          // 골 에어리어(IFAB 1조): 골라인에서 5.5m
        public const float GoalAreaHalfWidth = 9.16f;     // 골 에어리어 폭 18.32m의 절반(포스트에서 5.5m씩)
        public const float PenaltySpotDistance = 11f;     // 페널티 마크: 골라인에서 11m
        public const float PenaltyArcRadius = 9.15f;      // 페널티 아크: 페널티 마크에서 9.15m, 박스 밖 부분만 그린다
        public const float GoalHeight = 2.44f;            // 크로스바 아래 높이
        public const float LineWidth = 0.12f;             // 선 폭(IFAB 1조: 12cm 이하)
        public const float GoalPostDiameter = 0.12f;      // 포스트·크로스바 굵기(골라인 폭과 같게). GoalHalfWidth는 포스트 안쪽 면이라 화면 포스트 중심은 그 바깥 반 굵기

        // 선수 몸이 라인 밖으로 걸치지 않게 두는 여백
        public const float EdgeMargin = 0.5f;

        // 선수 사이 최소 간격. 포메이션 템플릿 파서가 슬롯 간격을 검사하고(FormationTemplateParser), 같은 행 count>1 스폰도 이 간격으로 벌린다(MatchAssembler)
        public const float MinSpacing = 1.2f;
    }
}
