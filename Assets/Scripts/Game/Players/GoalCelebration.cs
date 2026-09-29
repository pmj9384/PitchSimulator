using System.Collections.Generic;
using Game.Core.Placement;
using UnityEngine;

// 골 세리머니 안무(연출, 09-29 유저 Play "골 넣은 사람 세리머니 시간을 줘야"). 득점자는 공격 방향 코너 쪽으로 뛰고,
// 골 순간 가까이 있던 필드 동료는 득점자 곁으로 모인다. 뷰만 움직이고 PlayerState는 안 건드린다(시뮬은 이미 킥오프 자리에 있다).
// 언제 시작·끝낼지, 화면 정지, 끝난 뒤 킥오프 자리 스냅은 MatchManager 몫이다(09-29 리뷰: 매니저가 안무까지 들던 것을 분리)
public sealed class GoalCelebration
{
    // 연출 값. 규칙이 아니라 MatchTuning에 두지 않는다(출처 없음, 눈으로 맞춘 값)
    private const float RunSpeed = 6f;       // 득점자가 코너 쪽으로 뛰는 속도(m/s)
    private const float JoinSpeed = 5f;      // 동료가 득점자에게 모이는 속도
    private const float JoinRadius = 30f;    // 골 순간 득점자와 이 거리 안인 필드 동료만 모인다. 먼 수비수·GK는 제자리
    private const float Gap = 1.5f;          // 모인 동료는 득점자와 이만큼 떨어져 선다
    private const float CornerInset = 3f;    // 코너 플래그에서 필드 안쪽으로

    private readonly List<PlayerController> joiners = new List<PlayerController>();
    private PlayerController scorer;
    private Vector3 corner;
    private int ticksLeft;

    public bool IsPlaying => ticksLeft > 0;

    public void Begin(PlayerController goalScorer, IReadOnlyList<PlayerController> teammates, int ticks)
    {
        scorer = goalScorer;
        ticksLeft = ticks;

        Vector3 at = scorer.ViewPosition;
        float side = at.z >= 0f ? 1f : -1f;
        float cornerX = (FieldBounds.HalfLength - CornerInset) * scorer.State.AttackSign;
        float cornerZ = (FieldBounds.HalfWidth - CornerInset) * side;
        corner = new Vector3(cornerX, 0f, cornerZ);

        joiners.Clear();
        for (int i = 0; i < teammates.Count; i++)
        {
            PlayerController mate = teammates[i];
            if (mate == scorer || mate.State.IsGoalkeeper) { continue; }
            if (Vector3.Distance(mate.ViewPosition, at) > JoinRadius) { continue; }
            joiners.Add(mate);
        }
    }

    // 고정 스텝마다 한 번. 마지막 스텝에서 IsPlaying이 false가 된다
    public void Step(float dt)
    {
        if (!IsPlaying) { return; }

        ticksLeft--;
        scorer.MoveViewToward(corner, RunSpeed * dt);

        Vector3 scorerAt = scorer.ViewPosition;
        for (int i = 0; i < joiners.Count; i++)
        {
            PlayerController mate = joiners[i];
            mate.MoveViewToward(JoinTarget(mate.ViewPosition, scorerAt), JoinSpeed * dt);
        }

        if (!IsPlaying) { Stop(); }
    }

    // 판이 새로 차려질 때·끝났을 때 참조를 놓는다
    public void Stop()
    {
        ticksLeft = 0;
        scorer = null;
        joiners.Clear();
    }

    // 동료가 설 자리: 득점자에게서 지금 방향 그대로 Gap만큼 떨어진 점
    private static Vector3 JoinTarget(Vector3 mateAt, Vector3 scorerAt)
    {
        Vector3 away = mateAt - scorerAt;
        if (away.sqrMagnitude <= 0f) { return scorerAt; }
        return scorerAt + away.normalized * Gap;
    }
}
