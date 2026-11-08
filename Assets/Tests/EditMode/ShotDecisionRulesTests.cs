using Game.Core.Match;
using NUnit.Framework;

// 슛 결정 시간 순수 함수(11-08 S3 1단계). 확률표는 StatsBomb PL 15/16 거리별 퍼스트 터치 슛 비율
public class ShotDecisionRulesTests
{
    private static float Chance(float distance, float scale)
    {
        return ShotDecisionRules.FirstTouchChance(distance, MatchTuning.FirstTouchShotBandEdges, MatchTuning.FirstTouchShotRates, scale);
    }

    [Test]
    public void 표를_인자로_받아_구간을_고른다()
    {
        float[] edges = { 10f };
        float[] rates = { 0.9f, 0.1f };
        Assert.AreEqual(0.9f, ShotDecisionRules.FirstTouchChance(9.99f, edges, rates, 1f), 1e-6f);
        Assert.AreEqual(0.1f, ShotDecisionRules.FirstTouchChance(10f, edges, rates, 1f), 1e-6f);
    }

    [Test]
    public void 퍼스트_터치_확률은_거리_구간별_실측값이고_구간_상한은_다음_구간이다()
    {
        Assert.AreEqual(0.379f, Chance(0f, 1f), 1e-6f);
        Assert.AreEqual(0.379f, Chance(5.99f, 1f), 1e-6f);
        Assert.AreEqual(0.322f, Chance(6f, 1f), 1e-6f);
        Assert.AreEqual(0.404f, Chance(11f, 1f), 1e-6f);
        Assert.AreEqual(0.339f, Chance(16f, 1f), 1e-6f);
        Assert.AreEqual(0.208f, Chance(29.99f, 1f), 1e-6f);
        Assert.AreEqual(0.145f, Chance(30f, 1f), 1e-6f);
        Assert.AreEqual(0.145f, Chance(60f, 1f), 1e-6f);
    }

    [Test]
    public void 배율은_곱하고_0과_1_사이로_자른다()
    {
        Assert.AreEqual(0.379f * 0.7f, Chance(3f, 0.7f), 1e-6f);
        Assert.AreEqual(1f, Chance(3f, 5f), 1e-6f);
        Assert.AreEqual(0f, Chance(3f, -1f), 1e-6f);
    }

    [Test]
    public void 표의_구간_수와_비율_수가_맞는다()
    {
        Assert.AreEqual(MatchTuning.FirstTouchShotBandEdges.Length + 1, MatchTuning.FirstTouchShotRates.Length);
    }

    [Test]
    public void 준비_시간은_고정_스텝으로_반올림한_틱이다()
    {
        Assert.AreEqual(70, ShotDecisionRules.PrepTicks(1.4f));
        Assert.AreEqual(50, ShotDecisionRules.PrepTicks(1f));
        Assert.AreEqual(0, ShotDecisionRules.PrepTicks(0f));
    }
}
