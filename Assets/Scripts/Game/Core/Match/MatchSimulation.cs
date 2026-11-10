using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    public enum ShotOutcome { Goal, Caught, Parried, Missed, Blocked }

    public enum PossessionChange { Capture, PassReceived, Intercepted, Turnover }

    // 소유가 바뀐 틱의 보고. 경합·패스·가로채기가 실제로 나는지 콘솔에서 보려고
    public readonly struct PossessionReport
    {
        public readonly int NewOwnerId;
        public readonly int NewOwnerTeam;
        public readonly int PreviousOwnerId;   // NoOwner면 자유 공에서 잡음
        public readonly PossessionChange Kind;
        public readonly float X;
        public readonly float Z;

        public PossessionReport(int newOwnerId, int newOwnerTeam, int previousOwnerId, PossessionChange kind, float x, float z)
        {
            NewOwnerId = newOwnerId;
            NewOwnerTeam = newOwnerTeam;
            PreviousOwnerId = previousOwnerId;
            Kind = kind;
            X = x;
            Z = z;
        }
    }

    // 선수가 공을 찼다(패스·슛). 화면 킥 애니용(10-21, 슛·패스 구분 10-23). 방향을 같이 준다: 슛은 Kick 전에 알리므로(막혀도 찬 것) 그 순간 공 속도는 0이다(10-24 리뷰 🔴)
    public readonly struct KickReport
    {
        public readonly int KickerId;
        public readonly bool IsShot;
        public readonly float DirX;   // 찬 방향(정규화 안 함)
        public readonly float DirZ;

        public KickReport(int kickerId, bool isShot, float dirX, float dirZ)
        {
            KickerId = kickerId;
            IsShot = isShot;
            DirX = dirX;
            DirZ = dirZ;
        }
    }

    // 슛 하나의 결과 보고. MatchManager가 콘솔에 찍고 테스트가 대조한다
    public readonly struct ShotReport
    {
        public readonly int ShooterId;
        public readonly float Probability;   // 유효 슛의 골 확률 p(11-09 B3, 거리·슈터·GK 스탯). 막히거나 빗나간 슛에도 찬 순간의 값을 싣는다
        public readonly ShotOutcome Outcome;

        public ShotReport(int shooterId, float probability, ShotOutcome outcome)
        {
            ShooterId = shooterId;
            Probability = probability;
            Outcome = outcome;
        }
    }

    // 경기 루프의 핵심. 공·선수·점수·주사위를 들고 고정 스텝 한 틱을 돌린다. 엔진 없음(EditMode에서 그대로 굴린다).
    // MatchManager는 "언제 Tick을 부르나"와 화면 반영만 맡는다(09-16 결정: 공 매니저를 따로 두지 않는다).
    // 주사위는 nextRoll로 주입받는다. 경기는 System.Random(seed)을, 테스트는 고정 수열을 준다. 그래서 같은 시드 = 같은 경기.
    public sealed class MatchSimulation
    {
        public BallState Ball { get; internal set; }   // internal: 테스트가 상황을 만들 때만 쓴다(InternalsVisibleTo)
        public IReadOnlyList<PlayerState> Players => players;
        public int HomeGoals { get; private set; }
        public int AwayGoals { get; private set; }

        // 1주차 리트머스 판: 슛이 어떤 결과든 끝나면 킥오프로 되돌린다. ST가 10번 쏘려면 공이 매번 돌아와야 한다.
        // 2주차에 GK 배급(패스)이 생기면 끈다. 골(스펙 §7)과 빗나감(골라인 통과·정지)은 스위치와 무관하게 항상 킥오프이고,
        // 캐치·튕김만 이 스위치를 따른다
        public bool ResetAfterEveryShot { get; set; }

        public event Action<ShotReport>? ShotResolved;
        public event Action<PossessionReport>? PossessionChanged;
        public event Action<KickReport>? BallKicked;   // 선수가 공을 찼다. 화면 킥 애니용. 규칙·결정성과 무관

        public int TickCount => tickCount;                                   // 첫 Tick부터 센 수. 하프타임·HUD 시계의 기준(MatchManager.Ticks와 같은 값)
        public bool IsSecondHalf => tickCount >= HalfTimeTick;
        public MatchLength Length { get; }
        public int TotalTicks => Length.TotalTicks(Added);                  // 이 경기가 도는 틱 수. 매니저·러너·시즌이 이만큼 Tick을 부른다
        public int HalfTimeTick => MatchClock.HalfTimeTick(Added, Length);
        public AddedTime Added { get; private set; } = AddedTime.None;         // 표시 추가시간(하프타임 틱을 정한다). 조립기·매니저가 시드에서 같은 값을 넣는다
        public bool SidesSwitched => sidesSwitched;
        private int tickCount;
        private bool sidesSwitched;
        private int firstKickoffTeam = -1;                                    // 전반 킥오프 팀. 후반은 상대가 찬다. KickoffBy를 안 쓴 경기(리트머스)는 하프타임 없음
        public int TurnoverCount { get; private set; }
        public int TackleAttemptCount { get; private set; }
        public int TackleSuccessCount { get; private set; }
        public bool OwnerImmune => immunityTicksLeft > 0;   // 계측용

        private readonly List<PlayerState> players = new List<PlayerState>();
        private readonly List<TargetInfo> captureCandidates = new List<TargetInfo>();
        private readonly Func<float> nextRoll;
        private readonly BehaviorNode tree;
        private readonly ThreatGrid threat;   // xT 격자(11-03 ⑤). 선수 스냅샷에 실어 트리가 읽는다

        // 팀 전술 2개(스타일 카드). 기본은 "균형"과 같은 빈 값(전부 0)이고 MatchManager가 프리셋을 주입한다
        private readonly TeamTactics[] tactics = { new TeamTactics(), new TeamTactics() };

        // 틱마다 한 번 계산하는 팀 단위 국면: 공은 하나라 서드·역습도 팀당 하나. 22명이 같은 값을 본다
        private readonly List<TargetInfo>[] rosterSnapshot = { new List<TargetInfo>(), new List<TargetInfo>() };
        private readonly List<TargetInfo>[] pressEligible = { new List<TargetInfo>(), new List<TargetInfo>() };   // 팀별 압박 거리 안 선수(압박 순위 계산용)
        private readonly List<RunOffer>[] runOffers = { new List<RunOffer>(), new List<RunOffer>() };   // 팀별 사전 런 제안(11-03 ⑤)
        private readonly int[] runCarrierId = { BallState.NoOwner, BallState.NoOwner };               // 제안을 만든 때의 운반자. 바뀌면 제안을 버린다
        private readonly List<RunCandidate> runCandidateBuffer = new List<RunCandidate>();
        private readonly List<TargetInfo>[] looseCandidates = { new List<TargetInfo>(), new List<TargetInfo>() }; // 팀별 루즈볼 추격 후보(GK·얼음 제외)
        private readonly List<TargetInfo>[] blockCandidates = { new List<TargetInfo>(), new List<TargetInfo>() }; // 팀별 슛 블록 후보(얼음 제외, 09-30 리뷰). 틱 시작 스냅샷과 같은 시점
        // 팀별 "지금 맡은 선수"(09-28 F1b 히스테리시스). 압박 1순위는 상대 소유 중에만, 루즈볼 추격자는 소유 팀이 없을 때만 유지하고 아니면 -1
        private readonly int[] pressLeader = { -1, -1 };
        private readonly int[] pressLeaderSince = { 0, 0 };   // 지금 담당이 맡기 시작한 틱. 최소 재임(PressMinTenureTicks) 판정
        private readonly int[] looseChaser = { -1, -1 };
        // 리시버 마크(11-06 S3 ①): 팀별 이번 틱 배정과 직전 틱 배정(히스테리시스 기준). 틱마다 두 목록을 바꿔 쓴다
        private readonly List<MarkAssignment>[] marks = { new List<MarkAssignment>(), new List<MarkAssignment>() };
        private readonly List<MarkAssignment>[] previousMarks = { new List<MarkAssignment>(), new List<MarkAssignment>() };
        private readonly List<TargetInfo>[] markTargets = { new List<TargetInfo>(), new List<TargetInfo>() };
        private readonly List<TargetInfo>[] markCandidates = { new List<TargetInfo>(), new List<TargetInfo>() };         // 1차 후보 = 뒷줄(11-06 D1)
        private readonly List<TargetInfo>[] markSecondCandidates = { new List<TargetInfo>(), new List<TargetInfo>() };   // 2차 후보 = 둘째 줄 + tracking(marksAsSecond)
        private readonly int[] backLineCount = { 0, 0 };   // 팀별 뒷줄 수 = 마커 상한. FillBackLine이 틱마다 채운다
        private readonly float[] lineDeepest = { 0f, 0f };         // 팀별 가장 깊은 수비 시 자리 깊이(뒷줄 기준). 틱마다 다시 재는 버퍼(할당 없음)
        private readonly float[] secondLineDeepest = { 0f, 0f };   // 팀별 뒷줄 아닌 선수 중 가장 깊은 자리(둘째 줄 기준)
        private readonly float[] lineDepthByTeam = { 0f, 0f };     // 팀별 라인 깊이 L(우리 골라인에서 m, 11-08 D2 LineRules.LineDepth). 틱마다 FillLineDepth가 채운다
        private readonly float[] lineFloorByTeam = { 0f, 0f };     // 팀별 하한 깊이(GK 라인 + FloorMargin). 마크 지점도 이 아래로 안 간다
        private readonly float[] lineShiftByTeam = { 0f, 0f };     // 팀별 이동량 Δ = L − 뒷줄 깊이. 스냅샷 DefendLineShift
        private readonly float[] standCapByTeam = { 0f, 0f };      // 팀별 수비 자리 깊이 상한(공보다 골 쪽, 11-09). 스냅샷 DefendDepthCap
        private readonly List<TargetInfo> markOpponents = new List<TargetInfo>();   // 패스 비행 중 리시버를 도착점에 둔 상대 명부(대상 선정용)
        // 팀 국면 스냅샷 버퍼. 틱마다 새 배열을 만들면 3분에 9,000 × 3번 할당(09-21 전수조사 S5)
        private readonly bool[] counteringByTeam = new bool[2];
        private readonly bool[] counterPressingByTeam = new bool[2];
        private readonly Third[] thirdByTeam = new Third[2];
        private readonly int[] keeperIds = { -1, -1 };
        private int lastOwnerTeam = -1;       // 직전 틱 소유 팀. 바뀌면 턴오버
        private int ticksSinceTurnover = int.MaxValue;
        private int turnoverLoserTeam = -1;   // 방금 공을 잃은 팀(역압박 판정 대상)

        // 비행 중인 슛의 결정된 결과. 골이면 GK 접촉을 무시하고 골라인을 넘긴다
        private bool shotInFlight;
        private int shotTicks;                // 슛이 뜬 뒤 지난 틱. GK 반응 지연(KeeperReactionTicks)의 기준
        private bool shotWillScore;
        private int shooterId;
        private int shooterAttackSign;
        private float shotProbability;
        private float shotAimZ;               // 조준 Z(골 중심 기준). 골문 밖이면 골라인에서 Missed
        private float shotStartX;             // 찬 자리. GK 라인 교차점(BallZAtLine)의 재료
        private float shotStartZ;

        // 비행 중인 패스. 리시버는 스냅샷으로 "나한테 온다"를 알고 마중 나간다
        private bool passInFlight;
        private int passReceiverId = -1;
        private bool passIsRun;               // 비행 중 패스가 런 제안 착지점으로 찬 것인가(트리가 고른 PassChoice.IsRun을 킥 때 받는다). 리시버가 공 추격(감속 없음)으로 받을지 정한다
        private int passPasserId = -1;        // 되돌림 감점용: 받은 선수의 LastPasserId에 기록
        private float passTargetX;
        private float passTargetZ;
        private int ballOwnerAtLastTick = BallState.NoOwner;
        private int immunityTicksLeft;        // 소유 면역: 소유 뒤 이 틱 동안 태클 불가
        private int holdUpTicksLeft;          // 볼 끌기(개인 holdUp): 소유 뒤 킥까지 대기 틱
        internal int HoldUpTicksLeft => holdUpTicksLeft;   // 테스트: 골킥 대기(4초)가 다음 소유자에게 이어지지 않는지 틱 수로 본다
        private bool keeperJustCaught;        // 직전 슛을 GK가 잡았다 → 그 소유의 볼 끌기 대기는 KeeperHoldAfterCatchSeconds(10-25)
        private bool goalKickJustStarted;     // 골킥으로 GK에게 공을 쥐여 줬다 → 그 소유의 볼 끌기 대기는 GoalKickSetupSeconds(11-05)
        private int goalKickKeeperId = BallState.NoOwner;   // 골킥을 들고 있는 GK. 그 GK가 공을 쥐고 있는 동안 상대는 박스 밖에 머문다
        private readonly bool[] keeperAlternate = new bool[2];   // GK 배급 "섞어"의 교대 스위치(팀별). GK 패스가 실행될 때 뒤집고 스냅샷으로 트리에 준다(09-23 R2)
        // 킥 릴리스(09-21 Play 진단): 찬 공은 킥 원점에서 잡기 반경을 벗어난 뒤에야 누구든 잡을 수 있다.
        // 발치에 붙은 상대가 첫 틱(0.3m)에 그 자리에서 가로채 소유가 0.4초마다 뒤집히던 잠금을 막는다
        private float kickOriginX;
        private float kickOriginZ;
        private bool passReleased = true;
        // 찬 선수 가드(09-18, 09-21 재확인): 비행 중엔 소유 팀이 없어 자유 공 분기로 패서가 자기 공을 쫓는다.
        // 릴리스만 있으면 3틱째(공 0.9m, 패서 0.42m 따라옴)에 도로 잡아 0.15초마다 반복됐다. 공이 패서 반경을 벗어날 때까지 패서는 후보에서 뺀다
        private int lastKickerId = BallState.NoOwner;
        // 라인 앞 뒷줄 전진(09-29 수비 D1 박스 앞 → 11-06 D1에 CB 이름 → 뒷줄(LineRules)로 일반화 → 11-09 D2-b 라인 상대 구역). 개인 압박 거리 다이얼로 끌 수 없다(09-30 리뷰)
        private readonly int[] stepOutBackLine = { -1, -1 };   // 이번 틱 라인 앞에서 나설 팀별 뒷줄 선수. 압박 후보 계산 때 채운다
        private int kickoffTakerId = BallState.NoOwner;   // 킥오프 공을 쥔 키커. 첫 킥이나 소유 변경 때 푼다. 스냅샷 IsKickoffTaker로 트리에 준다
        // 드리블 돌파 의도: 공을 잡을 때 드리블 성향으로 한 번 굴린다. 트리가 틱마다 굴리면 같은 소유 안에서 돌파·패스가 번갈아 흔들린다
        private int takeOnOwnerId = BallState.NoOwner;
        private int takeOnTicksLeft;
        public int TakeOnCount { get; private set; }       // 돌파 의도가 선 횟수(계측)
        public int TakeOnLostCount { get; private set; }   // 돌파 의도 중 태클로 뺏긴 횟수(계측). 성공률 = 1 - 뺏김 ÷ 시도의 근사
        // 슛 결정 시간(11-08 S3 1단계): 소유를 얻을 때 퍼스트 터치를 한 번 굴리고, 못 얻으면 준비 틱이 끝날 때까지 트리가 슛을 고르지 않는다(스냅샷 ShotReady).
        // 돌파 의도와 같은 이유로 소유 때 한 번만 굴린다. 트리가 틱마다 굴리면 같은 소유 안에서 슛 가능·불가가 흔들린다
        private int shotPrepOwnerId = BallState.NoOwner;
        private int shotPrepTicksLeft;
        private int firstTouchOwnerId = BallState.NoOwner;   // 이번 소유에서 퍼스트 터치를 얻은 선수. 이 소유의 슛엔 볼 끌기를 안 건다
        public int FirstTouchReadyCount { get; private set; }   // 퍼스트 터치 주사위가 성공한 소유 수(계측)
        public int PassCount { get; private set; }
        public int MarkSwitchCount { get; private set; }   // 마크 배정에서 직전 틱에 없던 (마커, 대상) 쌍이 생긴 수(계측, 11-06 S3 ①). 틱마다 교대하면 폭주한다
        public int InterceptCount { get; private set; }

        // 격자는 전역이 아니라 생성자로 받는다: 생성 지점이 매니저(관전)·조립기(자동 대전)로 둘이라, 전역이면 한쪽이 안 넣을 때 같은 세팅의 판단이 갈린다
        // 경기 길이(11-05 S1)는 생성 때 정하고 바꾸지 않는다. 안 주면 게임 기본값(MatchLength.Default)
        public MatchSimulation(Func<float> nextRoll, BehaviorNode tree, ThreatGrid threat, MatchLength? length = null)
        {
            this.nextRoll = nextRoll;
            this.tree = tree;
            this.threat = threat ?? throw new ArgumentNullException(nameof(threat));
            Length = length ?? MatchLength.Default;
            Ball = BallState.FreeAt(0f, 0f);
        }

        public PlayerState AddPlayer(PlayerState player)
        {
            players.Add(player);
            if (player.IsGoalkeeper) { keeperIds[player.Team] = player.PlayerId; }
            return player;
        }

        public void SetTactics(int team, TeamTactics teamTactics)
        {
            tactics[team] = teamTactics;
        }

        public TeamTactics TacticsOf(int team)
        {
            return tactics[team];
        }

        // 팀 기준 공이 있는 서드·역습 여부. 결과 화면·테스트가 읽는다
        public Third BallThirdOf(int team)
        {
            return PositionRules.ThirdOf(Ball.X, AttackSignOf(team));
        }

        public bool IsCountering(int team)
        {
            if (Ball.Phase == BallPhase.Free || OwnerTeam() != team) { return false; }

            int threshold = tactics[team].CounterThreshold;
            if (threshold < 0) { return false; }

            int sign = AttackSignOf(team);
            int ahead = PositionRules.CountDefendersAhead(Ball.X, rosterSnapshot[1 - team], sign, keeperIds[1 - team]);
            return ahead <= threshold;
        }

        public bool IsCounterPressing(int team)
        {
            if (turnoverLoserTeam != team) { return false; }

            int sign = AttackSignOf(team);
            // 뺏긴 순간 우리 골 쪽에 남은 우리 수비 수 = 공보다 우리 골 쪽에 있는 우리 필드 플레이어(GK 제외)
            int behind = PositionRules.CountDefendersAhead(Ball.X, rosterSnapshot[team], -sign, keeperIds[team]);
            return PressRules.IsCounterPressing(behind, tactics[team].CounterPress, ticksSinceTurnover);
        }

        // 팀별 압박 후보 목록. 누가 후보인지(개인 압박 거리 × 팀 배율, 역압박이면 그 배율 ×2, 라인 앞 뒷줄 전진)는 시뮬이 여기서만 정하고
        // 팀 안에서 공 거리순 순위를 매긴다. 트리는 자기 순위만 보고 상한(MaxPressers) 안일 때만 간다(09-23 뭉침, 09-29 판정 한 곳으로)
        private void FillPressEligible()
        {
            pressEligible[0].Clear();
            pressEligible[1].Clear();
            stepOutBackLine[0] = StepOutBackLine(0);
            stepOutBackLine[1] = StepOutBackLine(1);
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.FrozenTicks > 0) { continue; }   // 태클 실패로 얼어 있는 선수는 순위에서 뺀다: 남겨 두면 1순위를 차지한 채 못 움직여 그 팀 압박이 0.5초 빈다
                if (p.PlayerId == stepOutBackLine[p.Team] || InPressDistance(p))
                {
                    pressEligible[p.Team].Add(new TargetInfo(p.PlayerId, p.X, p.Z));
                }
            }
        }

        // 팀 안 압박 순위. 라인 앞 전진 뒷줄(StepOutBackLine)은 거리와 상관없이 0순위다(11-09 D2-b): MaxPressers 1의 유일한 예외다.
        // 전진 뒷줄도 압박 후보(pressEligible)에 들어 있어 거리순 순위에서 자리를 차지한다. 그래서 "뒤에서 쫓는 1순위 + 앞에서 막는 뒷줄" 집게는 전진 뒷줄이 공에 가장 가깝지 않을 때만 생기고,
        // 가장 가까우면 그 선수 1명만 나간다. 거리순에서 빼서 늘 2명이 가게 할지는 압박 왕복 probe에서 잰 뒤 정한다(11-09 리뷰 🟡, 장부)
        // 스냅샷(트리가 읽음)과 마크 후보(압박자는 마커가 아님)가 이 한 함수를 쓴다
        private int PressRankOf(PlayerState p)
        {
            if (stepOutBackLine[p.Team] == p.PlayerId) { return 0; }
            return PressRules.PressRank(p.PlayerId, pressEligible[p.Team], Ball.X, Ball.Z, pressLeader[p.Team], PressMargin(p.Team));
        }

        private bool InPressDistance(PlayerState p)
        {
            float dx = Ball.X - p.X;
            float dz = Ball.Z - p.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            // 상대 공이 있는 서드 = 내 팀 기준 서드 그대로(공 위치는 하나). 압박 시작 열은 "상대 공이 어디 있나"로 읽는다(트리 ⑧에서 옮겨 온 판정, 09-29)
            int level = tactics[p.Team].PressStart[(int)thirdByTeam[p.Team]];
            return PressRules.ShouldPress(dist, p.Stats.PressRange, level, counterPressingByTeam[p.Team]);
        }

        // 공과 최소 간격(MinLineGap)과 전진 거리(EngageAhead)가 같은 10m라, 간격이 L을 자르는 동안 구역 경계 = 공 깊이다. 부동소수 반올림으로 경계에서 깜빡이지 않게 여유를 둔다
        private const float EngageTolerance = 0.01f;

        // 라인 앞에서 나설 뒷줄 선수(09-29 수비 D1 → 11-09 D2-b). 구역 = 라인 깊이 L + EngageAhead 안, |z| ≤ 박스 반폭. 뽑힌 선수는 압박 0순위(PressRankOf)다.
        // 11-07엔 FB가 안쪽으로 끌려온다고 폭을 ±10.08로 좁혔지만, 라인이 오른 D2에선 측면 운반자를 아무도 앞에서 안 막아 박스 반폭으로 되돌렸다(설계-D2-재설계 결정 4).
        // 공이 전진 구역 밖이거나 얼지 않은 뒷줄이 없으면 -1. 동률은 PlayerId 작은 쪽
        private int StepOutBackLine(int team)
        {
            float zoneDepth = lineDepthByTeam[team] + MatchTuning.EngageAhead + EngageTolerance;
            if (!PressRules.IsInStepOutZone(Ball.X, Ball.Z, AttackSignOf(team), zoneDepth, MatchTuning.EngageHalfWidth)) { return -1; }

            int best = -1;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != team || p.FrozenTicks > 0 || !p.IsBackLine) { continue; }
                float dx = Ball.X - p.X;
                float dz = Ball.Z - p.Z;
                float d2 = dx * dx + dz * dz;
                if (!IsCloserDefender(d2, p.PlayerId, bestD2, best)) { continue; }
                bestD2 = d2;
                best = p.PlayerId;
            }
            return best;
        }

        // 동률은 PlayerId 작은 쪽(PressRules.PressRank와 같은 규칙). 목록 순서에 기대지 않고 명시 비교한다
        private static bool IsCloserDefender(float d2, int playerId, float bestD2, int bestId)
        {
            if (d2 < bestD2) { return true; }
            return d2 == bestD2 && playerId < bestId;
        }

        // 팀별 "지금 맡은 선수" 갱신(09-28 F1b). 직전 틱 담당을 incumbent로 넘겨 도전자가 ChaserSwitchMargin 넘게 가까울 때만 바꾼다.
        // 루즈볼 후보는 우리 GK를 뺀 필드 플레이어(09-23 R3: 출격 못 하는 GK가 최근접이면 아무도 안 쫓던 것)와 얼지 않은 선수. GK 출격은 트리가 따로 본다
        private void UpdateChasers(int ownerTeam)
        {
            bool loose = Ball.Phase != BallPhase.Owned && ownerTeam == -1;
            for (int team = 0; team < 2; team++)
            {
                int candidate = ownerTeam == 1 - team
                    ? PressRules.FirstInRank(pressEligible[team], Ball.X, Ball.Z, pressLeader[team], PressMargin(team))
                    : -1;
                if (candidate != pressLeader[team]) { pressLeaderSince[team] = tickCount; }
                pressLeader[team] = candidate;

                if (!loose)
                {
                    looseChaser[team] = -1;
                    continue;
                }
                List<TargetInfo> candidates = looseCandidates[team];
                candidates.Clear();
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerState p = players[i];
                    if (p.Team != team || p.IsGoalkeeper || p.FrozenTicks > 0) { continue; }
                    candidates.Add(new TargetInfo(p.PlayerId, p.X, p.Z));
                }
                looseChaser[team] = PressRules.FirstInRank(candidates, Ball.X, Ball.Z, looseChaser[team], MatchTuning.ChaserSwitchMargin);
            }
        }

        // 리시버 마크 배정(11-06 S3 ①). 상대 소유이고 공이 우리 골 MarkZoneRadius 안일 때만. 대상 = 우리 골에 가까운 상대 필드 선수(소유자·GK 제외) 뒷줄 수까지,
        // 마커 후보 = 압박 순위 밖(⑧에 안 가는)이고 얼지 않은 우리 뒷줄(1차), 모자라면 둘째 줄 + tracking(2차, 11-06 D1·11-07 둘째 줄 한정). 직전 틱 배정을 넘겨 대상(MarkTargetMargin)·마커(ChaserSwitchMargin) 히스테리시스를 건다.
        // 직전 틱만 기억한다: 발동이 한 틱이라도 꺼지면(공이 MarkZoneRadius 밖·자유 공) 다음 발동은 처음부터 고른다
        private void UpdateMarks(int ownerTeam)
        {
            for (int team = 0; team < 2; team++)
            {
                List<MarkAssignment> last = marks[team];
                marks[team] = previousMarks[team];
                previousMarks[team] = last;
                marks[team].Clear();
                if (ownerTeam != 1 - team) { continue; }
                if (!MarkRules.IsActive(Ball.X, Ball.Z, AttackSignOf(team))) { continue; }

                int ownerId = Ball.Phase == BallPhase.Owned ? Ball.OwnerId : BallState.NoOwner;
                MarkRules.SelectTargets(MarkOpponentsOf(team), ownerId, keeperIds[1 - team], AttackSignOf(team), backLineCount[team],
                    previousMarks[team], MatchTuning.MarkTargetMargin, markTargets[team]);
                FillMarkCandidates(team);
                MarkRules.AssignTiered(markTargets[team], markCandidates[team], markSecondCandidates[team], previousMarks[team], MatchTuning.ChaserSwitchMargin, marks[team]);
                MarkSwitchCount += MarkRules.NewPairs(previousMarks[team], marks[team]);
            }
        }

        // 대상 선정에 쓰는 상대 명부. 상대 패스가 날아가는 중이면 리시버를 도착점에 둔다(마크 지점 MarkPointFor와 같은 점)
        private IReadOnlyList<TargetInfo> MarkOpponentsOf(int team)
        {
            if (!passInFlight) { return rosterSnapshot[1 - team]; }

            MarkRules.WithReceiverAt(rosterSnapshot[1 - team], passReceiverId, passTargetX, passTargetZ, markOpponents);
            return markOpponents;
        }

        // 마커 후보: 트리 ⑧(압박)에 가는 선수는 뺀다. 판정은 스냅샷 PressRank와 같은 호출이라 ⑧과 ⑧b가 한 선수에게 겹치지 않는다.
        // 1차 = 뒷줄(IsBackLine), 2차 = 둘째 줄(IsSecondLine, 뒷줄 바로 앞 한 줄)이면서 수비 역할이 마크 2차를 허용한 선수(DefenseRoles.csv marksAsSecond).
        // 공격수 슬롯에 tracking을 줘도 2차가 못 되는 이유: 공격수가 내려와 마커가 되면 역습 출구가 사라진다(11-07 리뷰). 그 외는 지역 자리(⑨)로
        private void FillMarkCandidates(int team)
        {
            List<TargetInfo> candidates = markCandidates[team];
            List<TargetInfo> second = markSecondCandidates[team];
            candidates.Clear();
            second.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != team || p.IsGoalkeeper || p.FrozenTicks > 0) { continue; }
                if (PressRankOf(p) < MatchTuning.MaxPressers) { continue; }
                if (p.IsBackLine)
                {
                    candidates.Add(new TargetInfo(p.PlayerId, p.X, p.Z));
                    continue;
                }
                if (p.IsSecondLine && p.Stats.MarksAsSecond) { second.Add(new TargetInfo(p.PlayerId, p.X, p.Z)); }
            }
        }

        // 뒷줄·둘째 줄 플래그와 뒷줄 수(11-06 D1, LineRules). 수비 시 자리는 진영 교체 때 부호만 뒤집혀 깊이가 불변이지만, 명부가 틱 사이에 바뀌는 테스트가 있어 틱마다 다시 센다(22명 세 바퀴)
        private void FillBackLine()
        {
            lineDeepest[0] = float.MaxValue;
            lineDeepest[1] = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.IsGoalkeeper) { continue; }
                float depth = LineRules.HomeDepth(p.DefendHomeX, p.AttackSign);
                if (depth < lineDeepest[p.Team]) { lineDeepest[p.Team] = depth; }
            }
            backLineCount[0] = 0;
            backLineCount[1] = 0;
            secondLineDeepest[0] = float.MaxValue;
            secondLineDeepest[1] = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                float depth = LineRules.HomeDepth(p.DefendHomeX, p.AttackSign);
                p.IsBackLine = !p.IsGoalkeeper && LineRules.IsBackLine(depth, lineDeepest[p.Team]);
                if (p.IsBackLine) { backLineCount[p.Team]++; }
                if (p.IsGoalkeeper || p.IsBackLine) { continue; }
                if (depth < secondLineDeepest[p.Team]) { secondLineDeepest[p.Team] = depth; }
            }
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                p.IsSecondLine = !p.IsGoalkeeper && LineRules.IsSecondLine(LineRules.HomeDepth(p.DefendHomeX, p.AttackSign), lineDeepest[p.Team], secondLineDeepest[p.Team]);
            }
        }

        // 팀 라인 깊이 L·하한·이동량 Δ·수비 자리 깊이 상한(11-08 D2, 상한·다이얼·최소 간격은 11-09 설계-D2-재설계.md). 압박 후보(FillPressEligible)보다 먼저 돈다: 전진 구역이 이번 틱 L을 읽는다.
        // 뒷줄 깊이는 다이얼을 얹은 최솟값(DialedRestDepth)이다. 편성이 정해지면 안 바뀌는 값이지만 FillBackLine과 같은 이유(틱 사이에 명부를 바꾸는 테스트)로 틱마다 다시 계산한다. 전술판 미니 피치(TacticsPreview)도 같은 LineRules 식을 쓴다
        private void FillLineDepth()
        {
            for (int team = 0; team < 2; team++)
            {
                int sign = AttackSignOf(team);
                float keeperLineDepth = KeeperLineDepthOf(team, sign);
                float ballDepth = LineRules.HomeDepth(Ball.X, sign);
                float lineDepth = LineRules.LineDepth(ballDepth, tactics[team].DefenseLine, keeperLineDepth);
                lineDepthByTeam[team] = lineDepth;
                lineFloorByTeam[team] = LineRules.LineFloor(keeperLineDepth);
                standCapByTeam[team] = LineRules.TeamStandDepthCap(ballDepth, keeperLineDepth);
                lineShiftByTeam[team] = LineRules.TeamLineShift(ballDepth, tactics[team].DefenseLine, keeperLineDepth, DialedRestDepth(team));
            }
        }

        // 다이얼을 얹은 수비 시 자리 깊이의 팀 최솟값(11-09 D2-a). 가장 깊은 선수가 정확히 L에 선다. 필드 선수가 없으면 float.MaxValue(TeamLineShift가 Δ 0으로 받는다)
        private float DialedRestDepth(int team)
        {
            float deepest = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != team || p.IsGoalkeeper) { continue; }
                deepest = Math.Min(deepest, LineRules.DialedHomeDepth(p.DefendHomeX, p.AttackSign, p.Stats.LineHeight));
            }
            return deepest;
        }

        // GK가 없는 명부(테스트)는 편성 GK 자리 기준 기본값
        private float KeeperLineDepthOf(int team, int sign)
        {
            if (keeperIds[team] == -1) { return MatchTuning.DefaultKeeperLineDepth; }
            return LineRules.HomeDepth(KeeperLineX(FindPlayer(keeperIds[team])), sign);
        }

        // 마커면 대상의 지금 위치·속도와 내 반응 지연(positioning)으로 마크 지점을 넣는다. 아니면 끈다
        private void FillMarkSnapshot(PlayerState p)
        {
            p.IsMarker = false;
            List<MarkAssignment> teamMarks = marks[p.Team];
            for (int i = 0; i < teamMarks.Count; i++)
            {
                if (teamMarks[i].MarkerId != p.PlayerId) { continue; }
                (float x, float z) spot = MarkPointFor(p, FindPlayer(teamMarks[i].TargetId));
                p.IsMarker = true;
                p.MarkX = spot.x;
                p.MarkZ = spot.z;
                return;
            }
        }

        // 지점은 마크 하한(LineRules.MarkFloorDepth = max(하한, min(L, 공 깊이 − 1.5))) 위, 마크 상한(공보다 골 쪽, StandDepthCap) 아래로 자른다(11-08 D2, 상한은 11-09 D2-a).
        // 상한이 없으면 마커가 맡은 상대 앞에 남아, 공을 몬 선수가 라인을 지나도 아무도 골 쪽에 없었다(11-09 probe: 슛 순간 뒷줄 마커 85%가 슈터보다 공 쪽, develop 70%).
        // 상대가 GK 옆까지 내려와도 마커는 하한에 서고, 그 상대는 오프사이드 위치가 된다(11-06 유저 Play).
        // 예외: 날아오는 패스의 리시버를 맡았으면 하한(GK 라인 + 4m)만 지키고 상한도 안 건다. 라인 L로 막으면 라인 뒤로 떨어지는 스루 패스를 마커가 못 따라가 리시버가 혼자 받는다(설계-수비-구조.md 8절 5번)
        private (float x, float z) MarkPointFor(PlayerState marker, PlayerState target)
        {
            (float x, float z) spot = UnflooredMarkPoint(marker, target);
            float x = LineRules.AboveDepth(spot.x, MarkFloorDepthFor(marker, target), marker.AttackSign);
            if (IsFlightReceiver(target)) { return (x, spot.z); }
            float cap = standCapByTeam[marker.Team];
            if (LineRules.HomeDepth(x, marker.AttackSign) > cap) { x = LineRules.XAtDepth(cap, marker.AttackSign); }
            return (x, spot.z);
        }

        private bool IsFlightReceiver(PlayerState target)
        {
            return passInFlight && passReceiverId == target.PlayerId;
        }

        private float MarkFloorDepthFor(PlayerState marker, PlayerState target)
        {
            int team = marker.Team;
            if (IsFlightReceiver(target)) { return lineFloorByTeam[team]; }
            float ballDepth = LineRules.HomeDepth(Ball.X, marker.AttackSign);
            return LineRules.MarkFloorDepth(lineDepthByTeam[team], ballDepth, lineFloorByTeam[team]);
        }

        // 대상이 날아오는 패스의 리시버면 그 패스의 도착점을 기준으로 선다(공의 비행은 모두가 본다). 리드 패스는 리시버 앞 최대 8m라
        // 리시버 지금 자리를 보면 받는 순간 마커가 4m 뒤에 있었다(11-06 진단 받는 순간 최근접 수비 중앙값 4.1m). 아니면 대상의 지금 위치·속도와 내 반응 지연
        private (float x, float z) UnflooredMarkPoint(PlayerState marker, PlayerState target)
        {
            if (passInFlight && passReceiverId == target.PlayerId)
            {
                return MarkRules.MarkPoint(passTargetX, passTargetZ, 0f, 0f, 0f, marker.AttackSign, MatchTuning.MarkDistance);
            }
            float delay = MarkRules.ReactionDelay(marker.Stats.Positioning);
            return MarkRules.MarkPoint(target.X, target.Z, target.VelX, target.VelZ, delay, marker.AttackSign, MatchTuning.MarkDistance);
        }

        // 압박 담당 교체 마진. 담당을 받은 지 최소 재임 안이면 사실상 무한(거리로 못 뺏김). 후보 목록에서 빠진 담당(얼음·범위 밖)은 마진과 무관하게 넘어간다(10-23 ③ probe 뒤 (b)안)
        private float PressMargin(int team)
        {
            if (tickCount - pressLeaderSince[team] < MatchTuning.PressMinTenureTicks) { return MatchTuning.LockedMargin; }
            return MatchTuning.ChaserSwitchMargin;
        }

        // 소유 팀. 비행 중인 패스는 찬 팀의 것이다: -1로 두면 22명 전부 자유 공으로 봐서 최근접 1명이 날아가는 공을 쫓고
        // 나머지는 양 팀 다 공격 자리(⑪)로 갔다가 받으면 돌아오는 왕복이 생겼다(Play: 상대 패스마다 수비 블록이 무너짐).
        // 찬 팀으로 두면 리시버만 마중(⑥), 아군은 자리(⑦), 상대는 압박·수비 자리(⑧·⑨)로 갈리고, 가로채기가 팀 전환으로 잡혀 역압박이 켜진다.
        // 슛·파링 비행은 여전히 -1(누구든 줍는다)
        public int OwnerTeam()
        {
            if (Ball.Phase == BallPhase.Owned) { return FindPlayer(Ball.OwnerId).Team; }
            if (passInFlight) { return FindPlayer(passPasserId).Team; }
            return -1;
        }

        // 킥오프를 하는 팀: 중앙 리셋 뒤 그 팀에서 중앙에 가장 가까운 필드 플레이어가 공을 갖는다. 실제 규칙(시작은 동전, 골 뒤엔
        // 실점 팀)과 같다. 자유 공 경합으로 두면 양 팀 ST가 등거리라 PlayerId 타이브레이크가 매 킥오프를 한 팀에 줬고(먼저 스폰된 팀이
        // 미러 세팅에서 75% 승·상대 0%), 스폰 순서를 뒤집으면 결과가 거울로 뒤집혔다. Kickoff(자유 공)는 리트머스·테스트용으로 남긴다
        // 추가시간은 종료 틱·하프타임 틱을 정하므로 첫 Tick 전에만 넣는다(경기 길이와 같이 경기 중에는 안 바뀐다)
        public void SetAddedTime(AddedTime added)
        {
            if (tickCount > 0) { throw new InvalidOperationException("[MatchSimulation] 추가시간은 첫 Tick 전에만 정한다"); }
            Added = added;
        }

        // 방향의 유일한 출처(09-27, 리뷰 S2). 팀 번호로 부호를 추측하는 코드를 새로 만들지 않는다
        public int AttackSignOf(int team)
        {
            int baseSign = team == 0 ? +1 : -1;
            return sidesSwitched ? -baseSign : baseSign;
        }

        public int TeamOfSign(int attackSign)
        {
            return attackSign == AttackSignOf(0) ? 0 : 1;
        }

        public void KickoffBy(int team)
        {
            if (firstKickoffTeam < 0) { firstKickoffTeam = team; }
            Kickoff();
            int best = BallState.NoOwner;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != team || p.IsGoalkeeper) { continue; }
                float d2 = p.X * p.X + p.Z * p.Z;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = p.PlayerId;
                }
            }
            if (best == BallState.NoOwner) { return; }   // 그 팀 필드 플레이어가 없으면 자유 공

            // IFAB 8조: 공은 센터 마크, 키커가 그 위에 서고 상대는 공에서 9.15m(센터서클) 밖. 전엔 공을 키커 자리(ST, 중앙에서 약 10m)로
            // 옮겨 줘서 센터 킥 없이 그 자리에서 바로 공격이 시작됐다. 모든 선수가 자기 진영에 있는 건 편성 posX(전부 −6 이하)가 이미 지킨다
            PlayerState kicker = FindPlayer(best);
            kicker.X = 0f;
            kicker.Z = 0f;
            Ball = BallRules.Own(Ball, best, 0f, 0f);
            kickoffTakerId = best;
            PushOutOfCenterCircle(1 - team);
        }

        // 수비 팀 선수를 센터서클 밖으로(자기 진영 쪽). 1-톱 포메이션의 ST(posX −6)가 서클 안에 서는 경우
        private void PushOutOfCenterCircle(int team)
        {
            float r = FieldBounds.CenterCircleRadius;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != team) { continue; }
                float d = (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
                if (d >= r) { continue; }
                if (d <= 0f)
                {
                    p.X = -r * AttackSignOf(team);
                    continue;
                }
                p.X = p.X / d * r;
                p.Z = p.Z / d * r;
            }
        }

        public void Kickoff()
        {
            Ball = BallState.FreeAt(0f, 0f);
            shotInFlight = false;
            lastOwnerTeam = -1;
            ticksSinceTurnover = int.MaxValue;
            turnoverLoserTeam = -1;
            passInFlight = false;   // runOffers·passIsRun은 여기서 안 비운다: 공이 Free가 되거나 킥오프 선수에게 쥐어져 다음 틱 UpdateRunOffersOf 가드가 비우고, passIsRun은 passInFlight가 꺼져 읽히지 않는다
            passReceiverId = -1;
            ballOwnerAtLastTick = BallState.NoOwner;
            holdUpTicksLeft = 0;
            keeperJustCaught = false;
            goalKickJustStarted = false;
            goalKickKeeperId = BallState.NoOwner;
            immunityTicksLeft = 0;
            passReleased = true;
            lastKickerId = BallState.NoOwner;
            kickoffTakerId = BallState.NoOwner;
            takeOnOwnerId = BallState.NoOwner;
            takeOnTicksLeft = 0;
            shotPrepOwnerId = BallState.NoOwner;
            shotPrepTicksLeft = 0;
            firstTouchOwnerId = BallState.NoOwner;
            pressLeader[0] = pressLeader[1] = -1;
            pressLeaderSince[0] = pressLeaderSince[1] = tickCount;
            looseChaser[0] = looseChaser[1] = -1;
            for (int team = 0; team < 2; team++)
            {
                marks[team].Clear();
                previousMarks[team].Clear();
            }
            for (int i = 0; i < players.Count; i++)
            {
                players[i].FrozenTicks = 0;
                players[i].TackleCooldownTicks = 0;
                players[i].LastPasserId = BallState.NoOwner;
            }
            for (int i = 0; i < players.Count; i++)
            {
                players[i].ReturnHome();
            }
        }

        // 고정 스텝 한 틱. 종료 틱이면 그 틱의 처리가 끝난 뒤에도 날아가는 슛을 마감한다(경기는 TotalTicks에서 멈춘다: 매니저·러너·시즌이 같은 수만큼 부른다)
        public void Tick(float deltaTime)
        {
            Advance(deltaTime);
            if (tickCount == TotalTicks) { FinishShotAtWhistle(); }
        }

        // 한 틱의 진행. 순서가 곧 규칙이다: 공 이동 → 라인 아웃 → 잡기/소유 → 슛 결과 → 선수 판단 → 선수 실행(PlayerId 순)
        private void Advance(float deltaTime)
        {
            tickCount++;
            if (tickCount == HalfTimeTick && firstKickoffTeam >= 0)
            {
                // 하프타임(IFAB 8조, 09-27): 진영 교체 + 중앙 리셋 + 전반 킥오프를 안 한 팀이 킥오프. 난수를 안 써 결정성 그대로.
                // 러너·시즌·인게임이 같은 Tick을 타므로 같은 시드 = 같은 경기
                FinishShotAtWhistle();   // 리셋 전에: 날아가던 슛의 결과를 먼저 낸다. 골이어도 후반 킥오프는 하프타임 규칙(아래)을 따른다
                for (int i = 0; i < players.Count; i++)
                {
                    players[i].SwitchSides();
                    players[i].Stamina = players[i].StaminaCap;   // 하프타임 휴식: 단기 체력은 다 찬다. 장기 상한은 그대로(후반이 전반보다 무겁다)
                }
                sidesSwitched = true;
                KickoffBy(1 - firstKickoffTeam);
            }

            if (shotInFlight) { shotTicks++; }
            Ball = BallRules.Step(Ball, deltaTime, MatchTuning.BallDeceleration);

            // 골라인에 못 미치고 감속으로 멈춘 슛. 감속 1.2(10-25)면 정지 거리 260m라 필드 안에선 안 일어나는 안전장치다. 빗나감으로 마감해야 옛 슛 표시가 남지 않는다
            if (shotInFlight && Ball.Phase != BallPhase.Flight)
            {
                Finish(ShotOutcome.Missed);
                RestartAfterMiss();
                return;
            }

            if (ResolveShotAtGoalLine()) { return; }
            if (ResetIfOut()) { return; }

            // 패스가 리드 목표에 못 미쳐 감속으로 멈추면 패스는 끝난 것이다(09-23 Play 잠금: 리시버가 ⑥으로 목표점에 서서 공 0.82m 옆에 멈추고,
            // 다른 선수는 그가 최근접이라 안 와서 26초 동안 아무도 못 잡았다). 자유 공으로 넘겨 ⑩ 최근접 추격이 잡게 한다
            if (passInFlight && Ball.Phase == BallPhase.Free)
            {
                passInFlight = false;
                passReceiverId = -1;
            }

            if (Ball.Phase == BallPhase.Free)
            {
                TryCapture();
            }
            else if (Ball.Phase == BallPhase.Flight && shotInFlight && !shotWillScore)
            {
                if (ResolveSaveOnContact()) { return; }
            }
            else if (Ball.Phase == BallPhase.Flight && !shotInFlight)
            {
                // 패스 비행: 리시버가 받거나 상대가 가로챈다. 슛도 패스도 아닌 비행(세이브 뒤 튕긴 공): 누구든 줍는다.
                // 09-21 리뷰: 튕긴 공에 잡기 분기가 없어 멈출 때까지 2초(8m)를 아무도 못 잡았다. 잡기 규칙은 같다(거리 ≤ 0.8)
                TryCapture();
            }

            OnOwnerChanged();
            ResolveTackles();   // 소유 변경 뒤에: 방금 잡은 선수는 같은 틱에 면역을 받는다

            FillSnapshots();

            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                p.ClearIntent();
                tree.Tick(p);
            }

            if (Ball.Phase == BallPhase.Owned)
            {
                BallState still = Ball;   // 소유자가 이번 틱에 안 움직이면 속도 0. 움직이면 Apply의 Carry가 다시 채운다
                still.VelX = 0f;
                still.VelZ = 0f;
                Ball = still;
            }
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                bool carrying = Ball.Phase == BallPhase.Owned && Ball.OwnerId == p.PlayerId;
                float beforeX = p.X;
                float beforeZ = p.Z;
                Apply(p, deltaTime);
                float moved = Distance(beforeX, beforeZ, p.X, p.Z);
                UpdateStamina(p, IsHighIntensity(p, moved / deltaTime, carrying && moved > 0f), Length.TimeScale);
            }
            KeepGoalKickBoxClear();
        }

        // 고강도 틱(10-29 가속 D): 이번 틱 실제 속도가 개인 최고 속도 × HighIntensityFraction 이상, 또는 공을 몰고 움직였다.
        // 전엔 "전력 행동 목록"(압박 1순위·루즈볼 추격·패스 마중·공 몰기)만 셌다. 이동에 가속·조깅이 생겨 실제 속도로 구분할 수 있게 됐고,
        // 그래서 공격 전진 런·늦은 복귀도 자연히 센다(10-29 유저: 체력 부담이 수비 설정에만 반응). 공 몰기는 최고 속도의 0.75라 문턱 아래지만
        // 드리블은 같은 속도의 달리기보다 힘이 더 든다(Reilly & Ball 1984, 원문 미확인)
        private static bool IsHighIntensity(PlayerState p, float actualSpeed, bool carriedAndMoved)
        {
            if (carriedAndMoved) { return true; }
            return actualSpeed >= MatchRules.SpeedMps(p.Stats.Speed) * MatchTuning.HighIntensityFraction;
        }

        private static float Distance(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // 틱당 체력 값은 3분 압축 기준이라 경기 길이의 TimeScale로 맞춘다(압축이면 1이라 그대로)
        private static void UpdateStamina(PlayerState p, bool exerted, float timeScale)
        {
            if (!exerted)
            {
                p.Stamina = FatigueRules.Recover(p.Stamina, p.StaminaCap, MatchTuning.FatigueRecoveryHalfLifeTicks / timeScale);
                return;
            }
            (float stamina, float cap) next = FatigueRules.Exert(p.Stamina, p.StaminaCap, p.Stats.Stamina,
                MatchTuning.FatigueDrainPerTick * timeScale, MatchTuning.FatigueCapLossPerTick * timeScale, MatchTuning.FatigueCapMin);
            p.Stamina = next.stamina;
            p.StaminaCap = next.cap;
        }

        // 틱마다 한 번: 명부 위치 스냅샷 → 턴오버 감지 → 팀 국면(서드·역습·역압박) → 22명에게 같은 값을 넣는다
        private void FillSnapshots()
        {
            if (takeOnTicksLeft > 0) { takeOnTicksLeft--; }
            rosterSnapshot[0].Clear();
            rosterSnapshot[1].Clear();
            blockCandidates[0].Clear();
            blockCandidates[1].Clear();
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                TargetInfo info = new TargetInfo(p.PlayerId, p.X, p.Z);
                rosterSnapshot[p.Team].Add(info);
                // 태클에 실패해 얼어 있는 선수는 제쳐진 상태라 슛을 못 막는다. 압박·루즈볼 후보와 같은 규칙
                if (p.FrozenTicks == 0) { blockCandidates[p.Team].Add(info); }
            }

            int ownerTeam = OwnerTeam();
            if (ownerTeam != -1 && lastOwnerTeam != -1 && ownerTeam != lastOwnerTeam)
            {
                ticksSinceTurnover = 0;
                turnoverLoserTeam = lastOwnerTeam;
            }
            else if (ticksSinceTurnover < int.MaxValue)
            {
                ticksSinceTurnover++;
            }
            if (ownerTeam != -1) { lastOwnerTeam = ownerTeam; }

            for (int team = 0; team < 2; team++)
            {
                counteringByTeam[team] = IsCountering(team);
                counterPressingByTeam[team] = IsCounterPressing(team);
                thirdByTeam[team] = BallThirdOf(team);
            }
            FillBackLine();
            FillLineDepth();
            FillPressEligible();
            UpdateChasers(ownerTeam);
            UpdateMarks(ownerTeam);
            UpdateRunOffers();

            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                int other = 1 - p.Team;
                p.Ball = Ball;
                p.BallOwnerTeam = ownerTeam;
                p.Tactics = tactics[p.Team];
                p.BallThird = thirdByTeam[p.Team];
                p.DefendLineShift = lineShiftByTeam[p.Team];
                p.DefendDepthCap = standCapByTeam[p.Team];
                p.IsCountering = counteringByTeam[p.Team];
                p.IsCounterPressing = counterPressingByTeam[p.Team];
                p.PressRank = PressRankOf(p);
                p.IsLooseBallChaser = looseChaser[p.Team] == p.PlayerId;
                FillMarkSnapshot(p);
                p.RunOffers = runOffers[p.Team];
                p.Threat = threat;
                p.IsKickoffTaker = kickoffTakerId == p.PlayerId;
                p.WantsTakeOn = takeOnOwnerId == p.PlayerId && takeOnTicksLeft > 0;
                bool inShotPrep = shotPrepOwnerId == p.PlayerId && shotPrepTicksLeft > 0;
                p.ShotReady = !inShotPrep;
                p.Teammates = TeammatesExcluding(p);
                p.Opponents = rosterSnapshot[other];
                p.IsKeeperAlternate = keeperAlternate[p.Team];
                p.OpponentKeeper = keeperIds[other] == -1 ? null : FindPlayer(keeperIds[other]).Stats;
                p.OpponentKeeperId = keeperIds[other];
                p.IsPassTarget = passInFlight && passReceiverId == p.PlayerId;
                p.IsRunPass = p.IsPassTarget && passIsRun;
                p.PassTargetX = passTargetX;
                p.PassTargetZ = passTargetZ;
                FillKeeperShotSnapshot(p);
            }
            // 슛 준비 틱은 스냅샷을 넣은 뒤에 준다: 소유한 틱을 포함해 정확히 PrepTicks 스냅샷 동안 ShotReady가 거짓(볼 끌기와 같은 N틱).
            // 돌파 의도는 앞에서 줄여 1틱 짧은데, 그쪽은 상수 뜻이 "2초 안팎"이라 그대로 둔다
            if (shotPrepTicksLeft > 0) { shotPrepTicksLeft--; }
        }

        // 사전 런 제안(11-03 ⑤): 팀의 공격 소유자(필드 플레이어)가 공을 쥐고 있는 동안만 있다. 공이 비행·자유 공이 되거나 운반자가 바뀌면 버린다
        // (비행 중엔 ⑥ 리시버 마중이 이어받는다). 이미 있는 제안은 매 틱 다시 재고, 새 제안은 RunPlanInterval 틱마다 찾는다(비용)
        private void UpdateRunOffers()
        {
            for (int team = 0; team < 2; team++)
            {
                UpdateRunOffersOf(team);
            }
        }

        private void UpdateRunOffersOf(int team)
        {
            List<RunOffer> offers = runOffers[team];
            if (Ball.Phase != BallPhase.Owned)
            {
                ClearRunOffers(team);
                return;
            }

            PlayerState carrier = FindPlayer(Ball.OwnerId);
            if (carrier.Team != team || carrier.IsGoalkeeper || carrier.PlayerId == kickoffTakerId)
            {
                ClearRunOffers(team);
                return;
            }
            if (runCarrierId[team] != carrier.PlayerId)
            {
                offers.Clear();
                runCarrierId[team] = carrier.PlayerId;
            }

            int third = (int)thirdByTeam[team];
            TeamTactics teamTactics = tactics[team];
            float allow = PassRules.ThroughAllow(teamTactics.PassRisk[third], carrier.Stats.PassRisk);   // 런 제안은 스루 허용치(11-03 ⑤)
            float average = MatchTuning.PassAverageSpeed[teamTactics.Tempo[third]];
            IReadOnlyList<TargetInfo> opponents = rosterSnapshot[1 - team];

            KeepRunOffers(team, carrier, average, allow, opponents);
            if (tickCount % MatchTuning.RunPlanInterval != 0) { return; }
            FindRunOffers(team, carrier, average, allow, opponents);
        }

        private void ClearRunOffers(int team)
        {
            runOffers[team].Clear();
            runCarrierId[team] = BallState.NoOwner;
        }

        private void KeepRunOffers(int team, PlayerState carrier, float average, float allow, IReadOnlyList<TargetInfo> opponents)
        {
            List<RunOffer> offers = runOffers[team];
            for (int i = offers.Count - 1; i >= 0; i--)
            {
                PlayerState runner = FindPlayer(offers[i].RunnerId);
                RunOffer kept = RunRules.Reevaluate(offers[i], carrier.X, carrier.Z, average, allow, opponents, RunCandidateOf(runner));
                if (kept.RunnerId == RunOffer.NoRunner || runner.FrozenTicks > 0)
                {
                    offers.RemoveAt(i);
                    continue;
                }
                offers[i] = kept;
            }
        }

        private void FindRunOffers(int team, PlayerState carrier, float average, float allow, IReadOnlyList<TargetInfo> opponents)
        {
            List<RunOffer> offers = runOffers[team];
            if (offers.Count >= MatchTuning.MaxRunners) { return; }

            runCandidateBuffer.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != team || p.IsGoalkeeper || p.FrozenTicks > 0) { continue; }
                if (p.Stats.RunStyle != RunRules.StyleBehind && p.Stats.RunStyle != RunRules.StyleShort) { continue; }
                runCandidateBuffer.Add(RunCandidateOf(p));
            }
            if (runCandidateBuffer.Count == 0) { return; }

            bool pressed = IsPressed(carrier);
            int third = (int)thirdByTeam[team];
            int passStyle = tactics[team].PassStyle[third];
            int widthLevel = tactics[team].Width[third];
            while (offers.Count < MatchTuning.MaxRunners)
            {
                RunOffer offer = RunRules.FindOffer(carrier.X, carrier.Z, carrier.AttackSign, average, allow, pressed, opponents, runCandidateBuffer, offers, carrier.PlayerId,
                    threat, passStyle, carrier.Stats.PassLength, widthLevel);
                if (offer.RunnerId == RunOffer.NoRunner) { return; }
                offers.Add(offer);
            }
        }

        private static RunCandidate RunCandidateOf(PlayerState p)
        {
            return new RunCandidate(p.PlayerId, p.X, p.Z, p.VelX, p.VelZ, MatchRules.SpeedMps(p.Stats.Speed), p.Stats.RunStyle,
                FatigueRules.Effort(p.Stamina, MatchTuning.FatigueEffortThreshold, MatchTuning.FatigueEffortMin));
        }

        // 동료 목록에서 나를 뺀 뷰. 22명 × 틱마다 새 리스트는 GC를 부르므로 선수마다 버퍼를 갖는다
        private readonly Dictionary<int, List<TargetInfo>> teammateBuffers = new Dictionary<int, List<TargetInfo>>();

        private IReadOnlyList<TargetInfo> TeammatesExcluding(PlayerState p)
        {
            List<TargetInfo> buffer;
            if (!teammateBuffers.TryGetValue(p.PlayerId, out buffer))
            {
                buffer = new List<TargetInfo>();
                teammateBuffers[p.PlayerId] = buffer;
            }
            buffer.Clear();
            List<TargetInfo> roster = rosterSnapshot[p.Team];
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].PlayerId != p.PlayerId) { buffer.Add(roster[i]); }
            }
            return buffer;
        }

        // ── 선수 실행
        private void Apply(PlayerState p, float deltaTime)
        {
            if (p.FrozenTicks > 0)
            {
                p.Stop();   // 실패한 태클러는 제쳐져 있다(09-21). 멈춘 채로 풀리면 다시 가속부터(09-29 F2b)
                return;
            }

            bool owner = Ball.Phase == BallPhase.Owned && Ball.OwnerId == p.PlayerId;

            // 볼 끌기(개인 holdUp): 소유 직후엔 킥 의도를 대기 틱 동안 무시한다. 타깃맨이 버티는 시간
            if (owner && holdUpTicksLeft > 0 && HoldsUpKick(p))
            {
                holdUpTicksLeft--;
                p.Stop();   // 볼 끌기 = 공을 발밑에 두고 버틴다
                return;
            }

            if (p.WantsShoot && owner)
            {
                Shoot(p);
                p.Stop();   // 킥은 디딤발을 딛고 찬다. 다음 이동은 다시 가속부터
                return;
            }

            if (p.WantsPass && owner && p.PassReceiverId != BallState.NoOwner && p.PassReceiverId != p.PlayerId)
            {
                Pass(p, p.PassReceiverId, p.PassLandingX, p.PassLandingZ, p.PassIsLob, p.PassIsRun);
                p.Stop();   // 슛과 같다. 차고 나면 다시 가속부터
                return;
            }

            (float x, float z, float vx, float vz) next = NextMotion(p, owner, deltaTime);
            p.X = next.x;
            p.Z = next.z;
            p.VelX = next.vx;
            p.VelZ = next.vz;

            if (owner)
            {
                Ball = BallRules.Carry(Ball, p.X, p.Z);
            }
        }

        // 이번 틱 이동(10-29 가속): 속도 상한·가속을 정하고 MovementRules에 넘긴다. 가속은 스탯 최고 속도에서(피로는 최고 속도만 깎고 가속은 안 깎는다)
        private (float x, float z, float vx, float vz) NextMotion(PlayerState p, bool owner, float deltaTime)
        {
            float topSpeed = MatchRules.SpeedMps(p.Stats.Speed);
            float accel = MovementRules.AccelerationFor(topSpeed, MatchTuning.ReactionTime);
            bool diving = IsKeeperDiving(p);
            float vx = p.VelX;
            float vz = p.VelZ;
            if (!diving)
            {
                // 다이빙이 끝난 GK는 8m/s가 남아 스탯 가속으로 천천히 줄며 미끄러졌다(10-29 리뷰). 다이빙은 버스트라 끝나면 최고 속도로 자른다
                (vx, vz) = MovementRules.ClampSpeed(vx, vz, topSpeed);
            }
            if (!p.WantsMove)
            {
                return MovementRules.Brake(p.X, p.Z, vx, vz, accel, deltaTime);
            }

            float speed = topSpeed * FatigueRules.Effort(p.Stamina, MatchTuning.FatigueEffortThreshold, MatchTuning.FatigueEffortMin);
            if (p.MoveMode == MoveMode.Jog)
            {
                speed = MovementRules.PositioningSpeed(speed, Distance(p.X, p.Z, p.MoveTargetX, p.MoveTargetZ), MatchTuning.JogSpeedFraction, MatchTuning.LateToPositionMeters);
            }
            if (diving)
            {
                // 슛 대응은 다이빙 버스트. 피로·스탯·가속 무관(KeeperCanReach가 즉시 이 속도로 본다. 같은 전제여야 "닿는다"고 본 슛에 실제로 닿는다).
                // 가속 한도를 한 틱에 다이빙 속도가 붙는 값으로 둔다. 골로 정해진 슛엔 스탯 속도·가속으로 한 발 늦게
                speed = MatchTuning.KeeperDiveSpeed;
                accel = MatchTuning.KeeperDiveSpeed / deltaTime;
            }
            if (owner) { speed *= MatchTuning.DribbleFactor; }
            bool arrive = p.MoveMode != MoveMode.Run;
            return MovementRules.Step(p.X, p.Z, vx, vz, p.MoveTargetX, p.MoveTargetZ, speed, accel, deltaTime, arrive);
        }

        private bool IsKeeperDiving(PlayerState p)
        {
            return p.IsGoalkeeper && p.IncomingShot && !shotWillScore;
        }

        // 슛(11-09 묶음 B 직렬): ① 블록(슛 선 안 수비, 거리 곡선 주사위) → ② 조준 → ③ 유효 슛 골/세이브 주사위 → 비행. 확률은 MatchRules, 주사위는 여기서만.
        // 결과는 찬 순간 정해지고(shotWillScore) 도착할 때 확정 표시된다. 블록은 조준보다 먼저라 막힌 슛은 조준·골 주사위를 안 쓴다
        private void Shoot(PlayerState shooter)
        {
            PlayerState? keeper = FindGoalkeeper(1 - shooter.Team);
            int reflexes = keeper != null ? keeper.Stats.Reflexes : 0;
            int diving = keeper != null ? keeper.Stats.Diving : 0;
            shooterId = shooter.PlayerId;
            takeOnOwnerId = BallState.NoOwner;
            shooterAttackSign = shooter.AttackSign;
            shotStartX = shooter.X;
            shotStartZ = shooter.Z;
            shotTicks = 0;
            float goalX = FieldBounds.HalfLength * shooter.AttackSign;

            // 골 확률은 xG가 아니라 유효 슛 조건부 확률(B3). 압박은 여기가 아니라 조준 오차(σ × 1.25)에 든다. 트리 판정(ShotChance)은 xG + 압박 로짓 그대로
            float shotDistance = MatchRules.ShotDistance(shooter.X, shooter.Z, shooter.AttackSign);
            shotProbability = MatchRules.GoalOnTargetProbability(shotDistance, shooter.Stats.Shot, reflexes, diving);

            // 조준 목표(주사위 없음): GK 반대쪽 구석. 블록 선도 이 목표로 본다(오차는 그 뒤. 블로커가 서는 2~4m 앞에서 각 오차 5~9°는 수십 cm)
            float side = AimSideFor(keeper, shooter, goalX);
            float targetZ = side * (FieldBounds.GoalHalfWidth - MatchTuning.AimInset);

            // ① 블록
            if (TryBlockShot(shooter, goalX, targetZ)) { return; }

            // ② 조준: 목표 ± 거리 × tan(σθ(shot)) 정규 오차(주사위 2개). 압박이면 σ × 1.25. 골문 밖이면 골라인에서 빗나감으로 마감
            float targetDx = goalX - shooter.X;
            float targetDz = targetZ - shooter.Z;
            float targetDistance = (float)Math.Sqrt(targetDx * targetDx + targetDz * targetDz);
            float sigma = MatchRules.AimSigmaMeters(targetDistance, MatchRules.AimSigmaDeg(shooter.Stats.Shot), IsPressed(shooter));
            shotAimZ = MatchRules.ShotAimZ(side, sigma, nextRoll(), nextRoll());

            // ③ 골/세이브(골문 밖이어도 주사위는 굴린다: 주사위 순서가 조준 결과에 안 갈리게)
            shotWillScore = MatchRules.Resolve(shotProbability, nextRoll());
            if (!MatchRules.IsOnTarget(shotAimZ)) { shotWillScore = false; }
            shotInFlight = true;
            if (shotWillScore && keeper != null)
            {
                // 골은 GK가 못 닿는 구석으로(골 여부는 이미 정해져 조준만 바뀐다). GK는 자기 라인에서 공을 만나므로 간격은 라인 교차점에서 잰다:
                // GK가 반응 ×2 뒤 스탯 속도로 라인 교차 시점까지 가는 거리 + 발 반경 + 여유. 골라인 조준점으로 환산하려면 라인 비율로 나눈다
                // (교차점 z는 조준 z의 비율 배로 움직인다). 비행 시간은 밀기 전 조준점 기준(가로 몇 m 차이는 0.02~0.03초, 여유 0.5m 안)
                float lineX = KeeperLineX(keeper);
                float fraction = MatchRules.LineFraction(shooter.X, goalX, lineX);
                float flightToLine = ShotFlightSecondsToLine(keeper, goalX);
                float gapAtLine = MatchRules.GoalAimGap(MatchRules.SpeedMps(keeper.Stats.Speed), flightToLine, KeeperReactionTicksFor(willScore: true) * MatchTuning.FixedStep);
                float keeperZAtGoalLine = shooter.Z + (keeper.Z - shooter.Z) / fraction;   // GK z를 골라인 조준 좌표로 환산한 기준점
                shotAimZ = MatchRules.AimAwayFromKeeper(shotAimZ, keeperZAtGoalLine, gapAtLine / fraction);
            }
            if (keeper != null && ShouldPullAimIntoReach()) { PullAimIntoKeeperReach(keeper, shooter, goalX); }
            BallKicked?.Invoke(new KickReport(shooter.PlayerId, true, goalX - shooter.X, shotAimZ - shooter.Z));
            Ball = BallRules.Kick(Ball, goalX - shooter.X, shotAimZ - shooter.Z, MatchTuning.ShotSpeed);
        }

        // 조준 구석의 좌우: GK가 슈터→골 중심 선에서 벗어난 반대쪽(자기 라인에서 잰다). GK가 없으면 슈터 자리의 먼 포스트
        private float AimSideFor(PlayerState? keeper, PlayerState shooter, float goalX)
        {
            if (keeper == null) { return MatchRules.AimSide(0f, 0f, shooter.Z); }
            float centerZAtLine = MatchRules.BallZAtLine(shooter.X, shooter.Z, goalX, 0f, KeeperLineX(keeper));
            return MatchRules.AimSide(keeper.Z, centerZAtLine, shooter.Z);
        }

        // 슈터 발 뻗는 범위 안 상대가 있나(슛 압박). 트리 IsPressed와 같은 반경·같은 틱 시작 스냅샷(라이브 위치를 쓰면 이번 틱에 먼저 움직인 선수만 반영돼 트리 판단과 갈린다)
        private bool IsPressed(PlayerState p)
        {
            float r2 = MatchTuning.PressedRadius * MatchTuning.PressedRadius;
            List<TargetInfo> opponents = rosterSnapshot[1 - p.Team];
            for (int i = 0; i < opponents.Count; i++)
            {
                float dx = opponents[i].X - p.X;
                float dz = opponents[i].Z - p.Z;
                if (dx * dx + dz * dz <= r2) { return true; }
            }
            return false;
        }

        // 슛 블록(09-29 수비 D3 → 11-09 B1 거리 곡선): 슛 선(슈터 → targetZ) 안 최근접 수비가 거리 곡선 확률로 막으면 공은 옆·뒤로 튕겨 자유 공(루즈볼 경합)이 된다.
        // 후보가 있을 때만 주사위를 쓴다. 막힌 슛도 찬 것이라 BallKicked를 결과(Finish)보다 먼저 알린다(러너·뷰가 찬 순간 재료를 결과와 묶는다)
        private bool TryBlockShot(PlayerState shooter, float goalX, float targetZ)
        {
            List<TargetInfo> candidates = blockCandidates[1 - shooter.Team];
            (int playerId, float distance) pick = MatchRules.ShotBlocker(shooter.X, shooter.Z, goalX, targetZ, candidates, keeperIds[1 - shooter.Team],
                MatchTuning.ShotBlockLaneHalfWidth, MatchTuning.ShotBlockMaxAhead);
            if (pick.playerId == -1) { return false; }
            if (!MatchRules.Resolve(MatchRules.BlockChance(pick.distance), nextRoll())) { return false; }

            BallKicked?.Invoke(new KickReport(shooter.PlayerId, true, goalX - shooter.X, targetZ - shooter.Z));
            // 튕김도 블로커를 고른 스냅샷 좌표로 계산한다. 라이브 위치는 PlayerId가 슈터보다 작으면 이번 틱에 이미 움직인 뒤다(판정과 실행의 시점 일치)
            TargetInfo blocker = FindSnapshot(candidates, pick.playerId);
            (float x, float z) dir = MatchRules.BlockReboundDirection(shooter.X, shooter.Z, goalX, targetZ, blocker.X, blocker.Z, MatchTuning.BlockReboundTurnDeg);
            // 09-29 리뷰: 슈터 쪽 직선으로 튕기면 슈터가 1~2틱 만에 도로 잡았다. 옆·뒤로 비틀고, 막은 선수 발(잡기 반경) 밖에서 출발시킨다
            float startX = blocker.X + dir.x * MatchTuning.BlockReboundStart;
            float startZ = blocker.Z + dir.z * MatchTuning.BlockReboundStart;
            Ball = BallRules.Kick(BallState.FreeAt(startX, startZ), dir.x, dir.z, MatchTuning.BlockReboundSpeed);
            lastKickerId = shooter.PlayerId;   // 슈터는 공이 자기 반경을 벗어날 때까지 못 잡는다(찬 선수 가드와 같은 규칙)
            Finish(ShotOutcome.Blocked);
            return true;
        }

        // 패스: 트리가 고른 착지점으로 직선 비행(전엔 여기서 받는 선수 앞 8m를 다시 계산해, 트리가 안전하다고 본 점과 실제로 찬 점이 달랐다).
        // 초속은 착지점까지 거리로 역산(도착 속도 = 공이 있는 서드의 팀 템포). 리시버는 스냅샷으로 알고 마중 나간다
        private void Pass(PlayerState passer, int receiverId, float landingX, float landingZ, bool lob, bool isRun)
        {
            Third third = PositionRules.ThirdOf(Ball.X, passer.AttackSign);
            float average = MatchTuning.PassAverageSpeed[tactics[passer.Team].Tempo[(int)third]];
            float dx = landingX - passer.X;
            float dz = landingZ - passer.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);

            passInFlight = true;
            passReceiverId = receiverId;
            passPasserId = passer.PlayerId;
            passTargetX = landingX;
            passTargetZ = landingZ;
            passIsRun = isRun;
            PassCount++;
            if (passer.IsGoalkeeper && tactics[passer.Team].GkDistribution == 1) { keeperAlternate[passer.Team] = !keeperAlternate[passer.Team]; }   // 섞어: 다음 배급은 반대
            kickOriginX = Ball.X;
            kickOriginZ = Ball.Z;
            passReleased = false;
            lastKickerId = passer.PlayerId;
            kickoffTakerId = BallState.NoOwner;   // 킥오프는 첫 킥으로 끝난다
            takeOnOwnerId = BallState.NoOwner;    // 공을 내줬으면 돌파도 끝

            if (lob)
            {
                // 발사각 고정, 착지 거리에서 초속을 역산한다. 트리가 도달 경쟁(InterceptRules)으로 본 착지점에 실제로 떨어진다(판정과 실행이 같은 점)
                Ball = BallRules.KickLobTo(Ball, dx, dz);
            }
            else
            {
                float speed = PassRules.KickSpeed(distance, average, MatchTuning.BallDeceleration, MatchTuning.PassSpeedMax);
                Ball = BallRules.Kick(Ball, dx, dz, speed);
            }
            BallKicked?.Invoke(new KickReport(passer.PlayerId, false, dx, dz));
        }

        // 소유자가 바뀐 틱: 패스 비행 마감(받았거나 가로챘거나), 볼 끌기 대기 시작
        private void OnOwnerChanged()
        {
            int ownerNow = Ball.Phase == BallPhase.Owned ? Ball.OwnerId : BallState.NoOwner;
            if (ownerNow == ballOwnerAtLastTick) { return; }
            int previousOwnerId = ballOwnerAtLastTick;
            ballOwnerAtLastTick = ownerNow;

            if (ownerNow == BallState.NoOwner) { return; }
            if (ownerNow != kickoffTakerId) { kickoffTakerId = BallState.NoOwner; }   // 키커가 아닌 선수가 공을 가지면 킥오프는 끝났다

            PlayerState owner = FindPlayer(ownerNow);
            PossessionChange kind = PossessionChange.Capture;
            owner.LastPasserId = BallState.NoOwner;   // 줍기·태클로 잡은 공엔 "방금 준 선수"가 없다(패스 수신 때만 쓰니 옛 값이 남아 되돌림 후보를 잘못 강등)
            if (passInFlight)
            {
                kind = ownerNow == passReceiverId ? PossessionChange.PassReceived : PossessionChange.Intercepted;
                if (kind == PossessionChange.PassReceived) { owner.LastPasserId = passPasserId; }
                if (kind == PossessionChange.Intercepted) { InterceptCount++; }
                passInFlight = false;
                passReceiverId = -1;
            }
            else if (previousOwnerId != BallState.NoOwner && FindPlayer(previousOwnerId).Team != owner.Team)
            {
                kind = PossessionChange.Turnover;   // 소유 중이던 공을 상대가 태클로 뺏음(09-21부터 실제로 난다)
                TurnoverCount++;
                if (previousOwnerId == takeOnOwnerId && takeOnTicksLeft > 0) { TakeOnLostCount++; }
            }
            RollTakeOn(owner);
            RollShotPrep(owner);

            holdUpTicksLeft = (int)Math.Round(HoldUpSecondsFor(owner) / MatchTuning.FixedStep);
            immunityTicksLeft = MatchTuning.PossessionImmunityTicks;
            PossessionChanged?.Invoke(new PossessionReport(ownerNow, owner.Team, previousOwnerId, kind, Ball.X, Ball.Z));
        }

        // 소유 직후 킥을 미루는 시간. 보통은 개인 볼 끌기(holdUp). 골킥 소유는 GoalKickSetupSeconds(11-05), GK가 슛을 잡은 소유는 KeeperHoldAfterCatchSeconds — 바로 차면 "잡자마자 패스"가 어색했다(10-25 유저 Play)
        private float HoldUpSecondsFor(PlayerState owner)
        {
            if (goalKickJustStarted)
            {
                goalKickJustStarted = false;
                return Math.Max(owner.Stats.HoldUp, MatchTuning.GoalKickSetupSeconds);
            }
            if (!keeperJustCaught) { return owner.Stats.HoldUp; }
            keeperJustCaught = false;
            return Math.Max(owner.Stats.HoldUp, MatchTuning.KeeperHoldAfterCatchSeconds);
        }

        // 돌파 의도는 소유가 바뀔 때 한 번 정한다. GK·킥오프 키커·우리 진영 서드는 안 굴린다. 드리블 성향 0(테스트 기본 스탯)이면 주사위를 안 써서
        // 주사위 수열로 슛을 잠근 옛 테스트가 그대로 돈다
        private void RollTakeOn(PlayerState owner)
        {
            takeOnOwnerId = BallState.NoOwner;
            takeOnTicksLeft = 0;
            if (owner.IsGoalkeeper || owner.PlayerId == kickoffTakerId || owner.Stats.Dribble <= 0f) { return; }
            if (PositionRules.ThirdOf(Ball.X, owner.AttackSign) == Third.Own) { return; }
            if (!MatchRules.Resolve(DribbleRules.TakeOnChance(owner.Stats.Dribble, MatchTuning.TakeOnChanceScale), nextRoll())) { return; }
            takeOnOwnerId = owner.PlayerId;
            takeOnTicksLeft = MatchTuning.TakeOnMaxTicks;
            TakeOnCount++;
        }

        // 볼 끌기가 이번 킥을 붙잡나. 패스는 늘 붙잡는다(타깃맨이 버티고 내준다). 슛은 퍼스트 터치를 얻은 소유면 안 붙잡는다:
        // 퍼스트 터치 = 받자마자 차는 슛인데 볼 끌기(타깃맨 1초)가 다시 막으면 판정과 실행의 전제가 어긋난다(11-08 S3 1단계 리뷰 🔴)
        private bool HoldsUpKick(PlayerState p)
        {
            if (p.WantsPass) { return true; }
            if (!p.WantsShoot) { return false; }
            bool firstTouch = firstTouchOwnerId == p.PlayerId;
            return !firstTouch;
        }

        // 슛 준비 시간을 소유 때 정한다(11-08 S3 1단계). GK·킥오프 키커는 슛을 안 하니 막지 않는다.
        // 사거리 밖에서 잡으면 주사위 없이 준비 시간만 건다: 몰고 들어와 차더라도 준비 시간은 소유한 순간부터 흐른다(StatsBomb 받기→슛 시간과 같은 기준)
        private void RollShotPrep(PlayerState owner)
        {
            shotPrepOwnerId = owner.PlayerId;
            shotPrepTicksLeft = 0;
            firstTouchOwnerId = BallState.NoOwner;
            if (owner.IsGoalkeeper || owner.PlayerId == kickoffTakerId) { return; }

            shotPrepTicksLeft = ShotDecisionRules.PrepTicks(MatchTuning.ShotPrepSeconds);
            float distance = MatchRules.ShotDistance(Ball.X, Ball.Z, owner.AttackSign);
            if (distance > MatchTuning.MaxShotRange) { return; }

            float chance = ShotDecisionRules.FirstTouchChance(distance, MatchTuning.FirstTouchShotBandEdges, MatchTuning.FirstTouchShotRates, MatchTuning.FirstTouchShotScale);
            if (!MatchRules.Resolve(chance, nextRoll())) { return; }
            shotPrepTicksLeft = 0;
            firstTouchOwnerId = owner.PlayerId;
            FirstTouchReadyCount++;
        }

        // 골라인을 넘은 슛: 골이면 득점, 아니면(GK가 못 건드렸으면) 빗나감. 둘 다 킥오프
        private bool ResolveShotAtGoalLine()
        {
            if (!shotInFlight) { return false; }
            if (Ball.X * shooterAttackSign < FieldBounds.HalfLength) { return false; }

            if (shotWillScore)
            {
                int scorerTeam = ScoreGoal();
                if (ResetAfterEveryShot) { Kickoff(); } else { KickoffBy(1 - scorerTeam); }   // 실점한 팀이 킥오프
                return true;
            }

            if (MatchRules.IsOnTarget(shotAimZ) && ResolveSaveAtLine()) { return true; }

            Finish(ShotOutcome.Missed);   // 골문 밖 조준. 골문 안 노골은 위에서 세이브로 끝난다(GK가 없는 시뮬만 여기로)
            RestartAfterMiss();
            return true;
        }

        // 세이브로 정해진 유효 슛만 조준을 GK 닿는 범위로 당긴다. 골·빗나감은 그대로
        private bool ShouldPullAimIntoReach()
        {
            if (shotWillScore) { return false; }
            return MatchRules.IsOnTarget(shotAimZ);
        }

        // 세이브로 정해진 슛을 GK가 지연 뒤 공이 자기 라인을 지나기 전에 닿는 교차점으로(11-09 B3, 옛 PushWide 대체). 교차점은 트리 ⑨b(MoveToShotLine)가 가는 점과 같고
        // 지연은 반응과 남은 얼음(태클 실패 정지) 중 긴 쪽(10-25 판정과 실행의 전제 일치). 닿는 폭은 라인 교차점에서 재고 골라인 조준 좌표로 환산한다(÷ 라인 비율).
        // 비행 시간이 조준점에 따라 조금 바뀌므로 당긴 점으로 한 번 더 잰다(두 번이면 경계 오차가 cm 아래)
        private void PullAimIntoKeeperReach(PlayerState keeper, PlayerState shooter, float goalX)
        {
            float lineX = KeeperLineX(keeper);
            float fraction = MatchRules.LineFraction(shooter.X, goalX, lineX);
            float keeperZAtGoalLine = shooter.Z + (keeper.Z - shooter.Z) / fraction;
            int delayTicks = Math.Max(KeeperReactionTicksFor(willScore: false), keeper.FrozenTicks);
            for (int pass = 0; pass < 2; pass++)
            {
                float flightToLine = Math.Max(0f, ShotFlightSecondsToLine(keeper, goalX));
                float reachAtLine = MatchRules.KeeperLateralReach(lineX - keeper.X, flightToLine, delayTicks * MatchTuning.FixedStep);
                shotAimZ = MatchRules.AimTowardKeeperReach(shotAimZ, keeperZAtGoalLine, reachAtLine / fraction);
            }
        }

        // GK가 지키는 라인 x: 트리 MoveToShotLine과 같은 수비 자리(DefendHome) 식
        private static float KeeperLineX(PlayerState keeper)
        {
            return PositionRules.KeeperLineX(keeper.DefendHomeX, keeper.DefendHomeZ, keeper.AttackSign, keeper.Stats.LineHeight);
        }

        // GK가 가야 할 z = 공이 GK 라인을 지나는 z(골라인 조준 z가 아니다). 근소 빗나감(B4-b)은 조준을 포스트로 잘라서 본다: GK는 포스트 쪽으로 몸을 던지되 골문 밖까지 쫓아가지 않는다
        private float KeeperTargetZ(PlayerState keeper)
        {
            float goalX = FieldBounds.HalfLength * shooterAttackSign;
            float aimInsidePosts = Math.Clamp(shotAimZ, -FieldBounds.GoalHalfWidth, FieldBounds.GoalHalfWidth);
            return MatchRules.BallZAtLine(shotStartX, shotStartZ, goalX, aimInsidePosts, KeeperLineX(keeper));
        }

        // 찬 자리에서 GK 라인 교차점까지의 비행 시간(초). 공이 감속하므로 "전체 비행 × 비율"보다 짧다(10-25 리뷰 🟡). 멈추면 -1
        private float ShotFlightSecondsToLine(PlayerState keeper, float goalX)
        {
            float lineX = KeeperLineX(keeper);
            float fraction = MatchRules.LineFraction(shotStartX, goalX, lineX);
            float dx = (goalX - shotStartX) * fraction;
            float dz = (shotAimZ - shotStartZ) * fraction;
            return MatchRules.ShotFlightTime((float)Math.Sqrt(dx * dx + dz * dz), MatchTuning.ShotSpeed, MatchTuning.BallDeceleration);
        }

        // GK가 슛에 반응하기 시작하는 지연(틱). 골로 정해진 슛은 두 배 늦게(못 막을 슛을 "늦게 반응한" 모양으로). 판정(GoalAimGap)과 실행(IncomingShotFor)이 같은 값을 쓴다
        private static int KeeperReactionTicksFor(bool willScore)
        {
            return willScore ? MatchTuning.KeeperReactionTicks * 2 : MatchTuning.KeeperReactionTicks;
        }

        // GK의 슛 대응 스냅샷(트리 ⑨a·⑨b, 뷰 다이빙). 대기·반응·목표 z·남은 시간을 같은 틱 값으로 채운다. 해당 없으면 전부 0
        private void FillKeeperShotSnapshot(PlayerState p)
        {
            p.WaitingForShot = IsWaitingForShot(p);
            p.IncomingShot = IncomingShotFor(p);
            if (!p.WaitingForShot && !p.IncomingShot)
            {
                p.IncomingShotTargetZ = 0f;
                p.IncomingShotSecondsToLine = 0f;
                return;
            }
            float goalX = FieldBounds.HalfLength * shooterAttackSign;
            p.IncomingShotTargetZ = KeeperTargetZ(p);
            p.IncomingShotSecondsToLine = Math.Max(0f, ShotFlightSecondsToLine(p, goalX) - shotTicks * MatchTuning.FixedStep);
        }

        // 내 골 쪽으로 슛(골문 안 또는 포스트 밖 NearMissReact 안, 11-09 B4-b)이 날아오는 GK인가. 대기(WaitingForShot)와 반응(IncomingShot)이 같은 조건을 나눠 쓴다: 한쪽만 바뀌면 "대기도 반응도 아닌" 틈이 생긴다.
        // 근소 빗나감에도 반응만 하고 세이브는 없다(ResolveSaveOnContact·ResolveSaveAtLine은 골문 안만)
        private bool IsShotAimedAtMyGoal(PlayerState p)
        {
            if (!shotInFlight || !p.IsGoalkeeper || p.Team == TeamOfSign(shooterAttackSign)) { return false; }
            return Math.Abs(shotAimZ) <= FieldBounds.GoalHalfWidth + MatchTuning.NearMissReact;
        }

        // 아직 반응 차례가 아닌 GK는 트리가 제자리에 둔다(10-25). 전엔 자리 규칙(공 z 추적)으로 미리 움직여 골 조준 간격(GoalAimGap)의 전제
        // "지연 뒤에만 움직임"과 달랐고, 골의 56%에서 GK가 마감 때 공 0.8m 안에 있었다(실험/2026-10-25-GK-골-간격-probe.md).
        // 노골 슛도 같다: 닿음 판정(KeeperCanReach)도 지연 뒤부터 움직인다고 봤다. 슛이 뜨는 틱엔 Apply 순서상 GK가 한 틱 더 움직일 수 있다(0.02초, 무시)
        private bool IsWaitingForShot(PlayerState p)
        {
            if (!IsShotAimedAtMyGoal(p)) { return false; }
            return shotTicks < KeeperReactionTicksFor(shotWillScore);
        }

        // 내 골로 오는 유효 슛에 GK가 반응할 차례인가(트리 ⑨b). 반응 지연 뒤부터
        private bool IncomingShotFor(PlayerState p)
        {
            if (!IsShotAimedAtMyGoal(p)) { return false; }
            return shotTicks >= KeeperReactionTicksFor(shotWillScore);
        }

        // 골문 안인데 노골로 정해진 슛이 GK 접촉 없이 골라인에 닿음: 공을 라인 안쪽 발 반경으로 들이고 GK를 그 자리로 당겨 세이브(찬 순간 "닿을 수 있다"고 본 슛이라 작은 보정). GK 없는 시뮬은 빗나감
        // 공을 라인 밖(골라인을 넘은 자리)에 둔 채 세이브하면 캐치 뒤 배급 패스·펀칭 튕김이 "나간 공"으로 판정돼 골킥 자리에 자유 공으로 떨어졌다(10-24 리뷰 뒤 probe: 골 1.37 → 2.00)
        private bool ResolveSaveAtLine()
        {
            PlayerState? keeper = FindGoalkeeper(1 - TeamOfSign(shooterAttackSign));
            if (keeper == null) { return false; }
            BallState inside = Ball;
            inside.X = shooterAttackSign * (FieldBounds.HalfLength - MatchTuning.CaptureRadius);
            Ball = inside;
            keeper.X = Ball.X;
            keeper.Z = Ball.Z;
            ResolveSave(keeper);
            if (ResetAfterEveryShot) { Kickoff(); }
            return true;
        }

        // 세이브: 캐치(GK 소유) 또는 튕김(앞으로 자유 공)
        private void ResolveSave(PlayerState keeper)
        {
            bool caught = MatchRules.Resolve(MatchRules.CatchProbability(keeper.Stats.Handling), nextRoll());
            if (caught)
            {
                Ball = BallRules.Own(Ball, keeper.PlayerId, keeper.X, keeper.Z);
                keeperJustCaught = true;   // 다음 OnOwnerChanged에서 볼 끌기 대기를 KeeperHoldAfterCatchSeconds로
                Finish(ShotOutcome.Caught);
                return;
            }
            Ball = BallRules.Kick(Ball, -shooterAttackSign, 0f, MatchTuning.ParrySpeed);
            Finish(ShotOutcome.Parried);
        }

        // 골이 아닌 유효 슛이 GK 반경에 닿으면 세이브. 근소 빗나감은 GK가 몸을 던져도 지나간다(B4-b)
        private bool ResolveSaveOnContact()
        {
            if (!MatchRules.IsOnTarget(shotAimZ)) { return false; }
            PlayerState? keeper = FindGoalkeeper(1 - TeamOfSign(shooterAttackSign));
            if (keeper == null) { return false; }

            float dx = keeper.X - Ball.X;
            float dz = keeper.Z - Ball.Z;
            if (dx * dx + dz * dz > MatchTuning.CaptureRadius * MatchTuning.CaptureRadius) { return false; }

            ResolveSave(keeper);

            if (ResetAfterEveryShot)
            {
                Kickoff();
                return true;
            }
            return false;
        }

        // 빗나간 슛은 골킥: 수비 팀 GK가 자기 자리에서 공을 갖고 배급한다. 킥오프로 리셋하면 같은 공격이 9.6초 주기로 그대로 재생됐고
        // 상대 팀이 공을 가질 기회가 없었다. GK가 없는 시뮬(리트머스·일부 테스트)과 ResetAfterEveryShot은 예전처럼 킥오프
        private void RestartAfterMiss()
        {
            PlayerState? keeper = ResetAfterEveryShot ? null : FindGoalkeeper(1 - TeamOfSign(shooterAttackSign));
            if (keeper == null)
            {
                Kickoff();
                return;
            }
            StartGoalKick(keeper);
        }

        // 골킥(IFAB 16조, 11-05): GK가 공을 쥐고 GoalKickSetupSeconds 동안 기다린다. 그동안 우리 팀은 트리가 공격 자리로 보내고(GK 소유 = 우리 공),
        // 상대는 박스 밖에 머문다. 09-23 Play: 빗나감 → GK 소유 → 붙은 ST가 배급을 릴리스 지점에서 가로채 7m 슛, 4회 만에 골.
        // 11-05 Play: 기다림 없이 바로 차서 제자리의 수비와 공을 주고받았다
        private void StartGoalKick(PlayerState keeper)
        {
            Ball = BallRules.Own(Ball, keeper.PlayerId, keeper.X, keeper.Z);
            goalKickJustStarted = true;
            goalKickKeeperId = keeper.PlayerId;
            PushOpponentsOutOfBox(keeper);
        }

        // 골킥을 든 GK가 공을 쥐고 있는 동안 상대를 박스 밖에 둔다. GK가 차거나 공이 GK를 떠나면 끝
        private void KeepGoalKickBoxClear()
        {
            if (goalKickKeeperId == BallState.NoOwner) { return; }
            if (Ball.Phase != BallPhase.Owned || Ball.OwnerId != goalKickKeeperId)
            {
                goalKickKeeperId = BallState.NoOwner;
                return;
            }
            PushOpponentsOutOfBox(FindPlayer(goalKickKeeperId));
        }

        private void PushOpponentsOutOfBox(PlayerState keeper)
        {
            float outsideX = -(FieldBounds.HalfLength - FieldBounds.PenaltyBoxDepth - 1f) * keeper.AttackSign;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != keeper.Team && MatchRules.IsInOwnPenaltyBox(p.X, p.Z, keeper.AttackSign))
                {
                    p.X = outsideX;
                    p.Stop();   // 박스로 달려들던 속도를 지운다. 안 지우면 대기 내내 제자리 전력 질주로 체력이 깎이고 풀리는 순간 튀어 나간다(11-05 리뷰)
                }
            }
        }

        // 골 한 번: 득점 팀 골 수 + 결과 보고. 골라인 도달과 휘슬 마감이 같이 쓴다. 킥오프는 부르는 쪽이 정한다(실점 팀 / 후반 킥오프 팀 / 종료면 없음)
        private int ScoreGoal()
        {
            int scorerTeam = TeamOfSign(shooterAttackSign);
            if (scorerTeam == 0) { HomeGoals++; } else { AwayGoals++; }
            Finish(ShotOutcome.Goal);
            return scorerTeam;
        }

        // 휘슬(하프타임·종료) 순간 날아가던 슛. 하프타임(09-29 재검증 ②, 09-30 수정): 하프타임 리셋(KickoffBy → Kickoff)이 shotInFlight만 지워
        // 결과 보고 없이 슛이 사라졌고 골이 될 슛이면 골도 사라졌다(경기의 약 7%가 하프타임 순간 슛 비행 중, 골 소실 약 1%).
        // 휘슬을 슛 뒤로 미루면(방식 A) 시계·HUD·러너·연출 멈춤이 전부 하프타임 틱에 묶여 있어 같이 바뀐다. 결과는 찬 순간 정해져 있으니(shotWillScore) 그 결과로 마감한다.
        // 골이 아닌 슛은 세이브가 될 슛이었을 수도 있지만 GK에 닿기 전이라 빗나감으로 센다.
        // 종료 휘슬도 같은 부류라 같이 쓴다: 경기가 TotalTicks에서 멈추면 그때 날아가던 슛은 결과 보고가 없었고 골이 될 슛이면 골도 사라졌다.
        // 종료 때는 킥오프로 리셋하지 않는다. 경기가 끝났다
        private void FinishShotAtWhistle()
        {
            if (!shotInFlight) { return; }
            if (!shotWillScore)
            {
                Finish(ShotOutcome.Missed);
                return;
            }
            ScoreGoal();
        }

        private void Finish(ShotOutcome outcome)
        {
            shotInFlight = false;
            ShotResolved?.Invoke(new ShotReport(shooterId, shotProbability, outcome));
        }

        // 태클: 소유자 태클 사거리(TackleRange) 안의 상대가 쿨다운이 끝났으면 시도. 성공하면 태클러 소유(깔끔한 탈취), 실패하면 태클러 정지.
        // 루즈볼 방식은 버렸다: 붙어 있으면 공이 소유자 뒤로 떨어지고 되찾기 경주에서 빠른 쪽(대개 원래 소유자)이 늘 이겨 태클이 무의미했다(테스트 트레이스)
        // 주사위 1개 소비 → 시드가 다르면 결과가 갈리는 두 번째 지점(첫째는 슛). 면역 중엔 시도 자체가 없다
        private void ResolveTackles()
        {
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.FrozenTicks > 0) { p.FrozenTicks--; }
                if (p.TackleCooldownTicks > 0) { p.TackleCooldownTicks--; }
            }
            if (immunityTicksLeft > 0) { immunityTicksLeft--; }
            if (Ball.Phase != BallPhase.Owned || immunityTicksLeft > 0) { return; }

            PlayerState owner = FindPlayer(Ball.OwnerId);
            if (owner.IsGoalkeeper) { return; }   // 공을 잡은 GK는 경합 대상이 아니다(규칙 12조). 09-23 탐지: 골문 앞 GK-ST 태클 핑퐁 7/30경기의 원인
            float reach2 = MatchTuning.TackleRange * MatchTuning.TackleRange;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState t = players[i];
                if (t.Team == owner.Team || t.FrozenTicks > 0 || t.TackleCooldownTicks > 0) { continue; }
                float dx = t.X - owner.X;
                float dz = t.Z - owner.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 > reach2) { continue; }

                TackleAttemptCount++;
                t.TackleCooldownTicks = MatchTuning.TackleCooldownTicks;
                if (!MatchRules.Resolve(MatchRules.TackleProbability(t.Stats.Tackle), nextRoll()))
                {
                    t.FrozenTicks = MatchTuning.TackleFailFreezeTicks;
                    continue;
                }

                TackleSuccessCount++;
                Ball = BallRules.Own(Ball, t.PlayerId, t.X, t.Z);
                OnOwnerChanged();   // 같은 틱에 Turnover 기록 + 새 소유자 면역
                return;   // 한 틱에 태클은 하나
            }
        }

        private void TryCapture()
        {
            if (passInFlight && !passReleased)
            {
                float odx = Ball.X - kickOriginX;
                float odz = Ball.Z - kickOriginZ;
                if (odx * odx + odz * odz <= MatchTuning.CaptureRadius * MatchTuning.CaptureRadius) { return; }   // 아직 발치. 아무도 못 잡는다
                passReleased = true;
            }

            captureCandidates.Clear();
            int keeperKickTeam = ProtectedKeeperKickTeam();
            int letThroughTeam = PassThroughTeam();
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (keeperKickTeam != -1 && p.Team != keeperKickTeam) { continue; }   // GK가 찬 공은 그 박스 안에선 상대가 못 잡는다
                if (p.Team == letThroughTeam && p.PlayerId != passReceiverId) { continue; }   // 받을 선수가 아닌 우리 편은 흘려 보낸다
                if (p.PlayerId == lastKickerId)
                {
                    float kdx = p.X - Ball.X;
                    float kdz = p.Z - Ball.Z;
                    if (kdx * kdx + kdz * kdz <= MatchTuning.CaptureRadius * MatchTuning.CaptureRadius) { continue; }
                    lastKickerId = BallState.NoOwner;
                }
                if (!BallRules.CanReachHeight(Ball.Y, IsKeeperInOwnBox(p))) { continue; }   // 머리 위로 넘어가는 공은 못 잡는다(10-29 ④-1)
                captureCandidates.Add(new TargetInfo(p.PlayerId, p.X, p.Z));
            }

            int ownerId = BallRules.TryCapture(Ball, captureCandidates, MatchTuning.CaptureRadius, allowFlight: !shotInFlight);
            if (ownerId == BallState.NoOwner) { return; }

            PlayerState owner = FindPlayer(ownerId);
            Ball = BallRules.Own(Ball, ownerId, owner.X, owner.Z);
        }

        // 공을 흘려 보내는 팀(11-06): 우리 편 패스가 착지점에 닿기 전이면 패서 팀, 아니면 -1. 그 팀에서 받을 선수가 아닌 선수는 공을 잡지 않는다.
        // Simple Soccer도 ReceiveBall 상태 선수만 공을 잡고, 트리도 받을 선수가 아니면 공을 쫓지 않는다(09-23). 이게 없으면 사전 런으로 패스 길에 선 러너가
        // 다른 선수에게 가는 패스를 길 중간에서 받아 가로채기로 셌다(추격 수신 뒤 판당 8 → 85, 패스 성공 79 → 71%).
        // 착지점을 지난 공은 받을 선수가 놓친 자유 공이라 누구든 잡는다. 런 판정(RunRules.RunnerMeetsBall)의 범위도 착지점까지다
        private int PassThroughTeam()
        {
            if (!passInFlight) { return -1; }

            float toLandingX = passTargetX - Ball.X;
            float toLandingZ = passTargetZ - Ball.Z;
            if (toLandingX * Ball.VelX + toLandingZ * Ball.VelZ <= 0f) { return -1; }   // 착지점을 지났다(또는 공이 서 있다)
            return OwnerTeam();
        }

        private static bool IsKeeperInOwnBox(PlayerState p)
        {
            if (!p.IsGoalkeeper) { return false; }
            return MatchRules.IsInOwnPenaltyBox(p.X, p.Z, p.AttackSign);
        }

        // GK 배급 보호(09-30 공격 칼날 C): GK가 찬 패스가 그 GK의 페널티 박스 안에 있는 동안은 상대가 못 잡는다. 그 팀 번호, 해당 없으면 -1.
        // 규칙 근거: 골킥은 공이 인플레이가 될 때까지 상대가 박스 밖(IFAB 16조), 손에 쥔 GK의 릴리스를 방해하면 반칙(12조).
        // 이 시뮬은 공이 땅으로만 가고 상대가 박스 안까지 쫓아와, 상대 진영 압박을 켠 ST가 GK 패스를 킥 지점 3m 앞에서 끊어 경기당 슛 7.5개(xG 0.31)가 났다
        // (균형 카드 득점 1.05 → 4.03, 실험/2026-09-30-공격-칼날-진단.md). 박스를 벗어난 공은 평소처럼 누구든 끊는다
        private int ProtectedKeeperKickTeam()
        {
            if (!passInFlight) { return -1; }
            PlayerState passer = FindPlayer(passPasserId);
            if (!passer.IsGoalkeeper) { return -1; }
            if (!MatchRules.IsInOwnPenaltyBox(Ball.X, Ball.Z, passer.AttackSign)) { return -1; }
            return passer.Team;
        }

        // 라인 밖(슛 비행 중은 제외: 골라인 판정이 먼저다) → 골라인은 그 골을 지키는 팀 GK의 골킥(11-05), 터치라인은 스로인 자리에 자유 공(스펙 §5)
        private bool ResetIfOut()
        {
            if (Ball.Phase == BallPhase.Owned || shotInFlight) { return false; }
            if (passInFlight && (Math.Abs(Ball.X) > FieldBounds.HalfLength || Math.Abs(Ball.Z) > FieldBounds.HalfWidth))
            {
                passInFlight = false;   // 나간 패스는 실패. 리셋은 아래가 한다
                passReceiverId = -1;
            }

            if (Math.Abs(Ball.X) > FieldBounds.HalfLength)
            {
                // 골라인 밖은 그 골을 지키는 팀의 골킥(11-05). 마지막에 수비가 건드린 공(코너킥)도 코너킥이 생길 때까지 골킥으로 둔다(스펙 §5 업데이트 1차)
                int sign = Math.Sign(Ball.X);
                PlayerState? keeper = FindGoalkeeper(1 - TeamOfSign(sign));
                if (keeper == null)
                {
                    Ball = BallState.FreeAt(sign * (FieldBounds.HalfLength - MatchTuning.GoalKickOffset), 0f);   // GK 없는 시뮬(리트머스·테스트)은 예전처럼 자유 공
                    return true;
                }
                StartGoalKick(keeper);
                return true;
            }
            if (Math.Abs(Ball.Z) > FieldBounds.HalfWidth)
            {
                float sign = Math.Sign(Ball.Z);
                Ball = BallState.FreeAt(Ball.X, sign * (FieldBounds.HalfWidth - MatchTuning.ThrowInInset));
                return true;
            }
            return false;
        }

        private PlayerState? FindGoalkeeper(int team)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Team == team && players[i].IsGoalkeeper) { return players[i]; }
            }
            return null;
        }

        private PlayerState FindPlayer(int playerId)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].PlayerId == playerId) { return players[i]; }
            }
            throw new InvalidOperationException($"PlayerId {playerId}가 명부에 없다");
        }

        private static TargetInfo FindSnapshot(List<TargetInfo> snapshot, int playerId)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].PlayerId == playerId) { return snapshot[i]; }
            }
            throw new InvalidOperationException($"스냅샷에 PlayerId {playerId}가 없다");
        }
    }
}
