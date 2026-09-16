using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using NUnit.Framework;

// StageCompositionParser 검증. 이 형식은 상대 편성과 플레이어 세팅 저장이 공유한다. 읽기·쓰기가 한 스키마를 보는지(왕복)까지 본다.
public class StageCompositionParserTests
{
    private const string Header = "stage,side,kind,id,count,posX,posZ";

    [Test]
    public void 정상_행이_값_그대로_매핑되고_팀이_계산된다()
    {
        List<StageEntry> rows = StageCompositionParser.Parse(Header + "\n1,enemy,player,GK,1,50,0\n1,player,player,ST,1,-10,0\n");

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(1, rows[0].Stage);
        Assert.AreEqual("enemy", rows[0].Side);
        Assert.AreEqual("player", rows[0].Kind);
        Assert.AreEqual("GK", rows[0].Id);
        Assert.AreEqual(1, rows[0].Count);
        Assert.AreEqual(50f, rows[0].PosX);
        Assert.AreEqual(0f, rows[0].PosZ);
        Assert.AreEqual(1, rows[0].Team, "enemy → 팀1");
        Assert.AreEqual(0, rows[1].Team, "player → 팀0");
    }

    [Test]
    public void 헤더만_있으면_빈_목록()
    {
        // "아직 세팅 없음"이 정상 상태. 세팅 저장 초기값
        Assert.AreEqual(0, StageCompositionParser.Parse(Header + "\n").Count);
    }

    [Test]
    public void 빈_텍스트는_예외()
    {
        Assert.Throws<FormatException>(() => StageCompositionParser.Parse(""));
    }

    [Test]
    public void 헤더_불일치는_예외()
    {
        var ex = Assert.Throws<FormatException>(() => StageCompositionParser.Parse("stage,team,kind,id,count,x,z\n1,0,player,GK,1,0,0\n"));
        StringAssert.Contains("헤더가 스키마와 다르다", ex.Message);
    }

    [Test]
    public void side가_player_enemy_밖이면_행_번호와_함께_예외()
    {
        var ex = Assert.Throws<FormatException>(() => StageCompositionParser.Parse(Header + "\n1,enemy,player,GK,1,50,0\n1,red,player,GK,1,50,2\n"));
        StringAssert.Contains("3행", ex.Message);
        StringAssert.Contains("side", ex.Message);
    }

    [Test]
    public void kind가_player_밖이면_예외()
    {
        var ex = Assert.Throws<FormatException>(() => StageCompositionParser.Parse(Header + "\n1,enemy,unit,GK,1,50,0\n"));
        StringAssert.Contains("kind", ex.Message);
    }

    [TestCase("1,enemy,player,GK,0,50,0", "count")]
    [TestCase("0,enemy,player,GK,1,50,0", "stage")]
    [TestCase("1,enemy,player,,1,50,0", "id")]
    public void 범위를_벗어난_값은_필드명을_알려준다(string badRow, string field)
    {
        var ex = Assert.Throws<FormatException>(() => StageCompositionParser.Parse(Header + "\n" + badRow + "\n"));
        StringAssert.Contains(field, ex.Message);
    }

    [Test]
    public void 쓰고_다시_읽으면_같다()
    {
        var original = new List<StageEntry>
        {
            new StageEntry { Stage = 1, Side = "player", Kind = "player", Id = "ST", Count = 1, PosX = -10f, PosZ = 2f },
            new StageEntry { Stage = 1, Side = "player", Kind = "player", Id = "GK", Count = 1, PosX = -50f, PosZ = -3.25f },
        };

        string csv = StageCompositionParser.Serialize(original);
        Assert.IsFalse(csv.Contains("\r"), "줄바꿈은 \\n 고정. 플랫폼 따라 저장본이 달라지면 비교가 깨진다");
        StringAssert.StartsWith(Header, csv);

        List<StageEntry> back = StageCompositionParser.Parse(csv);
        Assert.AreEqual(original.Count, back.Count);
        for (int i = 0; i < original.Count; i++)
        {
            AssertEntry(original[i], back[i], i);
        }
    }

    private static void AssertEntry(StageEntry expected, StageEntry actual, int i)
    {
        Assert.AreEqual(expected.Stage, actual.Stage, $"[{i}].Stage");
        Assert.AreEqual(expected.Side, actual.Side, $"[{i}].Side");
        Assert.AreEqual(expected.Kind, actual.Kind, $"[{i}].Kind");
        Assert.AreEqual(expected.Id, actual.Id, $"[{i}].Id");
        Assert.AreEqual(expected.Count, actual.Count, $"[{i}].Count");
        Assert.AreEqual(expected.PosX, actual.PosX, $"[{i}].PosX");
        Assert.AreEqual(expected.PosZ, actual.PosZ, $"[{i}].PosZ");
    }

    [Test]
    public void 실제_Resources_CSV는_스테이지1에_ST와_GK가_있다()
    {
        // 1주차 리트머스 편성(ST 1 vs GK 1). 파일이 바뀌면 여기서 잡힌다
        string csv = File.ReadAllText("Assets/Resources/Tables/StageComposition.csv");
        List<StageEntry> rows = StageCompositionParser.Parse(csv).FindAll(r => r.Stage == 1);

        Assert.AreEqual(2, rows.Count, "스테이지 1은 ST 1명 vs GK 1명");
        Assert.AreEqual("ST", rows[0].Id);
        Assert.AreEqual(0, rows[0].Team);
        Assert.AreEqual("GK", rows[1].Id);
        Assert.AreEqual(1, rows[1].Team);
    }
}
