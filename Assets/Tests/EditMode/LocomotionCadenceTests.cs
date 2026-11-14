using Game.Core.Match;
using NUnit.Framework;

// 이동 애니 재생 배율(10-29, 11-14 UAL 클립 기준 속도). 전력 클립 속도 이하는 1배(블렌드 트리가 섞는다), 위로는 속도에 비례, 상한 2배
public class LocomotionCadenceTests
{
    [Test]
    public void 전력_클립_속도_이하는_1배다()
    {
        Assert.AreEqual(1f, LocomotionCadence.For(0f), 1e-6f);
        Assert.AreEqual(1f, LocomotionCadence.For(LocomotionCadence.JogAnimSpeed), 1e-6f);
        Assert.AreEqual(1f, LocomotionCadence.For(LocomotionCadence.SprintAnimSpeed), 1e-6f);
    }

    [Test]
    public void 전력_클립보다_빠르면_다리_주기가_속도에_비례한다()
    {
        Assert.AreEqual(9f / LocomotionCadence.SprintAnimSpeed, LocomotionCadence.For(9f), 1e-5f);
        Assert.Greater(LocomotionCadence.For(9f), LocomotionCadence.For(8f));
    }

    [Test]
    public void 블렌드_기준_속도는_서기_걷기_조깅_전력_순으로_커진다()
    {
        Assert.Less(0f, LocomotionCadence.WalkAnimSpeed);
        Assert.Less(LocomotionCadence.WalkAnimSpeed, LocomotionCadence.JogAnimSpeed);
        Assert.Less(LocomotionCadence.JogAnimSpeed, LocomotionCadence.SprintAnimSpeed);
    }

    [Test]
    public void 재생_배율은_상한에서_멈춘다()
    {
        Assert.AreEqual(LocomotionCadence.Max, LocomotionCadence.For(20f), 1e-6f);
    }
}
