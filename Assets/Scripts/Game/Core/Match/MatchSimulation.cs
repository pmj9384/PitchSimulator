using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    public enum ShotOutcome { Goal, Caught, Parried, Missed }

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

        private readonly List<PlayerState> players = new List<PlayerState>();
        private readonly List<TargetInfo> captureCandidates = new List<TargetInfo>();
        private readonly Func<float> nextRoll;
        private readonly BehaviorNode tree;

        // 팀 전술 2개(스타일 카드). 기본은 "균형"과 같은 빈 값(전부 0)이고 MatchManager가 프리셋을 주입한다
        private readonly TeamTactics[] tactics = { new TeamTactics(), new TeamTactics() };

        // 틱마다 한 번 계산하는 팀 단위 국면(09-18): 공은 하나라 서드·역습도 팀당 하나. 22명이 같은 값을 본다
        private readonly List<TargetInfo>[] rosterSnapshot = { new List<TargetInfo>(), new List<TargetInfo>() };
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
        private float passTargetX;
        private float passTargetZ;
        private int ballOwnerAtLastTick = BallState.NoOwner;
        private int holdUpTicksLeft;          // 볼 끌기(개인 holdUp): 소유 뒤 킥까지 대기 틱
        private bool keeperAlternate;         // GK 배급 "섞어"의 교대 스위치
        private int lastKickerId = BallState.NoOwner;   // 방금 찬 선수. 공이 발치를 벗어날 때까지 자기 공을 다시 못 잡는다
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
            return PositionRules.ThirdOf(Ball.X, team == 0 ? +1 : -1);
        }

        public bool IsCountering(int team)
        {
            if (Ball.Phase == BallPhase.Free || OwnerTeam() != team) { return false; }

            int threshold = tactics[team].CounterThreshold;
            if (threshold < 0) { return false; }

            int sign = team == 0 ? +1 : -1;
            int ahead = PositionRules.CountDefendersAhead(Ball.X, rosterSnapshot[1 - team], sign, keeperIds[1 - team]);
            return ahead <= threshold;
        }

        public bool IsCounterPressing(int team)
        {
            if (turnoverLoserTeam != team) { return false; }

            int sign = team == 0 ? +1 : -1;
            // 뺏긴 순간 우리 골 쪽에 남은 우리 수비 수 = 공보다 우리 골 쪽에 있는 우리 필드 플레이어(GK 제외)
            int behind = PositionRules.CountDefendersAhead(Ball.X, rosterSnapshot[team], -sign, keeperIds[team]);
            return PressRules.IsCounterPressing(behind, tactics[team].CounterPress, ticksSinceTurnover);
        }

        public int OwnerTeam()
        {
            if (Ball.Phase != BallPhase.Owned) { return -1; }
            return FindPlayer(Ball.OwnerId).Team;
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
            lastKickerId = BallState.NoOwner;
            for (int i = 0; i < players.Count; i++)
            {
                players[i].ReturnHome();
            }
        }

        // 고정 스텝 한 틱. 순서가 곧 규칙이다: 공 이동 → 라인 아웃 → 잡기/소유 → 슛 결과 → 선수 판단 → 선수 실행(PlayerId 순)
        public void Tick(float deltaTime)
        {
            Ball = BallRules.Step(Ball, deltaTime, MatchTuning.BallDeceleration);

            // 골라인에 못 미치고 감속으로 멈춘 슛(정지 거리 78m 밖에서 쏜 경우). 빗나감으로 마감해야 옛 슛 표시가 남지 않는다
            if (shotInFlight && Ball.Phase != BallPhase.Flight)
            {
                Finish(ShotOutcome.Missed);
                Kickoff();
                return;
            }

            if (ResolveShotAtGoalLine()) { return; }
            if (ResetIfOut()) { return; }

            if (Ball.Phase == BallPhase.Free)
            {
                TryCapture();
            }
            else if (Ball.Phase == BallPhase.Flight && shotInFlight && !shotWillScore)
            {
                if (ResolveSaveOnContact()) { return; }
            }
            else if (Ball.Phase == BallPhase.Flight && passInFlight)
            {
                TryCapture();   // 리시버가 받거나 상대가 가로챈다. 잡기 규칙은 같다(거리 ≤ 0.8)
            }

            OnOwnerChanged();

            FillSnapshots();

            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                p.ClearIntent();
                tree.Tick(p);
            }

            for (int i = 0; i < players.Count; i++)
            {
                Apply(players[i], deltaTime);
            }
        }

        // 틱마다 한 번: 명부 위치 스냅샷 → 턴오버 감지 → 팀 국면(서드·역습·역압박) → 22명에게 같은 값을 넣는다
        private void FillSnapshots()
        {
            rosterSnapshot[0].Clear();
            rosterSnapshot[1].Clear();
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                rosterSnapshot[p.Team].Add(new TargetInfo(p.PlayerId, p.X, p.Z));
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

            bool[] countering = { IsCountering(0), IsCountering(1) };
            bool[] counterPressing = { IsCounterPressing(0), IsCounterPressing(1) };
            Third[] thirds = { BallThirdOf(0), BallThirdOf(1) };

            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                int other = 1 - p.Team;
                p.Ball = Ball;
                p.BallOwnerTeam = ownerTeam;
                p.Tactics = tactics[p.Team];
                p.BallThird = thirds[p.Team];
                p.IsCountering = countering[p.Team];
                p.IsCounterPressing = counterPressing[p.Team];
                p.Teammates = TeammatesExcluding(p);
                p.Opponents = rosterSnapshot[other];
                p.OpponentKeeperId = keeperIds[other];
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

            float speed = MatchRules.SpeedMps(p.Stats.Speed);
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
            shotWillScore = MatchRules.Resolve(shotProbability, nextRoll());
            shotInFlight = true;
            shooterId = shooter.PlayerId;
            shooterAttackSign = shooter.AttackSign;

            // 조준: 골 중심이 아니라 shot 스탯에 따라 퍼진 지점(09-18). 골문 밖이면 골라인에서 빗나감으로 마감
            shotAimZ = MatchRules.ShotAimZ(shooter.Stats.Shot, nextRoll());
            if (!MatchRules.IsOnTarget(shotAimZ)) { shotWillScore = false; }

            float goalX = FieldBounds.HalfLength * shooter.AttackSign;
            Ball = BallRules.Kick(Ball, goalX - shooter.X, shotAimZ - shooter.Z, MatchTuning.ShotSpeed);
        }

        // 패스: 리시버 현재 위치로 직선 비행. 속도는 공이 있는 서드의 팀 속도. 리시버는 스냅샷으로 알고 마중 나간다
        private void Pass(PlayerState passer, int receiverId)
        {
            PlayerState receiver = FindPlayer(receiverId);
            Third third = PositionRules.ThirdOf(Ball.X, passer.AttackSign);
            float speed = MatchTuning.PassSpeed[tactics[passer.Team].Tempo[(int)third]];

            passInFlight = true;
            passReceiverId = receiverId;
            passTargetX = receiver.X;
            passTargetZ = receiver.Z;
            PassCount++;
            lastKickerId = passer.PlayerId;

            Ball = BallRules.Kick(Ball, receiver.X - passer.X, receiver.Z - passer.Z, speed);
        }

        // 소유자가 바뀐 틱: 패스 비행 마감(받았거나 가로챘거나), 볼 끌기 대기 시작
        private void OnOwnerChanged()
        {
            int ownerNow = Ball.Phase == BallPhase.Owned ? Ball.OwnerId : BallState.NoOwner;
            if (ownerNow == ballOwnerAtLastTick) { return; }
            ballOwnerAtLastTick = ownerNow;

            if (ownerNow == BallState.NoOwner) { return; }

            if (passInFlight)
            {
                if (ownerNow != passReceiverId) { InterceptCount++; }
                passInFlight = false;
                passReceiverId = -1;
            }

            PlayerState owner = FindPlayer(ownerNow);
            holdUpTicksLeft = (int)Math.Round(owner.Stats.HoldUp / MatchTuning.FixedStep);
        }

        // GK 배급 대상(트리가 부른다). "섞어"는 부를 때마다 교대
        public int KeeperDistributionTarget(PlayerState keeper)
        {
            int level = tactics[keeper.Team].GkDistribution;
            List<TargetInfo> mates = rosterSnapshot[keeper.Team];
            if (mates.Count == 0)
            {
                // 스냅샷 전(테스트·킥오프 직후)엔 명부에서 직접 만든다
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].Team == keeper.Team) { mates.Add(new TargetInfo(players[i].PlayerId, players[i].X, players[i].Z)); }
                }
            }
            int target = PassRules.KeeperDistributionTarget(keeper.X, keeper.Z, mates, keeper.AttackSign, level, keeper.PlayerId, keeperAlternate);
            if (level == 1) { keeperAlternate = !keeperAlternate; }
            return target;
        }

        // 골라인을 넘은 슛: 골이면 득점, 아니면(GK가 못 건드렸으면) 빗나감. 둘 다 킥오프
        private bool ResolveShotAtGoalLine()
        {
            if (!shotInFlight) { return false; }
            if (Ball.X * shooterAttackSign < FieldBounds.HalfLength) { return false; }

            if (shotWillScore)
            {
                if (shooterAttackSign > 0) { HomeGoals++; } else { AwayGoals++; }
                Finish(ShotOutcome.Goal);
                Kickoff();
                return true;
            }

            Finish(ShotOutcome.Missed);   // 골문 밖 조준이거나(shotAimZ) GK를 지나쳤거나
            Kickoff();
            return true;
        }

        // 골이 아닌 슛이 GK 반경에 닿으면 세이브: 캐치(GK 소유) 또는 튕김(앞으로 자유 공)
        private bool ResolveSaveOnContact()
        {
            PlayerState? keeper = FindGoalkeeper(shooterAttackSign > 0 ? 1 : 0);
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

        private void Finish(ShotOutcome outcome)
        {
            shotInFlight = false;
            ShotResolved?.Invoke(new ShotReport(shooterId, shotProbability, outcome));
        }

        private void TryCapture()
        {
            captureCandidates.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.PlayerId == lastKickerId)
                {
                    // 찬 직후엔 공이 아직 발치라 자기 공을 도로 잡는다. 반경을 벗어나면 다시 후보
                    float kdx = p.X - Ball.X;
                    float kdz = p.Z - Ball.Z;
                    if (kdx * kdx + kdz * kdz <= MatchTuning.CaptureRadius * MatchTuning.CaptureRadius) { continue; }
                    lastKickerId = BallState.NoOwner;
                }
                captureCandidates.Add(new TargetInfo(p.PlayerId, p.X, p.Z));
            }

            int ownerId = BallRules.TryCapture(Ball, captureCandidates, MatchTuning.CaptureRadius, allowFlight: passInFlight);
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
    }
}
