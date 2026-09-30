using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using NUnit.Framework;

// PlayerTableParser 검증. ①정상 매핑(변형·노출·포커스·다이얼 11) ②실제 Resources CSV 전건 대조(자리 8 × 변형 54·총점 300·노출 17) ③헤더 불일치 ④빈 텍스트
// ⑤깨진 숫자(행 번호) ⑥중복 roleId(대소문자 무시) ⑦열 부족 ⑧범위 검증 경계값 ⑨총점 불일치
public class PlayerTableParserTests
{
    private const string ValidHeader =
        "roleId,variantId,exposed,focus,speed,stamina,pass,shot,tackle,positioning,reflexes,handling,diving,pushUp,pressRange,shotBias,passLength,width,lineHeight,roamRadius,passRisk,dribble,holdUp,gkRushRadius,displayName,description,icon";

    private const string StRow = "ST,st_poacher,true,2,60,45,40,90,20,45,0,0,0,30,8,0.2,15,10,0,3,0.5,0.5,0.1,0,포처,박스 안에서 골만 노린다,";
    private const string GkRow = "GK,gk_standard,true,1,30,30,30,5,10,15,80,60,40,0,3,0.9,25,0,0,2,0.3,0,0.5,12,골키퍼,박스 안에서 골문을 지킨다,";

    private const string ValidCsv = ValidHeader + "\n" + StRow + "\n" + GkRow + "\n";

    [Test]
    public void 정상CSV_값이_그대로_매핑된다()
    {
        List<PlayerStats> roles = PlayerTableParser.Parse(ValidCsv);

        Assert.AreEqual(2, roles.Count);
        PlayerStats st = roles[0];
        Assert.AreEqual("ST", st.RoleId);
        Assert.AreEqual("st_poacher", st.VariantId);
        Assert.IsTrue(st.Exposed);
        Assert.AreEqual(2, st.Focus);
        Assert.AreEqual(60, st.Speed);
        Assert.AreEqual(90, st.Shot);
        Assert.AreEqual(0, st.Reflexes, "필드 플레이어는 GK 스탯 0 허용");
        Assert.AreEqual(30f, st.PushUp);
        Assert.AreEqual(8f, st.PressRange);
        Assert.AreEqual(0.2f, st.ShotBias);
        Assert.AreEqual(3f, st.RoamRadius);
        Assert.AreEqual(0.5f, st.PassRisk);
        Assert.AreEqual(0.5f, st.Dribble);
        Assert.AreEqual(0.1f, st.HoldUp);
        Assert.AreEqual(0f, st.GkRushRadius);
        Assert.AreEqual("포처", st.DisplayName);
        Assert.AreEqual(PlayerStats.TotalPoints, st.BuildTotal);

        PlayerStats gk = roles[1];
        Assert.AreEqual("gk_standard", gk.VariantId);
        Assert.AreEqual(80, gk.Reflexes);
        Assert.AreEqual(12f, gk.GkRushRadius);
    }

    [Test]
    public void 같은_자리의_노출된_역할만_표_순서대로_고른다()
    {
        // 전술 화면의 역할 목록(09-30). 잠긴 역할과 다른 자리는 빠진다. SeasonState.SetPlayerRole이 받는 역할과 같은 집합이어야 한다
        List<PlayerStats> roles = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        List<PlayerStats> keepers = PlayerTableLookup.ExposedVariants(roles, "GK");
        Assert.AreEqual(2, keepers.Count);
        Assert.AreEqual("gk_standard", keepers[0].VariantId, "표 순서");
        Assert.AreEqual("gk_sweeper", keepers[1].VariantId);
        Assert.AreEqual(2, PlayerTableLookup.ExposedVariants(roles, "gk").Count, "자리 이름은 대소문자를 가리지 않는다");
        Assert.AreEqual(0, PlayerTableLookup.ExposedVariants(roles, "XX").Count, "없는 자리");
        foreach (PlayerStats r in PlayerTableLookup.ExposedVariants(roles, "ST"))
        {
            Assert.IsTrue(r.Exposed);
            Assert.AreEqual("ST", r.RoleId);
        }
    }

    [Test]
    public void 줄_수_있는_역할은_같은_자리이고_노출된_역할이다()
    {
        // 역할 목록·역할 변경·시즌 이월이 같이 쓰는 조건(09-30 리뷰: 세 곳이 각자 적어 두면 목록에 보이는 역할과 받아 주는 역할이 갈릴 수 있다)
        var exposed = new PlayerStats { RoleId = "ST", VariantId = "st_a", Exposed = true };
        var locked = new PlayerStats { RoleId = "ST", VariantId = "st_b", Exposed = false };
        Assert.IsTrue(PlayerTableLookup.IsAssignable(exposed, "ST"));
        Assert.IsTrue(PlayerTableLookup.IsAssignable(exposed, "st"), "자리 이름은 대소문자를 가리지 않는다");
        Assert.IsFalse(PlayerTableLookup.IsAssignable(exposed, "W"), "다른 자리");
        Assert.IsFalse(PlayerTableLookup.IsAssignable(locked, "ST"), "잠긴 역할");
    }

    [Test]
    public void 실제_Resources_CSV는_자리_8종_변형_54개_전부_총점_300이고_노출_17개다()
    {
        // 역할 마스터(전술-기획.md 3-1, 09-17): FM26 중심 + FC26·현실 용어. 전부 넣고 구현하며 뺀다
        string csv = File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv");
        List<PlayerStats> roles = PlayerTableParser.Parse(csv);

        string[] positions = { "GK", "CB", "FB", "DM", "CM", "AM", "W", "ST" };
        Assert.AreEqual(54, roles.Count, "변형 합계");
        Assert.AreEqual(17, roles.FindAll(r => r.Exposed).Count, "1차 노출");
        foreach (string pos in positions)
        {
            List<PlayerStats> variants = roles.FindAll(r => r.RoleId == pos);
            Assert.GreaterOrEqual(variants.Count, 4, $"{pos} 변형 수");
            Assert.GreaterOrEqual(variants.FindAll(r => r.Exposed).Count, 2, $"{pos} 1차 노출 2개 이상");
            for (int i = 1; i < variants.Count; i++)
            {
                Assert.AreEqual(variants[0].BuildTotal, variants[i].BuildTotal, $"{pos} 변형은 빌드가 같다");
                Assert.AreEqual(variants[0].Speed, variants[i].Speed, $"{pos} 변형은 빌드가 같다(speed)");
            }
        }
        foreach (PlayerStats r in roles)
        {
            Assert.AreEqual(PlayerStats.TotalPoints, r.BuildTotal, $"{r.VariantId} 총점");
            bool isGk = r.RoleId == "GK";
            Assert.AreEqual(isGk, r.GkRushRadius > 0f, $"{r.VariantId} GK 출격 반경은 GK만");
            Assert.AreEqual(isGk, r.Reflexes > 0, $"{r.VariantId} GK 스탯은 GK만");
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
                     "GK,gk_standard,true,1,30,30,30,abc,10,15,80,60,40,0,3,0.9,25,0,0,2,0.3,0,0.5,12,골키퍼,,\n";   // 3행 shot 깨짐

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
    public void 중복_variantId는_대소문자를_무시하고_행번호를_알려준다()
    {
        string csv = ValidHeader + "\n" + StRow + "\n" +
                     "ST,ST_POACHER,true,2,60,45,40,90,20,45,0,0,0,30,8,0.2,15,10,0,3,0.5,0.5,0.1,0,,,\n";   // 3행 = 2행과 대소문자만 다른 중복

        var ex = Assert.Throws<FormatException>(() => PlayerTableParser.Parse(csv));
        StringAssert.Contains("3행", ex.Message);
        StringAssert.Contains("중복", ex.Message);
    }

    // 범위 검증 경계값. 총점 고정과 다이얼 정의역이 조용히 무너지는 값을 로드에서 막는지
    [TestCase("ST,st_poacher,true,2,0,105,40,90,20,45,0,0,0,30,8,0.2,15,10,0,3,0.5,0.5,0.1,0,,,",   "speed")]        // 스탯 0 (합계는 300)
    [TestCase("ST,st_poacher,true,2,60,45,40,90,20,46,0,0,0,30,8,0.2,15,10,0,3,0.5,0.5,0.1,0,,,",   "합계")]         // 총점 301
    [TestCase("ST,st_poacher,true,2,60,45,40,90,20,45,0,0,0,30,0,0.2,15,10,0,3,0.5,0.5,0.1,0,,,",   "pressRange")]   // 압박 거리 0
    [TestCase("ST,st_poacher,true,2,60,45,40,90,20,45,0,0,0,30,8,1.5,15,10,0,3,0.5,0.5,0.1,0,,,",   "shotBias")]     // 확률 밖
    [TestCase("ST,st_poacher,true,2,60,45,40,90,20,45,0,0,0,30,8,0.2,0,10,0,3,0.5,0.5,0.1,0,,,",    "passLength")]   // 패스 길이 0
    [TestCase(",st_poacher,true,2,60,45,40,90,20,45,0,0,0,30,8,0.2,15,10,0,3,0.5,0.5,0.1,0,,,",     "roleId")]       // 자리 공백
    [TestCase("ST,,true,2,60,45,40,90,20,45,0,0,0,30,8,0.2,15,10,0,3,0.5,0.5,0.1,0,,,",             "variantId")]    // 변형 공백
    [TestCase("ST,st_poacher,true,2,60,45,40,90,20,45,0,0,0,-1,8,0.2,15,10,0,3,0.5,0.5,0.1,0,,,",   "pushUp")]       // 전진 폭 음수
    [TestCase("ST,st_poacher,true,2,60,45,40,90,20,45,-1,1,0,30,8,0.2,15,10,0,3,0.5,0.5,0.1,0,,,",  "reflexes")]     // GK 스탯 음수(합계는 300)
    [TestCase("ST,st_poacher,true,2,60,45,40,90,20,45,0,0,0,30,8,0.2,15,10,0,3,1.2,0.5,0.1,0,,,",   "passRisk")]     // 리스크 1 초과
    [TestCase("ST,st_poacher,true,3,60,45,40,90,20,45,0,0,0,30,8,0.2,15,10,0,3,0.5,0.5,0.1,0,,,",   "focus")]        // 포커스 3
    public void 범위를_벗어난_값은_필드명과_행번호를_알려준다(string badRow, string fieldName)
    {
        string csv = ValidHeader + "\n" + badRow + "\n";

        var ex = Assert.Throws<FormatException>(() => PlayerTableParser.Parse(csv));
        StringAssert.Contains("2행", ex.Message);
        StringAssert.Contains(fieldName, ex.Message);
    }
}
