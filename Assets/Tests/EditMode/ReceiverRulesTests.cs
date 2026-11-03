using Game.Core.Match;
using NUnit.Framework;

// 패스 후보 분류(11-03 ⑤ A1). 게이트가 리시버 위치가 아니라 착지점을 본다. 좌표는 공격 방향으로 잰 값
public class ReceiverRulesTests
{
    [Test]
    public void 착지가_패서보다_앞이면_압박과_무관하게_전진형이다()
    {
        Assert.AreEqual(PassKind.Forward, ReceiverRules.Classify(10f, 18f, pressed: false, isLastPasser: false));
        Assert.AreEqual(PassKind.Forward, ReceiverRules.Classify(10f, 18f, pressed: true, isLastPasser: false));
    }

    [Test]
    public void 착지가_패서와_같은_깊이면_옆이다_경계는_옛_규칙과_같다()
    {
        Assert.AreEqual(PassKind.Gated, ReceiverRules.Classify(10f, 10f, pressed: false, isLastPasser: false), "같은 깊이 + 무압박 = 후보 아님");
        Assert.AreEqual(PassKind.Sideways, ReceiverRules.Classify(10f, 10f, pressed: true, isLastPasser: false));
        Assert.AreEqual(PassKind.Forward, ReceiverRules.Classify(10f, 10.01f, pressed: false, isLastPasser: false));
    }

    [Test]
    public void 옆_뒤_착지는_압박받을_때만_후보다()
    {
        Assert.AreEqual(PassKind.Gated, ReceiverRules.Classify(10f, 4f, pressed: false, isLastPasser: false));
        Assert.AreEqual(PassKind.Sideways, ReceiverRules.Classify(10f, 4f, pressed: true, isLastPasser: false));
    }

    [Test]
    public void 방금_준_선수에게_뒤로_가면_되돌림이고_앞으로_가는_원투는_전진형이다()
    {
        Assert.AreEqual(PassKind.Return, ReceiverRules.Classify(10f, 4f, pressed: true, isLastPasser: true));
        Assert.AreEqual(PassKind.Gated, ReceiverRules.Classify(10f, 4f, pressed: false, isLastPasser: true), "무압박이면 되돌림도 후보가 아니다(옛 게이트 먼저)");
        Assert.AreEqual(PassKind.Forward, ReceiverRules.Classify(10f, 25f, pressed: false, isLastPasser: true), "원투: 되돌려 받은 선수에게 앞 공간으로");
    }

    [Test]
    public void 위협_증가가_패스가_더_커야_운반을_접는다_같으면_운반()
    {
        Assert.IsTrue(ReceiverRules.PassBeatsCarry(0.10f, 0.11f));
        Assert.IsFalse(ReceiverRules.PassBeatsCarry(0.10f, 0.10f));
        Assert.IsFalse(ReceiverRules.PassBeatsCarry(0.10f, 0.05f));
    }
}
