using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// LineRules 검증(11-06 수비 구조 D1). 뒷줄 = 수비 시 자리 깊이가 팀 최솟값 + BackLineBand(6m) 안. 역할 이름을 보지 않고 슬롯에서 파생한다.
// 편성 6종 전건: 4-4-2·4-3-3·4-2-3-1은 FB·CB 4명, 5-3-2는 5명(FB −38이 4m 차로 안), 3-5-2·3-4-3은 CB 3명(윙백 −31·−29는 밖)
public class LineRulesTests
{
    [Test]
    public void 수비_자리_깊이는_우리_골라인에서_잰_거리고_팀_부호에_따라_뒤집힌다()
    {
        Assert.AreEqual(11.5f, LineRules.HomeDepth(-41f, +1), 1e-4f, "팀 0 CB −41");
        Assert.AreEqual(11.5f, LineRules.HomeDepth(41f, -1), 1e-4f, "팀 1 CB +41");
        Assert.AreEqual(23.5f, LineRules.HomeDepth(-29f, +1), 1e-4f, "DM −29");
        Assert.AreEqual(FieldBounds.HalfLength, LineRules.HomeDepth(0f, +1), 1e-4f, "하프라인");
    }

    [Test]
    public void 뒷줄은_가장_깊은_자리에서_밴드_안이고_경계는_포함이다()
    {
        float deepest = 11.5f;
        Assert.IsTrue(LineRules.IsBackLine(11.5f, deepest), "가장 깊은 선수 자신");
        Assert.IsTrue(LineRules.IsBackLine(deepest + MatchTuning.BackLineBand, deepest), "밴드 끝(= 6m) 포함");
        Assert.IsFalse(LineRules.IsBackLine(deepest + MatchTuning.BackLineBand + 0.01f, deepest), "밴드 밖");
        Assert.IsFalse(LineRules.IsBackLine(23.5f, deepest), "DM(12m 차)");
    }

    [TestCase("4-4-2", 4)]
    [TestCase("4-3-3", 4)]
    [TestCase("5-3-2", 5)]
    [TestCase("3-5-2", 3)]
    [TestCase("4-2-3-1", 4)]
    [TestCase("3-4-3", 3)]
    public void 편성_6종의_뒷줄_수는_수비_시_자리에서_파생되고_GK는_빠진다(string formationId, int expected)
    {
        List<FormationTemplate> formations = FormationTemplateParser.Parse(File.ReadAllText("Assets/Resources/Tables/FormationTemplates.csv"));
        FormationTemplate formation = formations.Find(f => f.Id == formationId);
        Assert.IsNotNull(formation, formationId);

        float deepest = float.MaxValue;
        foreach (FormationSlot slot in formation.Slots)
        {
            if (slot.RoleId == "GK") { continue; }
            deepest = System.Math.Min(deepest, LineRules.HomeDepth(slot.PosX2, +1));
        }
        int count = 0;
        foreach (FormationSlot slot in formation.Slots)
        {
            if (slot.RoleId == "GK") { continue; }
            if (!LineRules.IsBackLine(LineRules.HomeDepth(slot.PosX2, +1), deepest)) { continue; }
            count++;
            Assert.IsTrue(slot.RoleId == "CB" || slot.RoleId == "FB", $"{formationId} 뒷줄은 CB·FB만 ({slot.RoleId} 슬롯 {slot.Slot})");
        }
        Assert.AreEqual(expected, count, formationId);
    }

    [Test]
    public void 둘째_줄은_뒷줄_아닌_선수_중_가장_깊은_자리에서_밴드_안이고_뒷줄은_아니다()
    {
        // 4-2-3-1: 뒷줄 깊이 11.5(CB −41), 둘째 줄 기준 23.5(DM −29). W·AM(−19 → 33.5)은 10m 차라 둘째 줄이 아니고, ST(−11 → 41.5)도 아니다
        float deepest = LineRules.HomeDepth(-41f, +1);
        float second = LineRules.HomeDepth(-29f, +1);
        Assert.IsTrue(LineRules.IsSecondLine(second, deepest, second), "DM");
        Assert.IsFalse(LineRules.IsSecondLine(LineRules.HomeDepth(-19f, +1), deepest, second), "W·AM");
        Assert.IsFalse(LineRules.IsSecondLine(LineRules.HomeDepth(-11f, +1), deepest, second), "ST");
        Assert.IsFalse(LineRules.IsSecondLine(deepest, deepest, second), "뒷줄 선수는 둘째 줄이 아니다");
        Assert.IsFalse(LineRules.IsSecondLine(LineRules.HomeDepth(-40f, +1), deepest, second), "뒷줄 FB(−40)도 아니다");
        // 4-4-2: 둘째 줄 기준 = CM −25(27.5). W −23(29.5)은 2m 차로 같은 줄, ST −13(39.5)은 밖
        float second442 = LineRules.HomeDepth(-25f, +1);
        Assert.IsTrue(LineRules.IsSecondLine(LineRules.HomeDepth(-23f, +1), deepest, second442), "4-4-2 W는 CM과 같은 줄");
        Assert.IsFalse(LineRules.IsSecondLine(LineRules.HomeDepth(-13f, +1), deepest, second442), "4-4-2 ST");
        // 밴드 끝 포함: 4-3-3 DM −29(23.5)에서 CM −23(29.5)은 정확히 6m라 둘째 줄이다. 0.1m 더 앞이면 아니다
        float second433 = LineRules.HomeDepth(-29f, +1);
        Assert.IsTrue(LineRules.IsSecondLine(LineRules.HomeDepth(-23f, +1), deepest, second433), "4-3-3 CM은 밴드 끝 = 둘째 줄");
        Assert.IsFalse(LineRules.IsSecondLine(LineRules.HomeDepth(-22.9f, +1), deepest, second433), "밴드 밖 0.1m");
    }

    [Test]
    public void 백3_편성의_윙백은_뒷줄이_아니다()
    {
        // 3-5-2 FB posX2 −31(깊이 21.5) 대 CB −41(11.5): 10m 차. 현실의 윙백처럼 미드 줄로 분류돼 리시버 마크 1차 후보에서 빠진다
        float deepest = LineRules.HomeDepth(-41f, +1);
        Assert.IsFalse(LineRules.IsBackLine(LineRules.HomeDepth(-31f, +1), deepest), "3-5-2 윙백");
        Assert.IsFalse(LineRules.IsBackLine(LineRules.HomeDepth(-29f, +1), deepest), "3-4-3 윙백");
        Assert.IsTrue(LineRules.IsBackLine(LineRules.HomeDepth(-38f, +1), LineRules.HomeDepth(-42f, +1)), "5-3-2 FB는 4m 차라 뒷줄");
    }

    // ── 라인 깊이(11-08 D2, 설계-수비-구조.md 8절)

    [Test]
    public void 기본_라인_깊이는_Metrica_앵커_3점을_지나고_사이는_직선이다()
    {
        Assert.AreEqual(15f, LineRules.RestLineDepth(17.5f), 1e-4f, "공이 우리 서드 중심");
        Assert.AreEqual(32f, LineRules.RestLineDepth(52.5f), 1e-4f, "하프라인");
        Assert.AreEqual(47f, LineRules.RestLineDepth(87.5f), 1e-4f, "상대 서드 중심");
        Assert.AreEqual(23.5f, LineRules.RestLineDepth(35f), 1e-4f, "첫 구간 가운데 = (15 + 32) / 2");
        Assert.AreEqual(39.5f, LineRules.RestLineDepth(70f), 1e-4f, "둘째 구간 가운데 = (32 + 47) / 2");
    }

    [Test]
    public void 앵커_밖은_끝_구간_기울기로_늘린다()
    {
        float lowSlope = (32f - 15f) / 35f;
        float highSlope = (47f - 32f) / 35f;
        Assert.AreEqual(15f - 17.5f * lowSlope, LineRules.RestLineDepth(0f), 1e-4f, "공이 우리 골라인");
        Assert.AreEqual(47f + 17.5f * highSlope, LineRules.RestLineDepth(105f), 1e-4f, "공이 상대 골라인");
    }

    [Test]
    public void 라인_깊이는_팀_수비_라인_오프셋을_더하고_하한과_상한으로_자른다()
    {
        float keeper = 4.5f;   // 편성 GK −48
        Assert.AreEqual(32f, LineRules.LineDepth(52.5f, 1, keeper), 1e-4f, "표준");
        Assert.AreEqual(27f, LineRules.LineDepth(52.5f, 0, keeper), 1e-4f, "깊게 −5");
        Assert.AreEqual(37f, LineRules.LineDepth(52.5f, 2, keeper), 1e-4f, "높게 +5");

        Assert.AreEqual(LineRules.LineFloor(keeper), LineRules.LineDepth(0f, 0, keeper), 1e-4f, "공이 골라인 + 깊게면 L₀ − 5 = 2.6이 하한 8.5로");
        Assert.AreEqual(keeper + MatchTuning.FloorMargin, LineRules.LineFloor(keeper), 1e-4f, "하한 = GK 라인 + 4");
        Assert.AreEqual(LineRules.LineFloor(6.9f), LineRules.LineDepth(0f, 1, 6.9f), 1e-4f, "스위퍼 GK(6.9m)면 하한 10.9가 L₀ 6.5를 자른다");

        Assert.AreEqual(MatchTuning.LineCap, LineRules.LineDepth(105f, 2, keeper), 1e-4f, "공이 상대 골라인 + 높게면 상한 55");
    }

    [Test]
    public void 라인_깊이는_공과_최소_간격을_지킨다()
    {
        // 11-09 D2-a: L = min(L₀ + 오프셋, 공 깊이 − MinLineGap) → 하한·상한. 수비 서드에서 공-라인 간격이 1.4m라 운반자가 그냥 지나갔다
        float keeper = 4.5f;
        Assert.AreEqual(30f - MatchTuning.MinLineGap, LineRules.LineDepth(30f, 1, keeper), 1e-4f, "공 30 → L₀ 21.1을 간격이 20으로");
        Assert.AreEqual(LineRules.RestLineDepth(40f), LineRules.LineDepth(40f, 1, keeper), 1e-4f, "공 40 → L₀ 25.9(간격 30은 안 걸림)");
        Assert.AreEqual(LineRules.LineFloor(keeper), LineRules.LineDepth(17.5f, 1, keeper), 1e-4f, "공 17.5 → 간격 7.5가 하한 8.5에 걸림");
    }

    [Test]
    public void 높게_오프셋은_최소_간격_안에서만_더해진다()
    {
        // 결정 3: 수비 서드에선 높게·깊게 차이가 사라지고(간격이 이긴다), 중원·상대 서드에서만 보인다
        float keeper = 4.5f;
        Assert.AreEqual(LineRules.LineDepth(30f, 1, keeper), LineRules.LineDepth(30f, 2, keeper), 1e-4f, "공 30: 높게도 간격 20");
        Assert.AreEqual(10f, LineRules.LineDepth(52.5f, 2, keeper) - LineRules.LineDepth(52.5f, 0, keeper), 1e-4f, "하프라인: 깊게/높게 차이 10m 그대로");
    }

    [Test]
    public void 편성_뒷줄_깊이는_다이얼을_포함한_최솟값이다()
    {
        // 11-09 D2-a: 다이얼을 빼고 재면 가장 깊은 선수가 L보다 다이얼만큼 앞에 섰다. CB(−41, lineHeight 20)·FB(−40, 15) → min(11.5 + 6, 12.5 + 4.5) = 17
        float cb = LineRules.DialedHomeDepth(-41f, +1, 20f);
        float fb = LineRules.DialedHomeDepth(-40f, +1, 15f);
        Assert.AreEqual(17.5f, cb, 1e-4f);
        Assert.AreEqual(17f, fb, 1e-4f);
        Assert.AreEqual(17.5f, LineRules.DialedHomeDepth(41f, -1, 20f), 1e-4f, "팀 1도 같은 깊이");
    }

    [Test]
    public void 이동량은_라인_깊이에서_편성_뒷줄_깊이를_뺀_것이고_전원이_같은_양을_옮긴다()
    {
        float rest = LineRules.HomeDepth(-41f, +1);   // CB −41 = 11.5m
        float shift = LineRules.LineShift(32f, rest);
        Assert.AreEqual(20.5f, shift, 1e-4f);
        // 같은 Δ를 CB(−41)와 CM(−25)에 더하면 둘 사이 간격(16m)이 그대로다: 라인 간격은 편성 posX2가 정한다
        float cb = PositionRules.DefendHome(-41f, 0f, +1, 0f, shift).x;
        float cm = PositionRules.DefendHome(-25f, 0f, +1, 0f, shift).x;
        Assert.AreEqual(16f, cm - cb, 1e-4f);
        Assert.AreEqual(32f, LineRules.HomeDepth(cb, +1), 1e-4f, "가장 깊은 선수가 L에 선다(개인 다이얼 0)");
    }

    [Test]
    public void 마크_하한은_라인과_공보다_골_쪽_선_중_깊은_쪽이고_GK_하한_아래로는_안_간다()
    {
        float floor = 8.5f;
        Assert.AreEqual(20f, LineRules.MarkFloorDepth(lineDepth: 20f, ballDepth: 40f, floorDepth: floor), 1e-4f, "공이 라인 앞이면 라인까지만");
        Assert.AreEqual(12f - MatchTuning.GoalSideMargin, LineRules.MarkFloorDepth(lineDepth: 20f, ballDepth: 12f, floorDepth: floor), 1e-4f, "공이 라인보다 깊으면 공보다 골 쪽 선(공 − 1.5)까지");
        Assert.LessOrEqual(LineRules.MarkFloorDepth(20f, 12f, floor), LineRules.StandDepthCap(12f, floor), "마크 하한은 마크 상한(StandDepthCap)을 넘지 않는다");
        Assert.AreEqual(floor, LineRules.MarkFloorDepth(lineDepth: 20f, ballDepth: 3f, floorDepth: floor), 1e-4f, "공이 하한보다 깊어도 하한");
    }

    [Test]
    public void 수비_자리_깊이_상한은_공보다_골_쪽이고_하한이_우선한다()
    {
        float floor = 8.5f;
        Assert.AreEqual(30f - MatchTuning.GoalSideMargin, LineRules.StandDepthCap(30f, floor), 1e-4f, "공 깊이 − 여유");
        Assert.AreEqual(floor, LineRules.StandDepthCap(5f, floor), 1e-4f, "공이 하한보다 깊으면 하한");
    }

    [Test]
    public void 깊이_상한은_뒷줄과_둘째_줄에만_걸리고_앞줄은_상한이_없다()
    {
        // 11-10 묶음 C-1: 팀 상한을 전원에게 걸면 ST·W·CM이 공 바로 뒤 한 줄에 서서 4-4-2 CM-ST가 2m 간격으로 겹쳤다(정상 수비 2m 쌍 ×3.0). 앞줄은 출구로 남는다
        Assert.AreEqual(16f, LineRules.StandDepthCapFor(isBackLine: true, isSecondLine: false, teamCap: 16f), "뒷줄은 팀 상한");
        Assert.AreEqual(16f, LineRules.StandDepthCapFor(isBackLine: false, isSecondLine: true, teamCap: 16f), "둘째 줄도 팀 상한");
        Assert.AreEqual(float.MaxValue, LineRules.StandDepthCapFor(isBackLine: false, isSecondLine: false, teamCap: 16f), "앞줄은 상한 없음");
    }

    [TestCase(+1)]
    [TestCase(-1)]
    public void 깊이_아래면_그_깊이로_올리고_앞이면_그대로다(int sign)
    {
        float ownGoalX = -sign * FieldBounds.HalfLength;
        Assert.AreEqual(ownGoalX + sign * 10f, LineRules.AboveDepth(ownGoalX + sign * 3f, 10f, sign), 1e-4f, "골 쪽이면 올림");
        Assert.AreEqual(ownGoalX + sign * 20f, LineRules.AboveDepth(ownGoalX + sign * 20f, 10f, sign), 1e-4f, "앞이면 그대로");
        Assert.AreEqual(ownGoalX + sign * 7f, LineRules.XAtDepth(7f, sign), 1e-4f, "XAtDepth는 HomeDepth의 역");
        Assert.AreEqual(7f, LineRules.HomeDepth(LineRules.XAtDepth(7f, sign), sign), 1e-4f);
    }
}
