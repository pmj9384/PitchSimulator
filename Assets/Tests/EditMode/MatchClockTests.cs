using Game.Core.Match;
using NUnit.Framework;

// 경기 시계(09-27 HUD): 3분 = 9,000틱 = 표시 90분. 종료 틱에서 정확히 90, 그 직전은 89, 범위 밖은 클램프
public class MatchClockTests
{
    [Test]
    public void 틱을_표시_분으로_바꾼다()
    {
        Assert.AreEqual(0, MatchClock.MinuteOf(0));
        Assert.AreEqual(0, MatchClock.MinuteOf(99), "1분 미만은 0");
        Assert.AreEqual(1, MatchClock.MinuteOf(100));
        Assert.AreEqual(45, MatchClock.MinuteOf(4500), "전반 끝");
        Assert.AreEqual(89, MatchClock.MinuteOf(8999));
        Assert.AreEqual(90, MatchClock.MinuteOf(9000), "종료 틱 = 90분");
        Assert.AreEqual(90, MatchClock.MinuteOf(12345), "넘치면 90");
        Assert.AreEqual(0, MatchClock.MinuteOf(-5), "음수는 0");
    }

    [Test]
    public void 초와_전후반도_틱에서_나온다()
    {
        Assert.AreEqual(0, MatchClock.SecondOf(0));
        Assert.AreEqual(36, MatchClock.SecondOf(60), "60틱 = 36초");
        Assert.AreEqual(0, MatchClock.SecondOf(100), "100틱 = 1:00");
        Assert.AreEqual(59, MatchClock.SecondOf(8999), "8999틱 = 89:59");
        Assert.AreEqual(0, MatchClock.SecondOf(9000), "9000틱 = 90:00");
        Assert.AreEqual(5400, MatchClock.TotalSecondsOf(9000));
        Assert.IsFalse(MatchClock.IsSecondHalf(4499));
        Assert.IsTrue(MatchClock.IsSecondHalf(4500), "4500틱 = 45:00부터 후반");
        Assert.AreEqual(45, MatchClock.MinuteOf(MatchTuning.HalfTimeTick));
    }

    [Test]
    public void 경기_길이_상수는_한_곳이고_서로_맞는다()
    {
        Assert.AreEqual(9000, MatchTuning.MatchTicks);
        Assert.AreEqual(MatchTuning.MatchSeconds, MatchTuning.MatchTicks * MatchTuning.FixedStep, 1e-3f);
    }
}
