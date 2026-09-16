using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using NUnit.Framework;

// PlayerTableParser 검증. ①정상 매핑(GK 3열 포함) ②실제 Resources CSV 전건 대조(역할 8종·총점 300) ③헤더 불일치 ④빈 텍스트
// ⑤깨진 숫자(행 번호) ⑥중복 roleId(대소문자 무시) ⑦열 부족 ⑧범위 검증 경계값 ⑨총점 불일치
public class PlayerTableParserTests
{
    private const string ValidHeader =
        "roleId,speed,stamina,pass,shot,tackle,positioning,reflexes,handling,diving,pushUp,pressRange,shotBias,passLength,width,lineHeight,displayName,description,icon";

    private const string StRow = "ST,60,45,40,90,20,45,0,0,0,25,8,0.3,15,10,0,스트라이커,,";
    private const string GkRow = "GK,30,30,30,5,10,15,80,60,40,0,3,0.9,25,0,0,골키퍼,,";

    private const string ValidCsv = ValidHeader + "\n" + StRow + "\n" + GkRow + "\n";

    [Test]
    public void 정상CSV_값이_그대로_매핑된다()
    {
        List<PlayerStats> roles = PlayerTableParser.Parse(ValidCsv);

        Assert.AreEqual(2, roles.Count);
        PlayerStats st = roles[0];
        Assert.AreEqual("ST", st.RoleId);
        Assert.AreEqual(60, st.Speed);
        Assert.AreEqual(45, st.Stamina);
        Assert.AreEqual(40, st.Pass);
        Assert.AreEqual(90, st.Shot);
        Assert.AreEqual(20, st.Tackle);
        Assert.AreEqual(45, st.Positioning);
        Assert.AreEqual(0, st.Reflexes, "필드 플레이어는 GK 스탯 0 허용");
        Assert.AreEqual(0, st.Handling);
        Assert.AreEqual(0, st.Diving);
        PlayerStats gk = roles[1];
        Assert.AreEqual(80, gk.Reflexes);
        Assert.AreEqual(60, gk.Handling);
        Assert.AreEqual(40, gk.Diving);
        Assert.AreEqual(25f, st.PushUp);
        Assert.AreEqual(8f, st.PressRange);
        Assert.AreEqual(0.3f, st.ShotBias);
        Assert.AreEqual(15f, st.PassLength);
        Assert.AreEqual(10f, st.Width);
        Assert.AreEqual(0f, st.LineHeight);
        Assert.AreEqual("스트라이커", st.DisplayName);
        Assert.AreEqual("", st.Description, "빈칸 허용. 3주차에 채움");
        Assert.AreEqual(PlayerStats.TotalPoints, st.BuildTotal);
    }

    [Test]
    public void 실제_Resources_CSV가_역할_8종이고_전부_총점_300이다()
    {
        string csv = File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv");
        List<PlayerStats> roles = PlayerTableParser.Parse(csv);

        string[] expected = { "GK", "CB", "FB", "DM", "CM", "AM", "W", "ST" };   // 스펙 §4-2 순서
        Assert.AreEqual(expected.Length, roles.Count, "역할 8종");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], roles[i].RoleId, $"{i + 1}번째 역할");
            Assert.AreEqual(PlayerStats.TotalPoints, roles[i].BuildTotal, $"{roles[i].RoleId} 총점");
        }
    }

    [Test]
    public void 헤더가_스키마와_다르면_예외()
    {
        string csv = ValidHeader.Replace("shot,", "SHOT오타,") + "\n" + StRow + "\n";   // shotBias는 건드리지 않는다

        var ex = Assert.Throws<FormatException>(() => PlayerTableParser.Parse(csv));
        StringAssert.Contains("헤더", ex.Message);
    }

    [Test]
    public void 빈_텍스트면_예외()
    {
        Assert.Throws<FormatException>(() => PlayerTableParser.Parse(""));
        Assert.Throws<FormatException>(() => PlayerTableParser.Parse("   \n  "));
    }

    [Test]
    public void 헤더만_있으면_예외()
    {
        var ex = Assert.Throws<FormatException>(() => PlayerTableParser.Parse(ValidHeader + "\n"));
        StringAssert.Contains("데이터 행이 없다", ex.Message, "역할 표는 비면 안 된다. 편성 CSV와 다른 점");
    }

    [Test]
    public void 숫자가_깨진_행은_행번호를_알려준다()
    {
        string csv = ValidHeader + "\n" + StRow + "\n" +
                     "GK,30,30,30,abc,10,15,80,60,40,0,3,0.9,25,0,0,골키퍼,,\n";   // 3행 shot 깨짐

        var ex = Assert.Throws<FormatException>(() => PlayerTableParser.Parse(csv));
        StringAssert.Contains("3행", ex.Message);
    }

    [Test]
    public void 열이_모자란_행은_예외()
    {
        string csv = ValidHeader + "\n" + StRow + "\n" + "GK,30,40\n";
        Assert.Throws<FormatException>(() => PlayerTableParser.Parse(csv));
    }

    [Test]
    public void 중복_roleId는_대소문자를_무시하고_행번호를_알려준다()
    {
        string csv = ValidHeader + "\n" + StRow + "\n" +
                     "st,60,45,40,90,20,45,0,0,0,25,8,0.3,15,10,0,,,\n";   // 3행 = 2행과 대소문자만 다른 중복

        var ex = Assert.Throws<FormatException>(() => PlayerTableParser.Parse(csv));
        StringAssert.Contains("3행", ex.Message);
        StringAssert.Contains("중복", ex.Message);
    }

    // 범위 검증 경계값. 총점 고정과 다이얼 정의역이 조용히 무너지는 값을 로드에서 막는지
    [TestCase("ST,0,105,40,90,20,45,0,0,0,25,8,0.3,15,10,0,,,",   "speed")]        // 스탯 0 (합계는 300)
    [TestCase("ST,60,45,40,90,20,46,0,0,0,25,8,0.3,15,10,0,,,",   "합계")]         // 총점 301
    [TestCase("ST,60,45,40,90,20,45,0,0,0,25,0,0.3,15,10,0,,,",   "pressRange")]   // 압박 거리 0
    [TestCase("ST,60,45,40,90,20,45,0,0,0,25,8,1.5,15,10,0,,,",   "shotBias")]     // 확률 밖
    [TestCase("ST,60,45,40,90,20,45,0,0,0,25,8,0.3,0,10,0,,,",    "passLength")]   // 패스 길이 0
    [TestCase(",60,45,40,90,20,45,0,0,0,25,8,0.3,15,10,0,,,",     "roleId")]       // id 공백
    [TestCase("ST,60,45,40,90,20,45,0,0,0,-1,8,0.3,15,10,0,,,",   "pushUp")]       // 전진 폭 음수
    [TestCase("ST,60,45,40,90,20,45,-1,1,0,25,8,0.3,15,10,0,,,",   "reflexes")]     // GK 스탯 음수(합계는 300)
    public void 범위를_벗어난_값은_필드명과_행번호를_알려준다(string badRow, string fieldName)
    {
        string csv = ValidHeader + "\n" + badRow + "\n";

        var ex = Assert.Throws<FormatException>(() => PlayerTableParser.Parse(csv));
        StringAssert.Contains("2행", ex.Message);
        StringAssert.Contains(fieldName, ex.Message);
    }
}
