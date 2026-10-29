using Game.Core.Match;
using NUnit.Framework;

// 이동 애니 재생 배율(10-29). 달리기 기준 속도 이하는 1배, 위로는 속도에 비례, 상한 2배
public class LocomotionCadenceTests
{
    [Test]
    public void 달리기_기준_속도_이하는_1배다()
    {
        Assert.AreEqual(1f, LocomotionCadence.For(0f), 1e-6f);
        Assert.AreEqual(1f, LocomotionCadence.For(LocomotionCadence.RunAnimSpeed), 1e-6f);
    }

    [Test]
    public void 전력_7은_조깅_4점2보다_다리가_빠르다()
    {
        Assert.AreEqual(1.75f, LocomotionCadence.For(7f), 1e-5f);
        Assert.AreEqual(1.05f, LocomotionCadence.For(4.2f), 1e-5f);
    }

    [Test]
    public void 재생_배율은_상한에서_멈춘다()
    {
        Assert.AreEqual(LocomotionCadence.Max, LocomotionCadence.For(20f), 1e-6f);
    }
}
