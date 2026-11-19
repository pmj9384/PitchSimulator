using System.Collections.Generic;
using Game.Core.Viewing;
using NUnit.Framework;

// 재생 커서(S4). 구간 경계에서 보임·다음 시작·건너뛸 틱 수를 잠근다
public class ReplayPlanTests
{
    private static ReplayPlan Plan()
    {
        return new ReplayPlan(new List<TickRange> { new TickRange(100, 200), new TickRange(500, 600) });
    }

    [Test]
    public void 구간_안에서만_보이고_끝_틱은_다음_구간_전까지_안_보인다()
    {
        ReplayPlan plan = Plan();

        Assert.IsFalse(plan.IsShown(0));
        Assert.IsFalse(plan.IsShown(99));
        Assert.IsTrue(plan.IsShown(100));
        Assert.IsTrue(plan.IsShown(199));
        Assert.IsFalse(plan.IsShown(200), "End는 반열린 구간이라 안 보인다");
        Assert.IsTrue(plan.IsShown(500));
        Assert.IsFalse(plan.IsShown(600));
        Assert.IsFalse(plan.IsShown(10000));
    }

    [Test]
    public void 다음_보일_틱은_지금_보이면_자신이고_구간이_끝나면_없음이다()
    {
        ReplayPlan plan = Plan();

        Assert.AreEqual(100, plan.NextShownTick(0));
        Assert.AreEqual(150, plan.NextShownTick(150));
        Assert.AreEqual(500, plan.NextShownTick(200));
        Assert.AreEqual(ReplayPlan.NoMoreShown, plan.NextShownTick(600));
    }

    [Test]
    public void 건너뛸_틱은_다음_구간이나_경기_끝까지를_예산으로_자른다()
    {
        ReplayPlan plan = Plan();

        Assert.AreEqual(100, plan.TicksToSkip(0, 1000, 100000));
        Assert.AreEqual(30, plan.TicksToSkip(0, 1000, 30));
        Assert.AreEqual(0, plan.TicksToSkip(100, 1000, 30), "보이는 중이면 0");
        Assert.AreEqual(300, plan.TicksToSkip(200, 1000, 100000));
        Assert.AreEqual(400, plan.TicksToSkip(600, 1000, 100000), "마지막 구간 뒤는 경기 끝까지");
        Assert.AreEqual(0, plan.TicksToSkip(1000, 1000, 100000), "경기가 끝났으면 0");
    }

    [Test]
    public void 전체_모드_구간이면_처음부터_끝까지_건너뛰지_않는다()
    {
        var plan = new ReplayPlan(new List<TickRange> { new TickRange(0, 1000) });

        for (int ticks = 0; ticks < 1000; ticks += 37)
        {
            Assert.AreEqual(0, plan.TicksToSkip(ticks, 1000, 5), $"{ticks}");
        }
    }
}
