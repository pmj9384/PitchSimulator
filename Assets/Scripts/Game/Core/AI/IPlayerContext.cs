using Game.Core.Data;
using Game.Core.Match;

namespace Game.Core.AI
{
    // BT가 선수에게 묻고(읽기) 시키는(행동) 유일한 창구. 트리는 MonoBehaviour를 모르고 이 계약만 본다.
    // 가짜 구현만 있으면 엔진 없이 트리를 테스트한다(CompositeNodeTests). 실제 구현은 순수 PlayerState(MatchSimulation 안).
    // 멤버는 09-16 공·소유·슛 설계에서 리트머스(ST 1 vs GK 1)에 필요한 것만 채웠다. 패스·압박(2주차)은 그때 늘린다.
    public interface IPlayerContext
    {
        // ── 나
        int PlayerId { get; }
        int Team { get; }
        int AttackSign { get; }        // +1 = +X 골을 노림(팀 0), -1 = 반대(팀 1)
        float X { get; }
        float Z { get; }
        PlayerStats Stats { get; }
        bool IsGoalkeeper { get; }

        // ── 공(이번 틱 스냅샷)
        BallPhase BallPhase { get; }
        bool OwnsBall { get; }
        float BallX { get; }
        float BallZ { get; }

        // ── 시키기. 실행(속도·판정)은 시뮬 몫, 트리는 의도만 남긴다
        void MoveToward(float x, float z);
        void Shoot();
    }
}
