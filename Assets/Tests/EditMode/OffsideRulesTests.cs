using System.Collections.Generic;
using Game.Core.Match;
using NUnit.Framework;

// 오프사이드 위치(FIFA 규칙 11)의 순수 판정. 휘슬은 없고 트리가 위치를 피한다(09-23)
public class OffsideRulesTests
{
    [Test]
    public void 온사이드_선은_상대_2번째_최종_선수와_공과_하프라인_중_가장_공격_쪽이다()
    {
        // 팀0(+X). 상대 GK 48, CB 31, CB 30, CM 20 → 2번째 최종 = 31
        var opp = new List<TargetInfo> { new TargetInfo(20, 48f, 0f), new TargetInfo(21, 31f, 5f), new TargetInfo(22, 30f, -5f), new TargetInfo(23, 20f, 0f) };
        Assert.AreEqual(31f, OffsideRules.OnsideLine(opp, +1, ballX: 10f), 1e-4f);
        Assert.AreEqual(35f, OffsideRules.OnsideLine(opp, +1, ballX: 35f), 1e-4f, "공이 수비 라인보다 앞이면 공이 선");
        Assert.AreEqual(0f, OffsideRules.OnsideLine(new List<TargetInfo> { new TargetInfo(20, -48f, 0f), new TargetInfo(21, -10f, 0f) }, +1, ballX: -30f), 1e-4f, "자기 진영에선 하프라인이 하한");
        Assert.AreEqual(OffsideRules.NoLine, OffsideRules.OnsideLine(new List<TargetInfo> { new TargetInfo(20, 48f, 0f) }, +1, 0f), "상대가 2명 미만이면 선 없음");

        // 팀1(-X): 상대 GK -48, CB -31 → 선은 공격 방향 좌표 31
        var opp1 = new List<TargetInfo> { new TargetInfo(0, -48f, 0f), new TargetInfo(1, -31f, 0f) };
        Assert.AreEqual(31f, OffsideRules.OnsideLine(opp1, -1, ballX: 0f), 1e-4f);
    }

    [Test]
    public void 선과_나란히는_온사이드고_그보다_골_쪽이면_오프사이드_위치다()
    {
        Assert.IsFalse(OffsideRules.IsOffsidePosition(31f, +1, 31f), "나란히 = 온사이드");
        Assert.IsTrue(OffsideRules.IsOffsidePosition(31.1f, +1, 31f));
        Assert.IsFalse(OffsideRules.IsOffsidePosition(30f, +1, 31f));
        Assert.IsTrue(OffsideRules.IsOffsidePosition(-32f, -1, 31f), "팀1은 -X가 골 쪽");
        Assert.IsFalse(OffsideRules.IsOffsidePosition(50f, +1, OffsideRules.NoLine), "선이 없으면 오프사이드 없음");
    }

    [Test]
    public void 자리는_온사이드_선에서_여유만큼_뒤로_잘린다()
    {
        Assert.AreEqual(30.5f, OffsideRules.ClampOnside(37f, +1, 31f, 0.5f), 1e-4f);
        Assert.AreEqual(20f, OffsideRules.ClampOnside(20f, +1, 31f, 0.5f), 1e-4f, "선 뒤면 그대로");
        Assert.AreEqual(-30.5f, OffsideRules.ClampOnside(-37f, -1, 31f, 0.5f), 1e-4f, "팀1");
        Assert.AreEqual(37f, OffsideRules.ClampOnside(37f, +1, OffsideRules.NoLine, 0.5f), 1e-4f);
    }
}
