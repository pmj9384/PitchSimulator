using Game.Core.Match;
using UnityEngine;

// 공의 겉모습. 순수 BallState를 transform에 비출 뿐 물리도 판정도 없다(스펙 §7: Rigidbody 안 씀).
// 위치는 고정 스텝 값 둘 사이를 프레임마다 보간하고, 회전은 화면에서 실제로 움직인 거리만큼 굴린다(구르는 각 = 거리 ÷ 반지름). 09-26
// 예외는 스로인 하나다(11-17): 들고 있는 동안은 던지는 선수의 두 손 사이에 붙이고, 던진 뒤에는 손 높이에서 땅까지 화면에서만 내린다
public class BallView : MonoBehaviour
{
    private const float Radius = 0.11f;   // 축구공 반지름(지름 22cm). 잔디 위에 얹히는 높이
    private const float CarryOffset = 0.55f;   // 소유 중 소유자 앞으로 띄우는 거리(m). 0.4는 유저 Play "좀 더 가야"(10-23). 시뮬은 공을 소유자 좌표에 두므로(BallRules.Carry) 그대로 그리면 다리 사이에 보인다(10-23 유저 Play). [가정] 출처 없음, Play로 조정
    private const float ReleaseSeconds = 0.4f;   // 스로인을 던진 뒤 손 높이에서 땅까지 내려오는 시간. 시뮬 공은 땅볼이라 화면만 내린다 [가정]
    private static readonly int ReleaseTicks = Mathf.RoundToInt(ReleaseSeconds / MatchTuning.FixedStep);

    private PlayerController throwInHolder;   // 스로인 공을 들고 있는 선수. 있으면 고정 스텝 값 대신 그 손 사이에 그린다(11-17)
    private int releaseTicksLeft;      // 던진 뒤 남은 내려오기 틱. 0이면 시뮬 높이 그대로
    private float releaseHeight;       // 놓은 순간 화면 공 높이(손 높이)
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

    // 고정 스텝 뒤 MatchManager가 부른다. carrierForward = 소유자 뷰가 보는 방향(소유 중이 아니면 0), holder = 스로인 공을 든 선수(없으면 null)
    public void Apply(in BallState ball, Vector3 carrierForward, PlayerController holder)
    {
        throwInHolder = holder;
        previous = current;
        if (throwInHolder != null)
        {
            current = transform.position;   // 손에 붙은 자리. 던진 뒤 첫 보간이 발 앞이 아니라 손에서 시작한다
            return;
        }
        current = Place(ball, carrierForward) + Vector3.up * ReleaseLift();
        if (releaseTicksLeft > 0) { releaseTicksLeft--; }
    }

    // 배치·킥오프: 보간 없이 바로
    public void Snap(in BallState ball, Vector3 carrierForward, PlayerController holder)
    {
        throwInHolder = holder;
        releaseTicksLeft = 0;
        current = Place(ball, carrierForward);
        previous = current;
        rendered = current;
        transform.position = current;
    }

    // 스로인을 던진 순간(MatchManager.OnBallKicked). 지금 손 높이에서 다음 Apply부터 땅으로 내려온다. 손에서 떼는 것은 그 Apply가 한다(공이 소유 중이 아니라 holder가 null로 온다)
    public void StartThrowRelease()
    {
        releaseHeight = transform.position.y;
        releaseTicksLeft = ReleaseTicks;
    }

    // 남은 내려오기 비율만큼 놓은 높이를 더한다(직선으로 줄어듦)
    private float ReleaseLift()
    {
        if (releaseTicksLeft <= 0) { return 0f; }
        return (releaseHeight - Radius) * ((float)releaseTicksLeft / ReleaseTicks);
    }

    // 시뮬 좌표 → 화면 좌표. 소유 중이면 발 앞. 판정은 시뮬 좌표 그대로라 화면만 움직인다
    private static Vector3 Place(in BallState ball, Vector3 carrierForward)
    {
        var position = new Vector3(ball.X, Radius + ball.Y, ball.Z);   // 띄운 공은 높이만큼 위(10-29 ④-1)
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
        if (throwInHolder != null) { return; }   // 손에 든 공은 애니가 손을 옮긴 뒤(LateUpdate) 붙인다
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

    // 스로인 공은 두 손 사이에 붙인다. 애니메이터가 뼈를 옮긴 뒤라 LateUpdate다. 손이 없는 몸은 MatchManager가 holder로 넘기지 않는다
    private void LateUpdate()
    {
        if (throwInHolder == null) { return; }
        Vector3 hands = throwInHolder.HandCenter();
        transform.position = hands;
        rendered = hands;
    }
}
