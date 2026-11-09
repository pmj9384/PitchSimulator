using System.Collections.Generic;
using Game.Core.Data;
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
    public void 수비_시_자리는_배치에서_개인_라인_높이와_팀_이동량만큼_앞으로_선다()
    {
        (float x, float z) home = PositionRules.DefendHome(baseX: -36f, baseZ: 7f, attackSign: +1, lineHeight: 6f, lineShift: 0f);
        Assert.AreEqual(-36f + 6f * MatchTuning.PositionDialScale, home.x, 1e-4f, "라인 높이 6 × 0.3 = 1.8");
        Assert.AreEqual(7f, home.z, 1e-4f);

        home = PositionRules.DefendHome(36f, 7f, -1, lineHeight: 6f, lineShift: 0f);
        Assert.AreEqual(36f - 6f * MatchTuning.PositionDialScale, home.x, 1e-4f, "팀 1은 -X가 앞");

        // 11-08 D2: 팀 이동량 Δ(LineRules.LineShift)는 공격 방향으로 더해진다. 팀 수비 라인 높이는 Δ 안에 들어 있다(LineRulesTests)
        home = PositionRules.DefendHome(-36f, 7f, +1, 6f, lineShift: 12f);
        Assert.AreEqual(-36f + 1.8f + 12f, home.x, 1e-4f, "팀 0은 +X로 Δ");
        home = PositionRules.DefendHome(36f, 7f, -1, 6f, lineShift: 12f);
        Assert.AreEqual(36f - 1.8f - 12f, home.x, 1e-4f, "팀 1은 −X로 Δ");
        home = PositionRules.DefendHome(-36f, 7f, +1, 6f, lineShift: -3f);
        Assert.AreEqual(-36f + 1.8f - 3f, home.x, 1e-4f, "Δ가 음수면 골 쪽으로");
    }

    // 공 지향 슬라이드(10-25 격자): 공격 가로만 (공 z − 자리 z) × 계수 수축, 수비 가로는 공 z × 계수 평행이동.
    // 수비 세로는 11-08 D2부터 슬라이드가 아니라 라인 깊이 Δ(LineRules)가 맡는다. 식마다의 이유는 PositionRules 주석, 수치는 실험/2026-10-25-슬라이드-수축-격자.md
    [Test]
    public void 수비_슬라이드는_가로만_공_z에_비례해_전원이_같은_양을_옮기고_세로는_안_움직인다()
    {
        float defendLateral = MatchTuning.SlideLateralDefend;

        (float x, float z) d = PositionRules.SlideDefend(-20f, 5f, ballZ: -20f, goalkeeper: false);
        Assert.AreEqual(-20f, d.x, 1e-4f, "세로 슬라이드 없음(11-08 D2에서 삭제. 세로는 Δ)");
        Assert.AreEqual(5f - 20f * defendLateral, d.z, 1e-4f, "가로는 공 z × 계수. 자리와 무관하게 같은 양");

        (float x, float z) center = PositionRules.SlideDefend(-41f, 7f, 0f, goalkeeper: false);
        Assert.AreEqual(-41f, center.x, 1e-4f, "공이 중앙이면 자리 그대로");
        Assert.AreEqual(7f, center.z, 1e-4f);
    }

    [Test]
    public void 공격_세로_슬라이드는_상한으로_잘린다()
    {
        // 공격: 공 50 × 0.4 = 20 > 15
        (float x, float z) a = PositionRules.SlideAttack(22f, 0f, 50f, 0f, goalkeeper: false);
        Assert.AreEqual(22f + MatchTuning.SlideVerticalMaxAttack, a.x, 1e-4f);
    }

    [Test]
    public void 공격_슬라이드는_가로를_자리와_공의_차이에_비례해_당긴다()
    {
        // 자리 (22, 6), 공 (10, 10): 세로 10×계수, 가로는 자리에서 공까지 4m × 계수
        (float x, float z) a = PositionRules.SlideAttack(22f, 6f, ballX: 10f, ballZ: 10f, goalkeeper: false);
        Assert.AreEqual(22f + 10f * MatchTuning.SlideVerticalAttack, a.x, 1e-4f);
        Assert.AreEqual(6f + 4f * MatchTuning.SlideLateralAttack, a.z, 1e-4f, "공격은 (공 z − 자리 z) × 계수");

        // 공이 중앙이면 세로는 0, 가로만 자리에서 중앙 쪽으로. 편성 자리 = 가장 벌어진 모양. 전술판 미니 피치(TacticsPreview)도 공 칸 (1,1) = (0, 0)으로 같은 식을 부른다
        (float x, float z) center = PositionRules.SlideAttack(22f, 6f, 0f, 0f, goalkeeper: false);
        Assert.AreEqual(22f, center.x, 1e-4f);
        Assert.AreEqual(6f - 6f * MatchTuning.SlideLateralAttack, center.z, 1e-4f);
    }

    [Test]
    public void GK_슬라이드는_가로만_조금이고_공격_수비_같다()
    {
        (float x, float z) d = PositionRules.SlideDefend(-48f, 0f, 20f, goalkeeper: true);
        Assert.AreEqual(-48f, d.x, 1e-4f, "세로 0");
        Assert.AreEqual(20f * MatchTuning.SlideLateralKeeper, d.z, 1e-4f);

        (float x, float z) a = PositionRules.SlideAttack(-48f, 0f, 30f, 20f, goalkeeper: true);
        Assert.AreEqual(d, a, "GK는 소유 여부와 무관");
    }

    [Test]
    public void 공격_때_공에서_먼_선수가_더_많이_움직여_두_선수_간격이_줄어들고_수비_때는_그대로다()
    {
        // 코칭 원칙 10(Coaches' Voice): 공 쪽은 터치라인을 쓰고 반대편 윙은 중앙으로 들어온다. 좌우 W(z ±24), 공 z=+20
        float k = MatchTuning.SlideLateralAttack;
        (float x, float z) near = PositionRules.SlideAttack(-18f, 24f, ballX: 0f, ballZ: 20f, goalkeeper: false);
        (float x, float z) far = PositionRules.SlideAttack(-18f, -24f, ballX: 0f, ballZ: 20f, goalkeeper: false);
        Assert.AreEqual(48f * (1f - k), near.z - far.z, 1e-4f, "두 선수 간격은 (1 − k)배로 줄어든다");

        (float x, float z) dNear = PositionRules.SlideDefend(-23f, 24f, 20f, goalkeeper: false);
        (float x, float z) dFar = PositionRules.SlideDefend(-23f, -24f, 20f, goalkeeper: false);
        Assert.AreEqual(48f, dNear.z - dFar.z, 1e-4f, "수비 블록은 같은 양을 옮겨 폭이 그대로");
    }

    // 10-29 ③d: 트리(MoveToAttackHome·MoveToDefendHome)와 전술판 미니 피치(TacticsPreview)가 같은 합성 함수를 부른다. 홈 + 공 지향 슬라이드가 한 곳에
    [Test]
    public void 공격_Stand는_공격_시_자리에_공_지향_슬라이드를_얹은_것과_같다()
    {
        var tactics = new TeamTactics { Mentality = 2, Width = new[] { 0, 1, 2 } };
        var stats = new PlayerStats { RoleId = "W", PushUp = 15f, Width = 10f };
        // 공 x 30 → 상대 진영 서드(폭 배열 [2] = 넓게)
        (float x, float z) home = PositionRules.AttackHome(-18f, 24f, +1, mentality: 2, pushUp: 15f, widthLevel: 2, width: 10f);
        (float x, float z) expected = PositionRules.SlideAttack(home.x, home.z, 30f, 10f, goalkeeper: false);
        Assert.AreEqual(expected, PositionRules.AttackStand(-18f, 24f, +1, tactics, stats, ballX: 30f, ballZ: 10f, goalkeeper: false));

        // 팀 1은 공 x 30이 우리 진영 서드(폭 배열 [0] = 좁게), 전진은 −X
        home = PositionRules.AttackHome(18f, 24f, -1, mentality: 2, pushUp: 15f, widthLevel: 0, width: 10f);
        expected = PositionRules.SlideAttack(home.x, home.z, 30f, 10f, goalkeeper: false);
        Assert.AreEqual(expected, PositionRules.AttackStand(18f, 24f, -1, tactics, stats, 30f, 10f, goalkeeper: false));

        var keeper = new PlayerStats { RoleId = "GK", PushUp = 0f, Width = 0f };
        home = PositionRules.AttackHome(-48f, 0f, +1, mentality: 2, pushUp: 0f, widthLevel: 2, width: 0f);
        Assert.AreEqual(PositionRules.SlideAttack(home.x, home.z, 30f, 10f, goalkeeper: true), PositionRules.AttackStand(-48f, 0f, +1, tactics, keeper, 30f, 10f, goalkeeper: true), "GK는 GK 슬라이드");
    }

    [Test]
    public void 수비_Stand는_수비_시_자리에_팀_이동량과_공_지향_슬라이드를_얹은_것과_같다()
    {
        var stats = new PlayerStats { RoleId = "CB", LineHeight = 6f };
        (float x, float z) home = PositionRules.DefendHome(-41f, 7f, +1, lineHeight: 6f, lineShift: 9f);
        (float x, float z) expected = PositionRules.SlideDefend(home.x, home.z, -20f, goalkeeper: false);
        Assert.AreEqual(expected, PositionRules.DefendStand(-41f, 7f, +1, stats, ballZ: -20f, lineShift: 9f, depthCap: float.MaxValue, goalkeeper: false));

        var keeper = new PlayerStats { RoleId = "GK", LineHeight = 0f };
        home = PositionRules.DefendHome(-48f, 0f, +1, lineHeight: 0f, lineShift: 0f);
        Assert.AreEqual(PositionRules.SlideDefend(home.x, home.z, -20f, goalkeeper: true), PositionRules.DefendStand(-48f, 0f, +1, keeper, -20f, lineShift: 9f, depthCap: 0f, goalkeeper: true), "GK는 Δ도 깊이 상한도 안 따르고 GK 슬라이드");
    }

    [TestCase(+1)]
    [TestCase(-1)]
    public void 필드_선수의_수비_자리는_깊이_상한보다_앞에_서지_않는다(int sign)
    {
        // 11-09 probe: 개인 라인 높이 다이얼이 Δ 위에 얹혀 뒷줄이 공보다 앞(공 쪽)에 섰고, 공을 몬 공격수가 라인 뒤에서 혼자 찼다(슛의 95%)
        var stats = new PlayerStats { RoleId = "CB", LineHeight = 20f };   // 다이얼 6m
        float baseX = LineRules.XAtDepth(12f, sign);
        (float x, float z) free = PositionRules.DefendStand(baseX, 7f, sign, stats, 0f, lineShift: 3f, depthCap: float.MaxValue, goalkeeper: false);
        Assert.AreEqual(21f, LineRules.HomeDepth(free.x, sign), 1e-4f, "상한이 없으면 12 + 6 + 3");
        (float x, float z) capped = PositionRules.DefendStand(baseX, 7f, sign, stats, 0f, lineShift: 3f, depthCap: 16f, goalkeeper: false);
        Assert.AreEqual(16f, LineRules.HomeDepth(capped.x, sign), 1e-4f, "상한 16에서 자른다");
        Assert.AreEqual(free.z, capped.z, 1e-4f, "가로는 그대로");
    }

    [Test]
    public void 필드_선수의_수비_자리는_공이_어디에_있어도_GK_라인보다_앞이다()
    {
        // 11-06 유저 Play "센터백 라인이 골키퍼랑 동일선상". D2(11-08)는 라인 깊이 하한 = GK 라인 + FloorMargin(4m)으로 막는다.
        // 깊게 라인(가장 낮은 팀 다이얼)·개인 라인 높이 0으로 가장 깊은 CB(편성 −41이 팀 최심)를 만들고, 공을 필드 격자 전역에 둔다.
        // GK 라인은 편성 −48 + 스위퍼 lineHeight 8(표 최댓값) × 0.3 = 골에서 6.9m. CB는 그보다 4m 앞이어야 한다
        var cb = new PlayerStats { RoleId = "CB", LineHeight = 0f };
        float keeperDepth = LineRules.HomeDepth(PositionRules.KeeperLineX(-48f, 0f, +1, 8f), +1);
        float restDepth = LineRules.HomeDepth(-41f, +1);
        for (float bx = -52f; bx <= 52f; bx += 2f)
        {
            for (float bz = -33f; bz <= 33f; bz += 6f)
            {
                float ballDepth = LineRules.HomeDepth(bx, +1);
                float shift = LineRules.LineShift(LineRules.LineDepth(ballDepth, 0, keeperDepth), restDepth);
                float cap = LineRules.StandDepthCap(ballDepth, LineRules.LineFloor(keeperDepth));
                (float x, float z) stand = PositionRules.DefendStand(-41f, 7f, +1, cb, bz, shift, cap, goalkeeper: false);
                Assert.GreaterOrEqual(LineRules.HomeDepth(stand.x, +1), keeperDepth + MatchTuning.FloorMargin - 1e-4f, $"공 ({bx}, {bz})");
            }
        }
    }

    [Test]
    public void GK_라인_x는_수비_시_자리의_x와_같다()
    {
        // 시뮬의 닿음 판정과 트리의 이동이 같은 함수를 쓴다(10-25). 라인 높이 6 × 0.3 = 1.8 앞
        Assert.AreEqual(PositionRules.DefendHome(-48f, 0f, +1, 6f, 0f).x, PositionRules.KeeperLineX(-48f, 0f, +1, 6f), 1e-4f, "GK 라인은 팀 수비 라인과 무관");
        Assert.AreEqual(48f - 6f * MatchTuning.PositionDialScale, PositionRules.KeeperLineX(48f, 0f, -1, 6f), 1e-4f, "팀 1은 -X가 앞");
    }

    [Test]
    public void 자리는_필드_안으로_클램프된다()
    {
        (float x, float z) home = PositionRules.AttackHome(48f, 30f, +1, mentality: 2, pushUp: 25f, widthLevel: 2, width: 20f);
        Assert.LessOrEqual(home.x, FieldBounds.HalfLength - FieldBounds.EdgeMargin);
        Assert.LessOrEqual(home.z, FieldBounds.HalfWidth - MatchTuning.TouchlineMargin, "가로는 터치라인 여유 3m 안쪽까지(09-26)");
    }

    [Test]
    public void 공_쪽_윙어는_슬라이드로_밀려도_터치라인_여유_안에_선다()
    {
        // 09-26 Play: 윙어 자리 z 31.5에 공 z 30 → 슬라이드 +6 → 가장자리 33.5에 붙어 라인 위 빌드업. 여유 3m면 30.5에서 멈춘다
        (float x, float z) slid = PositionRules.SlideAttack(7f, 31.5f, ballX: 10f, ballZ: 30f, goalkeeper: false);
        Assert.AreEqual(FieldBounds.HalfWidth - MatchTuning.TouchlineMargin, slid.z, 1e-4f);
    }
}
