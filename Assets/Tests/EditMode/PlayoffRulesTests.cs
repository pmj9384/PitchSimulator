using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 승강전(10-08, 스펙 §10 10-08 구현 설계). ①순위 → 계획 4갈래 ②단계 진행 ③합산·승부차기 승자 ④승부차기 확률·결정성 ⑤키커 ⑥세이브 왕복 ⑦4부 2위 → PO → 승강전 → 승격/잔류
public class PlayoffRulesTests
{
    private static List<PlayerStats> table;
    private static List<FormationTemplate> formations;
    private static TeamNameTable names;
    private static List<TierRule> tiers;
    private static List<TeamTactics> presets;
    private static List<StageEntry> myRows;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        formations = FormationTemplateParser.Parse(File.ReadAllText("Assets/Resources/Tables/FormationTemplates.csv"));
        names = TeamNameTable.Parse(File.ReadAllText("Assets/Resources/Tables/TeamNames.csv"));
        tiers = TierRuleParser.Parse(File.ReadAllText("Assets/Resources/Tables/TierRules.csv"));
        presets = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));
        myRows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1 && r.Team == 0);
    }

    private static TierRule Tier(int tier)
    {
        return tiers.Find(t => t.Tier == tier);
    }

    // 순위를 정해 둔 순위표: ranked[i]가 ranked[j](i < j)를 전부 1:0으로 이긴다
    private static List<MatchResult> ResultsForOrder(IReadOnlyList<int> ranked)
    {
        var results = new List<MatchResult>();
        for (int i = 0; i < ranked.Count; i++)
        {
            for (int j = i + 1; j < ranked.Count; j++) { results.Add(new MatchResult(ranked[i], ranked[j], 1, 0)); }
        }
        return results;
    }

    private static LeagueTable TableWithMeAt(int teamCount, int myRank)
    {
        var ranked = new List<int>();
        int next = 1;
        for (int i = 0; i < teamCount; i++)
        {
            if (i == myRank) { ranked.Add(SeasonState.MyTeamId); continue; }
            ranked.Add(next++);
        }
        var ids = new List<int>();
        for (int i = 0; i < teamCount; i++) { ids.Add(i); }
        return LeagueTable.Standings(ids, ResultsForOrder(ranked));
    }

    [Test]
    public void 순위에_따라_도전자_수성_없음이_갈린다()
    {
        PlayoffPlan second = PlayoffRules.Plan(TableWithMeAt(6, 1), 4, SeasonState.MyTeamId);
        Assert.AreEqual(PlayoffRole.Challenger, second.Role);
        Assert.IsTrue(second.SemifinalAtHome, "2위가 홈");
        Assert.AreEqual(3, second.OtherTier);
        Assert.AreEqual(TableWithMeAt(6, 1)[2].TeamId, second.SemifinalOpponentId, "상대는 3위");

        PlayoffPlan third = PlayoffRules.Plan(TableWithMeAt(6, 2), 4, SeasonState.MyTeamId);
        Assert.AreEqual(PlayoffRole.Challenger, third.Role);
        Assert.IsFalse(third.SemifinalAtHome);

        Assert.AreEqual(PlayoffRole.None, PlayoffRules.Plan(TableWithMeAt(6, 3), 4, SeasonState.MyTeamId).Role, "4위는 대상 아님");
        Assert.AreEqual(PlayoffRole.None, PlayoffRules.Plan(TableWithMeAt(12, 1), 1, SeasonState.MyTeamId).Role, "1부 2위는 올라갈 곳이 없다");
        Assert.AreEqual(PlayoffRole.None, PlayoffRules.Plan(TableWithMeAt(6, 4), 4, SeasonState.MyTeamId).Role, "4부 최하위 바로 위는 내려올 팀이 없다");

        PlayoffPlan defender = PlayoffRules.Plan(TableWithMeAt(10, 8), 2, SeasonState.MyTeamId);
        Assert.AreEqual(PlayoffRole.Defender, defender.Role, "2부 9위(최하위 바로 위)");
        Assert.AreEqual(3, defender.OtherTier);
    }

    [Test]
    public void 도전자는_단판을_지면_끝이고_이기면_두_경기를_더_한다()
    {
        var plan = new PlayoffPlan(PlayoffRole.Challenger, 3, true, 3);
        var results = new List<PlayoffResult>();
        Assert.AreEqual(PlayoffStage.Semifinal, PlayoffRules.NextStage(plan, results, 0));

        results.Add(new PlayoffResult(PlayoffStage.Semifinal, 0, 3, 0, 1));   // 홈(나) 0:1 패배
        Assert.AreEqual(PlayoffStage.Done, PlayoffRules.NextStage(plan, results, 0));

        results.Clear();
        results.Add(new PlayoffResult(PlayoffStage.Semifinal, 0, 3, 1, 1));   // 무승부면 홈(2위)
        Assert.AreEqual(PlayoffStage.LegOne, PlayoffRules.NextStage(plan, results, 0));
        results.Add(new PlayoffResult(PlayoffStage.LegOne, 0, 1005, 2, 0));
        Assert.AreEqual(PlayoffStage.LegTwo, PlayoffRules.NextStage(plan, results, 0));
        results.Add(new PlayoffResult(PlayoffStage.LegTwo, 1005, 0, 1, 0));
        Assert.AreEqual(PlayoffStage.Done, PlayoffRules.NextStage(plan, results, 0));

        var defender = new PlayoffPlan(PlayoffRole.Defender, -1, false, 3);
        Assert.AreEqual(PlayoffStage.LegOne, PlayoffRules.NextStage(defender, new List<PlayoffResult>(), 0), "수성은 단판 없이 1차전부터");
    }

    [Test]
    public void 합산은_두_경기_골을_더하고_동점이면_승부차기_점수로_가른다()
    {
        var legOne = new PlayoffResult(PlayoffStage.LegOne, 0, 1005, 2, 1);
        var legTwo = new PlayoffResult(PlayoffStage.LegTwo, 1005, 0, 2, 1);   // 합산 3:3
        Assert.AreEqual(-1, PlayoffRules.AggregateWinner(legOne, legTwo));
        Assert.AreEqual(1005, PlayoffRules.AggregateWinner(legOne, new PlayoffResult(PlayoffStage.LegTwo, 1005, 0, 3, 1)));

        var withPens = new PlayoffResult(PlayoffStage.LegTwo, 1005, 0, 2, 1, homePenalties: 3, awayPenalties: 4);
        Assert.AreEqual(0, PlayoffRules.TieWinner(legOne, withPens), "승부차기 4:3으로 원정(나)");
        Assert.Throws<System.InvalidOperationException>(() => PlayoffRules.TieWinner(legOne, legTwo), "동점인데 승부차기 점수가 없다");
        Assert.Throws<System.ArgumentException>(() => PlayoffRules.AggregateWinner(legOne, legOne), "2차전은 홈·원정이 바뀌어야 한다");
    }

    [Test]
    public void 승부차기_확률은_0_5와_0_95_사이고_같은_주사위면_같은_결과다()
    {
        Assert.AreEqual(0.76f, PlayoffRules.PenaltyChance(50, 50), 0.0001f);
        Assert.AreEqual(0.86f, PlayoffRules.PenaltyChance(100, 50), 0.0001f);
        Assert.AreEqual(0.95f, PlayoffRules.PenaltyChance(100, 0), 0.0001f, "위 클램프");
        Assert.AreEqual(0.5f, PlayoffRules.PenaltyChance(0, 200), 0.0001f, "아래 클램프(스탯 범위 밖 값으로만 닿는다)");

        int[] home = { 90, 80, 70, 60, 50 };
        int[] away = { 60, 60, 60, 60, 60 };
        int i = 0;
        float[] rolls = { 0.1f, 0.9f };   // 홈 킥은 늘 성공, 원정 킥은 늘 실패
        ShootoutResult a = PlayoffRules.Shootout(home, 50, away, 50, () => rolls[i++ % 2]);
        Assert.AreEqual(5, a.HomeScore);
        Assert.AreEqual(0, a.AwayScore);
        Assert.IsTrue(a.HomeWon);

        var r1 = new System.Random(7);
        var r2 = new System.Random(7);
        ShootoutResult b1 = PlayoffRules.Shootout(home, 50, away, 50, () => (float)r1.NextDouble());
        ShootoutResult b2 = PlayoffRules.Shootout(home, 50, away, 50, () => (float)r2.NextDouble());
        Assert.AreEqual(b1.HomeScore, b2.HomeScore);
        Assert.AreEqual(b1.AwayScore, b2.AwayScore);
        Assert.AreNotEqual(b1.HomeScore, b1.AwayScore, "끝났으면 동점이 아니다");
    }

    [Test]
    public void 키커는_GK를_빼고_슛_높은_순_5명이다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        List<int> shots = PlayoffRules.TopShooters(state.MyLineup(), 5);
        Assert.AreEqual(5, shots.Count);
        for (int i = 1; i < shots.Count; i++) { Assert.GreaterOrEqual(shots[i - 1], shots[i]); }
        Assert.Greater(PlayoffRules.KeeperHandling(state.MyLineup()), 0);
    }

    [Test]
    public void 승강전_결과는_세이브로_왕복하고_옛_세이브는_승강전_전이다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        state.AddPlayoffResult(new PlayoffResult(PlayoffStage.Semifinal, 0, 3, 1, 1));
        state.AddPlayoffResult(new PlayoffResult(PlayoffStage.LegOne, 0, 1005, 2, 0));
        state.AddPlayoffResult(new PlayoffResult(PlayoffStage.LegTwo, 1005, 0, 2, 0, 4, 5));

        SeasonState back = SeasonState.FromSave(state.ToSave(), table);
        Assert.AreEqual(3, back.PlayoffResults.Count);
        Assert.AreEqual(PlayoffStage.LegTwo, back.PlayoffResults[2].Stage);
        Assert.AreEqual(5, back.PlayoffResults[2].AwayPenalties);

        SeasonSave old = state.ToSave();
        old.playoff = null;   // 10-08 전 세이브
        Assert.AreEqual(0, SeasonState.FromSave(old, table).PlayoffResults.Count);
    }

    [Test]
    public void 사부_2위는_PO와_승강전_두_경기를_치르고_합산_결과대로_승격하거나_잔류한다()
    {
        TierRule tier = Tier(4);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(42, tier, table, formations, names);
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);

        // 정규 시즌: 라운드마다 결과를 넣되 순위가 1위 = 팀 1, 2위 = 나, 3위 = 팀 2 …가 되게 꾸민다
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);
        int[] order = { 1, 0, 2, 3, 4, 5 };
        var rankOf = new Dictionary<int, int>();
        for (int i = 0; i < order.Length; i++) { rankOf[order[i]] = i; }
        for (int round = 0; round < tier.Matches; round++)
        {
            var results = new List<MatchResult>();
            var fixtures = new List<Fixture>();
            SeasonSchedule.FixturesOfRound(schedule, round, fixtures);
            foreach (Fixture f in fixtures)
            {
                bool homeBetter = rankOf[f.HomeTeamId] < rankOf[f.AwayTeamId];
                results.Add(new MatchResult(f.HomeTeamId, f.AwayTeamId, homeBetter ? 1 : 0, homeBetter ? 0 : 1));
            }
            state.AddRound(results);
        }
        Assert.IsTrue(SeasonProgress.IsRegularSeasonOver(state, tier));
        Assert.IsFalse(SeasonProgress.IsOver(state, tier), "2위는 승강전이 남았다");
        Assert.AreEqual(PlayoffStage.Semifinal, SeasonProgress.PendingStage(state, tier));
        PlayoffPlan plan = SeasonProgress.Plan(state, tier);
        Assert.AreEqual(2, plan.SemifinalOpponentId, "3위 팀");

        // 단판 PO: 2위(나) 홈, 1:1 → 나
        MatchSetup semi = SeasonRunner.SetupPlayoffMatch(state, tier, plan, PlayoffStage.Semifinal, opponents, null, presets);
        Assert.IsTrue(semi.MyTeamIsHome);
        Assert.AreEqual(tier.Matches, semi.Fixture.Round, "라운드 번호는 정규 뒤에 이어 붙는다");
        state.AddPlayoffResult(SeasonRunner.ResolvePlayoff(semi, PlayoffStage.Semifinal, semi.ResultFor(1, 1), null));
        Assert.AreEqual(PlayoffStage.LegOne, SeasonProgress.PendingStage(state, tier));

        // 승강전: 3부 팀과 1차전 홈·2차전 원정, 합산 동점 → 승부차기로 갈린다
        GeneratedTeam foreign = SeasonRunner.ForeignTeam(42, Tier(3), plan.Role, table, formations, names, presets);
        Assert.GreaterOrEqual(foreign.TeamId, PlayoffRules.ForeignTeamIdBase);
        StringAssert.Contains("(3부)", foreign.Name);

        MatchSetup legOne = SeasonRunner.SetupPlayoffMatch(state, tier, plan, PlayoffStage.LegOne, opponents, foreign, presets);
        Assert.IsTrue(legOne.MyTeamIsHome, "도전자는 1차전 홈");
        state.AddPlayoffResult(SeasonRunner.ResolvePlayoff(legOne, PlayoffStage.LegOne, legOne.ResultFor(2, 1), null));

        MatchSetup legTwo = SeasonRunner.SetupPlayoffMatch(state, tier, plan, PlayoffStage.LegTwo, opponents, foreign, presets);
        Assert.IsFalse(legTwo.MyTeamIsHome, "2차전 원정");
        Assert.AreNotEqual(legOne.Seed, legTwo.Seed);
        PlayoffResult last = SeasonRunner.ResolvePlayoff(legTwo, PlayoffStage.LegTwo, legTwo.ResultFor(1, 2), state.PlayoffResults[1]);
        Assert.AreNotEqual(last.HomePenalties, last.AwayPenalties, "합산 3:3이라 승부차기로 갈렸다");
        state.AddPlayoffResult(last);

        Assert.IsTrue(SeasonProgress.IsOver(state, tier));
        SeasonOutcome outcome = SeasonProgress.Outcome(state, tier);
        bool won = PlayoffRules.TieWinner(state.PlayoffResults[1], last) == SeasonState.MyTeamId;
        Assert.AreEqual(won ? SeasonOutcomeKind.Promoted : SeasonOutcomeKind.Stayed, outcome.Kind);
        Assert.AreEqual(won ? 3 : 4, outcome.NextTier);
        Assert.AreEqual(2, outcome.FinalRank);

        // 같은 재료로 다시 돌리면 같은 승부차기(결정성)
        PlayoffResult again = SeasonRunner.ResolvePlayoff(legTwo, PlayoffStage.LegTwo, legTwo.ResultFor(1, 2), state.PlayoffResults[1]);
        Assert.AreEqual(last.HomePenalties, again.HomePenalties);
        Assert.AreEqual(last.AwayPenalties, again.AwayPenalties);
    }

    [Test]
    public void 수성_팀은_하위_부_PO_승자와_1차전_원정_2차전_홈으로_붙고_지면_강등이다()
    {
        TierRule tier = Tier(3);   // 8팀: 7위가 최하위 바로 위
        List<GeneratedTeam> opponents = TeamGenerator.Generate(43, tier, table, formations, names);
        SeasonState state = SeasonState.NewSeason(3, 43, myRows, table);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);
        int[] order = { 1, 2, 3, 4, 5, 6, 0, 7 };
        var rankOf = new Dictionary<int, int>();
        for (int i = 0; i < order.Length; i++) { rankOf[order[i]] = i; }
        for (int round = 0; round < tier.Matches; round++)
        {
            var results = new List<MatchResult>();
            var fixtures = new List<Fixture>();
            SeasonSchedule.FixturesOfRound(schedule, round, fixtures);
            foreach (Fixture f in fixtures)
            {
                bool homeBetter = rankOf[f.HomeTeamId] < rankOf[f.AwayTeamId];
                results.Add(new MatchResult(f.HomeTeamId, f.AwayTeamId, homeBetter ? 1 : 0, homeBetter ? 0 : 1));
            }
            state.AddRound(results);
        }
        PlayoffPlan plan = SeasonProgress.Plan(state, tier);
        Assert.AreEqual(PlayoffRole.Defender, plan.Role);
        Assert.AreEqual(PlayoffStage.LegOne, SeasonProgress.PendingStage(state, tier));

        GeneratedTeam foreign = SeasonRunner.ForeignTeam(43, Tier(4), plan.Role, table, formations, names, presets);
        StringAssert.Contains("(4부)", foreign.Name);
        MatchSetup legOne = SeasonRunner.SetupPlayoffMatch(state, tier, plan, PlayoffStage.LegOne, opponents, foreign, presets);
        Assert.IsFalse(legOne.MyTeamIsHome, "수성은 1차전 원정");
        state.AddPlayoffResult(SeasonRunner.ResolvePlayoff(legOne, PlayoffStage.LegOne, legOne.ResultFor(0, 1), null));
        MatchSetup legTwo = SeasonRunner.SetupPlayoffMatch(state, tier, plan, PlayoffStage.LegTwo, opponents, foreign, presets);
        Assert.IsTrue(legTwo.MyTeamIsHome, "2차전 홈");
        state.AddPlayoffResult(SeasonRunner.ResolvePlayoff(legTwo, PlayoffStage.LegTwo, legTwo.ResultFor(0, 2), state.PlayoffResults[0]));

        Assert.IsTrue(SeasonProgress.IsOver(state, tier));
        SeasonOutcome outcome = SeasonProgress.Outcome(state, tier);
        Assert.AreEqual(SeasonOutcomeKind.Relegated, outcome.Kind);
        Assert.AreEqual(4, outcome.NextTier);
    }
}
