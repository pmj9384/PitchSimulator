using Game.Core.Match;
using UnityEngine;

// 선수 1명의 실체(화면). 경기 중 위치·판단은 순수 PlayerState(MatchSimulation 안)가 갖고, 여기는 그 값을 transform에 비춘다.
// 09-16 결정: 경기 루프를 엔진 없이 EditMode에서 굴리기 위해 상태를 전부 순수 쪽에 두었다. 매니저는 모른다.
public class PlayerController : MonoBehaviour
{
    public PlayerState State { get; private set; }
    public int PlayerId => State.PlayerId;
    public int Team => State.Team;

    public void Setup(PlayerState state)
    {
        State = state;
        name = $"Player_{state.Team}_{state.Stats.RoleId}_{state.PlayerId}";   // 하이어라키에서 바로 읽히게
        SyncView();
    }

    // 고정 스텝 뒤 MatchManager가 부른다. 높이(y)는 발 높이 0
    public void SyncView()
    {
        transform.position = new Vector3(State.X, 0f, State.Z);
    }
}
