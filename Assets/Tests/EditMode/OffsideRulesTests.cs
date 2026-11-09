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

    [Test]
    public void 수비_라인은_공을_빼고_상대_뒤에서_2번째_선수다()
    {
        var opp = new List<TargetInfo> { new TargetInfo(20, 48f, 0f), new TargetInfo(21, 31f, 5f), new TargetInfo(22, 30f, -5f), new TargetInfo(23, 20f, 0f) };
        Assert.AreEqual(31f, OffsideRules.LastDefenderLine(opp, +1), 1e-4f);
        Assert.AreEqual(31f, OffsideRules.OnsideLine(opp, +1, ballX: 10f), 1e-4f, "공이 뒤면 온사이드 선과 같다");
        Assert.AreEqual(40f, OffsideRules.OnsideLine(opp, +1, ballX: 40f), 1e-4f, "온사이드 선은 공이 앞서면 공을 따라간다");
        Assert.Less(OffsideRules.LastDefenderLine(opp, +1), OffsideRules.OnsideLine(opp, +1, ballX: 40f), "수비 라인엔 공 위치 항이 없다");
        Assert.AreEqual(OffsideRules.NoLine, OffsideRules.LastDefenderLine(new List<TargetInfo> { new TargetInfo(20, 48f, 0f) }, +1));
        var opp1 = new List<TargetInfo> { new TargetInfo(0, -48f, 0f), new TargetInfo(1, -31f, 0f) };
        Assert.AreEqual(31f, OffsideRules.LastDefenderLine(opp1, -1), 1e-4f, "팀1은 공격 방향 좌표");
    }

    [Test]
    public void 온사이드_선은_GK_다음_가장_깊은_뒷줄이다()
    {
        // 성질 재확인(D2 규칙 자체는 잠그지 않는다): 11-09 D2 뒤 뒷줄은 라인 깊이 L(가장 깊은 선수)에서 다이얼 차만큼 흩어져 선다. 온사이드 선 식은 그대로라 L이 오르면 선도 같이 오른다(RunRules·트리 온사이드 클램프가 읽음).
        // 팀 1이 지키는 +X 골: GK 48(골에서 4.5m), 뒷줄 4명 깊이 L·L+1·L+2·L+3(L = 20 → x 32.5·31.5·30.5·29.5). 팀 0 공격 기준 2번째 선수 = L
        float lineDepth = 20f;
        var defenders = new List<TargetInfo> { new TargetInfo(20, 48f, 0f) };
        for (int i = 0; i < 4; i++) { defenders.Add(new TargetInfo(21 + i, LineRules.XAtDepth(lineDepth + i, -1), i * 5f)); }
        Assert.AreEqual(LineRules.XAtDepth(lineDepth, -1), OffsideRules.LastDefenderLine(defenders, +1), 1e-4f);
    }
}
