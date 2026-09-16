using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Placement;

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
        public BallState Ball { get; private set; }
        public IReadOnlyList<PlayerState> Players => players;
        public int HomeGoals { get; private set; }
        public int AwayGoals { get; private set; }

        // 1주차 리트머스 판: 슛이 어떤 결과든 끝나면 킥오프로 되돌린다. ST가 10번 쏘려면 공이 매번 돌아와야 한다.
        // 2주차에 GK 배급(패스)이 생기면 끈다. 골 뒤 킥오프는 스펙 §7이라 이 스위치와 무관하게 항상 한다
        public bool ResetAfterEveryShot { get; set; }

        public event Action<ShotReport>? ShotResolved;

        private readonly List<PlayerState> players = new List<PlayerState>();
        private readonly List<TargetInfo> captureCandidates = new List<TargetInfo>();
        private readonly Func<float> nextRoll;
        private readonly BehaviorNode tree;

        // 비행 중인 슛의 결정된 운명. 골이면 GK 접촉을 무시하고 골라인을 넘긴다
        private bool shotInFlight;
        private bool shotWillScore;
        private int shooterId;
        private int shooterAttackSign;
        private float shotProbability;

        public MatchSimulation(Func<float> nextRoll, BehaviorNode tree)
        {
            this.nextRoll = nextRoll;
            this.tree = tree;
            Ball = BallState.FreeAt(0f, 0f);
        }

        public PlayerState AddPlayer(PlayerState player)
        {
            players.Add(player);
            return player;
        }

        public void Kickoff()
        {
            Ball = BallState.FreeAt(0f, 0f);
            shotInFlight = false;
            for (int i = 0; i < players.Count; i++)
            {
                players[i].ReturnHome();
            }
        }

        // 고정 스텝 한 틱. 순서가 곧 규칙이다: 공 이동 → 라인 아웃 → 잡기/소유 → 슛 결과 → 선수 판단 → 선수 실행(PlayerId 순)
        public void Tick(float deltaTime)
        {
            Ball = BallRules.Step(Ball, deltaTime, MatchTuning.BallDeceleration);

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

            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                p.ClearIntent();
                p.Ball = Ball;
                tree.Tick(p);
            }

            for (int i = 0; i < players.Count; i++)
            {
                Apply(players[i], deltaTime);
            }
        }

        // ── 선수 실행
        private void Apply(PlayerState p, float deltaTime)
        {
            if (p.WantsShoot && Ball.Phase == BallPhase.Owned && Ball.OwnerId == p.PlayerId)
            {
                Shoot(p);
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

            float goalX = FieldBounds.HalfLength * shooter.AttackSign;
            Ball = BallRules.Kick(Ball, goalX - shooter.X, 0f - shooter.Z, MatchTuning.ShotSpeed);
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

            Finish(ShotOutcome.Missed);
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
                captureCandidates.Add(new TargetInfo(players[i].PlayerId, players[i].X, players[i].Z));
            }

            int ownerId = BallRules.TryCapture(Ball, captureCandidates, MatchTuning.CaptureRadius);
            if (ownerId == BallState.NoOwner) { return; }

            PlayerState owner = FindPlayer(ownerId);
            Ball = BallRules.Own(Ball, ownerId, owner.X, owner.Z);
        }

        // 라인 밖(슛 비행 중은 제외: 골라인 판정이 먼저다) → 가까운 쪽 골킥/스로인 자리에 자유 공(스펙 §5)
        private bool ResetIfOut()
        {
            if (Ball.Phase == BallPhase.Owned || shotInFlight) { return false; }

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
