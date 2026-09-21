using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Tactics;
using NUnit.Framework;

// TeamTacticsParser 검증(전술-기획.md, 09-17). ①서드 3열이 배열로 접힘 ②실제 CSV 스타일 카드 4장 전건 대조 ③값 정의역 0~2 ④역습 문턱
public class TeamTacticsParserTests
{
    private const string Header =
        "presetId,displayName,description,passStyleOwn,passStyleMid,passStyleOpp,passRiskOwn,passRiskMid,passRiskOpp,tempoOwn,tempoMid,tempoOpp,widthOwn,widthMid,widthOpp,pressStartOwn,pressStartMid,pressStartOpp,counter,counterPress,gkDistribution,mentality";
    private const string Row = "t,테스트,설명,0,1,2,0,1,2,0,1,2,2,1,0,1,1,0,2,1,0,1";

    [Test]
    public void 서드별_열_3개가_배열로_접힌다()
    {
        List<TeamTactics> list = TeamTacticsParser.Parse(Header + "\n" + Row + "\n");
        TeamTactics t = list[0];

        Assert.AreEqual("t", t.PresetId);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, t.PassStyle);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, t.PassRisk);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, t.Tempo);
        CollectionAssert.AreEqual(new[] { 2, 1, 0 }, t.Width);
        CollectionAssert.AreEqual(new[] { 1, 1, 0 }, t.PressStart);
        Assert.AreEqual(2, t.Counter);
        Assert.AreEqual(1, t.CounterPress);
        Assert.AreEqual(0, t.GkDistribution);
        Assert.AreEqual(1, t.Mentality);
        Assert.AreEqual(2, t.PassStyle[(int)Third.Opponent], "Third enum이 배열 인덱스");
    }

    [Test]
    public void 실제_Resources_CSV는_스타일_카드_4장이고_값이_전부_0에서_2다()
    {
        string csv = File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv");
        List<TeamTactics> list = TeamTacticsParser.Parse(csv);

        string[] expected = { "buildup", "balanced", "counter", "pressing" };
        Assert.AreEqual(expected.Length, list.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], list[i].PresetId);
        }

        TeamTactics counter = list[2];
        Assert.AreEqual(2, counter.Counter, "역습 축구는 역습 적극");
        Assert.AreEqual(0, counter.PressStart[(int)Third.Opponent], "역습 축구는 상대 진영에서 안 달려든다(끌어들임)");
        TeamTactics pressing = list[3];
        Assert.AreEqual(2, pressing.PressStart[(int)Third.Opponent], "압박 축구는 상대 진영부터 달려든다");
        Assert.AreEqual(2, pressing.CounterPress);
    }

    [Test]
    public void 역습_문턱은_성향에서_나온다()
    {
        Assert.AreEqual(-1, new TeamTactics { Counter = 0 }.CounterThreshold, "안 함");
        Assert.AreEqual(3, new TeamTactics { Counter = 1 }.CounterThreshold, "상황 봐서 = 공 앞 상대 수비 3명 이하");
        Assert.AreEqual(4, new TeamTactics { Counter = 2 }.CounterThreshold, "적극 = 4명 이하");
    }

    [TestCase("t,,,3,1,2,0,1,2,0,1,2,2,1,0,1,1,0,2,1,0,1", "passStyleOwn")]
    [TestCase("t,,,0,1,2,0,1,2,0,1,2,2,1,0,1,1,0,-1,1,0,1", "counter")]
    [TestCase(",,,0,1,2,0,1,2,0,1,2,2,1,0,1,1,0,2,1,0,1", "presetId")]
    public void 정의역_밖이면_필드명과_행번호를_알려준다(string badRow, string field)
    {
        var ex = Assert.Throws<FormatException>(() => TeamTacticsParser.Parse(Header + "\n" + badRow + "\n"));
        StringAssert.Contains("2행", ex.Message);
        StringAssert.Contains(field, ex.Message);
    }

    [Test]
    public void 중복_presetId는_예외()
    {
        Assert.Throws<FormatException>(() => TeamTacticsParser.Parse(Header + "\n" + Row + "\n" + Row + "\n"));
    }
}
