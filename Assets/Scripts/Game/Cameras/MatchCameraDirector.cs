using Unity.Cinemachine;
using UnityEngine;

// 공 위치에 따라 중계 카메라와 골대 카메라의 우선순위를 바꾼다. 카메라 이동·블렌드는 Cinemachine Brain 몫이고 여기는 "언제"만.
// 09-16 결정(B안): FM26 Broadcast처럼 여러 카메라를 잇는 방식. 골대 근처에서 낮고 가까운 골대 구도로 넘어간다.
// 매니저가 아니고 매니저를 모른다. 공의 Transform(BallView)만 읽는다. 값은 인스펙터(실험 트랙).
public class MatchCameraDirector : MonoBehaviour
{
    [SerializeField] private Transform ball;
    [SerializeField] private CinemachineCamera broadcastCamera;
    [SerializeField] private CinemachineCamera leftGoalCamera;    // -X 골(팀 0의 내 골)
    [SerializeField] private CinemachineCamera rightGoalCamera;   // +X 골
    [SerializeField] private float enterX = 30f;   // |공 X|가 이 밖이면 골대 카메라. 박스(36)보다 6m 앞
    [SerializeField] private float exitX = 24f;    // 이 안으로 돌아오면 중계 카메라. enterX와 차이가 왔다갔다를 막는다

    private const int Active = 20;
    private const int Idle = 0;

    private CinemachineCamera live;

    private void Start()
    {
        Switch(broadcastCamera);
    }

    // Brain이 LateUpdate에서 카메라를 정하므로 그 직전에 우선순위를 맞춘다
    private void LateUpdate()
    {
        if (ball == null) { return; }

        float x = ball.position.x;
        CinemachineCamera wanted = live;

        if (live == broadcastCamera)
        {
            if (x >= enterX) { wanted = rightGoalCamera; }
            else if (x <= -enterX) { wanted = leftGoalCamera; }
        }
        else if (Mathf.Abs(x) <= exitX)
        {
            wanted = broadcastCamera;
        }

        if (wanted != live) { Switch(wanted); }
    }

    private void Switch(CinemachineCamera next)
    {
        broadcastCamera.Priority = next == broadcastCamera ? Active : Idle;
        leftGoalCamera.Priority = next == leftGoalCamera ? Active : Idle;
        rightGoalCamera.Priority = next == rightGoalCamera ? Active : Idle;
        live = next;
    }
}
