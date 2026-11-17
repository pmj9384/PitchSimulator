using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// 스로인 판정(IFAB 15조, 11-17 v1 기능 1). 누가·어디서·얼마나 멀리만 본다. 실행(잡기 거르기·대기)은 ThrowInSimulationTests
public class RestartRulesTests
{
    private const float Inset = 1f;

    [Test]
    public void 재개_팀은_마지막에_닿은_팀의_상대다()
    {
        Assert.AreEqual(1, RestartRules.ThrowInTeam(0));
        Assert.AreEqual(0, RestartRules.ThrowInTeam(1));
    }

    [Test]
    public void 마지막_터치를_모르면_재개_팀이_없다()
    {
        Assert.AreEqual(RestartRules.NoTeam, RestartRules.ThrowInTeam(-1));
    }

    [Test]
    public void 던지는_자리는_나간_x와_터치라인_안쪽_inset이다()
    {
        (float x, float z) up = RestartRules.ThrowInSpot(12.3f, 34.4f, Inset);
        Assert.AreEqual(12.3f, up.x, 1e-4f);
        Assert.AreEqual(FieldBounds.HalfWidth - Inset, up.z, 1e-4f);

        (float x, float z) down = RestartRules.ThrowInSpot(-7f, -35f, Inset);
        Assert.AreEqual(-7f, down.x, 1e-4f);
        Assert.AreEqual(-(FieldBounds.HalfWidth - Inset), down.z, 1e-4f, "아래 터치라인");
    }

    [Test]
    public void 코너_근처에서_나간_공은_골라인_안쪽까지만_간다()
    {
        (float x, float z) far = RestartRules.ThrowInSpot(53.4f, 34.2f, Inset);
        Assert.AreEqual(FieldBounds.HalfLength - Inset, far.x, 1e-4f);

        (float x, float z) near = RestartRules.ThrowInSpot(-53.4f, -34.2f, Inset);
        Assert.AreEqual(-(FieldBounds.HalfLength - Inset), near.x, 1e-4f);
    }

    [Test]
    public void 자리_2m_안의_선수는_원_둘레로_밀리고_밖의_선수는_그대로다()
    {
        (float x, float z) pushed = RestartRules.KeepAway(10f, 32f, 10f, 33f, 2f);
        Assert.AreEqual(10f, pushed.x, 1e-4f);
        Assert.AreEqual(31f, pushed.z, 1e-4f, "자리에서 1m → 2m");

        (float x, float z) stay = RestartRules.KeepAway(10.3f, 30.7f, 10f, 33f, 2f);
        Assert.AreEqual(10.3f, stay.x, "밖이면 입력 그대로(시뮬이 같은 값인지로 밀었는지를 본다)");
        Assert.AreEqual(30.7f, stay.z);
    }

    [Test]
    public void 자리_위에_정확히_선_선수는_필드_안쪽으로_밀린다()
    {
        (float x, float z) top = RestartRules.KeepAway(10f, 33f, 10f, 33f, 2f);
        Assert.AreEqual(31f, top.z, 1e-4f, "위 터치라인이면 -Z");

        (float x, float z) bottom = RestartRules.KeepAway(10f, -33f, 10f, -33f, 2f);
        Assert.AreEqual(-31f, bottom.z, 1e-4f, "아래 터치라인이면 +Z");
    }

    [Test]
    public void 스로인_후보_거리는_상한과_같으면_안이고_넘으면_밖이다()
    {
        Assert.IsTrue(RestartRules.IsWithinThrow(0f, 33f, 15f, 13f, 25f), "3-4-5 삼각형: 정확히 25m");
        Assert.IsFalse(RestartRules.IsWithinThrow(0f, 33f, 15.1f, 13f, 25f));
    }
}
