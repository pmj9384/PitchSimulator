using Game.Core.Match;
using UnityEngine;

// 공의 겉모습. 순수 BallState를 transform에 비출 뿐 물리도 판정도 없다(스펙 §7: Rigidbody 안 씀).
public class BallView : MonoBehaviour
{
    private const float Radius = 0.11f;   // 축구공 반지름(지름 22cm). 잔디 위에 얹히는 높이

    public void Apply(in BallState ball)
    {
        transform.position = new Vector3(ball.X, Radius, ball.Z);
    }
}
