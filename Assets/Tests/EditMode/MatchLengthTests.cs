using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 경기 길이(11-05 S1, 스펙 §0 2번): 3분 압축(게임 기본값)과 실제 90분이 같은 물리·트리를 쓰고, 경기 시간으로 정한 값(체력·시계)만 길이를 따른다
public class MatchLengthTests
{
    private static MatchSimulation Assemble(int seed, MatchLength length)
    {
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        List<StageEntry> rows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1);
        TeamTactics balanced = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).Find(t => t.PresetId == "balanced");
        return MatchAssembler.Create(table, rows, balanced, balanced, seed, TestThreat.Real, length: length);
    }

    [Test]
    public void 압축은_추가시간이_있어도_9000틱이고_실제_90분은_추가시간만큼_틱이_늘어난다()
    {
        var added = new AddedTime(1, 3);
        Assert.AreEqual(9000, MatchLength.Compressed.TotalTicks(added), "압축은 표시 시계만 늘린다(09-27 규칙)");
        Assert.AreEqual(270000, MatchLength.RealTime.TotalTicks(AddedTime.None));
        Assert.AreEqual(270000 + 4 * 3000, MatchLength.RealTime.TotalTicks(added), "1분 = 3,000틱");
    }

    [Test]
    public void 압축의_시간_배율은_정확히_1이라_체력_값이_그대로다()
    {
        Assert.AreEqual(1f, MatchLength.Compressed.TimeScale, 0f, "곱해도 바이트가 같아야 기존 경기가 그대로다");
        Assert.AreEqual(1f / 30f, MatchLength.RealTime.TimeScale, 1e-7f);
        Assert.AreEqual(MatchLength.Compressed.RegulationTicks, MatchLength.Default.RegulationTicks, "S3 재튜닝 전까지 게임 기본값은 3분 압축");
    }

    [Test]
    public void 실제_90분_시계는_50틱이_1초이고_하프타임은_45분에_전반_추가시간을_더한_틱이다()
    {
        var added = new AddedTime(1, 3);
        MatchLength real = MatchLength.RealTime;
        Assert.AreEqual(1, MatchClock.TotalSecondsOf(50, added, real));
        Assert.AreEqual(138000, MatchClock.HalfTimeTick(added, real), "(45 + 1)분 × 3,000틱");

        ClockReading beforeHalf = MatchClock.Describe(137999, added, real);
        Assert.IsFalse(beforeHalf.IsSecondHalf);
        Assert.IsTrue(beforeHalf.InAddedTime);
        ClockReading startSecond = MatchClock.Describe(138000, added, real);
        Assert.IsTrue(startSecond.IsSecondHalf);
        Assert.AreEqual(45, startSecond.Minute);
        Assert.AreEqual(0, startSecond.Second);
        ClockReading end = MatchClock.Describe(282000, added, real);
        Assert.IsTrue(end.InAddedTime);
        Assert.AreEqual(90, end.Minute);
        Assert.AreEqual(3, end.AddedMinute);
    }

    [Test]
    public void 추가시간이_없으면_실제_90분은_270000틱에서_90분_00초로_끝나고_압축은_마지막_틱이_90분_더하기_추가시간이다()
    {
        ClockReading realEnd = MatchClock.Describe(270000, AddedTime.None, MatchLength.RealTime);
        Assert.IsFalse(realEnd.InAddedTime);
        Assert.AreEqual(90, realEnd.Minute);
        Assert.AreEqual(0, realEnd.Second);
        Assert.AreEqual(5399, MatchClock.TotalSecondsOf(269999, AddedTime.None, MatchLength.RealTime), "끝 한 틱 전은 89:59");

        var added = new AddedTime(2, 5);
        ClockReading compressedEnd = MatchClock.Describe(MatchLength.Compressed.TotalTicks(added), added, MatchLength.Compressed);
        Assert.IsTrue(compressedEnd.InAddedTime);
        Assert.AreEqual(90, compressedEnd.Minute);
        Assert.AreEqual(5, compressedEnd.AddedMinute);
        Assert.AreEqual(0, compressedEnd.AddedSecond);
    }

    [Test]
    public void 첫_틱_뒤에는_추가시간을_바꿀_수_없다()
    {
        MatchSimulation sim = Assemble(5, MatchLength.RealTime);
        sim.Tick(MatchTuning.FixedStep);
        Assert.Throws<System.InvalidOperationException>(() => sim.SetAddedTime(new AddedTime(2, 5)), "종료·하프타임 틱이 경기 중에 움직이면 안 된다");
    }

    [Test]
    public void 같은_시드면_실제_90분과_3분_압축의_첫_100틱_움직임이_같고_체력만_30분의_1로_준다()
    {
        // 100틱이면 가장 빨리 지치는 선수도 속도가 주는 문턱(0.3) 위라 움직임은 두 길이에서 같아야 한다. 체력은 경기 시간 기준이라 실제 90분이 덜 준다
        MatchSimulation compressed = Assemble(3, MatchLength.Compressed);
        MatchSimulation real = Assemble(3, MatchLength.RealTime);
        for (int i = 0; i < 100; i++)
        {
            compressed.Tick(MatchTuning.FixedStep);
            real.Tick(MatchTuning.FixedStep);
        }

        Assert.AreEqual(compressed.Ball.X, real.Ball.X, 0f);
        Assert.AreEqual(compressed.Ball.Z, real.Ball.Z, 0f);
        float lowestCompressed = 1f;
        float lowestReal = 1f;
        for (int i = 0; i < compressed.Players.Count; i++)
        {
            Assert.AreEqual(compressed.Players[i].X, real.Players[i].X, 0f, $"선수 {i} X");
            Assert.AreEqual(compressed.Players[i].Z, real.Players[i].Z, 0f, $"선수 {i} Z");
            lowestCompressed = System.Math.Min(lowestCompressed, compressed.Players[i].Stamina);
            lowestReal = System.Math.Min(lowestReal, real.Players[i].Stamina);
        }
        Assert.Less(lowestCompressed, 1f, "100틱 안에 누군가는 전력으로 뛴다");
        Assert.Less(1f - lowestReal, (1f - lowestCompressed) / 10f, "틱당 소모가 30분의 1이라 실제 90분은 훨씬 덜 준다(회복 반감기도 30배라 정확히 1/30은 아니다)");
    }

    [Test]
    public void 실제_90분_한_판은_추가시간까지_끝까지_돌고_하프타임에_진영이_한_번_바뀐다()
    {
        MatchSimulation sim = Assemble(5, MatchLength.RealTime);
        int switches = 0;
        bool wasSwitched = false;
        while (sim.TickCount < sim.TotalTicks)
        {
            sim.Tick(MatchTuning.FixedStep);
            if (sim.SidesSwitched && !wasSwitched) { switches++; }
            wasSwitched = sim.SidesSwitched;
        }
        Assert.AreEqual(MatchLength.RealTime.TotalTicks(sim.Added), sim.TickCount);
        Assert.Greater(sim.TotalTicks, MatchTuning.RealTimeTicks, "추가시간만큼 더 돈다");
        Assert.AreEqual(1, switches);
        Assert.IsTrue(sim.IsSecondHalf);
    }
}
