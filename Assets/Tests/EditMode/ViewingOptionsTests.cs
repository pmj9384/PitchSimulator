using Game.Core.Viewing;
using NUnit.Framework;

// 보기 모드·배속 값 검사(S4 설계 5-6). 저장값이 손상되거나 옛 값이면 기본(확장·×1)으로
public class ViewingOptionsTests
{
    [Test]
    public void 범위_밖_모드는_확장으로_읽는다()
    {
        Assert.AreEqual(ViewingMode.Extended, ViewingOptions.SanitizeMode(-1));
        Assert.AreEqual(ViewingMode.Extended, ViewingOptions.SanitizeMode(3));
        Assert.AreEqual(ViewingMode.Key, ViewingOptions.SanitizeMode(0));
        Assert.AreEqual(ViewingMode.Full, ViewingOptions.SanitizeMode(2));
    }

    [Test]
    public void 고를_수_없는_배속은_1로_읽는다()
    {
        Assert.AreEqual(1, ViewingOptions.SanitizeSpeed(0));
        Assert.AreEqual(1, ViewingOptions.SanitizeSpeed(3));
        Assert.AreEqual(4, ViewingOptions.SanitizeSpeed(4));
    }

    [Test]
    public void 배속은_전체_모드에만_적용된다()
    {
        Assert.AreEqual(4, ViewingOptions.EffectiveSpeed(ViewingMode.Full, 4));
        Assert.AreEqual(1, ViewingOptions.EffectiveSpeed(ViewingMode.Key, 4));
        Assert.AreEqual(1, ViewingOptions.EffectiveSpeed(ViewingMode.Extended, 2));
    }
}
