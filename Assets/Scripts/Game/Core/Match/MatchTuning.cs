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
        public const float CounterPressScale = 2f;                              // 역압박 중 개인 압박 거리에 추가로 곱하는 배율(단계 배율 × 이 값. 표준 2.0·적극 3.0, 10-08)
        public static readonly int[] CounterPressThreshold = { int.MaxValue, 3, 2 }; // 역압박 성향(안 함·상황 봐서·적극) → 뺏긴 순간 우리 뒤 수비 수 문턱
        public const int CounterPressWindowTicks = 300;                         // 역압박 창 6초(과르디올라 6초 룰, 2차 출처) ÷ 0.02s
        public const float ShotSpreadMin = 2.5f;                                // shot 100의 조준 반폭(m). 골문 반폭 3.66 안
        public const float ShotSpreadMax = 6f;                                  // shot 0의 조준 반폭(m). 골문 밖까지 퍼진다
        public const float MatchSeconds = 180f;   // 스펙 §0: 실시간 3분 = 게임 내 90분. 러너·시즌·인게임·테스트가 전부 이 한 곳을 본다(09-27 리뷰 X4)
        public const int MatchTicks = 9000;       // = MatchSeconds / FixedStep. float 나눗셈으로 파생하지 않고 정수로 못 박는다(테스트가 둘의 일치를 잠근다)
        public const int HalfTimeTick = MatchTicks / 2;   // 추가시간이 없을 때의 45:00 틱. 시계 테스트만 쓴다. 실제 하프타임 틱은 표시 추가시간을 반영한 MatchClock.HalfTimeTick이고, 진영 교체·킥오프 교대는 MatchSimulation.Tick이 한다(09-27)
        public const float FixedStep = 0.02f;                                   // 고정 스텝(초). 볼 끌기 초 → 틱 변환
        public const float PassLeadMax = 8f;                                    // 리드 패스가 리시버보다 앞설 수 있는 최대 거리(m). 너무 앞이면 상대 라인 뒤로 나간다
        // 착지점 후보(09-30 공격 칼날 A): 받는 선수 앞 리드 상한을 긴 것부터 본다. 8m 한 곳만 보던 땐 받는 선수가 상대 수비 라인에서 8m 넘게 떨어져 있어야만 안전 판정을 통과해
        // ST 전진 폭 30 → 33이면 득점 1.05 → 0.13, 라인 높이 +2.4m면 실점 1.11 → 0.16이었다(실험/2026-09-30-공격-칼날-진단.md).
        // 3.5 = gfootball 리드(받는 선수 속도 × 최대 0.5초, 7m/s 기준), 0 = Simple Soccer GetBestPassToReceiver 후보 중 "받는 선수 위치"
        public static readonly float[] PassLeadOptions = { PassLeadMax, 3.5f, 0f };
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
        // 드리블 돌파(09-29). 방향 고르기는 HELIOS 드리블 생성기(DribbleRules 주석), 빈도·지속은 출처 없는 출발값이라 러너로 조정한다.
        // 비교 기준: Opta 25/26 EPL 돌파 성공률 평균 36.7%, 돌파 시도가 많은 윙어 90분당 6.5~9.5회(theanalyst "Premier League's Most Impactful Dribblers")
        public const float TakeOnChanceScale = 0.3f;    // 공을 잡을 때 돌파 의도 확률 = 드리블 성향 × 이 값(윙어 0.9 → 27%). 우리 진영 서드에선 안 굴린다. 돌파가 전진 패스를 대신해 늘릴수록 골이 준다. 09-29 러너(상대 사거리 = 태클 2m 기준, 100판·300판 시드 101~·401~): 0.2 1.94·2.06·2.09 / 0.3 2.16·2.02·2.02 / 0.4 2.07·1.85·2.08 → 세 표본 모두 목표(골 2~4) 안인 0.3
        public const int TakeOnMaxTicks = 100;          // 돌파 의도 유지 2초. 그 뒤엔 평소 판단(패스·슛·드리블)
        public const float TakeOnEngageRange = 8f;      // 앞쪽 이 거리 안에 상대가 있을 때만 돌파 동작. 없으면 이미 제친 것
        public const float TakeOnLookahead = 4f;        // 방향 후보 지점까지 거리(m)
        public const float TakeOnAngleStep = 30f;       // 방향 후보 간격(°). HELIOS와 같은 값
        // 체력(09-29 전술 상성 ⑥, FatigueRules 주석). 회복 반감기·effort 구조·최저값은 레퍼런스, 소모·상한 감소는 출처 없는 값이라 러너로 맞췄다:
        // 끝 15분 고속 주행이 체력 없을 때보다 균형 FB -11%p·압박 FB -24%p·압박 W -14%p(레퍼런스 -8~-21%), 최고 속도 변화 0%.
        // 소모 0.02·상한 0.001·최저 0.85는 압박이 더 약해졌지만 균형 러너 골 1.82로 목표 밖이라 버렸다
        public const float FatigueDrainPerTick = 0.012f;         // 전력 행동 한 틱에 줄어드는 단기 체력(stamina 50 기준). 1초(50틱) 전력이면 1 → 0.4
        public const float FatigueCapLossPerTick = 0.0006f;      // 전력 행동 한 틱에 내려가는 장기 상한. 경기 동안 쌓여 후반 회복 상한이 낮아진다(경기 끝 평균 상한: 균형 0.79·압박 0.74)
        public const float FatigueCapMin = 0.5f;                 // 장기 상한의 바닥
        public const float FatigueRecoveryHalfLifeTicks = 95f;   // 회복 반감기. 전력 뒤 에너지(PCr) 재합성 반감기 약 57초(Bogdanis 1995)를 경기 시간으로 본다. 3분 = 90분이라 1틱(0.02초) = 경기 0.6초, 57 ÷ 0.6 ≈ 95틱
        public const float FatigueEffortThreshold = 0.3f;        // 단기 체력이 이 아래면 속도가 준다(RoboCup effort_dec_thr와 같은 비율)
        public const float FatigueEffortMin = 0.9f;              // 속도 배율 바닥(최대 10% 감소). 최고 속도는 거의 안 떨어진다는 레퍼런스라 감소 폭을 RoboCup(바닥 0.6, 40% 감소)보다 훨씬 작게
        // 박스 앞 센터백 전진(09-29 수비 D1): 공이 우리 골라인에서 이 거리 안(박스 폭 안)에 오면 공에 가장 가까운 CB는 개인 압박 거리와 상관없이 압박 후보가 된다.
        // 코칭 원칙: 박스 근처에선 가장 가까운 CB가 나가고 나머지가 메운다(Coaches' Voice "The modern centre-back"). 박스 깊이 + 5m는 출처 없는 출발값
        public const float BoxStepOutDepth = Placement.FieldBounds.PenaltyBoxDepth + 5f;
        // 슈터 압박·슛 블록(09-29 수비 D3, MatchRules.PressuredShotProbability·ShotBlocker 주석). 블록 확률·튕기는 속도는 출처 없는 출발값
        public const float ShotPressureLogit = 0.4f;     // 압박받는 슛의 로짓 감소
        public const float ShotBlockRange = TackleRange;  // 슛을 막을 수 있는 수비수 거리(발 뻗어 막는 범위 = 태클 사거리)
        public const float ShotBlockConeDeg = 20f;       // 슛 방향 기준 각도
        public const float ShotBlockChance = 0.3f;       // 후보가 있을 때 실제로 막을 확률
        public const float BlockReboundSpeed = 6f;       // 막힌 공이 튕겨 나가는 초속(m/s)
        public const float BlockReboundTurnDeg = 60f;    // 슛 반대 방향에서 비트는 각도. 슈터 쪽 직선이면 슈터가 도로 잡았다(09-29 리뷰)
        public const float BlockReboundStart = 0.9f;     // 튕긴 공이 막은 선수에게서 떨어져 출발하는 거리. 잡기 반경(0.8) 밖
        // 공격수 운반(10-08 결정 7, 09-30 B 실험). 노드는 슛 판단 바로 뒤, 슛 사거리(MaxShotRange) 안에서만.
        // 10-08 러너(균형 300판 합계 골 / ST 전진 20·25 득점): 여유 4m 1.75 / 0.34·0.83, 5m 1.72 / 0.28·0.86, 6m 1.74 / 0.27·0.79, 8m 1.69 / 0.23·0.57.
        // 돌파 뒤·패스 앞에 두고 사거리 제한 없이 6m면 1.86 / 0.10(ST 전진 20이 더 죽음). 기준 없는 때는 1.81 / 0.17·0.47. [가정] 4m
        public const float CarryComfortZone = 4f;                               // 앞쪽 이 거리 안에 상대가 없으면 "위협 없음"으로 보고 몬다
        public static readonly string[] CarryRoles = { "ST", "AM", "W" };       // 운반 규칙을 타는 자리(PlayerTable roleId)
        public const int MaxPressers = 1;                                       // 팀에서 동시에 압박(⑧)하는 인원 상한. 압박 거리 안인 선수를 공 거리순으로 세어 이 안만 간다(09-23 Play: 인원 제한이 없어 우리 진영 "적극"이면 CM 19.5m·FB/W/ST 12m 안 4~6명이 한꺼번에 달려들어 초등학교 경기처럼 뭉쳤다). Simple Soccer는 최근접 1명만 쫓고 FM도 1명 압박 + 커버. 러너에서 태클이 죽으면 2
    }
}
