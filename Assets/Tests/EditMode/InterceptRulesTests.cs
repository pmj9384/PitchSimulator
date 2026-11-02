using System;
using Game.Core.Match;
using NUnit.Framework;

// InterceptRules 검증(11-02 S2, 통합설계 코어 2). 선수 한 명의 가장 이른 포획 시각 EarliestCatch와 두 시각 차의 확률 Risk.
// 기대값은 BallRules.LobVelocity의 포물선과 땅 감속의 해석식으로 계산한다. 코어는 FixedStep(0.02초) 간격으로 훑으니 시각 허용 오차는 0.02초
public class InterceptRulesTests
{
    private const float Tick = MatchTuning.FixedStep;
    private const float Reaction = MatchTuning.ReactionTime;
    private const float Radius = MatchTuning.CaptureRadius;
    private const float Speed = MatchTuning.InterceptRunSpeed;

    // (startX, 0)에서 +X로 distance만큼 띄우는 로빙
    private static BallState Lob(float startX, float distance)
    {
        return BallRules.KickLobTo(BallState.FreeAt(startX, 0f), distance, 0f);
    }

    // 로빙 하강 중 공이 height를 지나는 시각: v·t − g·t²/2 = height의 큰 근
    private static float DescentTimeAt(float distance, float height)
    {
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(distance, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        float g = MatchTuning.Gravity;
        return (v.vertical + (float)Math.Sqrt(v.vertical * v.vertical - 2f * g * height)) / g;
    }

    [Test]
    public void 땅볼을_정면에서_기다리는_선수는_공이_반경에_들어오는_시각에_잡는다()
    {
        // 달리기 속도 0 = 제자리에서 기다린다. 공이 선수 앞 반경(0.8m)에 닿는 거리는 10 − 0.8 = 9.2m
        BallState ball = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 0f, 15f);
        float a = MatchTuning.BallDeceleration;
        float expected = (15f - (float)Math.Sqrt(15f * 15f - 2f * a * 9.2f)) / a;

        (float time, float x, float z, float arrival) got = InterceptRules.EarliestCatch(ball, 10f, 0f, 0f, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);

        Assert.AreEqual(expected, got.time, Tick);
        Assert.AreEqual(9.2f, got.x, 0.3f, "한 틱에 공이 15 × 0.02 = 0.3m 간다");
        Assert.AreEqual(0f, got.z, 1e-4f);
    }

    [Test]
    public void 로빙_하강_지점에_선_필드_선수는_공이_2점2m_아래가_된_뒤_잡는다()
    {
        // 25m 로빙이 (20,0)에서 출발. 공이 2.2m를 지나는 x = 20 + 수평 속도 × t. 선수는 그 점 바로 뒤에 서서 반경 안으로 들어오길 기다린다
        BallState ball = Lob(20f, 25f);
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(25f, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        float fieldTime = DescentTimeAt(25f, MatchTuning.FieldReachHeight);
        float playerX = 20f + v.horizontal * DescentTimeAt(25f, MatchTuning.KeeperReachHeight) + 0.1f;

        (float time, float x, float z, float arrival) got = InterceptRules.EarliestCatch(ball, playerX, 0f, 0f, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);

        Assert.AreEqual(fieldTime, got.time, Tick, "머리 위(2.2m 위)를 지나는 동안은 반경 안이어도 못 잡는다");
    }

    [Test]
    public void 같은_자리_GK는_자기_박스_안이면_2점6m에서_더_일찍_잡는다()
    {
        // 위와 같은 로빙을 상대 박스 쪽(x 36~52.5)으로 옮긴다. 이 GK의 공격 방향은 −X라 자기 골은 +X 끝이다
        BallState ball = Lob(25f, 25f);
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(25f, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        float playerX = 25f + v.horizontal * DescentTimeAt(25f, MatchTuning.KeeperReachHeight) + 0.1f;
        float keeperTime = DescentTimeAt(25f, MatchTuning.KeeperReachHeight);
        float fieldTime = DescentTimeAt(25f, MatchTuning.FieldReachHeight);

        float asKeeper = InterceptRules.EarliestCatch(ball, playerX, 0f, 0f, Reaction, Radius, isKeeper: true, keeperAttackSign: -1).time;
        float asField = InterceptRules.EarliestCatch(ball, playerX, 0f, 0f, Reaction, Radius, isKeeper: false, keeperAttackSign: 0).time;

        Assert.AreEqual(keeperTime, asKeeper, Tick);
        Assert.AreEqual(fieldTime, asField, Tick);
        Assert.Less(asKeeper, asField);
    }

    [Test]
    public void GK도_자기_박스_밖이면_필드_선수와_같은_높이다()
    {
        // 같은 로빙을 이 GK의 자기 박스(−X 끝)와 먼 곳에 놓으면 GK 표시가 있어도 2.2m 기준
        BallState ball = Lob(20f, 25f);
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(25f, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        float playerX = 20f + v.horizontal * DescentTimeAt(25f, MatchTuning.KeeperReachHeight) + 0.1f;

        float asKeeper = InterceptRules.EarliestCatch(ball, playerX, 0f, 0f, Reaction, Radius, isKeeper: true, keeperAttackSign: +1).time;
        float asField = InterceptRules.EarliestCatch(ball, playerX, 0f, 0f, Reaction, Radius, isKeeper: false, keeperAttackSign: 0).time;

        Assert.AreEqual(asField, asKeeper, 1e-5f);
    }

    [Test]
    public void 하강_입구에_선_상대가_착지점에_서서_기다리는_리시버보다_먼저_잡는다()
    {
        // 통합설계 2절 A의 재현: 리시버는 착지점(45,0)에 서서 기다리고(실행의 현재 모습) 상대는 공이 2.2m를 지나는 하강 입구에 선다.
        // 공은 상대의 반경 안을 2.2m 아래로 지나는 순간 잡히고, 착지점의 리시버 앞엔 그 뒤에 온다
        BallState ball = Lob(20f, 25f);
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(25f, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        float entranceX = 20f + v.horizontal * DescentTimeAt(25f, MatchTuning.FieldReachHeight);

        float waiting = InterceptRules.EarliestCatch(ball, 45f, 0f, 0f, Reaction, Radius, isKeeper: false, keeperAttackSign: 0).time;
        float opponent = InterceptRules.EarliestCatch(ball, entranceX, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0).time;

        Assert.Less(opponent, waiting);
        Assert.Greater(InterceptRules.Contest(waiting, 0f, opponent, 0f, MatchTuning.ArrivalSigma), 0.5f, "시각 차가 한 틱보다 크니 시각 차로 잰다");
    }

    [Test]
    public void 마중_나가는_리시버는_하강_입구에서_상대와_동시에_닿는다()
    {
        // 코어는 리시버가 달리는 선수(반응 0.5 + 거리 ÷ 속도)라고 본다. 착지점에서 입구까지 2.8m는 0.5 + 2.0 ÷ 7 = 0.79초라 공이 입구에 오기 전에 간다.
        // 판정이 낙관적으로 보는 이 간격은 S3(리시버가 실제로 자기 포획점으로 달린다)에서 실행이 따라온다
        BallState ball = Lob(20f, 25f);
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(25f, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        float entranceX = 20f + v.horizontal * DescentTimeAt(25f, MatchTuning.FieldReachHeight);

        float receiver = InterceptRules.EarliestCatch(ball, 45f, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0).time;
        float opponent = InterceptRules.EarliestCatch(ball, entranceX, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0).time;

        Assert.AreEqual(opponent, receiver, Tick);
    }

    [Test]
    public void 같은_거리_같은_반응이면_리시버와_상대가_동시이고_위험은_절반이다()
    {
        BallState ball = Lob(20f, 25f);
        var receiver = InterceptRules.EarliestCatch(ball, 45f, 5f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);
        var opponent = InterceptRules.EarliestCatch(ball, 45f, -5f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);

        Assert.AreEqual(receiver.time, opponent.time, 1e-5f);
        Assert.AreEqual(receiver.arrival, opponent.arrival, 1e-5f);
        Assert.AreEqual(0.5f, InterceptRules.Contest(receiver.time, receiver.arrival, opponent.time, opponent.arrival, MatchTuning.ArrivalSigma), 1e-4f);
    }

    [Test]
    public void 상대가_없으면_위험은_0이다()
    {
        Assert.AreEqual(0f, InterceptRules.Risk(2f, float.PositiveInfinity, MatchTuning.ArrivalSigma));
        Assert.AreEqual(0f, InterceptRules.Contest(2f, 1f, float.PositiveInfinity, float.PositiveInfinity, MatchTuning.ArrivalSigma));
    }

    [Test]
    public void 같은_틱에_잡을_수_있으면_먼저_도착한_쪽이_이긴다()
    {
        // 하강 입구(공이 2.2m 아래로 내려오는 틱)에 선 상대는 도착 0, 착지점에서 마중 나온 리시버는 0.79초 걸려 온다. 잡는 시각은 같은 틱이다.
        // 시각 차만 보면 0.5인데, 실행 포획은 같은 틱이면 공에 가까운 쪽이 잡고 먼저 자리 잡은 쪽이 가깝다
        BallState ball = Lob(20f, 25f);
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(25f, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        float entranceX = 20f + v.horizontal * DescentTimeAt(25f, MatchTuning.FieldReachHeight);
        var receiver = InterceptRules.EarliestCatch(ball, 45f, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);
        var opponent = InterceptRules.EarliestCatch(ball, entranceX, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);

        Assert.AreEqual(opponent.time, receiver.time, Tick * 0.5f, "전제: 같은 틱");
        Assert.Less(opponent.arrival, receiver.arrival);
        Assert.Greater(InterceptRules.Contest(receiver.time, receiver.arrival, opponent.time, opponent.arrival, MatchTuning.ArrivalSigma), 0.5f);
        Assert.Less(InterceptRules.Contest(opponent.time, opponent.arrival, receiver.time, receiver.arrival, MatchTuning.ArrivalSigma), 0.5f, "반대로 보면 반대쪽으로 기운다");
    }

    [Test]
    public void 킥_지점에_붙은_상대는_뜨기_전에_닿으니_위험이_거의_1이다()
    {
        BallState ball = Lob(20f, 25f);
        var receiver = InterceptRules.EarliestCatch(ball, 45f, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);
        var glued = InterceptRules.EarliestCatch(ball, 20.5f, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);

        Assert.AreEqual(0f, glued.time, 1e-6f, "공이 반경 안, 높이 0에서 출발");
        Assert.Greater(InterceptRules.Contest(receiver.time, receiver.arrival, glued.time, glued.arrival, MatchTuning.ArrivalSigma), 0.99f);
    }

    [Test]
    public void 멈출_때까지_아무도_못_닿는_선수는_정지점에서_도착_시각으로_끝난다()
    {
        // 5m/s 땅볼은 5 ÷ 감속초 뒤 25 ÷ (2 × 감속) m에서 멈춘다. 선수는 옆 40m라 한참 늦게 도착한다
        BallState ball = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 0f, 5f);
        float stopTime = 5f / MatchTuning.BallDeceleration;
        float stopX = 25f / (2f * MatchTuning.BallDeceleration);
        float arrival = Reaction + (40f - Radius) / Speed;

        (float time, float x, float z, float arrival) got = InterceptRules.EarliestCatch(ball, stopX, 40f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);

        Assert.Greater(arrival, stopTime, "전제: 선수는 공이 멈춘 뒤에 닿는다");
        Assert.AreEqual(arrival, got.time, 1e-3f);
        Assert.AreEqual(stopX, got.x, 1e-3f);
        Assert.AreEqual(0f, got.z, 1e-3f);
    }

    [Test]
    public void 멈춰_있는_공은_그_자리에서_선수_도착_시각이다()
    {
        BallState ball = BallState.FreeAt(10f, 0f);
        (float time, float x, float z, float arrival) got = InterceptRules.EarliestCatch(ball, 10f, 14f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);
        Assert.AreEqual(Reaction + (14f - Radius) / Speed, got.time, 1e-4f);
        Assert.AreEqual(10f, got.x, 1e-4f);
    }

    [Test]
    public void 최선_상대는_시각이_앞서면_고르고_같은_틱이면_먼저_도착한_쪽을_고른다()
    {
        // 지금 최선: 2.00초에 잡고 도착 1.0초
        const float bestTime = 2.00f;
        const float bestArrival = 1.0f;
        Assert.IsTrue(InterceptRules.IsBetterOpponent(1.98f, 1.9f, bestTime, bestArrival), "한 틱 이르게 잡으면 도착이 늦어도 이긴다(같은 틱이 아니다)");
        Assert.IsTrue(InterceptRules.IsBetterOpponent(2.005f, 0.5f, bestTime, bestArrival), "같은 틱이고 먼저 도착");
        Assert.IsFalse(InterceptRules.IsBetterOpponent(2.005f, 1.5f, bestTime, bestArrival), "같은 틱이지만 늦게 도착");
        Assert.IsFalse(InterceptRules.IsBetterOpponent(2.03f, 0.0f, bestTime, bestArrival), "한 틱 넘게 늦으면 도착이 빨라도 진다");
    }

    [Test]
    public void 반_틱_경계에서_Contest는_시각_차와_도착_차를_가른다()
    {
        // 리시버 2.00초(도착 1.0). 상대는 도착 0.0. 차 0.0099는 같은 틱이라 도착 차(상대가 1초 앞)로, 0.0101은 아니라 시각 차(리시버가 앞)로 잰다
        float sameTick = InterceptRules.Contest(2.00f, 1.0f, 2.0099f, 0.0f, MatchTuning.ArrivalSigma);
        float nextTick = InterceptRules.Contest(2.00f, 1.0f, 2.0101f, 0.0f, MatchTuning.ArrivalSigma);
        Assert.Greater(sameTick, 0.9f);
        Assert.Less(nextTick, 0.5f);
    }

    [Test]
    public void 최선_상대보다_한_틱_넘게_늦는_스캔은_멈추고_아니면_EarliestCatch와_같다()
    {
        BallState ball = Lob(20f, 25f);
        var full = InterceptRules.EarliestCatch(ball, 45f, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0);

        var cut = InterceptRules.EarliestCatchBefore(ball, 45f, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0, bestOpponentTime: full.time - 2f * Tick);
        Assert.IsTrue(float.IsPositiveInfinity(cut.time), "최선 상대보다 한 틱 넘게 늦다");

        var kept = InterceptRules.EarliestCatchBefore(ball, 45f, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0, bestOpponentTime: full.time);
        Assert.AreEqual(full.time, kept.time, 0f);
        Assert.AreEqual(full.arrival, kept.arrival, 0f);

        var none = InterceptRules.EarliestCatchBefore(ball, 45f, 0f, Speed, Reaction, Radius, isKeeper: false, keeperAttackSign: 0, bestOpponentTime: float.PositiveInfinity);
        Assert.AreEqual(full.time, none.time, 0f);
    }
}
