using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.Tactics;
using NUnit.Framework;

// 팀 생성 데이터 3종(플랜 09-25 칸): 포메이션 템플릿·이름 표·부 규칙. 파서 검증 + 실제 Resources CSV 전건 대조
public class TeamGenerationDataTests
{
    private const string FormHeader = "formationId,slot,roleId,posX,posZ,posX2,posZ2";

    private static string Eleven(string id, string overrideSlot3 = "3,CB,-36,7,-41,7")
    {
        return FormHeader + $"\n{id},1,GK,-48,0,-48,0\n{id},2,CB,-36,-7,-41,-7\n{id},{overrideSlot3}\n{id},4,FB,-35,-22,-40,-22\n{id},5,FB,-35,22,-40,22\n{id},6,CM,-20,-8,-25,-8\n{id},7,CM,-20,8,-25,8\n{id},8,W,-18,-24,-23,-24\n{id},9,W,-18,24,-23,24\n{id},10,ST,-8,-6,-13,-6\n{id},11,ST,-8,6,-13,6\n";
    }

    [Test]
    public void 포메이션_템플릿은_11슬롯_GK1_자기_진영_간격을_검증한다()
    {
        List<FormationTemplate> ok = FormationTemplateParser.Parse(Eleven("4-4-2"));
        Assert.AreEqual(1, ok.Count);
        Assert.AreEqual(11, ok[0].Slots.Count);
        Assert.AreEqual("GK", ok[0].Slots[0].RoleId);

        StringAssert.Contains("11", Assert.Throws<FormatException>(() => FormationTemplateParser.Parse(FormHeader + "\nx,1,GK,-48,0,-48,0\n")).Message);
        StringAssert.Contains("GK", Assert.Throws<FormatException>(() => FormationTemplateParser.Parse(Eleven("x", "3,GK,-36,7,-41,7"))).Message);
        StringAssert.Contains("자기 진영", Assert.Throws<FormatException>(() => FormationTemplateParser.Parse(Eleven("x", "3,CB,5,7,0,7"))).Message);
        StringAssert.Contains("간격", Assert.Throws<FormatException>(() => FormationTemplateParser.Parse(Eleven("x", "3,CB,-36,-7.5,-41,-7.5"))).Message);
        StringAssert.Contains("roleId", Assert.Throws<FormatException>(() => FormationTemplateParser.Parse(Eleven("x", "3,LB,-36,7,-41,7"))).Message);
    }

    [Test]
    public void 이름_표는_도시와_접미를_나누고_중복을_거른다()
    {
        TeamNameTable t = TeamNameTable.Parse("kind,value\ncity,서울\ncity,부산\nsuffix,FC\n");
        Assert.AreEqual(2, t.Cities.Count);
        Assert.AreEqual("부산 FC", t.Compose(1, 0));
        Assert.AreEqual("서울 FC", t.Compose(2, 5), "인덱스는 순환");
        Assert.Throws<FormatException>(() => TeamNameTable.Parse("kind,value\ncity,서울\ncity,서울\nsuffix,FC\n"));
        Assert.Throws<FormatException>(() => TeamNameTable.Parse("kind,value\ncity,서울\n"));
    }

    [Test]
    public void 부_규칙은_1_4부_전부_있고_총점이_상위_부일수록_크며_경기_수는_팀_수_빼기_1이다()
    {
        const string header = "tier,teams,matches,totalPoints,formations,presets,dialVariance,dualPositions,rebuild";
        List<TierRule> rules = TierRuleParser.Parse(header + "\n4,6,5,260,4-4-2,balanced,0,0,0\n3,8,7,275,4-4-2|4-3-3,balanced|counter,1,0,0\n2,10,9,290,4-4-2,balanced,2,1,0\n1,12,11,300,4-4-2,balanced,2,1,1\n");
        Assert.AreEqual(1, rules[0].Tier, "1부부터 오름차순");
        Assert.AreEqual(2, rules[2].Formations.Count, "3부 포메이션 2종(| 구분)");
        Assert.AreEqual("counter", rules[2].Presets[1]);
        Assert.IsTrue(rules[0].Rebuild);
        Assert.IsFalse(rules[3].DualPositions);

        StringAssert.Contains("matches", Assert.Throws<FormatException>(() => TierRuleParser.Parse(header + "\n4,6,6,260,4-4-2,balanced,0,0,0\n3,8,7,275,a,b,1,0,0\n2,10,9,290,a,b,2,1,0\n1,12,11,300,a,b,2,1,1\n")).Message);
        StringAssert.Contains("총점", Assert.Throws<FormatException>(() => TierRuleParser.Parse(header + "\n4,6,5,300,a,b,0,0,0\n3,8,7,275,a,b,1,0,0\n2,10,9,290,a,b,2,1,0\n1,12,11,300,a,b,2,1,1\n")).Message);
        StringAssert.Contains("전부", Assert.Throws<FormatException>(() => TierRuleParser.Parse(header + "\n4,6,5,260,a,b,0,0,0\n")).Message);
    }

    [Test]
    public void 실제_Resources_CSV_전건_대조_템플릿_6종_부_4개_이름_표()
    {
        List<FormationTemplate> forms = FormationTemplateParser.Parse(File.ReadAllText("Assets/Resources/Tables/FormationTemplates.csv"));
        Assert.AreEqual(6, forms.Count, "4-4-2·4-3-3·5-3-2·3-5-2·4-2-3-1·3-4-3");
        foreach (FormationTemplate f in forms) { Assert.AreEqual(11, f.Slots.Count, f.Id); }

        List<TierRule> tiers = TierRuleParser.Parse(File.ReadAllText("Assets/Resources/Tables/TierRules.csv"));
        Assert.AreEqual(new[] { 12, 10, 8, 6 }, new[] { tiers[0].Teams, tiers[1].Teams, tiers[2].Teams, tiers[3].Teams }, "1부 12 → 4부 6");
        Assert.AreEqual(new[] { 300, 290, 275, 260 }, new[] { tiers[0].TotalPoints, tiers[1].TotalPoints, tiers[2].TotalPoints, tiers[3].TotalPoints });

        // 교차 검증: 부 규칙이 가리키는 포메이션·프리셋 id가 실재한다
        var formIds = new HashSet<string>(forms.ConvertAll(f => f.Id));
        var presetIds = new HashSet<string>(TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).ConvertAll(p => p.PresetId));
        foreach (TierRule r in tiers)
        {
            foreach (string f in r.Formations) { Assert.IsTrue(formIds.Contains(f), $"{r.Tier}부 포메이션 {f}"); }
            foreach (string p in r.Presets) { Assert.IsTrue(presetIds.Contains(p), $"{r.Tier}부 프리셋 {p}"); }
        }

        TeamNameTable names = TeamNameTable.Parse(File.ReadAllText("Assets/Resources/Tables/TeamNames.csv"));
        Assert.GreaterOrEqual(names.Cities.Count, 12, "1부 12팀 이름이 안 겹치게");
        Assert.GreaterOrEqual(names.Suffixes.Count, 2);
    }
}
