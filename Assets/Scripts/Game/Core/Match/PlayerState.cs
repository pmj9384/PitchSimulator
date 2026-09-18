using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    // 선수 1명의 경기 중 상태(순수). 트리가 읽고 시키는 IPlayerContext의 실제 구현.
    // 위치·의도가 전부 여기 있어서 MatchSimulation이 엔진 없이 22명을 굴린다. PlayerController는 이 값을 화면에 비출 뿐이다.
    // 트리는 의도(MoveTarget·WantsShoot·WantsPass)만 남기고 실행은 시뮬이 한다. 그래야 실행 순서가 PlayerId 순으로 고정된다.
    // 스냅샷(공·서드·역습·동료·상대·상대 GK·패스 대상)은 시뮬이 트리 틱 직전에 채운다(09-18).
    public sealed class PlayerState : IPlayerContext
    {
        public int PlayerId { get; }
        public int Team { get; }
        public int AttackSign => Team == 0 ? +1 : -1;
        public float X { get; set; }
        public float Z { get; set; }
        public PlayerStats Stats { get; }
        public bool IsGoalkeeper { get; }

        // 자리 2쌍(스펙 §4-3). 배치 좌표 그대로. 킥오프 때 공격 시 자리로 돌아간다
        public float AttackHomeX { get; }
        public float AttackHomeZ { get; }
        public float DefendHomeX { get; }
        public float DefendHomeZ { get; }

        // ── 이번 틱 스냅샷(시뮬이 넣는다)
        public BallState Ball { get; set; }
        public BallPhase BallPhase => Ball.Phase;
        public bool OwnsBall => Ball.Phase == BallPhase.Owned && Ball.OwnerId == PlayerId;
        public float BallX => Ball.X;
        public float BallZ => Ball.Z;
        public int BallOwnerTeam { get; set; } = -1;

        public TeamTactics Tactics { get; set; } = new TeamTactics();
        public Third BallThird { get; set; }
        public bool IsCountering { get; set; }
        public bool IsCounterPressing { get; set; }

        public IReadOnlyList<TargetInfo> Teammates { get; set; } = System.Array.Empty<TargetInfo>();
        public IReadOnlyList<TargetInfo> Opponents { get; set; } = System.Array.Empty<TargetInfo>();
        public int OpponentKeeperId { get; set; } = -1;
        public PlayerStats? OpponentKeeper { get; set; }

        public bool IsPassTarget { get; set; }
        public float PassTargetX { get; set; }
        public float PassTargetZ { get; set; }

        // ── 의도(시뮬이 읽고 지운다)
        public bool WantsMove { get; private set; }
        public float MoveTargetX { get; private set; }
        public float MoveTargetZ { get; private set; }
        public bool WantsShoot { get; private set; }
        public bool WantsPass { get; private set; }
        public int PassReceiverId { get; private set; } = -1;

        // 수비 시 자리를 따로 안 주면 공격 시 자리와 같다(편성 CSV의 posX2·posZ2는 09-22)
        public PlayerState(int playerId, int team, PlayerStats stats, float x, float z)
            : this(playerId, team, stats, x, z, x, z)
        {
        }

        public PlayerState(int playerId, int team, PlayerStats stats, float attackX, float attackZ, float defendX, float defendZ)
        {
            PlayerId = playerId;
            Team = team;
            Stats = stats;
            X = attackX;
            Z = attackZ;
            AttackHomeX = attackX;
            AttackHomeZ = attackZ;
            DefendHomeX = defendX;
            DefendHomeZ = defendZ;
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

        public void Pass(int receiverId)
        {
            WantsPass = true;
            PassReceiverId = receiverId;
        }

        public void ClearIntent()
        {
            WantsMove = false;
            WantsShoot = false;
            WantsPass = false;
            PassReceiverId = -1;
        }

        public void ReturnHome()
        {
            X = AttackHomeX;
            Z = AttackHomeZ;
        }
    }
}
