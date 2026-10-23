using Game.Core.Match;
using UnityEngine;

// 선수 1명의 실체(화면). 경기 중 위치·판단은 순수 PlayerState(MatchSimulation 안)가 갖고, 여기는 그 값을 transform에 비춘다.
// 09-16 결정: 경기 루프를 엔진 없이 EditMode에서 굴리기 위해 상태를 전부 순수 쪽에 두었다. 매니저는 모른다.
public class PlayerController : MonoBehaviour
{
    private static readonly int SpeedParam = Animator.StringToHash("Speed");   // PlayerAnimator.controller의 float. 서기/걷기/달리기를 고른다
    private static readonly int KickParam = Animator.StringToHash("Kick");     // 트리거. 패스 순간(MatchSimulation.BallKicked)
    private static readonly int ShootParam = Animator.StringToHash("Shoot");   // 트리거. 슛 순간. 패스보다 백스윙·팔로스루가 크다(10-23 유저 "슛도 똑같아서 모르겠다")
    private static readonly int CatchParam = Animator.StringToHash("Catch");         // 트리거. GK가 슛을 잡음(ShotResolved Caught)
    private static readonly int ParryParam = Animator.StringToHash("Parry");         // 트리거. GK가 슛을 쳐냄(ShotResolved Parried)
    private static readonly int CelebrateParam = Animator.StringToHash("Celebrate"); // bool. 골 세리머니 동안 참

    [SerializeField] private Animator animator;

    private Vector3 previous;   // 직전 고정 스텝 위치
    private Vector3 current;    // 마지막 고정 스텝 위치. Update가 둘 사이를 보간한다

    private const float TurnDegreesPerSecond = 720f;   // [가정] 출처 없음. 한 스텝(0.02초) 사이 반대 방향 전환을 0.25초 안에 끝내는 값
    private const float MinMoveSqr = 0.0001f;            // 이보다 덜 움직이면 서 있는 것(향한 방향 유지)
    private const float KickFaceSeconds = 0.5f;          // 킥 뒤 이 동안은 이동 방향이 아니라 찬 방향을 본다. 찬 순간 즉시 돌린다: 720°/s로 돌리면 옆·뒤 패스는 접촉(0.18초) 전에 못 돈다(10-23 유저) [가정]
    private const float SaveSeconds = 0.45f;             // 세이브 클립이 끝날 때까지. 이 안에 오는 배급 킥은 애니를 안 튼다(GK가 잡자마자 차면 캐치가 0.05초 만에 끊겼다) [가정]

    private Vector3 faceOverride;       // 킥 직후 보는 방향
    private float faceOverrideUntil;    // 그 방향을 유지하는 끝 시각(Time.time)
    private float saveUntil;            // 세이브 애니가 끝나는 시각. 그 전의 킥 트리거는 버린다

    public PlayerState State { get; private set; }
    public int PlayerId => State.PlayerId;
    public int Team => State.Team;

    public void Setup(PlayerState state)
    {
        State = state;
        name = $"Player_{state.Team}_{state.Stats.VariantId}_{state.PlayerId}";   // 하이어라키에서 바로 읽히게
        ResetAnimation();   // 풀에서 다시 꺼낸 몸에 지난 경기의 트리거·세리머니가 남아 있을 수 있다
        SnapView();
    }

    // 고정 스텝 뒤 MatchManager가 부른다. 높이(y)는 발 높이 0
    public void SyncView()
    {
        previous = current;
        current = new Vector3(State.X, 0f, State.Z);
        SetSpeed();
    }

    // 배치·킥오프처럼 순간이동이 맞는 때: 보간 없이 바로 세운다
    public void SnapView()
    {
        current = new Vector3(State.X, 0f, State.Z);
        previous = current;
        transform.position = current;
        SetSpeed();
        FaceKickoff();
    }

    public Vector3 ViewPosition => current;

    // 찬 순간. direction = 공이 떠난 방향(시뮬 공 속도). 몸을 그쪽으로 돌려 발이 공을 향하게 한다. 슛과 패스는 클립이 다르다
    public void PlayKick(Vector3 direction, bool shot)
    {
        FaceToward(direction, KickFaceSeconds);
        if (animator == null || Time.time < saveUntil) { return; }
        animator.SetTrigger(shot ? ShootParam : KickParam);
    }

    public void PlayCatch()
    {
        saveUntil = Time.time + SaveSeconds;
        if (animator != null) { animator.SetTrigger(CatchParam); }
    }

    public void PlayParry()
    {
        saveUntil = Time.time + SaveSeconds;
        if (animator != null) { animator.SetTrigger(ParryParam); }
    }

    public void SetCelebrating(bool on)
    {
        if (animator != null) { animator.SetBool(CelebrateParam, on); }
    }

    private void ResetAnimation()
    {
        faceOverrideUntil = 0f;
        saveUntil = 0f;
        if (animator == null) { return; }
        animator.ResetTrigger(KickParam);
        animator.ResetTrigger(ShootParam);
        animator.ResetTrigger(CatchParam);
        animator.ResetTrigger(ParryParam);
        animator.SetBool(CelebrateParam, false);
    }

    private void FaceToward(Vector3 direction, float seconds)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < MinMoveSqr) { return; }
        faceOverride = direction;
        faceOverrideUntil = Time.time + seconds;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    // 시뮬 밖 연출(골 세리머니, 09-29): 상태는 그대로 두고 화면만 옮긴다. 보간은 SyncView와 같은 방식
    public void MoveViewToward(Vector3 target, float maxStep)
    {
        previous = current;
        current = Vector3.MoveTowards(current, target, maxStep);
        SetSpeed();
    }

    // 연출 중 제자리: 직전 스텝과의 보간을 끊는다. 안 끊으면 Update가 같은 두 점 사이를 스텝마다 되풀이해 떨린다
    public void HoldView()
    {
        previous = current;
        SetSpeed();
    }

    // 애니 Speed(서기·걷기·달리기 선택) = 직전 스텝에서 움직인 거리 ÷ 고정 스텝(m/s). 시뮬·연출·정지 모두 이 한 곳에서 넣는다:
    // 세리머니·하프타임 멈춤에서 안 넣으면 골 직전 달리던 값이 남아 제자리에서 달린다
    private void SetSpeed()
    {
        if (animator == null) { return; }
        animator.SetFloat(SpeedParam, (current - previous).magnitude / MatchTuning.FixedStep);
    }

    // 스냅은 전부 킥오프 자리(배치·킥오프·하프타임·세리머니 끝)라 센터 마크를 보게 세운다. 안 돌리면 직전 진행 방향(세리머니 쪽)을 본 채 선다 [가정]
    private void FaceKickoff()
    {
        Vector3 toCenter = -current;
        if (toCenter.sqrMagnitude < MinMoveSqr) { return; }
        transform.rotation = Quaternion.LookRotation(toCenter, Vector3.up);
    }

    private void Update()
    {
        transform.position = Vector3.Lerp(previous, current, ViewInterpolation.Alpha());
        FaceMovement();
    }

    // 모델이 가는 방향을 본다. 서 있으면 마지막 방향 그대로. 킥 직후엔 찬 방향
    private void FaceMovement()
    {
        Vector3 step = Time.time < faceOverrideUntil ? faceOverride : current - previous;
        if (step.sqrMagnitude < MinMoveSqr) { return; }
        Quaternion target = Quaternion.LookRotation(step, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, TurnDegreesPerSecond * Time.deltaTime);
    }
}
