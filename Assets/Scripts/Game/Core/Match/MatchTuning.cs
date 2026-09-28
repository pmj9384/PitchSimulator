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
        public const float DribbleFactor = 0.75f;     // 공을 몰면 이동 속도의 이 비율(엘리트 70~80%). 09-23에 0.6으로 내렸던 건 비행 중 소유 팀 버그로 수비 블록이 무너지던 때 10골을 잡으려던 것. 블록이 서는 09-26엔 0.75로 W(8.4)가 FB(9.1)에 잡힐락 말락, ST(6.3)가 CB(5.6)를 겨우 따돌린다. 개인차는 dribble 다이얼 몫(나중)
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
        public const float LateralScoreScale = 0.005f;                          // 리시버 점수의 측면 가중: |z| 1m당(× 폭 배율). 09-18 0.02는 라인 위 윙어에게 +0.62(전진 12m 상당)라 어디서든 윙어가 최고점, 10경기 소유 시간의 31%가 터치라인 9m 안(86% W). 0.005면 +0.16(전진 3m)
        public const float TouchlineMargin = 3f;                                // 자리 계산의 Z 클램프 여유(m). 필드 가장자리 0.5m로 두면 공 쪽 윙어가 슬라이드로 라인 밖까지 밀려 라인 위(z 33.5)에 붙어 빌드업했다(09-26 Play)
        public const float WideZoneZ = 25f;                                     // 형태 지표(09-26): |z|가 이 이상이면 "측면 구역"(터치라인 9m 안). 러너 CSV·감시 테스트가 소유 시간의 측면 비율을 잰다. 09-26 터치라인 빌드업(31%)을 정지 탐지·리뷰가 못 잡아 넣음
        public const float PositionDialScale = 0.3f;                            // 개인 자리 다이얼 중 측면 쏠림·라인 높이(선수표 0~25)를 미터로 바꾸는 배율. 전진 폭은 미터 그대로(편성 posX가 그 전제)(09-26 Play: 그대로 더하니 FB·W 폭 42·49로 터치라인에 붙고, CB 라인 높이 20이 편성 posX2가 이미 담은 라인 간격 위에 얹혀 수비 4줄이 5m 안에 뭉쳤다). 스펙 §6: 간격은 바둑알(편성)이 정하고 다이얼은 그 위의 미세 조정
        public static readonly float[] WidthScale = { 0.5f, 1f, 1.5f };        // 폭(좁게·표준·넓게) → 개인 측면 쏠림에 곱하는 배율
        public static readonly float[] PassRiskAllow = { 0.2f, 0.5f, 0.8f };   // 패스 리스크(안전·균형·모험) → 허용하는 가로채기 위험도
        public static readonly float[] PassStyleLengthScale = { 0.6f, 1f, 1.8f }; // 패스 방식(짧게·직접·롱볼) → 개인 선호 패스 거리에 곱함
        public static readonly float[] PassArrivalSpeed = { 2f, 5f, 8f };       // 속도(느리게·표준·빠르게) → 패스가 목표점에 닿을 때 남는 속도 m/s. 초속은 거리로 역산(PassRules.KickSpeed). 09-23 전엔 초속 12/15/18 고정이라 정지 거리 18/28/40m 밖은 못 미치고 5m 패스는 받는 순간 15→0으로 꺾였다
        public const float PassSpeedMax = 22f;                                  // 패스 초속 상한(강한 킥, 슛 25 아래). 도착 속도 5·감속 4면 57m까지 닿고 그 밖은 못 미쳐 멈춘다
        public const float InterceptRunSpeed = SpeedMpsAt50;                    // 가로채기 판정의 상대 달리기 속도. 09-23 밤까지 5(반응·가속 가정)였는데 이 시뮬의 선수는 가속 없이 즉시 전력이고 추격 예측까지 하니 판정이 실제보다 낙관적이었다. 킥 속도 역산으로 공이 느려지자 "안전" 패스를 압박 선수가 매번 끊는 결정적 왕복(100판 전부 0:0)이 됐다. 시뮬과 같은 값(7)으로
        public const float InterceptReach = 1.2f;                               // 달려와서 발을 뻗어 공을 건드리는 범위(m). 잡기 반경 0.8보다 넓다(밸런스 값)
        public static readonly float[] PressStartScale = { 0f, 1f, 1.5f };      // 압박 시작(안 감·표준·적극) → 개인 압박 거리 배율. 0은 안 씀(ShouldPress가 먼저 거름)
        public const float CounterPressScale = 2f;                              // 역압박 중 개인 압박 거리 배율
        public static readonly int[] CounterPressThreshold = { int.MaxValue, 3, 2 }; // 역압박 성향(안 함·상황 봐서·적극) → 뺏긴 순간 우리 뒤 수비 수 문턱
        public const int CounterPressWindowTicks = 300;                         // 역압박 창 6초(과르디올라 6초 룰, 2차 출처) ÷ 0.02s
        public const float ShotSpreadMin = 2.5f;                                // shot 100의 조준 반폭(m). 골문 반폭 3.66 안
        public const float ShotSpreadMax = 6f;                                  // shot 0의 조준 반폭(m). 골문 밖까지 퍼진다
        public const float MatchSeconds = 180f;   // 스펙 §0: 실시간 3분 = 게임 내 90분. 러너·시즌·인게임·테스트가 전부 이 한 곳을 본다(09-27 리뷰 X4)
        public const int MatchTicks = 9000;
        public const int HalfTimeTick = MatchTicks / 2;   // 45분. 후반 킥오프는 전반 킥오프를 안 한 팀(실제 규칙). 진영 교체는 없다(스펙 §11 1차, 09-27 유저 결정)        // = MatchSeconds / FixedStep. float 나눗셈으로 파생하지 않고 정수로 못 박는다(테스트가 둘의 일치를 잠근다)
        public const float FixedStep = 0.02f;                                   // 고정 스텝(초). 볼 끌기 초 → 틱 변환
        public const float PassLeadMax = 8f;                                    // 리드 패스가 리시버보다 앞설 수 있는 최대 거리(m). 너무 앞이면 상대 라인 뒤로 나간다
        public const float DribbleReluctance = 0.3f;                            // 개인 드리블 성향이 이 미만이면 골 대신 가까운 아군 쪽으로 몰아 패스를 노린다
        public const float BackPassScale = 0.3f;                                // 옆·뒤 아군 후보 점수 배율(플랜 09-22 칸). 전진 5m 이상 앞 후보는 못 이기고, 1~3m 앞 찔끔 후보에겐 이길 수 있다(의도: 핑퐁 방지)
        // 공 지향 슬라이드(09-21 유저: "자리가 완전 고정은 아닌 것 같다"): 자리에 공 좌표 × 계수를 더해 블록이 공을 따라 평행이동한다.
        // 지역 방어는 공 기준(Spielverlagerung ball-oriented zonal marking). 수비는 컴팩트(가로 큼), 공격은 침투(세로 큼). 출발값, 러너로 조정
        public const float SlideLateralDefend = 0.3f;   // 09-26 0.4 → 0.3: 압박 1명 제한·킥 속도 역산 뒤 블록이 너무 촘촘해 100판 골 0.6. 0.3이면 1.06·슛 10
        public const float SlideVerticalDefend = 0.3f;
        public const float SlideVerticalMaxDefend = 10f;
        public const float SlideLateralAttack = 0.2f;
        public const float SlideVerticalAttack = 0.4f;
        public const float SlideVerticalMaxAttack = 15f;
        public const float SlideLateralKeeper = 0.15f;                           // GK는 가로만 조금. 라인을 따라 나오면 안 된다
        // 태클(09-21 설계, 유저 승인): 소유자 발 뻗는 범위(InterceptReach) 안의 상대가 시도. FM은 tackling 대 dribbling 대결, 우리는 공이 발에 붙어 대결 판정이 필요
        public const float OnsideMargin = 0.5f;                                 // 공격 자리를 온사이드 선에서 이만큼 뒤로(09-23 오프사이드 위치 회피). 부동소수 경계 여유
        public const float PursuitMaxLookahead = 1f;                            // 추격 예측 상한(초). 목표 = 공 + 공 속도 × min(거리 ÷ (내 속도 + 공 속도), 상한). Simple Soccer pursuit(09-23)
        public const float TackleRange = 2f;                                     // 태클 사거리(런지·슬라이딩). 발 뻗는 1.2m로는 3분에 접촉 0틱(압박 선수가 3m에 오면 소유자가 먼저 돌림)
        public const float TackleBaseChance = 0.3f;                              // 시도 1회 성공 확률의 기본. × 로지스틱(tackle 스탯). 소유자 저항(볼 컨트롤)은 스탯이 없어 1차 제외
        public const int TackleCooldownTicks = 35;                              // 태클러당 재시도 간격 0.7초. 틱마다 굴리면 확률이 폭주한다. 면역(25)·실패 정지(25)와 같은 값이면 "면역 끝 = 재시도"가 맞물려 핑퐁 리듬이 생긴다(09-23)
        public const int PossessionImmunityTicks = 25;                          // 소유 뒤 0.5초는 못 뺏김(유저 09-21 "뺏고 나서 몇 초는 바로 못 뺏게"). 붙은 둘이 틱마다 뒤집는 것 방지, 첫 터치에 해당
        public const int TackleFailFreezeTicks = 25;                            // 실패한 태클러는 0.5초 정지(제쳐짐). 실패 비용이 없으면 압박이 공짜다
        public const float ShotBiasXgScale = 0.5f;                              // 슛 판정 = xG ≥ 슛 성향(0~1) × 이 값. xG는 정면 11m가 0.18이라 0~0.3이 실용 범위(09-21: 성향 0.2를 그대로 비교하니 3분 슛 0). 09-26 0.3 → 0.5: 포처 문턱 0.06이면 코너(|z| 12) xG 0.09에서 받자마자 쏴 슛 전부가 0.09였다. 0.10이면 정면 15m 안
        public const float CounterForwardMargin = 3f;                           // 역습 리시버는 패서보다 이만큼 앞선 아군만(09-21 리뷰: 자기만 빼면 뒤로 돌려 핑퐁)
        public const float PressedRadius = TackleRange;                         // 상대가 이 거리 안이면 "압박받는 중". 옆·뒤 돌리기는 이때만(09-21). 09-23: 3m였을 땐 압박이 3m 선을 넘는 순간 3틱 만에 안전한 뒤 패스로 도망가 접촉이 0이었다. 태클 사거리와 같게 두면 같은 틱에 태클이 먼저 시도되고 실패해야 돌린다
        public const float BackPassDepthPenalty = 0.01f;                        // 뒤로 1m마다 깎는 점수. 20m 뒤면 -0.2라 깊은 백패스(GK 등)는 0 이하로 떨어져 별도 컷 없이 후보에서 빠진다
        public const float ChaserSwitchMargin = 2f;                             // 추격자 히스테리시스(09-28 F1b): 압박 1순위·루즈볼 추격자는 도전자가 이만큼 더 가까워야 넘겨준다. 출처가 주는 수치는 없어 태클 사거리와 같은 값으로 출발(러너·Play로 조정)
        public const int MaxPressers = 1;                                       // 팀에서 동시에 압박(⑧)하는 인원 상한. 압박 거리 안인 선수를 공 거리순으로 세어 이 안만 간다(09-23 Play: 인원 제한이 없어 우리 진영 "적극"이면 CM 19.5m·FB/W/ST 12m 안 4~6명이 한꺼번에 달려들어 초등학교 경기처럼 뭉쳤다). Simple Soccer는 최근접 1명만 쫓고 FM도 1명 압박 + 커버. 러너에서 태클이 죽으면 2
    }
}
