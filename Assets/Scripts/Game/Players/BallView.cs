using Game.Core.Match;
using UnityEngine;

// 공의 겉모습. 순수 BallState를 transform에 비출 뿐 물리도 판정도 없다(스펙 §7: Rigidbody 안 씀).
// 위치는 고정 스텝 값 둘 사이를 프레임마다 보간하고, 회전은 화면에서 실제로 움직인 거리만큼 굴린다(구르는 각 = 거리 ÷ 반지름). 09-26
public class BallView : MonoBehaviour
{
    private const float Radius = 0.11f;   // 축구공 반지름(지름 22cm). 잔디 위에 얹히는 높이

    private Vector3 previous;
    private Vector3 current;
    private Vector3 rendered;   // 직전 프레임에 그린 위치. 회전량은 이 차이로 잰다

    // 고정 스텝 뒤 MatchManager가 부른다
    public void Apply(in BallState ball)
    {
        previous = current;
        current = new Vector3(ball.X, Radius, ball.Z);
    }

    // 배치·킥오프: 보간 없이 바로
    public void Snap(in BallState ball)
    {
        current = new Vector3(ball.X, Radius, ball.Z);
        previous = current;
        rendered = current;
        transform.position = current;
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
