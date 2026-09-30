using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    public enum ShotOutcome { Goal, Caught, Parried, Missed, Blocked }

    public enum PossessionChange { Capture, PassReceived, Intercepted, Turnover }

    // 소유가 바뀐 틱의 보고. 경합·패스·가로채기가 실제로 나는지 콘솔에서 보려고(09-18)
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

    // 슛 하나의 결과 보고. MatchManager가 콘솔에 찍고 테스트가 대조한다
    public readonly struct ShotReport
    {
        public readonly int ShooterId;
        public readonly float Probability;   // 골 확률 p(GK 보정 포함)
        public readonly ShotOutcome Outcome;

        public ShotReport(int shooterId, float probability, ShotOutcome outcome)
        {
            ShooterId = shooterId;
            Probability = probability;
            Outcome = outcome;
        }
    }

    // 경기 루프의 순수 심장. 공·선수·점수·주사위를 들고 고정 스텝 한 틱을 돌린다. 엔진 없음(EditMode에서 그대로 굴린다).
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

        public int TickCount => tickCount;                                   // 첫 Tick부터 센 수. 하프타임·HUD 시계의 기준(MatchManager.Ticks와 같은 값)
        public bool IsSecondHalf => tickCount >= MatchClock.HalfTimeTick(Added);
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

        // 팀 전술 2개(스타일 카드). 기본은 "균형"과 같은 빈 값(전부 0)이고 MatchManager가 프리셋을 주입한다
        private readonly TeamTactics[] tactics = { new TeamTactics(), new TeamTactics() };

        // 틱마다 한 번 계산하는 팀 단위 국면(09-18): 공은 하나라 서드·역습도 팀당 하나. 22명이 같은 값을 본다
        private readonly List<TargetInfo>[] rosterSnapshot = { new List<TargetInfo>(), new List<TargetInfo>() };
        private readonly List<TargetInfo>[] pressEligible = { new List<TargetInfo>(), new List<TargetInfo>() };   // 팀별 압박 거리 안 선수(압박 순위 계산용)
        private readonly List<TargetInfo>[] looseCandidates = { new List<TargetInfo>(), new List<TargetInfo>() }; // 팀별 루즈볼 추격 후보(GK·얼음 제외)
        private readonly List<TargetInfo>[] blockCandidates = { new List<TargetInfo>(), new List<TargetInfo>() }; // 팀별 슛 블록 후보(얼음 제외, 09-30 리뷰). 틱 시작 스냅샷과 같은 시점
        // 팀별 "지금 맡은 선수"(09-28 F1b 히스테리시스). 압박 1순위는 상대 소유 중에만, 루즈볼 추격자는 소유 팀이 없을 때만 유지하고 아니면 -1
        private readonly int[] pressLeader = { -1, -1 };
        private readonly int[] looseChaser = { -1, -1 };
        // 팀 국면 스냅샷 버퍼. 틱마다 새 배열을 만들면 3분에 9,000 × 3번 할당(09-21 전수조사 S5)
        private readonly bool[] counteringByTeam = new bool[2];
        private readonly bool[] counterPressingByTeam = new bool[2];
        private readonly Third[] thirdByTeam = new Third[2];
        private readonly int[] keeperIds = { -1, -1 };
        private int lastOwnerTeam = -1;       // 직전 틱 소유 팀. 바뀌면 턴오버
        private int ticksSinceTurnover = int.MaxValue;
        private int turnoverLoserTeam = -1;   // 방금 공을 잃은 팀(역압박 판정 대상)

        // 비행 중인 슛의 결정된 운명. 골이면 GK 접촉을 무시하고 골라인을 넘긴다
        private bool shotInFlight;
        private bool shotWillScore;
        private int shooterId;
        private int shooterAttackSign;
        private float shotProbability;
        private float shotAimZ;               // 조준 Z(골 중심 기준). 골문 밖이면 골라인에서 Missed

        // 비행 중인 패스(09-18). 리시버는 스냅샷으로 "나한테 온다"를 알고 마중 나간다
        private bool passInFlight;
        private int passReceiverId = -1;
        private int passPasserId = -1;        // 되돌림 금지(09-23)용: 받은 선수의 LastPasserId에 기록
        private float passTargetX;
        private float passTargetZ;
        private int ballOwnerAtLastTick = BallState.NoOwner;
        private int immunityTicksLeft;        // 소유 면역(09-21): 소유 뒤 이 틱 동안 태클 불가
        private int holdUpTicksLeft;          // 볼 끌기(개인 holdUp): 소유 뒤 킥까지 대기 틱
        private readonly bool[] keeperAlternate = new bool[2];   // GK 배급 "섞어"의 교대 스위치(팀별). GK 패스가 실행될 때 뒤집고 스냅샷으로 트리에 준다(09-23 R2)
        // 킥 릴리스(09-21 Play 진단): 찬 공은 킥 원점에서 잡기 반경을 벗어난 뒤에야 누구든 잡을 수 있다.
        // 발치에 붙은 상대가 첫 틱(0.3m)에 그 자리에서 가로채 소유가 0.4초마다 뒤집히던 잠금을 막는다
        private float kickOriginX;
        private float kickOriginZ;
        private bool passReleased = true;
        // 찬 선수 가드(09-18, 09-21 재확인): 비행 중엔 소유 팀이 없어 자유 공 분기로 패서가 자기 공을 쫓는다.
        // 릴리스만 있으면 3틱째(공 0.9m, 패서 0.42m 따라옴)에 도로 잡아 0.15초마다 반복됐다. 공이 패서 반경을 벗어날 때까지 패서는 후보에서 뺀다
        private int lastKickerId = BallState.NoOwner;
        // 박스 앞 CB 전진(09-29 수비 D1). 필드 역할 이름을 시뮬이 직접 보는 유일한 예외(GK 제외). 개인 압박 거리 다이얼로 끌 수 없다(09-30 리뷰, 1차 출시는 유지)
        private const string CenterBackRole = "CB";
        private readonly int[] stepOutCenterBack = { -1, -1 };   // 이번 틱 박스 앞에서 나설 팀별 CB. 압박 후보 계산 때 채운다
        private int kickoffTakerId = BallState.NoOwner;   // 킥오프 공을 쥔 키커(09-29). 첫 킥이나 소유 변경 때 푼다. 스냅샷 IsKickoffTaker로 트리에 준다
        // 드리블 돌파 의도(09-29): 공을 잡을 때 드리블 성향으로 한 번 굴린다. 트리가 틱마다 굴리면 같은 소유 안에서 돌파·패스가 번갈아 흔들린다
        private int takeOnOwnerId = BallState.NoOwner;
        private int takeOnTicksLeft;
        public int TakeOnCount { get; private set; }       // 돌파 의도가 선 횟수(계측)
        public int TakeOnLostCount { get; private set; }   // 돌파 의도 중 태클로 뺏긴 횟수(계측). 성공률 = 1 - 뺏김 ÷ 시도의 근사
        public int PassCount { get; private set; }
        public int InterceptCount { get; private set; }

        public MatchSimulation(Func<float> nextRoll, BehaviorNode tree)
        {
            this.nextRoll = nextRoll;
            this.tree = tree;
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

        // 팀별 압박 후보 목록. 누가 후보인지(개인 압박 거리 × 팀 배율, 역압박 ×2, 박스 앞 CB 전진)는 시뮬이 여기서만 정하고
        // 팀 안에서 공 거리순 순위를 매긴다. 트리는 자기 순위만 보고 상한(MaxPressers) 안일 때만 간다(09-23 뭉침, 09-29 판정 한 곳으로)
        private void FillPressEligible()
        {
            pressEligible[0].Clear();
            pressEligible[1].Clear();
            stepOutCenterBack[0] = StepOutCenterBack(0);
            stepOutCenterBack[1] = StepOutCenterBack(1);
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.FrozenTicks > 0) { continue; }   // 태클 실패로 얼어 있는 선수는 순위에서 뺀다(09-26 리뷰): 남겨 두면 1순위를 차지한 채 못 움직여 그 팀 압박이 0.5초 빈다
                if (p.PlayerId == stepOutCenterBack[p.Team] || InPressDistance(p))
                {
                    pressEligible[p.Team].Add(new TargetInfo(p.PlayerId, p.X, p.Z));
                }
            }
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

        // 박스 앞에서 나설 센터백(09-29 수비 D1). 공이 전진 구역 밖이거나 얼지 않은 CB가 없으면 -1. 동률은 PlayerId 작은 쪽
        private int StepOutCenterBack(int team)
        {
            if (!PressRules.IsInStepOutZone(Ball.X, Ball.Z, AttackSignOf(team), MatchTuning.BoxStepOutDepth)) { return -1; }

            int best = -1;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != team || p.FrozenTicks > 0 || p.Stats.RoleId != CenterBackRole) { continue; }
                float dx = Ball.X - p.X;
                float dz = Ball.Z - p.Z;
                float d2 = dx * dx + dz * dz;
                if (!IsCloserCenterBack(d2, p.PlayerId, bestD2, best)) { continue; }
                bestD2 = d2;
                best = p.PlayerId;
            }
            return best;
        }

        // 동률은 PlayerId 작은 쪽(PressRules.PressRank와 같은 규칙). 목록 순서에 기대지 않고 명시 비교한다(09-30 리뷰)
        private static bool IsCloserCenterBack(float d2, int playerId, float bestD2, int bestId)
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
                pressLeader[team] = ownerTeam == 1 - team
                    ? PressRules.FirstInRank(pressEligible[team], Ball.X, Ball.Z, pressLeader[team], MatchTuning.ChaserSwitchMargin)
                    : -1;

                if (!loose) { looseChaser[team] = -1; continue; }
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

        // 소유 팀. 비행 중인 패스는 찬 팀의 것이다(09-23): -1로 두면 22명 전부 자유 공으로 봐서 최근접 1명이 날아가는 공을 쫓고
        // 나머지는 양 팀 다 공격 자리(⑪)로 갔다가 받으면 돌아오는 왕복이 생겼다(Play: 상대 패스마다 수비 블록이 무너짐).
        // 찬 팀으로 두면 리시버만 마중(⑥), 아군은 자리(⑦), 상대는 압박·수비 자리(⑧·⑨)로 갈리고, 가로채기가 팀 전환으로 잡혀 역압박이 켜진다.
        // 슛·파링 비행은 여전히 -1(누구든 줍는다)
        public int OwnerTeam()
        {
            if (Ball.Phase == BallPhase.Owned) { return FindPlayer(Ball.OwnerId).Team; }
            if (passInFlight) { return FindPlayer(passPasserId).Team; }
            return -1;
        }

        // 킥오프를 하는 팀(09-23): 중앙 리셋 뒤 그 팀에서 중앙에 가장 가까운 필드 플레이어가 공을 갖는다. 실제 규칙(시작은 동전, 골 뒤엔
        // 실점 팀)과 같다. 자유 공 경합으로 두면 양 팀 ST가 등거리라 PlayerId 타이브레이크가 매 킥오프를 한 팀에 줬고(먼저 스폰된 팀이
        // 미러 세팅에서 75% 승·상대 0%), 스폰 순서를 뒤집으면 결과가 거울로 뒤집혔다. Kickoff()(자유 공)는 리트머스·테스트용으로 남긴다
        public void SetAddedTime(AddedTime added)
        {
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
                if (d2 < bestD2) { bestD2 = d2; best = p.PlayerId; }
            }
            if (best == BallState.NoOwner) { return; }   // 그 팀 필드 플레이어가 없으면 자유 공

            // IFAB 8조(09-29): 공은 센터 마크, 키커가 그 위에 서고 상대는 공에서 9.15m(센터서클) 밖. 전엔 공을 키커 자리(ST, 중앙에서 약 10m)로
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
            passInFlight = false;
            passReceiverId = -1;
            ballOwnerAtLastTick = BallState.NoOwner;
            holdUpTicksLeft = 0;
            immunityTicksLeft = 0;
            passReleased = true;
            lastKickerId = BallState.NoOwner;
            kickoffTakerId = BallState.NoOwner;
            takeOnOwnerId = BallState.NoOwner;
            takeOnTicksLeft = 0;
            pressLeader[0] = pressLeader[1] = -1;
            looseChaser[0] = looseChaser[1] = -1;
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

        // 고정 스텝 한 틱. 순서가 곧 규칙이다: 공 이동 → 라인 아웃 → 잡기/소유 → 슛 결과 → 선수 판단 → 선수 실행(PlayerId 순)
        public void Tick(float deltaTime)
        {
            tickCount++;
            if (tickCount == MatchClock.HalfTimeTick(Added) && firstKickoffTeam >= 0)
            {
                // 하프타임(IFAB 8조, 09-27): 진영 교체 + 중앙 리셋 + 전반 킥오프를 안 한 팀이 킥오프. 난수를 안 써 결정성 그대로.
                // 러너·시즌·인게임이 같은 Tick을 타므로 같은 시드 = 같은 경기
                for (int i = 0; i < players.Count; i++)
                {
                    players[i].SwitchSides();
                    players[i].Stamina = players[i].StaminaCap;   // 하프타임 휴식: 단기 체력은 다 찬다. 장기 상한은 그대로(후반이 전반보다 무겁다)
                }
                sidesSwitched = true;
                KickoffBy(1 - firstKickoffTeam);
            }

            Ball = BallRules.Step(Ball, deltaTime, MatchTuning.BallDeceleration);

            // 골라인에 못 미치고 감속으로 멈춘 슛(정지 거리 78m 밖에서 쏜 경우). 빗나감으로 마감해야 옛 슛 표시가 남지 않는다
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
                bool sprinting = IsSprintAction(p);
                float beforeX = p.X;
                float beforeZ = p.Z;
                Apply(p, deltaTime);
                bool moved = p.X != beforeX || p.Z != beforeZ;
                UpdateStamina(p, sprinting && moved);
            }
        }

        // 현실에서 전력인 행동(09-29 체력): 공 몰기, 압박 1순위, 루즈볼 추격, 나에게 오는 패스 마중. 스냅샷 값만 봐서 실행 순서와 무관하다
        private bool IsSprintAction(PlayerState p)
        {
            if (Ball.Phase == BallPhase.Owned && Ball.OwnerId == p.PlayerId) { return true; }
            if (p.IsLooseBallChaser || p.IsPassTarget) { return true; }
            return p.BallOwnerTeam == 1 - p.Team && p.PressRank < MatchTuning.MaxPressers;   // 트리 ⑧과 같은 조건(PressRank는 압박 거리 안인 선수만 순위가 있다)
        }

        private static void UpdateStamina(PlayerState p, bool exerted)
        {
            if (!exerted)
            {
                p.Stamina = FatigueRules.Recover(p.Stamina, p.StaminaCap, MatchTuning.FatigueRecoveryHalfLifeTicks);
                return;
            }
            (float stamina, float cap) next = FatigueRules.Exert(p.Stamina, p.StaminaCap, p.Stats.Stamina,
                MatchTuning.FatigueDrainPerTick, MatchTuning.FatigueCapLossPerTick, MatchTuning.FatigueCapMin);
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
                // 태클에 실패해 얼어 있는 선수는 제쳐진 상태라 슛을 못 막는다. 압박·루즈볼 후보와 같은 규칙(09-30 리뷰)
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
            FillPressEligible();
            UpdateChasers(ownerTeam);

            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                int other = 1 - p.Team;
                p.Ball = Ball;
                p.BallOwnerTeam = ownerTeam;
                p.Tactics = tactics[p.Team];
                p.BallThird = thirdByTeam[p.Team];
                p.IsCountering = counteringByTeam[p.Team];
                p.IsCounterPressing = counterPressingByTeam[p.Team];
                p.PressRank = PressRules.PressRank(p.PlayerId, pressEligible[p.Team], Ball.X, Ball.Z, pressLeader[p.Team], MatchTuning.ChaserSwitchMargin);
                p.IsLooseBallChaser = looseChaser[p.Team] == p.PlayerId;
                p.IsKickoffTaker = kickoffTakerId == p.PlayerId;
                p.WantsTakeOn = takeOnOwnerId == p.PlayerId && takeOnTicksLeft > 0;
                p.Teammates = TeammatesExcluding(p);
                p.Opponents = rosterSnapshot[other];
                p.KeeperAlternate = keeperAlternate[p.Team];
                p.OpponentKeeper = keeperIds[other] == -1 ? null : FindPlayer(keeperIds[other]).Stats;
                p.IsPassTarget = passInFlight && passReceiverId == p.PlayerId;
                p.PassTargetX = passTargetX;
                p.PassTargetZ = passTargetZ;
            }
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
            if (p.FrozenTicks > 0) { return; }   // 실패한 태클러는 제쳐져 있다(09-21)

            bool owner = Ball.Phase == BallPhase.Owned && Ball.OwnerId == p.PlayerId;

            // 볼 끌기(개인 holdUp): 소유 직후엔 킥 의도를 대기 틱 동안 무시한다. 타깃맨이 버티는 시간
            if (owner && holdUpTicksLeft > 0 && (p.WantsShoot || p.WantsPass))
            {
                holdUpTicksLeft--;
                return;
            }

            if (p.WantsShoot && owner)
            {
                Shoot(p);
                return;
            }

            if (p.WantsPass && owner && p.PassReceiverId != BallState.NoOwner && p.PassReceiverId != p.PlayerId)
            {
                Pass(p, p.PassReceiverId);
                return;
            }

            if (!p.WantsMove) { return; }

            float speed = MatchRules.SpeedMps(p.Stats.Speed) * FatigueRules.Effort(p.Stamina, MatchTuning.FatigueEffortThreshold, MatchTuning.FatigueEffortMin);
            bool carrying = Ball.Phase == BallPhase.Owned && Ball.OwnerId == p.PlayerId;
            if (carrying) { speed *= MatchTuning.DribbleFactor; }

            float dx = p.MoveTargetX - p.X;
            float dz = p.MoveTargetZ - p.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            float step = speed * deltaTime;
            if (dist <= step)
            {
                p.X = p.MoveTargetX;
                p.Z = p.MoveTargetZ;
            }
            else
            {
                p.X += dx / dist * step;
                p.Z += dz / dist * step;
            }

            if (carrying)
            {
                Ball = BallRules.Carry(Ball, p.X, p.Z);
            }
        }

        // 슛: 확률은 MatchRules, 운명은 주사위 하나로 여기서 정한다. 공은 골 중심으로 날아가고 결과는 도착할 때 확정 표시된다
        private void Shoot(PlayerState shooter)
        {
            PlayerState? keeper = FindGoalkeeper(1 - shooter.Team);
            int reflexes = keeper != null ? keeper.Stats.Reflexes : 0;
            int diving = keeper != null ? keeper.Stats.Diving : 0;

            shotProbability = MatchRules.ShotProbability(shooter.X, shooter.Z, shooter.AttackSign, shooter.Stats.Shot, reflexes, diving);
            if (IsPressed(shooter)) { shotProbability = MatchRules.PressuredShotProbability(shotProbability, MatchTuning.ShotPressureLogit); }   // 트리 WantsShot과 같은 식(09-29 D3)
            shotWillScore = MatchRules.Resolve(shotProbability, nextRoll());
            shotInFlight = true;
            shooterId = shooter.PlayerId;
            takeOnOwnerId = BallState.NoOwner;
            shooterAttackSign = shooter.AttackSign;

            // 조준: 골 중심이 아니라 shot 스탯에 따라 퍼진 지점(09-18). 골문 밖이면 골라인에서 빗나감으로 마감
            shotAimZ = MatchRules.ShotAimZ(shooter.Stats.Shot, nextRoll());
            if (!MatchRules.IsOnTarget(shotAimZ)) { shotWillScore = false; }

            float goalX = FieldBounds.HalfLength * shooter.AttackSign;
            if (TryBlockShot(shooter, goalX)) { return; }
            Ball = BallRules.Kick(Ball, goalX - shooter.X, shotAimZ - shooter.Z, MatchTuning.ShotSpeed);
        }

        // 슈터 발 뻗는 범위 안 상대가 있나(슛 압박). 트리 IsPressed와 같은 반경·같은 틱 시작 스냅샷(09-29 리뷰: 라이브 위치를 쓰면 이번 틱에 먼저 움직인 선수만 반영돼 트리 판단과 갈린다)
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

        // 슛 블록(09-29 수비 D3): 슛 방향 앞의 수비수가 막으면 공은 옆·뒤로 튕겨 자유 공(루즈볼 경합)이 된다. 후보가 있을 때만 주사위를 쓴다
        private bool TryBlockShot(PlayerState shooter, float goalX)
        {
            List<TargetInfo> candidates = blockCandidates[1 - shooter.Team];
            int blockerId = MatchRules.ShotBlocker(shooter.X, shooter.Z, goalX, shotAimZ, candidates, keeperIds[1 - shooter.Team],
                MatchTuning.ShotBlockRange, MatchTuning.ShotBlockConeDeg);
            if (blockerId == -1) { return false; }
            if (!MatchRules.Resolve(MatchTuning.ShotBlockChance, nextRoll())) { return false; }

            // 튕김도 블로커를 고른 스냅샷 좌표로 계산한다. 라이브 위치는 PlayerId가 슈터보다 작으면 이번 틱에 이미 움직인 뒤다(09-30 리뷰: 판정과 실행의 시점 일치)
            TargetInfo blocker = FindSnapshot(candidates, blockerId);
            (float x, float z) dir = MatchRules.BlockReboundDirection(shooter.X, shooter.Z, goalX, shotAimZ, blocker.X, blocker.Z, MatchTuning.BlockReboundTurnDeg);
            // 09-29 리뷰: 슈터 쪽 직선으로 튕기면 슈터가 1~2틱 만에 도로 잡았다. 옆·뒤로 비틀고, 막은 선수 발(잡기 반경) 밖에서 출발시킨다
            float startX = blocker.X + dir.x * MatchTuning.BlockReboundStart;
            float startZ = blocker.Z + dir.z * MatchTuning.BlockReboundStart;
            Ball = BallRules.Kick(BallState.FreeAt(startX, startZ), dir.x, dir.z, MatchTuning.BlockReboundSpeed);
            lastKickerId = shooter.PlayerId;   // 슈터는 공이 자기 반경을 벗어날 때까지 못 잡는다(찬 선수 가드와 같은 규칙)
            Finish(ShotOutcome.Blocked);
            return true;
        }

        // 패스: 리드 목표점으로 직선 비행. 초속은 목표까지 거리로 역산(도착 속도 = 공이 있는 서드의 팀 템포). 리시버는 스냅샷으로 알고 마중 나간다
        private void Pass(PlayerState passer, int receiverId)
        {
            PlayerState receiver = FindPlayer(receiverId);
            Third third = PositionRules.ThirdOf(Ball.X, passer.AttackSign);
            float arrival = MatchTuning.PassArrivalSpeed[tactics[passer.Team].Tempo[(int)third]];

            // 리드 패스: 리시버가 공 도착 때 있을 앞쪽 점으로. 리시버 속도는 그 선수 speed 스탯. 비행 시간은 리시버 거리 기준 평균 속도로
            float dx0 = receiver.X - passer.X;
            float dz0 = receiver.Z - passer.Z;
            float dist = (float)Math.Sqrt(dx0 * dx0 + dz0 * dz0);
            float average = PassRules.AverageSpeed(PassRules.KickSpeed(dist, arrival, MatchTuning.BallDeceleration, MatchTuning.PassSpeedMax), dist, MatchTuning.BallDeceleration);
            (float x, float z) target = PassRules.LeadTarget(passer.X, receiver.X, receiver.Z, receiver.AttackSign, dist, average, MatchRules.SpeedMps(receiver.Stats.Speed));
            float dx1 = target.x - passer.X;
            float dz1 = target.z - passer.Z;
            float speed = PassRules.KickSpeed((float)Math.Sqrt(dx1 * dx1 + dz1 * dz1), arrival, MatchTuning.BallDeceleration, MatchTuning.PassSpeedMax);

            passInFlight = true;
            passReceiverId = receiverId;
            passPasserId = passer.PlayerId;
            passTargetX = target.x;
            passTargetZ = target.z;
            PassCount++;
            if (passer.IsGoalkeeper && tactics[passer.Team].GkDistribution == 1) { keeperAlternate[passer.Team] = !keeperAlternate[passer.Team]; }   // 섞어: 다음 배급은 반대
            kickOriginX = Ball.X;
            kickOriginZ = Ball.Z;
            passReleased = false;
            lastKickerId = passer.PlayerId;
            kickoffTakerId = BallState.NoOwner;   // 킥오프는 첫 킥으로 끝난다
            takeOnOwnerId = BallState.NoOwner;    // 공을 내줬으면 돌파도 끝

            Ball = BallRules.Kick(Ball, target.x - passer.X, target.z - passer.Z, speed);
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
            owner.LastPasserId = BallState.NoOwner;   // 줍기·태클로 잡은 공엔 "방금 준 선수"가 없다(09-26 리뷰: 패스 수신 때만 쓰니 옛 값이 남아 되돌림 후보를 잘못 강등)
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

            holdUpTicksLeft = (int)Math.Round(owner.Stats.HoldUp / MatchTuning.FixedStep);
            immunityTicksLeft = MatchTuning.PossessionImmunityTicks;
            PossessionChanged?.Invoke(new PossessionReport(ownerNow, owner.Team, previousOwnerId, kind, Ball.X, Ball.Z));
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

        // 골라인을 넘은 슛: 골이면 득점, 아니면(GK가 못 건드렸으면) 빗나감. 둘 다 킥오프
        private bool ResolveShotAtGoalLine()
        {
            if (!shotInFlight) { return false; }
            if (Ball.X * shooterAttackSign < FieldBounds.HalfLength) { return false; }

            if (shotWillScore)
            {
                int scorerTeam = TeamOfSign(shooterAttackSign);
                if (scorerTeam == 0) { HomeGoals++; } else { AwayGoals++; }
                Finish(ShotOutcome.Goal);
                if (ResetAfterEveryShot) { Kickoff(); } else { KickoffBy(1 - scorerTeam); }   // 실점한 팀이 킥오프
                return true;
            }

            Finish(ShotOutcome.Missed);   // 골문 밖 조준이거나(shotAimZ) GK를 지나쳤거나
            RestartAfterMiss();
            return true;
        }

        // 골이 아닌 슛이 GK 반경에 닿으면 세이브: 캐치(GK 소유) 또는 튕김(앞으로 자유 공)
        private bool ResolveSaveOnContact()
        {
            PlayerState? keeper = FindGoalkeeper(1 - TeamOfSign(shooterAttackSign));
            if (keeper == null) { return false; }

            float dx = keeper.X - Ball.X;
            float dz = keeper.Z - Ball.Z;
            if (dx * dx + dz * dz > MatchTuning.CaptureRadius * MatchTuning.CaptureRadius) { return false; }

            bool caught = MatchRules.Resolve(MatchRules.CatchProbability(keeper.Stats.Handling), nextRoll());
            if (caught)
            {
                Ball = BallRules.Own(Ball, keeper.PlayerId, keeper.X, keeper.Z);
                Finish(ShotOutcome.Caught);
            }
            else
            {
                Ball = BallRules.Kick(Ball, -shooterAttackSign, 0f, MatchTuning.ParrySpeed);
                Finish(ShotOutcome.Parried);
            }

            if (ResetAfterEveryShot)
            {
                Kickoff();
                return true;
            }
            return false;
        }

        // 빗나간 슛은 골킥: 수비 팀 GK가 자기 자리에서 공을 갖고 배급한다(09-23). 킥오프로 리셋하면 같은 공격이 9.6초 주기로 그대로 재생됐고
        // 상대 팀이 공을 가질 기회가 없었다. GK가 없는 시뮬(리트머스·일부 테스트)과 ResetAfterEveryShot은 예전처럼 킥오프
        private void RestartAfterMiss()
        {
            PlayerState? keeper = ResetAfterEveryShot ? null : FindGoalkeeper(1 - TeamOfSign(shooterAttackSign));
            if (keeper == null) { Kickoff(); return; }
            Ball = BallRules.Own(Ball, keeper.PlayerId, keeper.X, keeper.Z);
            // 골킥 때 상대는 박스 밖(규칙 16조). 09-23 Play: 빗나감 → GK 소유 → 붙은 ST가 배급을 릴리스 지점에서 가로채 7m 슛, 4회 만에 골
            float outsideX = -(FieldBounds.HalfLength - FieldBounds.PenaltyBoxDepth - 1f) * keeper.AttackSign;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Team != keeper.Team && MatchRules.IsInOwnPenaltyBox(p.X, p.Z, keeper.AttackSign)) { p.X = outsideX; }
            }
        }

        private void Finish(ShotOutcome outcome)
        {
            shotInFlight = false;
            ShotResolved?.Invoke(new ShotReport(shooterId, shotProbability, outcome));
        }

        // 태클(09-21): 소유자 태클 사거리(TackleRange) 안의 상대가 쿨다운이 끝났으면 시도. 성공하면 태클러 소유(깔끔한 탈취), 실패하면 태클러 정지.
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
            if (owner.IsGoalkeeper) { return; }   // 공을 잡은 GK는 경합 대상이 아니다(규칙 12조). 09-23 탐지: 골문 앞 GK-ST 태클 핑퐁 7/30경기의 뿌리
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
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.PlayerId == lastKickerId)
                {
                    float kdx = p.X - Ball.X;
                    float kdz = p.Z - Ball.Z;
                    if (kdx * kdx + kdz * kdz <= MatchTuning.CaptureRadius * MatchTuning.CaptureRadius) { continue; }
                    lastKickerId = BallState.NoOwner;
                }
                captureCandidates.Add(new TargetInfo(p.PlayerId, p.X, p.Z));
            }

            int ownerId = BallRules.TryCapture(Ball, captureCandidates, MatchTuning.CaptureRadius, allowFlight: !shotInFlight);
            if (ownerId == BallState.NoOwner) { return; }

            PlayerState owner = FindPlayer(ownerId);
            Ball = BallRules.Own(Ball, ownerId, owner.X, owner.Z);
        }

        // 라인 밖(슛 비행 중은 제외: 골라인 판정이 먼저다) → 가까운 쪽 골킥/스로인 자리에 자유 공(스펙 §5)
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
                float sign = Math.Sign(Ball.X);
                Ball = BallState.FreeAt(sign * (FieldBounds.HalfLength - MatchTuning.GoalKickOffset), 0f);
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
