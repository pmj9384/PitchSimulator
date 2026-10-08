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
        public const float CenterCircleRadius = 9.15f;    // 킥오프 때 상대가 떨어져 있어야 하는 거리(IFAB 1조 센터서클, 8조 킥오프)

        // 선수 몸이 라인 밖으로 걸치지 않게 두는 여백
        public const float EdgeMargin = 0.5f;

        // 선수 사이 최소 간격(스펙 §8 "격자 없음, 최소 간격 검사"). 같은 행 count>1 스폰도 이 간격으로 벌린다
        public const float MinSpacing = 1.2f;

        // 필드 좌표(중앙 원점, +X가 팀 0의 상대 골) ↔ 필드 그림 안 0~1 비율. 전술 화면 칩이 양쪽으로 쓴다(10-08 배치: 드롭 위치를 좌표로 되돌릴 때 ToUnit의 역이어야 칩이 저장 때마다 밀리지 않는다)
        public static (float u, float v) ToUnit(float x, float z)
        {
            return ((x + HalfLength) / (2f * HalfLength), (z + HalfWidth) / (2f * HalfWidth));
        }

        public static (float x, float z) FromUnit(float u, float v)
        {
            return (u * 2f * HalfLength - HalfLength, v * 2f * HalfWidth - HalfWidth);
        }

        // 배치(10-08): 칩이 보이는 자리 = 편성 자리 + 오프셋(전진 정도·개인 다이얼). 놓은 자리에 그대로 서게 하려면 그 오프셋을 뺀 값을 편성 자리로 저장한다
        public static (float x, float z) BaseFromShown(float droppedX, float droppedZ, float shownX, float shownZ, float baseX, float baseZ)
        {
            return (droppedX - (shownX - baseX), droppedZ - (shownZ - baseZ));
        }
    }
}
