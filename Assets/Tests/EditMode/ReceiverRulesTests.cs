using Game.Core.Match;
using NUnit.Framework;

// 되돌림 감점(11-03 P1 [가정 P-G2]). 게이트를 없애고 핑퐁 방지를 값의 감점으로 한다. 좌표는 공격 방향으로 잰 값
public class ReceiverRulesTests
{
    [Test]
    public void 방금_준_선수에게_뒤로_가면_감점이다()
    {
        Assert.AreEqual(MatchTuning.ReturnPenalty, ReceiverRules.ReturnPenaltyFor(10f, 4f, isLastPasser: true));
    }

    [Test]
    public void 같은_깊이_옆으로_되돌려도_감점이다_경계는_앞이_아니면_감점()
    {
        Assert.AreEqual(MatchTuning.ReturnPenalty, ReceiverRules.ReturnPenaltyFor(10f, 10f, isLastPasser: true));
        Assert.AreEqual(0f, ReceiverRules.ReturnPenaltyFor(10f, 10.01f, isLastPasser: true));
    }

    [Test]
    public void 앞_공간으로의_원투는_감점이_없다()
    {
        Assert.AreEqual(0f, ReceiverRules.ReturnPenaltyFor(10f, 25f, isLastPasser: true), "되돌려 받은 선수에게 앞 공간으로");
    }

    [Test]
    public void 방금_준_선수가_아니면_뒤로_줘도_감점이_없다()
    {
        Assert.AreEqual(0f, ReceiverRules.ReturnPenaltyFor(10f, 4f, isLastPasser: false));
    }
}
