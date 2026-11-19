using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using Game.Core.Viewing;
using NUnit.Framework;

// 하이라이트 재생의 결과 동일성(11-06 S4 설계 5-2·5-3). 1차(헤드리스로 끝까지)에서 사건을 모아 구간을 만들고,
// 2차(같은 조립으로 새 시뮬)를 ReplayPlan대로 "보이면 1틱, 아니면 예산만큼 묶어서" 돌려도 같은 경기가 끝나는지 잠근다.
// MatchManager의 재생 루프는 같은 ReplayPlan.TicksToSkip을 부르므로 여기 Drive가 그 루프의 엔진 없는 판이다
public class HighlightReplayTests
{
    private static List<PlayerStats> table;
    private static List<StageEntry> rows;
    private static TeamTactics balanced;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        rows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(IsStageOne);
        balanced = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).Find(IsBalanced);
    }

    private static bool IsStageOne(StageEntry row)
    {
        return row.Stage == 1;
    }

    private static bool IsBalanced(TeamTactics tactics)
    {
        return tactics.PresetId == "balanced";
    }

    private static MatchSimulation Assemble(int seed, MatchLength length)
    {
        return MatchAssembler.Create(table, rows, balanced, balanced, seed, TestThreat.Real, length: length);
    }

    // 1차: 끝까지 돌리며 사건을 모은다
    private static List<MatchEvent> RunHeadless(MatchSimulation sim)
    {
        using (var log = new MatchEventLog(sim))
        {
            for (int i = 0; i < sim.TotalTicks; i++)
            {
                sim.Tick(MatchTuning.FixedStep);
            }
            return new List<MatchEvent>(log.Events);
        }
    }

    // 2차: 매니저 재생 루프의 엔진 없는 판. 스텝마다 보이면 1틱, 아니면 TicksToSkip만큼. 몇 번 보였는지(=화면에 보낸 틱 수)를 돌려준다
    private static int Drive(MatchSimulation sim, ReplayPlan plan, int budget)
    {
        int ticks = 0;
        int shownTicks = 0;
        while (ticks < sim.TotalTicks)
        {
            int skip = plan.TicksToSkip(ticks, sim.TotalTicks, budget);
            if (skip == 0)
            {
                sim.Tick(MatchTuning.FixedStep);
                ticks++;
                shownTicks++;
                continue;
            }
            for (int i = 0; i < skip; i++)
            {
                sim.Tick(MatchTuning.FixedStep);
            }
            ticks += skip;
        }
        return shownTicks;
    }

    private static void AssertSameEvents(IReadOnlyList<MatchEvent> expected, IReadOnlyList<MatchEvent> actual, string label)
    {
        Assert.AreEqual(expected.Count, actual.Count, $"{label}: 사건 수");
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i].Kind, actual[i].Kind, $"{label}: {i}번 종류");
            Assert.AreEqual(expected[i].Tick, actual[i].Tick, $"{label}: {i}번 틱");
            Assert.AreEqual(expected[i].ResolveTick, actual[i].ResolveTick, $"{label}: {i}번 결과 틱");
            Assert.AreEqual(expected[i].PlayerId, actual[i].PlayerId, $"{label}: {i}번 선수");
            Assert.AreEqual(expected[i].Probability, actual[i].Probability, $"{label}: {i}번 p");
            Assert.AreEqual(expected[i].Outcome, actual[i].Outcome, $"{label}: {i}번 결과");
        }
    }

    private static void AssertSamePositions(MatchSimulation expected, MatchSimulation actual, string label)
    {
        Assert.AreEqual(expected.HomeGoals, actual.HomeGoals, $"{label}: 팀 0 골");
        Assert.AreEqual(expected.AwayGoals, actual.AwayGoals, $"{label}: 팀 1 골");
        Assert.AreEqual(expected.Ball.X, actual.Ball.X, $"{label}: 공 X");
        Assert.AreEqual(expected.Ball.Z, actual.Ball.Z, $"{label}: 공 Z");
        for (int i = 0; i < expected.Players.Count; i++)
        {
            Assert.AreEqual(expected.Players[i].X, actual.Players[i].X, $"{label}: #{i} X");
            Assert.AreEqual(expected.Players[i].Z, actual.Players[i].Z, $"{label}: #{i} Z");
        }
    }

    // 세 모드 × 계산 예산 3개: 스코어·사건 목록·22명 좌표가 1차와 같다(장부 머지 조건 "세 모드의 최종 스코어·이벤트 목록이 같음")
    [Test]
    public void 세_모드와_건너뛰기_예산이_달라도_1차와_같은_경기가_끝난다([Values(ViewingMode.Key, ViewingMode.Extended, ViewingMode.Full)] ViewingMode mode)
    {
        const int seed = 7;
        MatchSimulation first = Assemble(seed, MatchLength.Compressed);
        List<MatchEvent> firstEvents = RunHeadless(first);
        Assert.Greater(firstEvents.Count, 0, "사건이 0이면 비교가 약하다");
        IReadOnlyList<TickRange> ranges = HighlightRules.Build(firstEvents, mode, first.TotalTicks, HighlightRules.TicksPerSecond(first));

        int[] budgets = { 1, 1000, 100000 };
        for (int b = 0; b < budgets.Length; b++)
        {
            MatchSimulation second = Assemble(seed, MatchLength.Compressed);
            using (var log = new MatchEventLog(second))
            {
                Drive(second, new ReplayPlan(ranges), budgets[b]);
                string label = $"{mode} 예산 {budgets[b]}";
                AssertSameEvents(firstEvents, log.Events, label);
                AssertSamePositions(first, second, label);
            }
        }
    }

    // 매니저가 GameReady에서 부르는 1차 계산 함수가 헤드리스 사건으로 만든 구간과 같은 구간을 낸다
    [Test]
    public void 미리_계산_함수는_헤드리스_사건으로_만든_구간과_같다()
    {
        const int seed = 7;
        MatchSimulation headless = Assemble(seed, MatchLength.Compressed);
        List<MatchEvent> events = RunHeadless(headless);
        IReadOnlyList<TickRange> expected = HighlightRules.Build(events, ViewingMode.Extended, headless.TotalTicks, HighlightRules.TicksPerSecond(headless));

        IReadOnlyList<TickRange> planned = HighlightPlanner.Plan(Assemble(seed, MatchLength.Compressed), ViewingMode.Extended);

        Assert.AreEqual(expected.Count, planned.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i].Start, planned[i].Start, $"{i}번 시작");
            Assert.AreEqual(expected[i].End, planned[i].End, $"{i}번 끝");
        }
    }

    // 실제 90분(완료 판정 길이)에서 핵심 모드 재생이 1차와 같고, 하이라이트가 골·세이브·큰 찬스를 빠짐없이 덮는다
    [Test]
    public void 실제_90분_핵심_재생은_1차와_같고_골과_세이브를_빠짐없이_보여_준다()
    {
        const int seed = 1;
        MatchSimulation first = Assemble(seed, MatchLength.RealTime);
        List<MatchEvent> firstEvents = RunHeadless(first);
        float ticksPerSecond = HighlightRules.TicksPerSecond(first);
        IReadOnlyList<TickRange> key = HighlightRules.Build(firstEvents, ViewingMode.Key, first.TotalTicks, ticksPerSecond);
        IReadOnlyList<TickRange> extended = HighlightRules.Build(firstEvents, ViewingMode.Extended, first.TotalTicks, ticksPerSecond);

        MatchSimulation second = Assemble(seed, MatchLength.RealTime);
        int shown;
        using (var log = new MatchEventLog(second))
        {
            shown = Drive(second, new ReplayPlan(key), 1000);
            AssertSameEvents(firstEvents, log.Events, "핵심 90분");
            AssertSamePositions(first, second, "핵심 90분");
        }
        Assert.Less(shown, first.TotalTicks, "핵심은 경기 일부만 보여 준다");

        int goals = 0;
        for (int i = 0; i < firstEvents.Count; i++)
        {
            MatchEvent e = firstEvents[i];
            if (e.Kind != MatchEventKind.Shot) { continue; }
            if (e.IsGoal)
            {
                goals++;
                Assert.IsTrue(Covers(key, e), $"골({e.Tick}틱)이 핵심에 없다");
            }
            if (e.Probability >= ViewingTuning.BigChanceP)
            {
                Assert.IsTrue(Covers(key, e), $"큰 찬스({e.Tick}틱)가 핵심에 없다");
            }
            if (e.IsOnTarget)
            {
                Assert.IsTrue(Covers(extended, e), $"유효 슛·세이브({e.Tick}틱)가 확장에 없다");
            }
        }
        Assert.AreEqual(first.HomeGoals + first.AwayGoals, goals, "골 사건 수 = 스코어 합");
    }

    // 찬 순간부터 결과까지 전부 한 구간 안에 보인다
    private static bool Covers(IReadOnlyList<TickRange> ranges, MatchEvent shot)
    {
        for (int i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Start <= shot.Tick - 1 && shot.ResolveTick <= ranges[i].End) { return true; }
        }
        return false;
    }
}
