using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 로비 홈의 "지난 경기 설정 대 실측" 재료(스펙 §2-1, 10-15): 실측 → 요약 숫자, 세이브 왕복, 옛 세이브는 없음
public class TacticsReadoutSummaryTests
{
    private static List<PlayerStats> table;
    private static List<StageEntry> myRows;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        myRows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1 && r.Team == 0);
    }

    // 서드마다 다른 값을 둔다: 요약이 엉뚱한 서드를 읽으면 걸리게(10-15 리뷰. 전부 0이면 오독이 통과한다)
    private static TeamTactics Setting()
    {
        var t = new TeamTactics();
        t.PassStyle[(int)Third.Own] = 0;
        t.PassStyle[(int)Third.Middle] = 2;      // 롱볼
        t.PassStyle[(int)Third.Opponent] = 1;
        t.PressStart[(int)Third.Own] = 2;
        t.PressStart[(int)Third.Middle] = 0;
        t.PressStart[(int)Third.Opponent] = 1;   // 표준
        t.Counter = 1;
        return t;
    }

    private static TacticsReadoutSummary Sample()
    {
        return new TacticsReadoutSummary(new[] { 3, 10, 4 }, new[] { 20f, 16.5f, 12f }, new[] { 1, 4, 5 }, 3, 2, 12, 0, 2, 1);
    }

    [Test]
    public void 실측에서_요약을_만들면_서드별_값과_그_경기_설정이_같이_담긴다()
    {
        var readout = new TacticsReadout();
        readout.AddPass(Third.Middle, 10f);
        readout.AddPass(Third.Middle, 20f);
        readout.AddRegain(Third.Opponent);
        readout.AddCounter();
        readout.AddShot(duringCounter: true);
        readout.AddShot(duringCounter: false);

        TacticsReadoutSummary summary = TacticsReadoutSummary.From(readout, Setting());

        Assert.AreEqual(2, summary.PassCount(Third.Middle));
        Assert.AreEqual(15f, summary.MeanPassLength(Third.Middle), 1e-4f);
        Assert.AreEqual(0, summary.PassCount(Third.Own));
        Assert.AreEqual(1, summary.RegainCount(Third.Opponent));
        Assert.AreEqual(1, summary.Counters);
        Assert.AreEqual(1, summary.CounterShots);
        Assert.AreEqual(2, summary.Shots);
        Assert.AreEqual(2, summary.SettingPassStyle, "중원 패스 방식");
        Assert.AreEqual(1, summary.SettingPressStart, "상대 진영 압박 시작");
        Assert.AreEqual(0f, summary.MeanPassLength(Third.Own), "패스가 없는 서드의 평균은 0");
        Assert.AreEqual(1, summary.SettingCounter);
    }

    [Test]
    public void 세이브_왕복은_요약을_그대로_돌려준다()
    {
        SeasonState state = SeasonState.NewSeason(4, 7, myRows, table);
        state.SetLastReadout(Sample());

        SeasonState back = SeasonState.FromSave(state.ToSave(), table);

        Assert.IsNotNull(back.LastReadout);
        TacticsReadoutSummary s = back.LastReadout;
        Assert.AreEqual(3, s.PassCount(Third.Own));
        Assert.AreEqual(10, s.PassCount(Third.Middle));
        Assert.AreEqual(4, s.PassCount(Third.Opponent));
        Assert.AreEqual(20f, s.MeanPassLength(Third.Own), 1e-4f);
        Assert.AreEqual(16.5f, s.MeanPassLength(Third.Middle), 1e-4f);
        Assert.AreEqual(12f, s.MeanPassLength(Third.Opponent), 1e-4f);
        Assert.AreEqual(1, s.RegainCount(Third.Own));
        Assert.AreEqual(5, s.RegainCount(Third.Opponent));
        Assert.AreEqual(3, s.Counters);
        Assert.AreEqual(2, s.CounterShots);
        Assert.AreEqual(12, s.Shots);
        Assert.AreEqual(0, s.SettingPassStyle);
        Assert.AreEqual(2, s.SettingPressStart);
        Assert.AreEqual(1, s.SettingCounter);
    }

    [Test]
    public void 옛_세이브와_새_시즌엔_요약이_없다()
    {
        SeasonState fresh = SeasonState.NewSeason(4, 7, myRows, table);
        Assert.IsNull(fresh.LastReadout, "아직 치른 경기가 없다");

        SeasonSave old = fresh.ToSave();
        old.lastReadout = null;   // 10-15 전 세이브엔 이 필드가 없다 → Json.NET이 null로 둔다
        Assert.IsNull(SeasonState.FromSave(old, table).LastReadout);
    }

    // 보조 표시값이 깨졌다고 시즌 로드가 실패하면 새 시즌으로 초기화된다(SeasonSystem.Load의 catch). 요약만 비우고 나머지는 살아야 한다
    [Test]
    public void 깨진_요약_세이브는_요약만_비우고_시즌은_그대로_읽는다()
    {
        SeasonState state = SeasonState.NewSeason(4, 7, myRows, table);
        state.SetLastReadout(Sample());

        SeasonSave shortArray = state.ToSave();
        shortArray.lastReadout!.passCounts = new[] { 1, 2 };
        Assert.IsNull(SeasonState.FromSave(shortArray, table).LastReadout, "서드 배열 2칸");

        SeasonSave outOfRange = state.ToSave();
        outOfRange.lastReadout!.passStyle = 7;
        Assert.IsNull(SeasonState.FromSave(outOfRange, table).LastReadout, "설정 범위 밖");

        SeasonSave nullArray = state.ToSave();
        nullArray.lastReadout!.regains = null!;   // 깨진 JSON 흉내
        SeasonState back = SeasonState.FromSave(nullArray, table);
        Assert.IsNull(back.LastReadout, "배열 null");
        Assert.AreEqual(4, back.Tier, "시즌 자체는 그대로");
    }

    [Test]
    public void 설정_범위_밖이면_요약_생성이_던진다()
    {
        Assert.Throws<System.ArgumentException>(() => new TacticsReadoutSummary(new[] { 0, 0, 0 }, new float[3], new int[3], 0, 0, 0, 3, 0, 0));
        Assert.Throws<System.ArgumentException>(() => new TacticsReadoutSummary(new[] { 0, 0, 0 }, new float[3], new int[3], -1, 0, 0, 0, 0, 0));
    }

    [Test]
    public void 세이브_DTO는_요약의_배열을_복사해_들고_있다()
    {
        TacticsReadoutSummary summary = Sample();
        SeasonSave.ReadoutEntry entry = SeasonSave.ReadoutEntry.From(summary);
        entry.passCounts[1] = 99;
        Assert.AreEqual(10, summary.PassCount(Third.Middle), "DTO를 고쳐도 요약은 그대로");
    }
}
