using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using NUnit.Framework;

// StageTableParser 검증. 열은 스펙 §10(stage, displayName, opponentName).
public class StageTableParserTests
{
    private const string Header = "stage,displayName,opponentName";

    [Test]
    public void 정상_행이_값_그대로_매핑된다()
    {
        List<StageInfo> stages = StageTableParser.Parse(Header + "\n1,첫 경기,수비 5백\n2,,\n");

        Assert.AreEqual(2, stages.Count);
        Assert.AreEqual(1, stages[0].Stage);
        Assert.AreEqual("첫 경기", stages[0].DisplayName);
        Assert.AreEqual("수비 5백", stages[0].OpponentName);
        Assert.AreEqual("", stages[1].DisplayName, "빈칸 허용");
    }

    [Test]
    public void stage_중복은_행_번호와_함께_예외()
    {
        var ex = Assert.Throws<FormatException>(() => StageTableParser.Parse(Header + "\n1,a,b\n1,c,d\n"));
        StringAssert.Contains("3행", ex.Message);
        StringAssert.Contains("중복", ex.Message);
    }

    [Test]
    public void stage_0은_예외()
    {
        var ex = Assert.Throws<FormatException>(() => StageTableParser.Parse(Header + "\n0,a,b\n"));
        StringAssert.Contains("stage", ex.Message);
    }

    [Test]
    public void 숫자가_깨진_행은_행번호를_알려준다()
    {
        var ex = Assert.Throws<FormatException>(() => StageTableParser.Parse(Header + "\n1,a,b\nx,c,d\n"));
        StringAssert.Contains("3행", ex.Message);
    }

    [Test]
    public void 열이_모자란_행은_예외()
    {
        Assert.Throws<FormatException>(() => StageTableParser.Parse(Header + "\n1,a\n"));
    }

    [Test]
    public void 헤더_불일치와_빈_텍스트는_예외()
    {
        Assert.Throws<FormatException>(() => StageTableParser.Parse("stage,name,enemy\n1,a,b\n"));
        Assert.Throws<FormatException>(() => StageTableParser.Parse(""));
        Assert.Throws<FormatException>(() => StageTableParser.Parse(Header + "\n"), "헤더만 = 데이터 없음 (스테이지 표는 비면 안 된다)");
    }

    [Test]
    public void 실제_Resources_CSV가_스펙0절_스테이지_5와_일치한다()
    {
        string csv = File.ReadAllText("Assets/Resources/Tables/StageTable.csv");
        List<StageInfo> stages = StageTableParser.Parse(csv);

        Assert.AreEqual(5, stages.Count, "첫 출시 스테이지 5");
        for (int i = 0; i < stages.Count; i++)
        {
            Assert.AreEqual(i + 1, stages[i].Stage, $"{i + 1}번째 행의 stage");
            Assert.IsNotEmpty(stages[i].OpponentName, $"스테이지 {i + 1} 상대 이름");
        }
    }
}
