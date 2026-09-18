using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;
using NUnit.Framework;

// PositionRules 검증(09-18 확정 스펙 §6). ①서드 경계 ±17.5와 팀 1 부호 반전 ②공 앞쪽 상대 수(GK 제외)
// ③공격 시 자리 = 배치 + 전진(팀 −5/0/+5 + 개인 pushUp) + 폭(서드 배율 × 개인 width), 이탈 반경 클램프 ④수비 시 자리 + 라인 높이
public class PositionRulesTests
{
    [TestCase(-30f, +1, Third.Own)]
    [TestCase(-17.5f, +1, Third.Own)]       // 경계 포함: 우리 진영 쪽
    [TestCase(-17.4f, +1, Third.Middle)]
    [TestCase(0f, +1, Third.Middle)]
    [TestCase(17.4f, +1, Third.Middle)]
    [TestCase(17.5f, +1, Third.Opponent)]   // 경계 포함: 상대 진영 쪽
    [TestCase(40f, +1, Third.Opponent)]
    [TestCase(40f, -1, Third.Own)]          // 팀 1은 +X가 자기 진영
    [TestCase(-40f, -1, Third.Opponent)]
    public void 서드는_공_X와_공격_방향으로_정한다(float ballX, int attackSign, Third expected)
    {
        Assert.AreEqual(expected, PositionRules.ThirdOf(ballX, attackSign));
    }

    [Test]
    public void 공_앞쪽_상대_수는_GK를_빼고_공보다_상대_골_쪽에_있는_수다()
    {
        // 팀 0(+X 공격) 기준. 공은 x=10. 상대 GK(x=48)·CB(x=30)·CB(x=25)·DM(x=12)은 앞, CM(x=5)·ST(x=-8)는 뒤
        var opponents = new List<TargetInfo>
        {
            new TargetInfo(playerId: 11, x: 48f, z: 0f),
            new TargetInfo(playerId: 12, x: 30f, z: -7f),
            new TargetInfo(playerId: 13, x: 25f, z: 7f),
            new TargetInfo(playerId: 14, x: 12f, z: 0f),
            new TargetInfo(playerId: 15, x: 5f, z: 0f),
            new TargetInfo(playerId: 16, x: -8f, z: 0f),
        };

        Assert.AreEqual(3, PositionRules.CountDefendersAhead(10f, opponents, +1, keeperId: 11), "GK 제외 3명");
        Assert.AreEqual(4, PositionRules.CountDefendersAhead(10f, opponents, +1, keeperId: -1), "GK가 없으면 4명");
        Assert.AreEqual(2, PositionRules.CountDefendersAhead(10f, opponents, -1, keeperId: 11), "팀 1 기준 앞쪽 = -X 쪽: CM·ST");
        Assert.AreEqual(0, PositionRules.CountDefendersAhead(10f, new List<TargetInfo>(), +1, keeperId: -1));
    }

    [Test]
    public void 공격_시_자리는_배치에_전진과_폭_오프셋을_더한다()
    {
        // 배치 (-20, 8), 팀 0. 전진 정도 균형(1) → 0, pushUp 15 → X +15. 폭 표준(1) → 배율 1.0, width 10 → Z는 부호 방향으로 +10
        (float x, float z) home = PositionRules.AttackHome(baseX: -20f, baseZ: 8f, attackSign: +1, mentality: 1, pushUp: 15f, widthLevel: 1, width: 10f, roamRadius: 100f);
        Assert.AreEqual(-5f, home.x, 1e-4f);
        Assert.AreEqual(18f, home.z, 1e-4f, "오른쪽에 선 선수는 더 오른쪽으로");

        // 왼쪽 선수(z<0)는 더 왼쪽으로. 공격적(2) → +5. 폭 넓게(2) → 배율 1.5
        home = PositionRules.AttackHome(-20f, -8f, +1, mentality: 2, pushUp: 15f, widthLevel: 2, width: 10f, roamRadius: 100f);
        Assert.AreEqual(0f, home.x, 1e-4f);
        Assert.AreEqual(-23f, home.z, 1e-4f, "-8 - 10×1.5");

        // 팀 1은 전진이 -X
        home = PositionRules.AttackHome(20f, 0f, -1, mentality: 0, pushUp: 10f, widthLevel: 0, width: 10f, roamRadius: 100f);
        Assert.AreEqual(15f, home.x, 1e-4f, "20 - (−5 + 10)");
        Assert.AreEqual(0f, home.z, 1e-4f, "중앙(z=0) 선수는 폭 오프셋 없음");
    }

    [Test]
    public void 자리_이탈_반경이_오프셋을_클램프한다()
    {
        (float x, float z) home = PositionRules.AttackHome(-20f, 0f, +1, mentality: 2, pushUp: 25f, widthLevel: 1, width: 0f, roamRadius: 10f);
        Assert.AreEqual(-10f, home.x, 1e-4f, "+30이 반경 10으로 잘림");
    }

    [Test]
    public void 수비_시_자리는_배치에서_라인_높이만큼_앞으로_선다()
    {
        (float x, float z) home = PositionRules.DefendHome(baseX: -36f, baseZ: 7f, attackSign: +1, lineHeight: 6f);
        Assert.AreEqual(-30f, home.x, 1e-4f);
        Assert.AreEqual(7f, home.z, 1e-4f);

        home = PositionRules.DefendHome(36f, 7f, -1, lineHeight: 6f);
        Assert.AreEqual(30f, home.x, 1e-4f, "팀 1은 -X가 앞");
    }

    [Test]
    public void 자리는_필드_안으로_클램프된다()
    {
        (float x, float z) home = PositionRules.AttackHome(48f, 30f, +1, mentality: 2, pushUp: 25f, widthLevel: 2, width: 20f, roamRadius: 100f);
        Assert.LessOrEqual(home.x, FieldBounds.HalfLength - FieldBounds.EdgeMargin);
        Assert.LessOrEqual(home.z, FieldBounds.HalfWidth - FieldBounds.EdgeMargin);
    }
}
