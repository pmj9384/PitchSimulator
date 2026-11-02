using System.Collections.Generic;
using Game.Core.Match;
using NUnit.Framework;

// BallRules 검증(09-16 확정 스펙). ①비행 감속이 v²/2a 근처에서 멈추고 음수 속도가 없다 ②잡기 반경 경계·타이브레이크·후보 없음
// ③Kick이 방향을 정규화하고 소유를 푼다 ④Own/Carry가 위치를 소유자에 붙인다
public class BallRulesTests
{
    private const float Dt = 0.02f;   // Unity 고정 스텝 기본값

    [Test]
    public void 슛_25mps는_감속_상수로_정지_거리만큼_가서_멈추고_Free가_된다()
    {
        BallState ball = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 0f, MatchTuning.ShotSpeed);
        int ticks = 0;
        while (ball.Phase == BallPhase.Flight && ticks < 10000)
        {
            ball = BallRules.Step(ball, Dt, MatchTuning.BallDeceleration);
            ticks++;
            Assert.GreaterOrEqual(ball.VelX, 0f, "속도가 음수로 뒤집히면 안 된다");
        }

        Assert.AreEqual(BallPhase.Free, ball.Phase);
        Assert.AreEqual(0f, ball.VelX);
        float a = MatchTuning.BallDeceleration;
        int expectedTicks = (int)System.Math.Ceiling(MatchTuning.ShotSpeed / a / Dt);
        float stopDistance = MatchTuning.ShotSpeed * MatchTuning.ShotSpeed / (2f * a);   // 감속 1.2면 260m(필드보다 길다. 10-25 실측 기반 감속)
        Assert.AreEqual(expectedTicks, ticks, "속도 / 감속 = 정지 시간(올림)");
        Assert.That(ball.X, Is.InRange(stopDistance - 1.5f, stopDistance + 0.2f), "해석값 v²/(2a). 반암시적 오일러라 약간 짧다");
        Assert.AreEqual(0f, ball.Z, "X축으로만 찼으니 Z는 그대로");
    }

    [Test]
    public void Step은_비행이_아니면_아무것도_안_한다()
    {
        BallState free = BallState.FreeAt(3f, 4f);
        BallState after = BallRules.Step(free, Dt, MatchTuning.BallDeceleration);
        Assert.AreEqual(3f, after.X);
        Assert.AreEqual(BallPhase.Free, after.Phase);
    }

    [Test]
    public void 잡기는_반경_경계를_포함하고_밖이면_NoOwner()
    {
        BallState ball = BallState.FreeAt(0f, 0f);
        var onEdge = new List<TargetInfo> { new TargetInfo(playerId: 5, x: 0.8f, z: 0f) };
        var outside = new List<TargetInfo> { new TargetInfo(playerId: 5, x: 0.81f, z: 0f) };

        Assert.AreEqual(5, BallRules.TryCapture(ball, onEdge, MatchTuning.CaptureRadius), "정확히 0.8m = 잡는다");
        Assert.AreEqual(BallState.NoOwner, BallRules.TryCapture(ball, outside, MatchTuning.CaptureRadius));
    }

    [Test]
    public void 잡기_동률은_PlayerId_작은_쪽이고_후보가_없으면_NoOwner()
    {
        BallState ball = BallState.FreeAt(0f, 0f);
        var tie = new List<TargetInfo>
        {
            new TargetInfo(playerId: 9, x: 0.5f, z: 0f),
            new TargetInfo(playerId: 2, x: 0f, z: 0.5f),
        };

        Assert.AreEqual(2, BallRules.TryCapture(ball, tie, MatchTuning.CaptureRadius));
        Assert.AreEqual(BallState.NoOwner, BallRules.TryCapture(ball, new List<TargetInfo>(), MatchTuning.CaptureRadius));
        Assert.AreEqual(BallState.NoOwner, BallRules.TryCapture(ball, null, MatchTuning.CaptureRadius));
    }

    [Test]
    public void 소유_중이거나_비행_중인_공은_잡기_판정을_안_한다()
    {
        var close = new List<TargetInfo> { new TargetInfo(playerId: 1, x: 0f, z: 0f) };
        BallState owned = BallRules.Own(BallState.FreeAt(0f, 0f), ownerId: 7, ownerX: 0f, ownerZ: 0f);
        BallState flying = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 0f, 10f);

        Assert.AreEqual(BallState.NoOwner, BallRules.TryCapture(owned, close, MatchTuning.CaptureRadius));
        Assert.AreEqual(BallState.NoOwner, BallRules.TryCapture(flying, close, MatchTuning.CaptureRadius));
    }

    [Test]
    public void Kick은_방향을_정규화하고_소유를_푼다()
    {
        BallState owned = BallRules.Own(BallState.FreeAt(0f, 0f), ownerId: 7, ownerX: 1f, ownerZ: 1f);
        BallState kicked = BallRules.Kick(owned, 3f, 4f, 10f);   // 3-4-5 삼각형

        Assert.AreEqual(BallPhase.Flight, kicked.Phase);
        Assert.AreEqual(BallState.NoOwner, kicked.OwnerId);
        Assert.AreEqual(6f, kicked.VelX, 1e-4f);
        Assert.AreEqual(8f, kicked.VelZ, 1e-4f);
        Assert.AreEqual(1f, kicked.X, "차는 순간 위치는 그대로");
    }

    [Test]
    public void Carry는_소유자_이동_속도를_공_속도로_기록한다()
    {
        BallState ball = BallRules.Own(BallState.FreeAt(0f, 0f), 0, 0f, 0f);
        ball = BallRules.Carry(ball, 0.1f, 0f);   // 한 틱(0.02s)에 0.1m → 5m/s
        Assert.AreEqual(5f, ball.VelX, 1e-3f);
        Assert.AreEqual(0f, ball.VelZ, 1e-4f);
        Assert.AreEqual(BallPhase.Owned, ball.Phase, "국면은 그대로");
    }

    [Test]
    public void Own과_Carry는_공을_소유자_위치에_붙인다()
    {
        BallState ball = BallRules.Own(BallState.FreeAt(0f, 0f), ownerId: 3, ownerX: 10f, ownerZ: -2f);
        Assert.AreEqual(BallPhase.Owned, ball.Phase);
        Assert.AreEqual(3, ball.OwnerId);
        Assert.AreEqual(10f, ball.X);

        ball = BallRules.Carry(ball, 11f, -2.5f);
        Assert.AreEqual(11f, ball.X);
        Assert.AreEqual(-2.5f, ball.Z);
        Assert.AreEqual(3, ball.OwnerId, "옮겨도 소유자는 그대로");
    }

    // ── 공 높이(10-29 ④-1)
    private static BallState Lob(float speed, float angleDeg)
    {
        float rad = angleDeg * (float)System.Math.PI / 180f;
        BallState ball = BallState.FreeAt(0f, 0f);
        return BallRules.KickLob(ball, 1f, 0f, speed * (float)System.Math.Cos(rad), speed * (float)System.Math.Sin(rad));
    }

    [Test]
    public void 로빙은_포물선_공식대로_날아_떨어진다()
    {
        // 16m/s·40°: 거리 v²·sin2θ/g = 25.7m, 체공 2v·sinθ/g = 2.10초
        BallState ball = Lob(16f, 40f);
        int ticks = 0;
        while (ball.Airborne)
        {
            ball = BallRules.Step(ball, 0.02f, MatchTuning.BallDeceleration);
            ticks++;
        }
        Assert.AreEqual(2.10f, ticks * 0.02f, 0.03f, "체공 시간");
        Assert.AreEqual(25.7f, ball.X, 0.3f, "착지 거리");
        Assert.AreEqual(0f, ball.Y, 1e-6f);
        Assert.AreEqual(BallPhase.Flight, ball.Phase, "땅에 닿으면 굴러간다");
    }

    [Test]
    public void 공중에선_수평_속도가_줄지_않고_착지_뒤부터_굴림_감속이다()
    {
        BallState ball = Lob(16f, 40f);
        float vx = ball.VelX;
        ball = BallRules.Step(ball, 0.02f, MatchTuning.BallDeceleration);
        Assert.AreEqual(vx, ball.VelX, 1e-5f, "공중");
        while (ball.Airborne) { ball = BallRules.Step(ball, 0.02f, MatchTuning.BallDeceleration); }
        float landed = ball.VelX;
        ball = BallRules.Step(ball, 0.02f, MatchTuning.BallDeceleration);
        Assert.AreEqual(landed - MatchTuning.BallDeceleration * 0.02f, ball.VelX, 1e-4f, "땅");
    }

    [Test]
    public void 로빙이_착지하면_잔디가_수평_속도를_깎는다()
    {
        // 11-02 리뷰: 안 깎으면 25m 로빙이 12m/s로 내려앉아 60m를 굴렀다. FIFA Angle Ball Rebound 유지율
        BallState ball = Lob(16f, 40f);
        float vx = ball.VelX;
        while (ball.Airborne) { ball = BallRules.Step(ball, 0.02f, MatchTuning.BallDeceleration); }
        Assert.AreEqual(vx * MatchTuning.LobBounceSpeedRetention, ball.VelX, 1e-4f);
        Assert.AreEqual(0f, ball.VelY, 0f, "세로로 튀지 않는다");
    }

    [Test]
    public void 땅볼은_높이가_0으로_남는다()
    {
        BallState ball = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 0f, 10f);
        for (int i = 0; i < 50; i++) { ball = BallRules.Step(ball, 0.02f, MatchTuning.BallDeceleration); }
        Assert.AreEqual(0f, ball.Y, 0f);
        Assert.IsFalse(ball.Airborne);
    }

    [Test]
    public void 머리_위로_넘어가는_공은_필드_선수가_못_닿고_박스_안_GK는_닿는다()
    {
        Assert.IsTrue(BallRules.CanReachHeight(MatchTuning.FieldReachHeight, keeperInOwnBox: false), "경계 포함");
        Assert.IsFalse(BallRules.CanReachHeight(MatchTuning.FieldReachHeight + 0.01f, keeperInOwnBox: false));
        Assert.IsTrue(BallRules.CanReachHeight(2.5f, keeperInOwnBox: true));
        Assert.IsFalse(BallRules.CanReachHeight(MatchTuning.KeeperReachHeight + 0.01f, keeperInOwnBox: true));
    }

    // ── 공 궤적 질의(11-02 S1)

    private static BallState Lob(float distance)
    {
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(distance, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        return BallRules.KickLob(BallState.FreeAt(0f, 0f), 1f, 0f, v.horizontal, v.vertical);
    }

    private static BallState InAir(float y, float velY)
    {
        BallState ball = BallState.FreeAt(0f, 0f);
        ball.Phase = BallPhase.Flight;
        ball.VelX = 10f;
        ball.Y = y;
        ball.VelY = velY;
        return ball;
    }

    // 시뮬과 같은 Step을 ticks번 돌린 공
    private static BallState Ticked(BallState ball, int ticks)
    {
        for (int i = 0; i < ticks; i++) { ball = BallRules.Step(ball, Dt, MatchTuning.BallDeceleration); }
        return ball;
    }

    // 해석식과 틱 적분이 갈리는 크기(dt = 0.02 기준). Step은 속도를 먼저 바꾸고 그 속도로 옮기는 반암시적 오일러라
    // 높이는 중력·dt·t/2(t = 2.5초에 0.25m), 땅 위 위치는 감속·dt·t/2만큼 해석식보다 짧다. 착지는 틱 경계에서만 감지돼 최대 한 틱(수평 속도 × dt ≈ 0.25m) 늦다.
    // 그래서 위치 허용치는 한 틱 이동(0.25m)에 감속 오차(1.2 × 0.02 × 3 / 2 = 0.04m)를 더하고 여유를 둔 0.4m, 높이는 0.3m로 잡는다
    private const float PositionTolerance = 0.4f;
    private const float HeightTolerance = 0.3f;

    private static void AssertMatchesTicks(BallState start, int ticks)
    {
        (float x, float y, float z) analytic = BallRules.PositionAt(start, ticks * Dt);
        BallState ticked = Ticked(start, ticks);
        Assert.AreEqual(ticked.X, analytic.x, PositionTolerance, $"x, {ticks}틱");
        Assert.AreEqual(ticked.Y, analytic.y, HeightTolerance, $"높이, {ticks}틱");
        Assert.AreEqual(ticked.Z, analytic.z, PositionTolerance, $"z, {ticks}틱");
    }

    [Test]
    public void PositionAt은_로빙_착지_전에_틱_적분과_같다()
    {
        AssertMatchesTicks(Lob(25f), 50);
    }

    [Test]
    public void PositionAt은_로빙_착지_순간에_틱_적분과_같다()
    {
        BallState lob = Lob(25f);
        AssertMatchesTicks(lob, (int)System.Math.Round(BallRules.LandingTime(lob) / Dt));
    }

    [Test]
    public void PositionAt은_로빙_착지_뒤_굴림에서_틱_적분과_같다()
    {
        BallState lob = Lob(25f);
        AssertMatchesTicks(lob, (int)System.Math.Round(BallRules.LandingTime(lob) / Dt) + 50);
    }

    [Test]
    public void PositionAt은_땅볼에서_틱_적분과_같고_멈춘_뒤엔_그_자리에_있다()
    {
        BallState ground = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 1f, 10f);
        AssertMatchesTicks(ground, 100);

        float a = MatchTuning.BallDeceleration;
        float stop = 10f * 10f / (2f * a) / (float)System.Math.Sqrt(2.0);
        (float x, float y, float z) far = BallRules.PositionAt(ground, 60f);
        Assert.AreEqual(stop, far.x, 1e-3f, "해석값 v²/(2a)를 방향으로 나눈 자리에서 멈춘다");
        Assert.AreEqual(far.x, far.z, 1e-3f);
    }

    [Test]
    public void PositionAt은_착지_순간_높이가_0이고_뒤엔_수평_속도가_보존율만큼_남는다()
    {
        BallState lob = Lob(25f);
        float landing = BallRules.LandingTime(lob);
        Assert.AreEqual(25f, BallRules.PositionAt(lob, landing).x, 1e-2f, "LobVelocity가 역산한 착지 거리");
        Assert.AreEqual(0f, BallRules.PositionAt(lob, landing).y, 1e-3f);

        float keep = MatchTuning.LobBounceSpeedRetention;
        float rolled = 1f;
        float expected = lob.VelX * keep * rolled - MatchTuning.BallDeceleration * rolled * rolled / 2f;
        Assert.AreEqual(25f + expected, BallRules.PositionAt(lob, landing + rolled).x, 1e-2f);
    }

    [Test]
    public void LandingTime은_로빙이_계산한_체공_시간과_같고_땅볼은_0이다()
    {
        (float horizontal, float vertical, float flightTime) v = BallRules.LobVelocity(25f, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        Assert.AreEqual(v.flightTime, BallRules.LandingTime(Lob(25f)), 1e-4f);
        BallState ground = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 0f, 10f);
        Assert.AreEqual(0f, BallRules.LandingTime(ground), 0f);
    }

    [Test]
    public void BelowHeightFrom은_땅볼이면_0이다()
    {
        BallState ground = BallRules.Kick(BallState.FreeAt(7f, -3f), 1f, 0f, 12f);
        Assert.AreEqual(0f, BallRules.BelowHeightFrom(ground, MatchTuning.FieldReachHeight), 0f);
    }

    [Test]
    public void BelowHeightFrom은_하강_중인_높은_공이_닿는_높이로_내려오는_시각이다()
    {
        BallState ball = InAir(6f, -3f);
        float reach = MatchTuning.FieldReachHeight;
        float g = MatchTuning.Gravity;
        float expected = (-3f + (float)System.Math.Sqrt(9f + 2f * g * (6f - reach))) / g;
        float t = BallRules.BelowHeightFrom(ball, reach);
        Assert.AreEqual(expected, t, 1e-3f);
        Assert.AreEqual(reach, BallRules.PositionAt(ball, t).y, 1e-3f, "그 시각에 공이 정확히 닿는 높이");
        Assert.AreEqual(10f * t, BallRules.PositionAt(ball, t).x, 1e-3f);
    }

    [Test]
    public void BelowHeightFrom은_이미_닿는_높이_아래면_상승_중이어도_0이다()
    {
        Assert.AreEqual(0f, BallRules.BelowHeightFrom(InAir(1f, 5f), MatchTuning.FieldReachHeight), 0f);
        Assert.AreEqual(0f, BallRules.BelowHeightFrom(InAir(1f, -5f), MatchTuning.FieldReachHeight), 0f);
    }

    [Test]
    public void BelowHeightFrom은_닿는_높이가_클수록_더_이르다()
    {
        BallState ball = InAir(6f, 4f);
        float keeper = BallRules.BelowHeightFrom(ball, BallRules.ReachHeight(keeperInOwnBox: true));
        float field = BallRules.BelowHeightFrom(ball, BallRules.ReachHeight(keeperInOwnBox: false));
        Assert.Less(keeper, field, "손이 더 높이 닿으니 공이 덜 내려와도 된다");
        Assert.AreEqual(MatchTuning.KeeperReachHeight, BallRules.ReachHeight(keeperInOwnBox: true), 0f);
        Assert.AreEqual(MatchTuning.FieldReachHeight, BallRules.ReachHeight(keeperInOwnBox: false), 0f);
    }

    [Test]
    public void FirstAboveHeight는_로빙_발사_구간의_끝이고_낮은_로빙은_착지_시각이다()
    {
        BallState lob = Lob(25f);
        float reach = MatchTuning.FieldReachHeight;
        float t = BallRules.FirstAboveHeight(lob, reach);
        Assert.AreEqual(reach, BallRules.PositionAt(lob, t).y, 1e-3f);
        Assert.Less(t, BallRules.LandingTime(lob) / 2f, "정점 전에 넘어선다");

        BallState low = Lob(5f);
        Assert.AreEqual(BallRules.LandingTime(low), BallRules.FirstAboveHeight(low, reach), 1e-4f, "정점이 닿는 높이 아래면 끝까지 닿는다");
        Assert.AreEqual(0f, BallRules.FirstAboveHeight(InAir(6f, 1f), reach), 0f, "이미 위에 있으면 0");
    }

    [Test]
    public void 잡거나_다시_차면_높이가_0이_된다()
    {
        BallState ball = Lob(16f, 40f);
        ball = BallRules.Step(ball, 0.02f, MatchTuning.BallDeceleration);
        Assert.AreEqual(0f, BallRules.Own(ball, 3, ball.X, ball.Z).Y, 0f);
        Assert.AreEqual(0f, BallRules.Kick(ball, 1f, 0f, 5f).VelY, 0f);
    }
}
