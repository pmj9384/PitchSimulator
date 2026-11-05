using Game.Core.Match;
using NUnit.Framework;

// 경기 시계(09-27 HUD): 3분 = 9,000틱 = 표시 90분. 종료 틱에서 정확히 90, 그 직전은 89, 범위 밖은 클램프
public class MatchClockTests
{
    [Test]
    public void 틱을_표시_분으로_바꾼다()
    {
        Assert.AreEqual(0, MatchClock.MinuteOf(0, MatchLength.Compressed));
        Assert.AreEqual(0, MatchClock.MinuteOf(99, MatchLength.Compressed), "1분 미만은 0");
        Assert.AreEqual(1, MatchClock.MinuteOf(100, MatchLength.Compressed));
        Assert.AreEqual(45, MatchClock.MinuteOf(4500, MatchLength.Compressed), "전반 끝");
        Assert.AreEqual(89, MatchClock.MinuteOf(8999, MatchLength.Compressed));
        Assert.AreEqual(90, MatchClock.MinuteOf(9000, MatchLength.Compressed), "종료 틱 = 90분");
        Assert.AreEqual(90, MatchClock.MinuteOf(12345, MatchLength.Compressed), "넘치면 90");
        Assert.AreEqual(0, MatchClock.MinuteOf(-5, MatchLength.Compressed), "음수는 0");
    }

    [Test]
    public void 초와_전후반도_틱에서_나온다()
    {
        Assert.AreEqual(0, MatchClock.SecondOf(0, MatchLength.Compressed));
        Assert.AreEqual(36, MatchClock.SecondOf(60, MatchLength.Compressed), "60틱 = 36초");
        Assert.AreEqual(0, MatchClock.SecondOf(100, MatchLength.Compressed), "100틱 = 1:00");
        Assert.AreEqual(59, MatchClock.SecondOf(8999, MatchLength.Compressed), "8999틱 = 89:59");
        Assert.AreEqual(0, MatchClock.SecondOf(9000, MatchLength.Compressed), "9000틱 = 90:00");
        Assert.AreEqual(5400, MatchClock.TotalSecondsOf(9000, MatchLength.Compressed));
        Assert.IsFalse(MatchClock.IsSecondHalf(4499, MatchLength.Compressed));
        Assert.IsTrue(MatchClock.IsSecondHalf(4500, MatchLength.Compressed), "4500틱 = 45:00부터 후반");
        Assert.AreEqual(45, MatchClock.MinuteOf(MatchTuning.HalfTimeTick, MatchLength.Compressed));
    }

    [Test]
    public void 추가시간은_표시_시계만_늘리고_경기_길이는_그대로다()
    {
        var added = new AddedTime(1, 3);   // 전반 +1, 후반 +3 → 표시 94분 = 5,640초를 9,000틱에
        Assert.AreEqual(5640, MatchClock.TotalDisplaySeconds(added));
        Assert.AreEqual(5640, MatchClock.TotalSecondsOf(9000, added, MatchLength.Compressed), "종료 틱 = 90+3:00");
        int ht = MatchClock.HalfTimeTick(added, MatchLength.Compressed);
        Assert.AreEqual(2760, MatchClock.TotalSecondsOf(ht, added, MatchLength.Compressed), "하프타임 틱에서 표시 시계가 정확히 45+1:00");
        Assert.Less(MatchClock.TotalSecondsOf(ht - 1, added, MatchLength.Compressed), 2760);

        ClockReading before = MatchClock.Describe(ht - 1, added, MatchLength.Compressed);
        Assert.IsFalse(before.IsSecondHalf); Assert.IsTrue(before.InAddedTime); Assert.AreEqual(45, before.Minute); Assert.AreEqual(0, before.AddedMinute);
        ClockReading start2 = MatchClock.Describe(ht, added, MatchLength.Compressed);
        Assert.IsTrue(start2.IsSecondHalf); Assert.IsFalse(start2.InAddedTime); Assert.AreEqual(45, start2.Minute); Assert.AreEqual(0, start2.Second, "후반은 45:00부터");
        ClockReading end = MatchClock.Describe(9000, added, MatchLength.Compressed);
        Assert.IsTrue(end.IsSecondHalf); Assert.IsTrue(end.InAddedTime); Assert.AreEqual(90, end.Minute); Assert.AreEqual(3, end.AddedMinute); Assert.AreEqual(0, end.AddedSecond);

        ClockReading none = MatchClock.Describe(9000, AddedTime.None, MatchLength.Compressed);
        Assert.IsFalse(none.InAddedTime); Assert.AreEqual(90, none.Minute);
        Assert.AreEqual(4500, MatchClock.HalfTimeTick(AddedTime.None, MatchLength.Compressed));
    }

    [Test]
    public void 추가시간은_시드마다_결정적이고_범위_안이다()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            AddedTime a = AddedTime.FromSeed(seed);
            Assert.AreEqual(a.FirstHalfMinutes, AddedTime.FromSeed(seed).FirstHalfMinutes);
            Assert.That(a.FirstHalfMinutes, Is.InRange(1, 2), $"seed {seed} 전반");
            Assert.That(a.SecondHalfMinutes, Is.InRange(2, 5), $"seed {seed} 후반");
        }
        Assert.AreNotEqual(AddedTime.FromSeed(1).SecondHalfMinutes * 10 + AddedTime.FromSeed(1).FirstHalfMinutes,
            AddedTime.FromSeed(2).SecondHalfMinutes * 10 + AddedTime.FromSeed(2).FirstHalfMinutes, "이웃 시드가 늘 같진 않다(해시)");
    }

    [Test]
    public void 경기_길이_상수는_한_곳이고_서로_맞는다()
    {
        Assert.AreEqual(9000, MatchTuning.MatchTicks);
        Assert.AreEqual(MatchTuning.MatchSeconds, MatchTuning.MatchTicks * MatchTuning.FixedStep, 1e-3f);
        Assert.AreEqual(MatchClock.DisplaySeconds, MatchTuning.RealTimeTicks * MatchTuning.FixedStep, 1e-2f, "실제 90분 = 5,400초");
    }
}
