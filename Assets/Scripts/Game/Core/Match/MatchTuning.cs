namespace Game.Core.Match
{
    // 경기 수치의 한 곳에서 정의. 전부 현실 축구 수치(m·m/s·초)라 서로 검산이 된다.
    // 3분 경기라는 형식과 현실 템포가 어긋나는 부분은 Tempo 하나로만 흡수한다(11대11 자동 대전에서 조정).
    // 기하(골대·박스)는 FieldBounds, 스탯 스키마는 PlayerStats. 여기는 "규칙에 들어가는 숫자"만.
    public static class MatchTuning
    {
        public const float CaptureRadius = 0.8f;      // 공과 선수 거리가 이 안이면 소유(유저 결정. 발이 닿는 범위)
        public const float BallDeceleration = 1.2f;   // 비행·굴림 감속 m/s². 천연 잔디 측정 0.45~1.5(Baker 1990, 90% 구간)·FIFA 롤 시험 환산 0.9~1.4의 중간(10-25, 실험/2026-10-25-공-속도-대조.md).
                                                      // 09-23~10-24의 4.0은 "15m/s 패스가 28m 가서 멈추게" 맞춘 설계값이라 공이 빨리 죽어 데굴데굴 보였다(10-25 유저)
        public const float ShotSpeed = 25f;           // 슛 초속 m/s(프로 강슛 약 100km/h)
        public const float DribbleFactor = 0.75f;     // 공을 몰면 이동 속도의 이 비율(엘리트 70~80%). 09-23에 0.6으로 내렸던 건 비행 중 소유 팀 버그로 수비 블록이 무너지던 때 10골을 잡으려던 것. 블록이 서는 09-26엔 0.75로 W(8.4)가 FB(9.1)에 잡힐락 말락, ST(6.3)가 CB(5.6)를 겨우 따돌린다. 개인차는 dribble 다이얼 몫(나중)
        public const float SpeedMpsAt50 = 7f;         // speed 스탯 50 = 7m/s(경기 중 달리기). 선형 변환의 기준점
        public const float Tempo = 1f;                // 이동 속도 전체 배율. 3분 경기에 공격 횟수를 맞추는 손잡이
        public const float CatchBase = 0.5f;          // 세이브 중 캐치(GK 소유) 비율. 나머지는 앞으로 튕겨 자유 공. 0.65 → 0.5(10-23 격자: 펀칭 23 → 31%로 세컨드볼. 09-28 유저 "슛만 차면 다 잡아")
        public const float CatchHandlingScale = 0.2f; // handling 0→100이 캐치 확률을 ±0.2 움직인다(0.3~0.7)
        public const float ParrySpeed = 4.4f;         // 튕겨 나가는 공의 초속. 약 8m 가서 멈춤(√(2·1.2·8)). 감속 4 때 8m/s와 같은 거리(10-25)
        public const float GoalKickOffset = 5.5f;     // 골킥 자리 = 골라인에서 5.5m(골 에어리어 앞)
        public const float ThrowInInset = 1f;         // 스로인 자리 = 터치라인 안쪽 1m
        public const float MaxShotRange = 40f;        // 골 중심에서 이 밖은 슛 확률 0. xG 다항식이 학습 범위 밖(원거리)에서 역주행해서 막는다
        public const float StatLogitScale = 1f;       // 능력치 보정 폭: 스탯 0↔100이 로짓을 ±1 움직인다(오즈 약 2.7배)

        // ── 전술 판정. 전부 밸런스 값: 자동 대전 러너로 조정한다
        public const float ThirdBoundary = 17.5f;                              // 필드 105m를 3등분한 경계. 우리 진영 ≤ −17.5 < 중원 < 17.5 ≤ 상대 진영
        public static readonly float[] MentalityOffset = { -5f, 0f, 5f };      // 전진 정도(수비적·균형·공격적) → 공격 시 자리 X 오프셋(m)
        public static readonly float[] DefenseLineOffset = { -5f, 0f, 5f };    // 수비 라인 높이(깊게·표준·높게) → 수비 시 자리 X 오프셋(m), 필드 선수만. [가정] 전진 정도와 대칭. 10-27 FM Defensive Line. 기본 라인 자체가 현실보다 약 15m 낮은 문제(장부 F3)는 이 레버와 별개
        public const float LateralScoreScale = 0.005f;                          // 리시버 적합도의 측면 가중: |z| 1m당(× 폭 배율). 적합도는 ReceiverFitWeight(0.02)를 곱해 xT 단위로 패스 값에 더한다(11-03 P1, 전진 가중은 xT가 대신 센다). 09-18 0.02는 라인 위 윙어가 어디서든 최고점이라 소유 시간의 31%가 터치라인 9m 안이었다(86% W)
        public const float TouchlineMargin = 3f;                                // 자리 계산의 Z 클램프 여유(m). 필드 가장자리 0.5m로 두면 공 쪽 윙어가 슬라이드로 라인 밖까지 밀려 라인 위(z 33.5)에 붙어 빌드업했다
        public const float WideZoneZ = 25f;                                     // 형태 지표: |z|가 이 이상이면 "측면 구역"(터치라인 9m 안). 러너 CSV·감시 테스트가 소유 시간의 측면 비율을 잰다. 09-26 터치라인 빌드업(31%)을 정지 탐지·리뷰가 못 잡아 넣음
        public const float PositionDialScale = 0.3f;                            // 개인 자리 다이얼 중 측면 쏠림·라인 높이(선수표 0~25)를 미터로 바꾸는 배율. 전진 폭은 미터 그대로(편성 posX가 그 전제)(09-26 Play: 그대로 더하니 FB·W 폭 42·49로 터치라인에 붙고, CB 라인 높이 20이 편성 posX2가 이미 담은 라인 간격 위에 얹혀 수비 4줄이 5m 안에 뭉쳤다). 스펙 §6: 간격은 바둑알(편성)이 정하고 다이얼은 그 위의 미세 조정
        public static readonly float[] WidthScale = { 0.5f, 1f, 1.5f };        // 폭(좁게·표준·넓게) → 개인 측면 쏠림에 곱하는 배율
        public static readonly float[] PassRiskAllow = { 0.2f, 0.5f, 0.8f };   // 패스 리스크(안전·균형·모험) → 허용하는 가로채기 위험도
        public static readonly float[] PassStyleLengthScale = { 0.6f, 1f, 1.8f }; // 패스 방식(짧게·직접·롱볼) → 개인 선호 패스 거리에 곱함
        public static readonly float[] PassAverageSpeed = { 10f, 12.2f, 14.5f };   // 속도(느리게·표준·빠르게) → 패스의 평균 속도 m/s. Metrica 10~20m 패스 775건의 Q1·중앙값·Q3(10-25). 초속·도착은 거리로 역산(PassRules.KickSpeed).
                                                                                 // 09-23~10-24는 "도착 속도" 2/5/8이 입력이라 15m 표준 패스의 평균이 8.5로 실측보다 30% 느렸다
        public const float PassSpeedMax = 22f;                                  // 패스 초속 상한(강한 킥, 슛 25 아래). 평균 14.5·감속 1.2면 2·14.5·(22−14.5)/1.2 ≈ 180m까지 식이 성립해 사실상 안 걸린다
        public const float InterceptRunSpeed = SpeedMpsAt50;                    // 가로채기 판정의 상대 달리기 속도. 09-23 밤까지 5(반응·가속 가정)였는데 당시(09-23) 시뮬의 선수는 가속 없이 즉시 전력이고 추격 예측까지 하니 판정이 실제보다 낙관적이었다. 킥 속도 역산으로 공이 느려지자 "안전" 패스를 압박 선수가 매번 끊는 결정적 왕복(100판 전부 0:0)이 됐다. 시뮬과 같은 값(7)으로  // 10-29부터 이동에 가속이 있고, 그 늦음은 반응 지연(ReactionTime)으로 판정에 따로 넣는다
        public const float InterceptReach = 1.2f;                               // 달려와서 발을 뻗어 공을 건드리는 범위(m). 잡기 반경 0.8보다 넓다(밸런스 값)
        public static readonly float[] PressStartScale = { 0f, 1f, 1.5f };      // 압박 시작(안 감·표준·적극) → 개인 압박 거리 배율. 0은 안 씀(ShouldPress가 먼저 거름)
        public const float CounterPressScale = 2f;                              // 역압박 중 개인 압박 거리에 추가로 곱하는 배율(단계 배율 × 이 값. 표준 2.0·적극 3.0, 10-08)
        public static readonly int[] CounterPressThreshold = { int.MaxValue, 3, 2 }; // 역압박 성향(안 함·상황 봐서·적극) → 뺏긴 순간 우리 뒤 수비 수 문턱
        public const int CounterPressWindowTicks = 300;                         // 역압박 창 6초(과르디올라 6초 룰, 2차 출처) ÷ 0.02s
        public const float ShotSpreadMin = 3f;                                  // shot 100의 조준 반폭(m). 2.5 → 3(10-23 격자): 2.5/6이면 유효 슛 97%라 빗나감이 거의 없었다. 격자는 3.5/7(빗나감 13~15%)이 좋았지만 shot 90의 반폭이 3.85로 골문(3.66)을 넘어 "주사위 0 = 골문 안" 전제(테스트 4개)가 깨져 3으로. shot 90 → 3.4
        public const float ShotSpreadMax = 7f;                                  // shot 0의 조준 반폭(m). 골문 밖까지 퍼진다. 6 → 7(10-23 격자, 위와 같이)
        public const float MatchSeconds = 180f;   // 3분 압축(MatchLength.Compressed): 실시간 3분 = 게임 내 90분(09-27 리뷰 X4). 경기마다 실제 길이는 MatchSimulation.TotalTicks가 정한다
        public const int MatchTicks = 9000;       // = MatchSeconds / FixedStep. float 나눗셈으로 파생하지 않고 정수로 못 박는다(테스트가 둘의 일치를 잠근다)
        public const int RealTimeTicks = 270000;  // 실제 90분(MatchLength.RealTime, 11-05 S1) = 5,400초 / FixedStep. 위와 같이 정수로 못 박는다
        public const int HalfTimeTick = MatchTicks / 2;   // 추가시간이 없을 때의 45:00 틱. 시계 테스트만 쓴다. 실제 하프타임 틱은 표시 추가시간을 반영한 MatchClock.HalfTimeTick이고, 진영 교체·킥오프 교대는 MatchSimulation.Tick이 한다
        public const float FixedStep = 0.02f;                                   // 고정 스텝(초). 볼 끌기 초 → 틱 변환
        public const float PassLeadMax = 8f;                                    // 리드 패스가 리시버보다 앞설 수 있는 최대 거리(m). 너무 앞이면 상대 라인 뒤로 나간다
        // 착지점 후보(09-30 공격 칼날 A): 받는 선수 앞 리드 상한을 긴 것부터 본다. 8m 한 곳만 보던 땐 받는 선수가 상대 수비 라인에서 8m 넘게 떨어져 있어야만 안전 판정을 통과해
        // ST 전진 폭 30 → 33이면 득점 1.05 → 0.13, 라인 높이 +2.4m면 실점 1.11 → 0.16이었다(실험/2026-09-30-공격-칼날-진단.md).
        // 3.5 = gfootball 리드(받는 선수 속도 × 최대 0.5초, 7m/s 기준), 0 = Simple Soccer GetBestPassToReceiver 후보 중 "받는 선수 위치"
        public static readonly float[] PassLeadOptions = { PassLeadMax, 3.5f, 0f };
        public const float DribbleReluctance = 0.3f;                            // 개인 드리블 성향이 이 미만이면 골 대신 가까운 아군 쪽으로 몰아 패스를 노린다
        // 공 지향 슬라이드(PositionRules.SlideAttack·SlideDefend). 지역 방어는 공 기준(Spielverlagerung ball-oriented zonal marking).
        // 수비는 컴팩트(가로 큼), 공격은 침투(세로 큼). 공격 가로만 수축식이고 나머지는 평행이동(10-25, 실험/2026-10-25-슬라이드-수축-격자.md)
        public const float SlideLateralDefend = 0.3f;   // 09-26 0.4 → 0.3: 압박 1명 제한·킥 속도 역산 뒤 블록이 너무 촘촘해 100판 골 0.6. 0.3이면 1.06·슛 10
        public const float SlideVerticalDefend = 0.3f;
        public const float SlideVerticalMaxDefend = 10f;
        public const float SlideLateralAttack = 0.2f;   // 수축 계수. 10-25 격자: 0.18/0.20/0.25에서 4부 승률 46/74/48%로 튐(ST 가로채기 문턱). 0.2는 시드 3블록 73~76%로 재현
        public const float SlideVerticalAttack = 0.4f;
        public const float SlideVerticalMaxAttack = 15f;
        public const float SlideLateralKeeper = 0.15f;                           // GK는 가로만 조금. 라인을 따라 나오면 안 된다
        // 태클(09-21 설계, 유저 승인): 소유자 발 뻗는 범위(InterceptReach) 안의 상대가 시도. FM은 tackling 대 dribbling 대결, 우리는 공이 발에 붙어 대결 판정이 필요
        public const float OnsideMargin = 0.5f;                                 // 공격 자리를 온사이드 선에서 이만큼 뒤로(09-23 오프사이드 위치 회피). 부동소수 경계 여유
        public const float PursuitMaxLookahead = 1f;                            // 추격 예측 상한(초). 목표 = 공 + 공 속도 × min(거리 ÷ (내 속도 + 공 속도), 상한). Simple Soccer pursuit
        public const float TackleRange = 2f;                                     // 태클 사거리(런지·슬라이딩). 발 뻗는 1.2m로는 3분에 접촉 0틱(압박 선수가 3m에 오면 소유자가 먼저 돌림)
        public const float TackleBaseChance = 0.3f;                              // 시도 1회 성공 확률의 기본. × 로지스틱(tackle 스탯). 소유자 저항(볼 컨트롤)은 스탯이 없어 1차 제외
        public const int TackleCooldownTicks = 35;                              // 태클러당 재시도 간격 0.7초. 틱마다 굴리면 확률이 폭주한다. 면역(25)·실패 정지(25)와 같은 값이면 "면역 끝 = 재시도"가 맞물려 핑퐁 리듬이 생긴다
        public const int PossessionImmunityTicks = 25;                          // 소유 뒤 0.5초는 못 뺏김(유저 09-21 "뺏고 나서 몇 초는 바로 못 뺏게"). 붙은 둘이 틱마다 뒤집는 것 방지, 첫 터치에 해당
        public const int TackleFailFreezeTicks = 25;                            // 실패한 태클러는 0.5초 정지(제쳐짐). 실패 비용이 없으면 압박이 공짜다
        public const float ShotValueBase = 1f;                                  // 슛 값 배율 = 기본 + 성향 이득 × (1 − 슛 성향). 슛은 운반·패스와 같은 값 비교에 들어가고 고정 문턱은 없다 [가정 P-G17, 격자로 고정]
        public const float ShotValueBiasGain = 4f;                            // 슛 성향이 낮을수록(포처 0.2) 배율이 이만큼 커서 자주 쏜다. 0이면 슛 성향이 슛 선택에 안 든다
        public const float CounterForwardMargin = 3f;                           // 역습 리시버는 패서보다 이만큼 앞선 아군만(자기만 빼면 뒤로 돌려 핑퐁)
        public const float PressedRadius = TackleRange;                         // 상대가 이 거리 안이면 "압박받는 중"(슛 확률 감점·운반 위험의 붙은 압박). 09-23: 3m였을 땐 압박이 3m 선을 넘는 순간 3틱 만에 안전한 뒤 패스로 도망가 접촉이 0이었다. 태클 사거리와 같게 두면 같은 틱에 태클이 먼저 시도되고 실패해야 돌린다
        public const int PressMinTenureTicks = 50;                              // 압박 1순위를 받으면 이 동안(1초)은 거리로 안 뺏긴다(10-23 probe: 왕복의 45%가 태클 실패 정지 0.5초 뒤 되찾기, 간격 중앙값 0.50초). 후보에서 빠지면(얼음·범위 밖) 즉시 넘어간다 [가정 1초, 러너로 조정]
        public const float LockedMargin = 1000f;                                // 최소 재임 중 히스테리시스 마진. 사실상 무한이라 현 담당이 0순위를 지킨다
        public const float ChaserSwitchMargin = 2f;                             // 추격자 히스테리시스(09-28 F1b): 압박 1순위·루즈볼 추격자는 도전자가 이만큼 더 가까워야 넘겨준다. 출처가 주는 수치는 없어 태클 사거리와 같은 값으로 출발(러너·Play로 조정)
        // 드리블 돌파. 방향 고르기는 HELIOS 드리블 생성기(DribbleRules 주석), 빈도·지속은 출처 없는 출발값이라 러너로 조정한다.
        // 비교 기준: Opta 25/26 EPL 돌파 성공률 평균 36.7%, 돌파 시도가 많은 윙어 90분당 6.5~9.5회(theanalyst "Premier League's Most Impactful Dribblers")
        public const float TakeOnChanceScale = 0.3f;    // 공을 잡을 때 돌파 의도 확률 = 드리블 성향 × 이 값(윙어 0.9 → 27%). 우리 진영 서드에선 안 굴린다. 돌파가 전진 패스를 대신해 늘릴수록 골이 준다. 09-29 러너(상대 사거리 = 태클 2m 기준, 100판·300판 시드 101~·401~): 0.2 1.94·2.06·2.09 / 0.3 2.16·2.02·2.02 / 0.4 2.07·1.85·2.08 → 세 표본 모두 목표(골 2~4) 안인 0.3
        public const int TakeOnMaxTicks = 100;          // 돌파 의도 유지 2초. 그 뒤엔 평소 판단(패스·슛·드리블)
        public const float TakeOnEngageRange = 8f;      // 앞쪽 이 거리 안에 상대가 있을 때만 돌파 동작. 없으면 이미 제친 것
        public const float TakeOnLookahead = 4f;        // 방향 후보 지점까지 거리(m)
        public const float TakeOnAngleStep = 30f;       // 방향 후보 간격(°). HELIOS와 같은 값
        // 체력(09-29 전술 상성 ⑥, FatigueRules 주석). 회복 반감기·effort 구조·최저값은 레퍼런스, 소모·상한 감소는 출처 없는 값이라 러너로 맞췄다:
        // 끝 15분 고속 주행이 체력 없을 때보다 균형 FB -11%p·압박 FB -24%p·압박 W -14%p(레퍼런스 -8~-21%), 최고 속도 변화 0%.
        // 소모 0.02·상한 0.001·최저 0.85는 압박이 더 약해졌지만 균형 러너 골 1.82로 목표 밖이라 버렸다
        public const float HighIntensityFraction = 5.5f / SpeedMpsAt50;   // 체력이 줄어드는 고강도 = 개인 최고 속도의 이 비율 이상(10-29 가속 D). speed 50이면 5.5m/s = 19.8km/h, 트래킹 연구의 고강도 달리기(HSR) 표준 문턱(PMC11534638). 절대값으로 두면 speed 30(4.2m/s) 선수는 영원히 안 지쳐 비율로 둔다
        // 아래 틱당 체력 값 3개는 3분 압축 기준이다. 다른 길이는 MatchLength.TimeScale을 곱한다(반감기는 나눈다, 11-05 S1). 실제 90분 값은 S3에서 다시 잰다
        public const float FatigueDrainPerTick = 0.00216f;       // 고강도 한 틱에 줄어드는 단기 체력(stamina 50 기준). 10-29 가속 D: 실제 속도로 세자 고강도 틱이 늘어 09-30 값(0.012)의 0.18배로 맞췄다(균형 칸 경기 끝 상한 0.77·압박 0.73, 09-30 기준 0.79·0.74)
        public const float FatigueCapLossPerTick = 0.000108f;    // 고강도 한 틱에 내려가는 장기 상한. 소모와 같은 0.18배(10-29). 경기 동안 쌓여 후반 회복 상한이 낮아진다
        public const float FatigueCapMin = 0.5f;                 // 장기 상한의 바닥
        public const float FatigueRecoveryHalfLifeTicks = 95f;   // 회복 반감기. 전력 뒤 에너지(PCr) 재합성 반감기 약 57초(Bogdanis 1995)를 경기 시간으로 본다. 3분 = 90분이라 1틱(0.02초) = 경기 0.6초, 57 ÷ 0.6 ≈ 95틱
        public const float FatigueEffortThreshold = 0.3f;        // 단기 체력이 이 아래면 속도가 준다(RoboCup effort_dec_thr와 같은 비율)
        public const float FatigueEffortMin = 0.9f;              // 속도 배율 바닥(최대 10% 감소). 최고 속도는 거의 안 떨어진다는 레퍼런스라 감소 폭을 RoboCup(바닥 0.6, 40% 감소)보다 훨씬 작게
        // 박스 앞 센터백 전진(09-29 수비 D1): 공이 우리 골라인에서 이 거리 안(박스 폭 안)에 오면 공에 가장 가까운 CB는 개인 압박 거리와 상관없이 압박 후보가 된다.
        // 코칭 원칙: 박스 근처에선 가장 가까운 CB가 나가고 나머지가 메운다(Coaches' Voice "The modern centre-back"). 박스 깊이 + 5m는 출처 없는 출발값
        public const float BoxStepOutDepth = Placement.FieldBounds.PenaltyBoxDepth + 5f;
        // 슈터 압박·슛 블록(09-29 수비 D3, MatchRules.PressuredShotProbability·ShotBlocker 주석). 블록 확률·튕기는 속도는 출처 없는 출발값
        public const float ShotPressureLogit = 0.4f;     // 압박받는 슛의 로짓 감소
        // GK 슛 대응(10-23 유저 Play 3·4차). 규칙은 MatchSimulation.Shoot·IncomingShotFor·ResolveSaveAtLine, 판정은 MatchRules.KeeperCanReach·GoalAimGap·PushWide·AimAwayFromKeeper
        public const float KeeperDiveSpeed = 8f;         // 슛에 반응하는 동안 GK가 라인 교차점(수비 자리 x·공이 그 라인을 지나는 z)으로 가는 속도 m/s(스탯과 무관한 다이빙 버스트). 스탯 30 = 4.2m/s로는 0.5초에 1.5m라 화면에서 제자리 셔플로 보였다(10-23 유저 Play 4차). [가정] 실제 GK는 포스트 쪽 3m를 0.5~0.6초에 닿는다. 격자(20판): 마감 때 공과 간격 0.56 → 0.11m, 빗나감 5 → 1%, 골 불변
        // 골 조준 간격 자체는 MatchRules.GoalAimGap(GK 스탯 속도 × 남은 비행 시간 + 발 반경)에서 파생한다(10-25. 10-23엔 2.5m 고정)
        public const float GoalAimKeeperMargin = 0.5f;   // 그 간격에 더하는 여유(m). [가정] 화면에서 GK 몸이 공을 안 뚫는 최소
        public const float KeeperLineMinFraction = 0.2f; // 슈터 → 골라인 경로에서 GK 라인 비율의 하한. 슈터가 라인에 붙은 슛은 교차점을 경로 20% 지점으로 본다(÷비율 폭주 방지, 10-25) [가정]
        public const int KeeperReactionTicks = 10;       // GK가 슛에 반응해 라인 교차점으로 움직이기 시작하는 지연(0.2초). [가정] 사람 단순 반응 시간 0.2~0.25초. 골로 정해진 슛엔 ×2(못 막을 슛을 "늦게 반응한" 모양으로)
        public const float GoalAimPostInset = 0.3f;      // 골로 정해진 슛의 조준점이 포스트 안쪽으로 두는 여유(m). 포스트에 맞는 모양을 피한다 [가정]
        public const float GoalKickSetupSeconds = 4f;          // 골킥(11-05 유저 Play "골킥 때 수비랑 공을 주고받는다"): GK가 공을 들고 기다리는 시간. 그동안 양 팀이 트리로 자기 자리로 가고 상대는 박스 밖에 머문다(IFAB 16조). [가정] 출처 없는 출발값. 3분 압축이면 경기 시간 2분이라 Play로 본다
        public const float KeeperHoldAfterCatchSeconds = 2f;   // GK가 슛을 잡은 뒤 배급까지 들고 있는 시간. FIFA 규칙은 6초 안(Law 12). 바로 차면 "잡자마자 패스"가 어색했다(10-25 유저 Play) [가정 2초]
        public const float FormationTransitionWarnMeters = 15f;   // 선수의 공격 자리·수비 자리 거리가 이보다 멀면 전술판이 경고(FM26 "둘 나란히" 경고처럼 막지 않음). 4-4-2 공수 차이 5m, 4-4-2↔4-3-3 대응 최대 15m쯤 [가정]
        public const float ShotWideMargin = 0.5f;        // 노골로 정해졌는데 GK가 못 닿을 유효 슛을 포스트 밖으로 미는 거리(m). 골대 안으로 들어가는 노골을 없앤다(10-23 유저 "안 들어가면 골대 안으로 가면 안 된다"). [가정] 출처 없음, 화면에서 확실히 밖으로 보이는 값
        public const float ShotResolveLogitBonus = 0.8f; // 슛 확정 확률에만 더하는 로짓(판정 ShouldShoot은 안 본다). 10-23 인게임 경로 계측: 슛이 현실의 절반(5.9/팀)이라 골 1.44·0:0 22%였다. 0.6 → 0.8은 GK 슛 대응·조준 반폭·압박 재임을 넣은 뒤 격자(최종 4부 20시즌 골 2.13·0:0 9%·무 27%). [가정] 임시 레버
                                                         // 문턱을 낮추면 나쁜 자리에서 일찍 쏴 골이 줄고(0.88), 확정만 올리면 슛 수 그대로 골 2.17·0:0 6%(+0.3 1.76 / +0.9 2.70). 유저 "중간점".
                                                         // 임시 레버: ⑦ 공격 장면·④ 침투로 슛이 늘면 0으로 되돌린다. 기록 실험/2026-10-23-골-가뭄-레버-격자.md
        public const float ShotBlockRange = TackleRange;  // 슛을 막을 수 있는 수비수 거리(발 뻗어 막는 범위 = 태클 사거리)
        public const float ShotBlockConeDeg = 20f;       // 슛 방향 기준 각도
        public const float ShotBlockChance = 0.3f;       // 후보가 있을 때 실제로 막을 확률
        public const float BlockReboundSpeed = 3.3f;     // 막힌 공이 튕겨 나가는 초속(m/s). 약 4.5m 가서 멈춤, 감속 4 때 6m/s와 같은 거리(10-25)
        public const float BlockReboundTurnDeg = 60f;    // 슛 반대 방향에서 비트는 각도. 슈터 쪽 직선이면 슈터가 도로 잡았다
        public const float BlockReboundStart = 0.9f;     // 튕긴 공이 막은 선수에게서 떨어져 출발하는 거리. 잡기 반경(0.8) 밖
        // 공격수 운반(10-08 결정 7, 09-30 B 실험). 11-03 P1+: 자격 조건(역할·서드·슛 사거리)을 없애고 필드 선수 전원이 후보다.
        // 11-03 P1: 앞 4m에 상대가 없으면 무조건 몰던 문턱을 없애고, 운반도 패스처럼 값(ThreatRules.CarryValue)으로 비교한다. 위험은 DribbleRules.CarryRisk
        public const float CarryComfortZone = 6f;                               // 운반 위험을 재는 앞쪽 거리(m). 이 밖의 상대는 위험 0 [가정 P-G1, 격자로 고정]. 옛 문턱은 4m였다
        public const float CarryRiskScale = 2f;                                 // 운반 위험 식의 계수: 가장 가까운 앞 상대 항에 곱한다 [가정 P-G1, 격자로 고정]
        public const float CarryHorizon = 30f;                                  // 운반 값을 재는 구간(m). 골 쪽으로 이만큼 간 점의 xT를 본다. 짧으면 운반의 xT 이득이 패스보다 늘 작아 공격수가 안 몬다 [가정 P-G5]
        public const float ReceiverFitWeight = 0.02f;                          // 거리 적합·측면 가중을 패스 값에 더하는 크기(xT 단위). 팀 전술(패스 방식·폭)이 값에 들어오는 유일한 길이라 0이면 카드 차이가 사라진다 [가정 P-G3, 격자로 고정]
        public const float ReturnPenalty = 0.02f;                               // 방금 나에게 준 선수에게 뒤로 되돌리는 패스의 감점(xT 단위). 핑퐁 방지를 게이트 대신 값으로 한다 [가정 P-G2]
        // 가속(09-29 F2b, 10-29 개인화. MovementRules 주석). 판단 쪽 반응 지연과 이동 가속이 같은 값에서 나온다(이동과 판정이 같은 전제)
        // 공 높이(10-29 ④-1). 로빙만 띄운다. 높이 값은 출처를 못 찾았다(설계초안-공높이-침투 3절) [가정 전부]
        public const float Gravity = 9.81f;
        public const float FieldReachHeight = 2.2f;     // 필드 선수가 공에 닿는 최고 높이(m). 키 1.8m + 헤딩·발 뻗기 [가정]
        public const float KeeperReachHeight = 2.6f;    // 자기 박스 안 GK가 손으로 닿는 높이(m). 크로스바 2.44m 위까지 [가정]
        public const int LobMinPassStyle = 1;           // 로빙을 차는 최소 패스 방식(0 짧게·1 직접·2 롱볼). 짧게 서드는 안 띄운다(11-02: 띄우면 짧게·롱볼 카드의 패스 길이 차이가 사라졌다)
        public const float LobLaunchAngleDeg = 40f;     // 로빙 발사각. 초속은 착지 거리에서 역산(BallRules.LobVelocity) [가정]
        public const float LobMinDistance = 15f;        // 이보다 짧은 패스는 띄우지 않는다 [가정]
        public const float LobMaxDistance = 45f;        // 이보다 긴 패스도 띄우지 않는다(초속 22m/s 근처) [가정]
        public const float ArrivalSigma = 0.45f;        // 도착 시간 차를 확률로 바꾸는 폭(초). Spearman 2017 "Beyond Expected Goals" pitch control의 tti_sigma
        public const float LobBounceSpeedRetention = 0.53f;   // 로빙 착지 때 남는 수평 속도 비율. FIFA Quality Programme(2015) Angle Ball Rebound Pro 건조 45~60%의 중앙. 시험 입사각·속도는 미확인 [가정]
        public const float JogSpeedFraction = 0.6f;     // 자리 잡기 조깅 상한 = 최고 속도 × 이 값. speed 50이면 4.2m/s, 트래킹 속도 구간의 조깅 상한 4m/s(14.4km/h, PMC11534638)와 비슷. [가정] 러너로 오프볼 스프린트 약 80에 맞춘다
        public const float PassSafetyBand = 0.1f;       // 패스 안전 가중의 경계 띠(허용치 ± 이 값에서 1 → 0, PassRules.SafetyWeight). [가정] 출처 없음, 10-25 격자로 확인
        public const float ForwardRunMargin = 1f;       // 아군 소유 중 공격 자리가 이보다 앞이면 전진 런(전력). [가정] 제자리 떨림만 거르는 값
        // 늦음 문턱 이력: 10-29 유저 Play "조깅과 전력 차이가 안 느껴짐". 8m면 5.5m/s 이상 시간의 48%가 늦은 복귀였다(공이 빨리 움직이면 자리도 한 번에 크게 옮겨진다).
        // 12m면 오프볼 스프린트 108 → 87/판(목표 약 80). 16m·항상 조깅은 ST 쏠림 67~70%
        public const float LateToPositionMeters = 12f;  // 자리에서 이보다 멀면 조깅 대신 전력 복귀. [가정] 출처 없음
        public const float AccelerationAt50 = 7f;                                  // speed 50 선수의 가속 m/s². Spearman 2018·Shaw 코드의 amax 7. 정지 출발 10m 1.93초(현실 2.04초)
        public const float ReactionTime = SpeedMpsAt50 / (2f * AccelerationAt50);  // 0.5초. 정지한 선수가 가속으로 잃는 거리 = 최고 속도로 이만큼 늦게 출발한 것(MovementRules.ReactionEquivalent).
                                                                                   // 개인 가속은 이 값이 모든 선수에게 같도록 최고 속도에서 유도한다(MovementRules.AccelerationFor)
        // 사전 런(11-03 ⑤, 설계초안-침투-스루 3절 C·D). 값은 설계초안 9절 격자(러너 100판 × 20칸)로 정했다. 출처가 주는 수치는 없어 범위만 HELIOS(6~30m, ±40°)를 따른다
        // RunPlanInterval: 5틱 주기는 판당 시뮬 시간이 1.33배, 10틱은 1.19배인데 지표 차이는 잡음 안이라 10으로 둔다
        public const int RunPlanInterval = 10;                                       // 런 제안을 새로 찾는 주기(틱). 이미 있는 제안은 매 틱 다시 잰다
        public const int MaxRunners = 1;                                             // 팀당 동시에 사전 런을 하는 인원. SkillCorner 팀·경기당 18.7회는 4.8분에 1번이라 겹침이 드물다
        public const float RunAngleDeg = 40f;                                        // 런 목표 방향의 한쪽 폭(공격 방향 기준). HELIOS 스루 후보 ±40°
        public const int RunDirectionCount = 3;                                      // 방향 후보 수(1·3·5). 가운데부터 좌우로 균등
        public static readonly float[] RunDistances = { 30f, 18f, 10f, 6f };        // 러너 앞 목표 거리 후보(긴 것부터). HELIOS 6~30m 안
        public const float RunSupportDistance = 7f;                                  // 내려와 받기: 패서 앞 이 거리. 코칭 자료(FIFA Training Centre 지원 간격 6~8m, 2차)

        // RunBehindMargin: SkillCorner는 받는 위치가 최종 수비수와 나란하다(0.06m)고 하지만 1m로 두면 윙어가 판당 0.8번만 달린다(공격 자리가 라인에서 25m 뒤). [가정]
        public const float RunBehindMargin = 5f;                                     // 뒤공간 목표는 수비 라인(공 제외)에서 이만큼 뒤까지 인정

        // 스루 위험 허용치(11-03 ⑤): 런 제안 착지점으로 차는 패스에만 쓴다. 땅볼 판정(LandingRisk)은 여유폭 척도라 허용치 0.5에서도 성공이 89%였다.
        // SkillCorner opendata A리그 20경기 749런의 "패스를 받은 런 성공률" 51.9%(339회 중 176, 95% 구간 46.6~57.2%)에 성공률이 가까운 값으로 맞췄다(실측 맞춤 [가정], 격자는 설계초안 9절: 0.5 → 63%, 0.55 → 58%, 0.6 → 59%, 0.65 이상은 72~95%로 튀거나 제안이 사라진다).
        // 팀 PassRisk와의 관계: 균형(0.5) 기준 편차를 그대로 얹는다(PassRules.ThroughAllow). 안전 카드는 스루도 덜 찬다
        public const float ThroughRiskAllow = 0.55f;   // 스루(런 제안 착지점) 위험 허용치 기준값

        // RunArrivalMargin: 실행의 작은 손실(제동·방향 틀기)에도 판정이 안 뒤집히게 하는 여유다. {0, 0.1, 0.2, 0.3} × ThroughRiskAllow 3값 24칸 중 완료 기준을 가장 많이 만족한 칸(설계 A1 8절). [가정]
        public const float RunArrivalMargin = 0.1f;   // 런 판정: 러너가 목표점에 공 도착 시각보다 이만큼 일찍 닿아야 도달 가능
        public const int MaxPressers = 1;                                       // 팀에서 동시에 압박(⑧)하는 인원 상한. 압박 거리 안인 선수를 공 거리순으로 세어 이 안만 간다(09-23 Play: 인원 제한이 없어 우리 진영 "적극"이면 CM 19.5m·FB/W/ST 12m 안 4~6명이 한꺼번에 달려들어 초등학교 경기처럼 뭉쳤다). Simple Soccer는 최근접 1명만 쫓고 FM도 1명 압박 + 커버. 러너에서 태클이 죽으면 2
    }
}
