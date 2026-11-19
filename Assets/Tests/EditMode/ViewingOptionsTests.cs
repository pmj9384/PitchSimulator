using Game.Core.Viewing;
using NUnit.Framework;

// 보기 모드·배속 값 검사(S4 설계 5-6, 11-19 배속 4단·모든 모드). 저장값이 손상되거나 옛 값이면 기본(하이라이트·×1)으로
public class ViewingOptionsTests
{
    [Test]
    public void 범위_밖_모드는_하이라이트로_읽는다()
    {
        Assert.AreEqual(ViewingMode.Extended, ViewingOptions.SanitizeMode(-1));
        Assert.AreEqual(ViewingMode.Extended, ViewingOptions.SanitizeMode(3));
        Assert.AreEqual(ViewingMode.Key, ViewingOptions.SanitizeMode(0));
        Assert.AreEqual(ViewingMode.Full, ViewingOptions.SanitizeMode(2));
    }

    // 11-19 유저 Play "배속은 FM처럼 좌우 슬라이더": ×1~×4 정수 4단
    [Test]
    public void 배속은_1부터_4까지_정수_4단이다()
    {
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, ViewingOptions.Speeds);
    }

    [Test]
    public void 고를_수_없는_배속은_1로_읽는다()
    {
        Assert.AreEqual(1, ViewingOptions.SanitizeSpeed(0));
        Assert.AreEqual(1, ViewingOptions.SanitizeSpeed(5));
        Assert.AreEqual(3, ViewingOptions.SanitizeSpeed(3));
        Assert.AreEqual(4, ViewingOptions.SanitizeSpeed(4));
    }

    // 패널 세로 목록은 위에서부터 전체 경기·하이라이트·주요 장면(FM26 순서). 줄 번호와 enum 값이 달라 변환을 잠근다
    [Test]
    public void 패널_줄_순서는_전체_경기_하이라이트_주요_장면이다()
    {
        Assert.AreEqual(ViewingMode.Full, ViewingOptions.ModeAtRow(0));
        Assert.AreEqual(ViewingMode.Extended, ViewingOptions.ModeAtRow(1));
        Assert.AreEqual(ViewingMode.Key, ViewingOptions.ModeAtRow(2));
        Assert.AreEqual(ViewingOptions.DefaultMode, ViewingOptions.ModeAtRow(3));
    }

    [Test]
    public void 모든_모드는_한_줄에_있고_줄과_모드가_서로_되돌아간다()
    {
        foreach (ViewingMode mode in System.Enum.GetValues(typeof(ViewingMode)))
        {
            int row = ViewingOptions.RowOf(mode);
            Assert.GreaterOrEqual(row, 0, $"{mode}가 목록에 없다");
            Assert.AreEqual(mode, ViewingOptions.ModeAtRow(row));
        }
        Assert.AreEqual(System.Enum.GetValues(typeof(ViewingMode)).Length, ViewingOptions.ModeRows.Length);
    }
}
