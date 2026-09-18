namespace Game.Core.Match
{
    // 경기 수치의 단일 정의처(09-16 확정 스펙). 전부 현실 축구 수치(m·m/s·초)라 서로 검산이 된다.
    // 3분 경기라는 형식과 현실 템포가 어긋나는 부분은 Tempo 하나로만 흡수한다(11대11 자동 대전에서 조정).
    // 기하(골대·박스)는 FieldBounds, 스탯 스키마는 PlayerStats. 여기는 "규칙에 들어가는 숫자"만.
    public static class MatchTuning
    {
        public const float CaptureRadius = 0.8f;      // 공과 선수 거리가 이 안이면 소유(유저 결정. 발이 닿는 범위)
        public const float BallDeceleration = 4f;     // 비행·굴림 감속 m/s². 잔디 위 15m/s 패스가 30~40m 가서 멈추는 값
        public const float ShotSpeed = 25f;           // 슛 초속 m/s(프로 강슛 약 100km/h)
        public const float DribbleFactor = 0.75f;     // 공을 몰면 이동 속도의 이 비율(엘리트 70~80%)
        public const float SpeedMpsAt50 = 7f;         // speed 스탯 50 = 7m/s(경기 중 달리기). 선형 변환의 기준점
        public const float Tempo = 1f;                // 이동 속도 전체 배율. 3분 경기에 공격 횟수를 맞추는 손잡이
        public const float CatchBase = 0.65f;         // 세이브 중 캐치(GK 소유) 비율. 나머지는 앞으로 튕겨 자유 공
        public const float CatchHandlingScale = 0.2f; // handling 0→100이 캐치 확률을 ±0.2 움직인다(0.45~0.85)
        public const float ParrySpeed = 8f;           // 튕겨 나가는 공의 초속. 박스 근처에 떨어지는 값
        public const float GoalKickOffset = 5.5f;     // 골킥 자리 = 골라인에서 5.5m(골 에어리어 앞)
        public const float ThrowInInset = 1f;         // 스로인 자리 = 터치라인 안쪽 1m
        public const float MaxShotRange = 40f;        // 골 중심에서 이 밖은 슛 확률 0. xG 다항식이 학습 범위 밖(원거리)에서 역주행해서 막는다
        public const float StatLogitScale = 1f;       // 능력치 보정 폭: 스탯 0↔100이 로짓을 ±1 움직인다(오즈 약 2.7배)

        // ── 전술 판정(09-18 확정 스펙). 전부 밸런스 값: 자동 대전 러너로 조정한다
        public const float ThirdBoundary = 17.5f;                              // 필드 105m를 3등분한 경계. 우리 진영 ≤ −17.5 < 중원 < 17.5 ≤ 상대 진영
        public static readonly float[] MentalityOffset = { -5f, 0f, 5f };      // 전진 정도(수비적·균형·공격적) → 공격 시 자리 X 오프셋(m)
        public static readonly float[] WidthScale = { 0.5f, 1f, 1.5f };        // 폭(좁게·표준·넓게) → 개인 측면 쏠림에 곱하는 배율
        public static readonly float[] PassRiskAllow = { 0.2f, 0.5f, 0.8f };   // 패스 리스크(안전·균형·모험) → 허용하는 가로채기 위험도
        public static readonly float[] PassStyleLengthScale = { 0.6f, 1f, 1.8f }; // 패스 방식(짧게·직접·롱볼) → 개인 선호 패스 거리에 곱함
        public static readonly float[] PassSpeed = { 12f, 15f, 18f };           // 속도(느리게·표준·빠르게) → 패스 초속 m/s
        public const float AverageRunSpeed = 7f;                                // 상대 스탯을 모를 때 가로채기 판정에 쓰는 달리기 속도(speed 50)
        public static readonly float[] PressStartScale = { 0f, 1f, 1.5f };      // 압박 시작(안 감·표준·적극) → 개인 압박 거리 배율. 0은 안 씀(ShouldPress가 먼저 거름)
        public const float CounterPressScale = 2f;                              // 역압박 중 개인 압박 거리 배율
        public static readonly int[] CounterPressThreshold = { int.MaxValue, 3, 2 }; // 역압박 성향(안 함·상황 봐서·적극) → 뺏긴 순간 우리 뒤 수비 수 문턱
        public const int CounterPressWindowTicks = 300;                         // 역압박 창 6초(과르디올라 6초 룰, 2차 출처) ÷ 0.02s
        public const float ShotSpreadMin = 2.5f;                                // shot 100의 조준 반폭(m). 골문 반폭 3.66 안
        public const float ShotSpreadMax = 6f;                                  // shot 0의 조준 반폭(m). 골문 밖까지 퍼진다
        public const float FixedStep = 0.02f;                                   // 고정 스텝(초). 볼 끌기 초 → 틱 변환
        public const float PassLeadMax = 8f;                                    // 리드 패스가 리시버보다 앞설 수 있는 최대 거리(m). 너무 앞이면 상대 라인 뒤로 나간다
        public const float DribbleReluctance = 0.3f;                            // 개인 드리블 성향이 이 미만이면 골 대신 가까운 아군 쪽으로 몰아 패스를 노린다
    }
}
