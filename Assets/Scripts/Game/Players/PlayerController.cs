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
        name = $"Player_{state.Team}_{state.Stats.VariantId}_{state.PlayerId}";   // 하이어라키에서 바로 읽히게
        SnapView();
    }

    private Vector3 previous;   // 직전 고정 스텝 위치
    private Vector3 current;    // 마지막 고정 스텝 위치. Update가 둘 사이를 보간한다(09-26)

    // 고정 스텝 뒤 MatchManager가 부른다. 높이(y)는 발 높이 0
    public void SyncView()
    {
        previous = current;
        current = new Vector3(State.X, 0f, State.Z);
    }

    // 배치·킥오프처럼 순간이동이 맞는 때: 보간 없이 바로 세운다
    public void SnapView()
    {
        current = new Vector3(State.X, 0f, State.Z);
        previous = current;
        transform.position = current;
    }

    public Vector3 ViewPosition => current;

    // 시뮬 밖 연출(골 세리머니, 09-29): 상태는 그대로 두고 화면만 옮긴다. 보간은 SyncView와 같은 방식
    public void MoveViewToward(Vector3 target, float maxStep)
    {
        previous = current;
        current = Vector3.MoveTowards(current, target, maxStep);
    }

    // 연출 중 제자리: 직전 스텝과의 보간을 끊는다. 안 끊으면 Update가 같은 두 점 사이를 스텝마다 되풀이해 떨린다
    public void HoldView()
    {
        previous = current;
    }

    private void Update()
    {
        transform.position = Vector3.Lerp(previous, current, ViewInterpolation.Alpha());
    }
}
