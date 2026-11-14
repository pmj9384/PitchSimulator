using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Placement;
using NUnit.Framework;

// 경기장 4종 1단계(11-06, 설계 실험/2026-11-05-설계-경기장-4종.md): Stadiums.csv·좌석 배치·관중 채움·경기장 주인. 화면 변화 없음
public class StadiumTests
{
    private static List<StadiumSpec> Real()
    {
        return StadiumSpecParser.Parse(File.ReadAllText("Assets/Resources/Tables/Stadiums.csv"));
    }

    [Test]
    public void 실제_Stadiums_csv는_부마다_한_행이고_1부로_갈수록_면과_단과_채움이_늘고_마모는_준다()
    {
        List<StadiumSpec> specs = Real();
        Assert.AreEqual(4, specs.Count);
        for (int i = 0; i < 4; i++) { Assert.AreEqual(i + 1, specs[i].Tier); }
        for (int i = 1; i < 4; i++)
        {
            StadiumSpec upper = specs[i - 1];
            StadiumSpec lower = specs[i];
            Assert.GreaterOrEqual(upper.Stands.Count, lower.Stands.Count, $"{upper.Tier}부 면");
            Assert.Greater(upper.Rows, lower.Rows, $"{upper.Tier}부 단");
            Assert.Greater(upper.FillPercent, lower.FillPercent, $"{upper.Tier}부 채움");
            Assert.Less(upper.Wear, lower.Wear, $"{upper.Tier}부 마모");
            Assert.GreaterOrEqual(upper.Stripe, lower.Stripe, $"{upper.Tier}부 줄무늬");
        }
        Assert.AreEqual(new[] { StandSide.Far }, specs[3].Stands, "4부 = 먼 쪽 한 면(본부석)");
    }

    [Test]
    public void TierRules의_경기장_id는_Stadiums_csv의_같은_부_행을_가리킨다()
    {
        List<TierRule> tiers = TierRuleParser.Parse(File.ReadAllText("Assets/Resources/Tables/TierRules.csv"));
        List<StadiumSpec> specs = Real();
        foreach (TierRule tier in tiers)
        {
            StadiumSpec spec = StadiumSpecParser.Find(specs, tier.Stadium);
            Assert.AreEqual(tier.Tier, spec.Tier, tier.Stadium);
        }
    }

    [Test]
    public void 잘못된_행은_행_번호와_함께_거부한다()
    {
        const string Header = "id,tier,stands,rows,rowDepth,rowHeight,baseHeight,roof,lightTowers,scoreboards,adBoards,fillPercent,wear,stripe,runoff\n";
        string ok = "a,1,far,1,0.8,0.4,1,,0,0,,10,0,0,3\nb,2,far,1,0.8,0.4,1,,0,0,,10,0,0,3\nc,3,far,1,0.8,0.4,1,,0,0,,10,0,0,3\n";
        Assert.DoesNotThrow(() => StadiumSpecParser.Parse(Header + ok + "d,4,far,1,0.8,0.4,1,,0,0,,10,0,0,3\n"));
        Assert.Throws<FormatException>(() => StadiumSpecParser.Parse(Header + ok + "d,4,top,1,0.8,0.4,1,,0,0,,10,0,0,3\n"), "모르는 면 이름");
        Assert.Throws<FormatException>(() => StadiumSpecParser.Parse(Header + ok + "d,4,far|far,1,0.8,0.4,1,,0,0,,10,0,0,3\n"), "같은 면 중복");
        Assert.Throws<FormatException>(() => StadiumSpecParser.Parse(Header + ok + "d,4,far,1,0.8,0.4,1,,0,0,,101,0,0,3\n"), "채움 100 초과");
        Assert.Throws<FormatException>(() => StadiumSpecParser.Parse(Header + ok + "c,4,far,1,0.8,0.4,1,,0,0,,10,0,0,3\n"), "id 중복");
        Assert.Throws<FormatException>(() => StadiumSpecParser.Parse(Header + ok), "4부 없음");
        Assert.Throws<FormatException>(() => StadiumSpecParser.Parse(Header + ok + "d,4,far,1,0.8,0.4,1,,0,0,,10,0,1.5,3\n"), "줄무늬 1 초과");
    }

    [Test]
    public void 좌석_수는_단_조각_길이를_좌석_간격으로_나눈_값의_합이고_통로_홈만큼_빠진다()
    {
        // 통로 전 4부 888 · 3부 2,688 · 2부 7,560 · 1부 11,460(설계 §1 약 900·2,800·6,300·13,600. 11-06 런오프 축소·2부 4면 뒤. 1부 차이는 코너 관중석이 없어서).
        // 11-14 통로 홈: 끊은 단마다 10석씩 빠진다(4부·3부 3단 = 30석, 2부·1부 1단 = 10석. 설계 실험/2026-11-12-설계-경기장-오브젝트.md 4-2 표와 같은 값)
        List<StadiumSpec> specs = Real();
        Assert.AreEqual(11450, StadiumLayout.Seats(specs[0]).Count, "1부");
        Assert.AreEqual(7550, StadiumLayout.Seats(specs[1]).Count, "2부");
        Assert.AreEqual(2658, StadiumLayout.Seats(specs[2]).Count, "3부");
        Assert.AreEqual(858, StadiumLayout.Seats(specs[3]).Count, "4부");
    }

    // 11-14 fix/tunnel-recess: 윗면이 통로 높이(2.4m) 이하인 앞쪽 단만 끊는다. 4부·3부는 기단 1m라 3단(1.4·1.8·2.2), 2부·1부는 기단 2m라 1단(2.4)
    [Test]
    public void 통로는_먼_쪽_중앙에서_윗면이_통로_높이_이하인_앞쪽_단만_끊는다()
    {
        int[] expectedCut = { 1, 1, 3, 3 };   // 1부·2부·3부·4부
        List<StadiumSpec> specs = Real();
        for (int t = 0; t < specs.Count; t++)
        {
            StadiumSpec spec = specs[t];
            Assert.IsTrue(StadiumLayout.HasTunnel(spec), $"{spec.Id} 먼 쪽 관중석이 있어 통로가 있다");
            TunnelSpot tunnel = StadiumLayout.TunnelOf(spec);
            Assert.AreEqual(expectedCut[t], tunnel.CutRows, $"{spec.Id} 끊은 단 수");
            Assert.AreEqual(StadiumLayout.TunnelWidth * 0.5f + StadiumLayout.TunnelSideMargin, tunnel.HalfWidth, 1e-4f, $"{spec.Id} 반폭");
            Assert.AreEqual(FieldBounds.HalfWidth + spec.Runoff - StadiumLayout.BaseFrontMargin, tunnel.MouthZ, 1e-4f, $"{spec.Id} 입구 = 기단 앞면");
            Assert.AreEqual(FieldBounds.HalfWidth + spec.Runoff + tunnel.CutRows * spec.RowDepth, tunnel.BackZ, 1e-4f, $"{spec.Id} 뒤 벽 = 끊지 않은 첫 단 앞면");
            int pieces = 0;
            foreach (StandRow row in StadiumLayout.Rows(spec))
            {
                if (row.Side != StandSide.Far) { continue; }

                bool cut = row.Index < tunnel.CutRows;
                Assert.AreEqual(cut, row.Height <= StadiumLayout.TunnelHeight + 1e-3f, $"{spec.Id} {row.Index}단: 끊는 단 = 윗면이 통로 높이 이하");
                if (!cut) { continue; }

                pieces++;
                bool leftOfTunnel = row.ToX <= -tunnel.HalfWidth + 1e-4f;
                bool rightOfTunnel = row.FromX >= tunnel.HalfWidth - 1e-4f;
                Assert.IsTrue(leftOfTunnel || rightOfTunnel, $"{spec.Id} {row.Index}단 조각 {row.Piece}가 홈을 덮지 않는다");
            }
            Assert.AreEqual(tunnel.CutRows * 2, pieces, $"{spec.Id} 끊은 단은 좌·우 2조각");
        }
    }

    [Test]
    public void 통로_홈_안에는_좌석이_없고_홈_지붕은_뒤_단_윗면을_넘지_않는다()
    {
        foreach (StadiumSpec spec in Real())
        {
            TunnelSpot tunnel = StadiumLayout.TunnelOf(spec);
            foreach (SeatSlot seat in StadiumLayout.Seats(spec))
            {
                if (seat.Side != StandSide.Far) { continue; }

                bool insideRecess = Math.Abs(seat.X) < tunnel.HalfWidth && seat.Z < tunnel.BackZ;
                Assert.IsFalse(insideRecess, $"{spec.Id} 좌석 ({seat.X:F2}, {seat.Z:F2})이 통로 홈 안에 있다");
            }
            float backRowTop = spec.BaseHeight + (tunnel.CutRows + 1) * spec.RowHeight;
            Assert.LessOrEqual(tunnel.RoofTop, backRowTop + 1e-4f, $"{spec.Id} 홈 지붕 윗면 ≤ 끊지 않은 첫 단 윗면(뒤 좌석을 안 가린다)");
            Assert.GreaterOrEqual(tunnel.Height, 1.8f, $"{spec.Id} 선수(1.8m)가 들어가는 높이");
        }
    }

    [Test]
    public void 관중석은_선과_런오프_바깥에서_시작하고_뒤_단일수록_높다()
    {
        StadiumSpec t4 = Real()[3];
        List<StandRow> rows = StadiumLayout.Rows(t4);
        int cut = StadiumLayout.TunnelOf(t4).CutRows;
        Assert.AreEqual(t4.Rows + cut, rows.Count, "통로가 끊은 단은 2조각");
        Assert.Greater(rows[0].FromZ, FieldBounds.HalfWidth + t4.Runoff, "먼 쪽 첫 단은 터치라인 + 런오프 바깥");
        for (int i = 1; i < rows.Count; i++)
        {
            if (rows[i].Index == rows[i - 1].Index)
            {
                Assert.AreEqual(rows[i - 1].FromZ, rows[i].FromZ, 1e-4f, "같은 단의 조각은 깊이가 같다");
                continue;
            }

            Assert.Greater(rows[i].FromZ, rows[i - 1].FromZ, "뒤 단이 더 멀다");
            Assert.Greater(rows[i].Height, rows[i - 1].Height, "뒤 단이 더 높다");
        }
        foreach (SeatSlot seat in StadiumLayout.Seats(t4))
        {
            Assert.Greater(seat.Z, FieldBounds.HalfWidth, "좌석이 경기장 안에 들어오지 않는다");
        }
    }

    [Test]
    public void 면마다_관중석은_자기_쪽_선_바깥에만_있고_다른_면과_겹치지_않는다()
    {
        StadiumSpec t1 = Real()[0];   // 4면
        float touch = FieldBounds.HalfWidth + t1.Runoff;
        float goal = FieldBounds.HalfLength + t1.Runoff;
        foreach (SeatSlot seat in StadiumLayout.Seats(t1))
        {
            if (seat.Side == StandSide.Far) { Assert.Greater(seat.Z, touch, "먼 쪽 = +Z"); }
            else if (seat.Side == StandSide.Near) { Assert.Less(seat.Z, -touch, "가까운 쪽 = −Z"); }
            else if (seat.Side == StandSide.EndLeft) { Assert.Less(seat.X, -goal, "왼쪽 골대 뒤 = −X"); }
            else { Assert.Greater(seat.X, goal, "오른쪽 골대 뒤 = +X"); }
            bool inTouchBand = System.Math.Abs(seat.Z) > touch;
            bool inGoalBand = System.Math.Abs(seat.X) > goal;
            Assert.IsFalse(inTouchBand && inGoalBand, "모서리(코너)에는 아직 좌석이 없다: 두 면이 겹치지 않는다");
        }
    }

    [Test]
    public void 관중_채움은_같은_시즌_같은_홈_팀이면_같고_비율은_채움값_근처다()
    {
        StadiumSpec t1 = Real()[0];
        int seats = StadiumLayout.Seats(t1).Count;
        uint seed = StadiumRules.FillSeed(7, 1, 3);
        Assert.AreEqual(seed, StadiumRules.FillSeed(7, 1, 3), "같은 입력 = 같은 시드");
        Assert.AreNotEqual(seed, StadiumRules.FillSeed(7, 1, 4), "홈 팀이 다르면 빈자리도 다르다");
        int filled = 0;
        for (int i = 0; i < seats; i++)
        {
            if (StadiumRules.IsFilled(seed, i, t1.FillPercent)) { filled++; }
        }
        Assert.AreEqual(t1.FillPercent / 100.0, filled / (double)seats, 0.02, "채움 비율");
        Assert.IsFalse(StadiumRules.IsFilled(seed, 0, 0), "0%면 아무도 없다");
        Assert.IsTrue(StadiumRules.IsFilled(seed, 0, 100), "100%면 전부");
    }

    [Test]
    public void 경기장은_홈_팀의_부이고_승강전_원정만_상대_부다()
    {
        // 도전자(4부, 상대 3부): 1차전 홈·2차전 원정. 수성(3부, 상대 4부): 1차전 원정·2차전 홈(PlayoffRole 주석)
        Assert.AreEqual(4, SeasonRunner.StadiumTierOf(4, 3, PlayoffStage.LegOne, myHome: true), "도전자 1차전 홈 = 내 부");
        Assert.AreEqual(3, SeasonRunner.StadiumTierOf(4, 3, PlayoffStage.LegTwo, myHome: false), "도전자 2차전 원정 = 위 부 경기장");
        Assert.AreEqual(4, SeasonRunner.StadiumTierOf(3, 4, PlayoffStage.LegOne, myHome: false), "수성 1차전 원정 = 아래 부 경기장");
        Assert.AreEqual(3, SeasonRunner.StadiumTierOf(3, 4, PlayoffStage.LegTwo, myHome: true), "수성 2차전 홈 = 내 부");
        Assert.AreEqual(4, SeasonRunner.StadiumTierOf(4, 3, PlayoffStage.Semifinal, myHome: false), "단판은 같은 부");
    }
}
