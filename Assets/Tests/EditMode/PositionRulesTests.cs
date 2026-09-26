using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;
using NUnit.Framework;

// PositionRules 검증(09-18 확정 스펙 §6). ①서드 경계 ±17.5와 팀 1 부호 반전 ②공 앞쪽 상대 수(GK 제외)
// ③공격 시 자리 = 배치 + 전진(팀 −5/0/+5 + 개인 pushUp) + 폭(서드 배율 × 개인 width), 필드 안 클램프(이탈 반경과 무관) ④수비 시 자리 + 라인 높이
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
        // 배치 (-20, 8), 팀 0. 전진 정도 균형(1) → 0, pushUp 15 → X +15(미터 그대로). 폭 표준(1) → 배율 1.0, width 10 × 0.3 → Z는 부호 방향으로 +3 (다이얼 배율 09-26, 폭·라인 높이만)
        float s = MatchTuning.PositionDialScale;
        (float x, float z) home = PositionRules.AttackHome(baseX: -20f, baseZ: 8f, attackSign: +1, mentality: 1, pushUp: 15f, widthLevel: 1, width: 10f);
        Assert.AreEqual(-5f, home.x, 1e-4f);
        Assert.AreEqual(8f + 10f * s, home.z, 1e-4f, "오른쪽에 선 선수는 더 오른쪽으로");

        // 왼쪽 선수(z<0)는 더 왼쪽으로. 공격적(2) → +5(팀 오프셋은 미터 그대로). 폭 넓게(2) → 배율 1.5
        home = PositionRules.AttackHome(-20f, -8f, +1, mentality: 2, pushUp: 15f, widthLevel: 2, width: 10f);
        Assert.AreEqual(0f, home.x, 1e-4f);
        Assert.AreEqual(-8f - 10f * s * 1.5f, home.z, 1e-4f, "-8 - 10×0.3×1.5");

        // 팀 1은 전진이 -X
        home = PositionRules.AttackHome(20f, 0f, -1, mentality: 0, pushUp: 10f, widthLevel: 0, width: 10f);
        Assert.AreEqual(15f, home.x, 1e-4f, "20 - (−5 + 10)");
        Assert.AreEqual(0f, home.z, 1e-4f, "중앙(z=0) 선수는 폭 오프셋 없음");
    }

    [Test]
    public void 전진_폭은_이탈_반경과_무관하게_그대로_올라간다()
    {
        // 09-21: 이탈 반경(ST 3m)이 전진 폭(30m)을 잘라 공격 형태가 자기 진영에 갇혀 3분 슛 0이었다. 전진 폭은 자리를 정의하는 값이라 안 자른다
        (float x, float z) home = PositionRules.AttackHome(-8f, 6f, +1, mentality: 1, pushUp: 30f, widthLevel: 1, width: 10f);
        Assert.AreEqual(22f, home.x, 1e-4f, "ST 배치 -8 + 전진 30(이탈 반경 3으로 안 잘림)");
        Assert.AreEqual(6f + 10f * MatchTuning.PositionDialScale, home.z, 1e-4f);
    }

    [Test]
    public void 수비_시_자리는_배치에서_라인_높이만큼_앞으로_선다()
    {
        (float x, float z) home = PositionRules.DefendHome(baseX: -36f, baseZ: 7f, attackSign: +1, lineHeight: 6f);
        Assert.AreEqual(-36f + 6f * MatchTuning.PositionDialScale, home.x, 1e-4f, "라인 높이 6 × 0.3 = 1.8");
        Assert.AreEqual(7f, home.z, 1e-4f);

        home = PositionRules.DefendHome(36f, 7f, -1, lineHeight: 6f);
        Assert.AreEqual(36f - 6f * MatchTuning.PositionDialScale, home.x, 1e-4f, "팀 1은 -X가 앞");
    }

    [Test]
    public void 공_지향_슬라이드는_공_쪽으로_평행이동하고_세로는_상한으로_잘린다()
    {
        // 수비 시: 공 (30, -20) → 세로 30×0.3 = 9, 가로 -20×0.3 = -6(09-26 0.4 → 0.3: 블록이 너무 촘촘해 골 0.6)
        (float x, float z) d = PositionRules.SlideTowardBall(-20f, 5f, ballX: 30f, ballZ: -20f, defending: true, goalkeeper: false);
        Assert.AreEqual(-11f, d.x, 1e-4f, "공이 멀면 라인이 올라간다");
        Assert.AreEqual(-1f, d.z, 1e-4f, "공이 왼쪽이면 왼쪽으로");

        // 세로 상한: 공 X 45 × 0.3 = 13.5 → 10으로 잘림
        d = PositionRules.SlideTowardBall(-20f, 0f, 45f, 0f, defending: true, goalkeeper: false);
        Assert.AreEqual(-10f, d.x, 1e-4f);

        // 공격 시: 세로 0.4·상한 15, 가로 0.2
        (float x, float z) a = PositionRules.SlideTowardBall(22f, 6f, 45f, 10f, defending: false, goalkeeper: false);
        Assert.AreEqual(37f, a.x, 1e-4f, "45×0.4 = 18 → 15로 잘림");
        Assert.AreEqual(8f, a.z, 1e-4f);

        // GK: 세로 0, 가로 0.15
        (float x, float z) gk = PositionRules.SlideTowardBall(-48f, 0f, 30f, 20f, defending: true, goalkeeper: true);
        Assert.AreEqual(-48f, gk.x, 1e-4f);
        Assert.AreEqual(3f, gk.z, 1e-4f);
    }

    [Test]
    public void 자리는_필드_안으로_클램프된다()
    {
        (float x, float z) home = PositionRules.AttackHome(48f, 30f, +1, mentality: 2, pushUp: 25f, widthLevel: 2, width: 20f);
        Assert.LessOrEqual(home.x, FieldBounds.HalfLength - FieldBounds.EdgeMargin);
        Assert.LessOrEqual(home.z, FieldBounds.HalfWidth - FieldBounds.EdgeMargin);
    }
}
