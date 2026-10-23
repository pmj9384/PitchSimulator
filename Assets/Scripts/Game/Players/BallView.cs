using Game.Core.Match;
using UnityEngine;

// 공의 겉모습. 순수 BallState를 transform에 비출 뿐 물리도 판정도 없다(스펙 §7: Rigidbody 안 씀).
// 위치는 고정 스텝 값 둘 사이를 프레임마다 보간하고, 회전은 화면에서 실제로 움직인 거리만큼 굴린다(구르는 각 = 거리 ÷ 반지름). 09-26
public class BallView : MonoBehaviour
{
    private const float Radius = 0.11f;   // 축구공 반지름(지름 22cm). 잔디 위에 얹히는 높이
    private const float CarryOffset = 0.4f;   // 소유 중 소유자 앞으로 띄우는 거리(m). 시뮬은 공을 소유자 좌표에 두므로(BallRules.Carry) 그대로 그리면 다리 사이에 보인다(10-23 유저 Play). [가정] 출처 없음, Play로 조정

    private Vector3 previous;
    private Vector3 current;
    private Vector3 rendered;   // 직전 프레임에 그린 위치. 회전량은 이 차이로 잰다

    // 씬에 미리 놓인 오브젝트라 스폰 훅이 없다. 첫 Snap(킥오프) 전 프레임에 원점(보간 값 기본 0)으로 튀지 않게 지금 자리로 시작
    private void Awake()
    {
        current = transform.position;
        previous = current;
        rendered = current;
    }

    // 고정 스텝 뒤 MatchManager가 부른다. carrierForward = 소유자 뷰가 보는 방향(소유 중이 아니면 0)
    public void Apply(in BallState ball, Vector3 carrierForward)
    {
        previous = current;
        current = Place(ball, carrierForward);
    }

    // 배치·킥오프: 보간 없이 바로
    public void Snap(in BallState ball, Vector3 carrierForward)
    {
        current = Place(ball, carrierForward);
        previous = current;
        rendered = current;
        transform.position = current;
    }

    // 시뮬 좌표 → 화면 좌표. 소유 중이면 발 앞. 판정은 시뮬 좌표 그대로라 화면만 움직인다
    private static Vector3 Place(in BallState ball, Vector3 carrierForward)
    {
        var position = new Vector3(ball.X, Radius, ball.Z);
        if (ball.Phase != BallPhase.Owned) { return position; }
        return position + carrierForward * CarryOffset;
    }

    // 연출 중 제자리(골 세리머니, 09-29): 골망 앞에 멈춘 공을 그대로 둔다
    public void Hold()
    {
        previous = current;
    }

    private void Update()
    {
        Vector3 next = Vector3.Lerp(previous, current, ViewInterpolation.Alpha());
        Vector3 delta = next - rendered;
        float distance = delta.magnitude;
        if (distance > 0f)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, delta / distance);   // 진행 방향에 수직인 수평 축으로 앞으로 구른다
            transform.Rotate(axis, distance / Radius * Mathf.Rad2Deg, Space.World);
        }
        transform.position = next;
        rendered = next;
    }
}
