using Game.Core.AI;
using Game.Core.Data;

namespace Game.Core.Match
{
    // 선수 1명의 경기 중 상태(순수). 트리가 읽고 시키는 IPlayerContext의 실제 구현.
    // 위치·의도가 전부 여기 있어서 MatchSimulation이 엔진 없이 22명을 굴린다. PlayerController는 이 값을 화면에 비출 뿐이다.
    // 트리는 의도(MoveTarget·WantsShoot)만 남기고 실행은 시뮬이 한다. 그래야 실행 순서가 PlayerId 순으로 고정된다.
    public sealed class PlayerState : IPlayerContext
    {
        public int PlayerId { get; }
        public int Team { get; }
        public int AttackSign => Team == 0 ? +1 : -1;
        public float X { get; set; }
        public float Z { get; set; }
        public PlayerStats Stats { get; }
        public bool IsGoalkeeper { get; }

        // 킥오프 때 돌아갈 자리
        public float HomeX { get; }
        public float HomeZ { get; }

        // 이번 틱 공 스냅샷(시뮬이 트리 틱 직전에 넣는다)
        public BallState Ball { get; set; }
        public BallPhase BallPhase => Ball.Phase;
        public bool OwnsBall => Ball.Phase == BallPhase.Owned && Ball.OwnerId == PlayerId;
        public float BallX => Ball.X;
        public float BallZ => Ball.Z;

        // 의도(시뮬이 읽고 지운다)
        public bool WantsMove { get; private set; }
        public float MoveTargetX { get; private set; }
        public float MoveTargetZ { get; private set; }
        public bool WantsShoot { get; private set; }

        public PlayerState(int playerId, int team, PlayerStats stats, float x, float z)
        {
            PlayerId = playerId;
            Team = team;
            Stats = stats;
            X = x;
            Z = z;
            HomeX = x;
            HomeZ = z;
            IsGoalkeeper = string.Equals(stats.RoleId, "GK", System.StringComparison.OrdinalIgnoreCase);
        }

        public void MoveToward(float x, float z)
        {
            WantsMove = true;
            MoveTargetX = x;
            MoveTargetZ = z;
        }

        public void Shoot()
        {
            WantsShoot = true;
        }

        public void ClearIntent()
        {
            WantsMove = false;
            WantsShoot = false;
        }

        public void ReturnHome()
        {
            X = HomeX;
            Z = HomeZ;
        }
    }
}
