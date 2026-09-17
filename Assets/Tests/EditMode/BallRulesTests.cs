using System.Collections.Generic;
using Game.Core.Match;
using NUnit.Framework;

// BallRules 검증(09-16 확정 스펙). ①비행 감속이 v²/2a 근처에서 멈추고 음수 속도가 없다 ②잡기 반경 경계·타이브레이크·후보 없음
// ③Kick이 방향을 정규화하고 소유를 푼다 ④Own/Carry가 위치를 소유자에 붙인다
public class BallRulesTests
{
    private const float Dt = 0.02f;   // Unity 고정 스텝 기본값

    [Test]
    public void 슛_25mps는_감속4로_약78m_가서_멈추고_Free가_된다()
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
        Assert.AreEqual(313, ticks, "25 / 4 = 6.25초 = 313틱(올림)");
        Assert.That(ball.X, Is.InRange(77f, 78.2f), "해석값 25²/(2·4) = 78.125m. 반암시적 오일러라 약간 짧다");
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
}
