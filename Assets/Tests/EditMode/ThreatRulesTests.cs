using System;
using System.IO;
using Game.Core.Data;
using Game.Core.Match;
using NUnit.Framework;

// xT 격자(11-03 ⑤ xT): 이중선형 보간·공격 방향·폭 대칭, 운반·패스 위협 증가, ThreatTable.csv 파서와 실제 파일 모양
public class ThreatRulesTests
{
    // 열 값 = 열 번호 × 0.01, 행은 폭 가운데(3·4)가 +0.1: 계산이 손으로 되는 격자
    private static ThreatGrid Sample()
    {
        var cells = new float[ThreatGrid.Rows, ThreatGrid.Columns];
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            for (int c = 0; c < ThreatGrid.Columns; c++)
            {
                float center = (r == 3 || r == 4) ? 0.1f : 0f;
                cells[r, c] = c * 0.01f + center;
            }
        }
        return new ThreatGrid(cells);
    }

    [Test]
    public void 칸_중심에서는_그_칸_값이고_칸_사이는_선형으로_섞인다()
    {
        ThreatGrid grid = Sample();
        // 열 폭 105/12 = 8.75m. 열 0 중심 x = −52.5 + 4.375 = −48.125, 열 1 중심 = −39.375. 행 폭 68/8 = 8.5m: 행 3 중심 z = −4.25, 행 4 중심 z = +4.25
        Assert.AreEqual(0.00f + 0.1f, grid.Value(-48.125f, -4.25f, +1), 1e-5f);
        Assert.AreEqual(0.01f + 0.1f, grid.Value(-39.375f, 4.25f, +1), 1e-5f);
        Assert.AreEqual(0.005f + 0.1f, grid.Value(-43.75f, 0f, +1), 1e-5f, "열 0과 1 한가운데 + 폭 가운데(행 3·4 같은 값)");
        Assert.AreEqual(0.1f * 0.5f, grid.Value(-48.125f, -8.5f, +1), 1e-5f, "행 2(0)와 행 3(0.1) 한가운데");
    }

    [Test]
    public void 가장자리_바깥은_가장_가까운_칸_중심_값이다()
    {
        ThreatGrid grid = Sample();
        Assert.AreEqual(0.11f + 0.1f, grid.Value(52.5f, 0f, +1), 1e-5f, "골라인 위 중앙 = 열 11 값");
        Assert.AreEqual(0f, grid.Value(-52.5f, 34f, +1), 1e-5f, "자기 골라인 모서리 = 열 0, 행 7");
    }

    [Test]
    public void 팀1은_공격_방향_좌표로_읽는다()
    {
        ThreatGrid grid = Sample();
        Assert.AreEqual(grid.Value(30f, 5f, +1), grid.Value(-30f, 5f, -1), 1e-6f, "공격 방향으로 같은 깊이면 같은 값, 폭(z)은 그대로");
        Assert.Greater(grid.Value(30f, 0f, +1), grid.Value(-30f, 0f, +1), "팀0은 +X가 골 쪽");
        Assert.Greater(grid.Value(-30f, 0f, -1), grid.Value(30f, 0f, -1), "팀1은 −X가 골 쪽");
    }

    [Test]
    public void 폭_대칭_격자는_z_부호를_바꿔도_같다()
    {
        ThreatGrid grid = Sample();
        Assert.AreEqual(grid.Value(20f, 12f, +1), grid.Value(20f, -12f, +1), 1e-6f);
    }

    [Test]
    public void 운반_가치는_목표_값_빼기_지금_값이고_패스_가치는_성공_확률을_곱한다()
    {
        ThreatGrid grid = Sample();
        float now = grid.Value(0f, 0f, +1);
        float carry = ThreatRules.CarryGain(grid, 0f, 0f, 4f, 0f, +1);
        Assert.AreEqual(grid.Value(4f, 0f, +1) - now, carry, 1e-6f);

        float spot = grid.Value(40f, 0f, +1);
        Assert.AreEqual(0.5f * spot - now, ThreatRules.PassGain(grid, 0f, 0f, 40f, 0f, +1, 0.5f), 1e-6f);
        Assert.AreEqual(-now, ThreatRules.PassGain(grid, 0f, 0f, 40f, 0f, +1, 1f), 1e-6f, "위험 1이면 도착 값 0: 소유를 잃는 만큼 손해");
        Assert.AreEqual(spot - now, ThreatRules.PassGain(grid, 0f, 0f, 40f, 0f, +1, 0f), 1e-6f);
    }

    [Test]
    public void 격자_크기가_12열_8행이_아니면_예외다()
    {
        Assert.Throws<ArgumentException>(() => new ThreatGrid(new float[8, 11]));
        Assert.Throws<ArgumentException>(() => new ThreatGrid(new float[7, 12]));
    }

    private static string Csv(int skipColumn = -1, string extra = "")
    {
        string text = "# 설명 줄\ncolumn,row,threat\n";
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 12; c++)
            {
                if (c == skipColumn && r == 0) { continue; }
                text += $"{c},{r},{(c * 0.01f + r * 0.001f).ToString(System.Globalization.CultureInfo.InvariantCulture)}\n";
            }
        }
        return text + extra;
    }

    [Test]
    public void 파서는_주석_줄을_건너뛰고_96칸을_행과_열대로_읽는다()
    {
        ThreatGrid grid = ThreatTableParser.Parse(Csv());
        Assert.AreEqual(0.003f + 0.05f, grid.Cell(3, 5), 1e-5f);
        Assert.AreEqual(0.11f + 0.007f, grid.Cell(7, 11), 1e-5f);
    }

    [Test]
    public void 파서는_빠진_칸_중복_범위_밖_빈_텍스트를_행번호와_함께_막는다()
    {
        Assert.Throws<FormatException>(() => ThreatTableParser.Parse(""));
        var missing = Assert.Throws<FormatException>(() => ThreatTableParser.Parse(Csv(skipColumn: 4)));
        StringAssert.Contains("96칸", missing!.Message);
        var dup = Assert.Throws<FormatException>(() => ThreatTableParser.Parse(Csv(extra: "0,0,0.5\n")));
        StringAssert.Contains("두 번", dup!.Message);
        var range = Assert.Throws<FormatException>(() => ThreatTableParser.Parse("column,row,threat\n12,0,0.1\n"));
        StringAssert.Contains("column", range!.Message);
        var value = Assert.Throws<FormatException>(() => ThreatTableParser.Parse("column,row,threat\n0,0,1.5\n"));
        StringAssert.Contains("threat", value!.Message);
        var nan = Assert.Throws<FormatException>(() => ThreatTableParser.Parse("column,row,threat\n0,0,NaN\n"), "NaN은 범위 비교를 피해 가므로 따로 막는다");
        StringAssert.Contains("threat", nan!.Message);
        Assert.Throws<FormatException>(() => ThreatTableParser.Parse("col,row,threat\n0,0,0.1\n"), "헤더가 다르면");
    }

    [Test]
    public void 실제_ThreatTable_CSV는_골_앞_중앙이_최대고_폭_대칭이며_골에_가까울수록_크다()
    {
        ThreatGrid grid = ThreatTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/ThreatTable.csv"));
        float max = 0f;
        int maxRow = -1;
        int maxColumn = -1;
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            for (int c = 0; c < ThreatGrid.Columns; c++)
            {
                if (grid.Cell(r, c) <= max) { continue; }
                max = grid.Cell(r, c);
                maxRow = r;
                maxColumn = c;
            }
        }
        Assert.AreEqual(ThreatGrid.Columns - 1, maxColumn, "골 쪽 마지막 열");
        Assert.IsTrue(maxRow == 3 || maxRow == 4, "폭 가운데");
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            for (int c = 0; c < ThreatGrid.Columns; c++)
            {
                Assert.AreEqual(grid.Cell(r, c), grid.Cell(ThreatGrid.Rows - 1 - r, c), 1e-5f, $"폭 대칭 ({c},{r})");
            }
        }
        float own = 0f;
        float opp = 0f;
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            own += grid.Cell(r, 0);
            opp += grid.Cell(r, ThreatGrid.Columns - 1);
        }
        Assert.Greater(opp, own * 3f, "상대 골 앞 열이 우리 골 앞 열보다 훨씬 크다");
    }
}
