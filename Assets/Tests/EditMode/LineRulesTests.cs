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
    public void 백3_편성의_윙백은_뒷줄이_아니다()
    {
        // 3-5-2 FB posX2 −31(깊이 21.5) 대 CB −41(11.5): 10m 차. 현실의 윙백처럼 미드 줄로 분류돼 리시버 마크 1차 후보에서 빠진다
        float deepest = LineRules.HomeDepth(-41f, +1);
        Assert.IsFalse(LineRules.IsBackLine(LineRules.HomeDepth(-31f, +1), deepest), "3-5-2 윙백");
        Assert.IsFalse(LineRules.IsBackLine(LineRules.HomeDepth(-29f, +1), deepest), "3-4-3 윙백");
        Assert.IsTrue(LineRules.IsBackLine(LineRules.HomeDepth(-38f, +1), LineRules.HomeDepth(-42f, +1)), "5-3-2 FB는 4m 차라 뒷줄");
    }
}
