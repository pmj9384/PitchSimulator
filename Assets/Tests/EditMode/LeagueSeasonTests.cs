using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 팀 생성기·일정·시즌 상태·세이브 왕복(09-26 확정 스펙 10항목, 플랜 09-27 칸).
// ①같은 시드 = 같은 팀 ②4부는 4-4-2·balanced·기본 변형·총점 260·자리 2쌍 같음 ③3부는 슬롯 1~2개만 다른 변형, 부 안 유일
// ④총점 축소는 합계가 정확 ⑤라운드로빈 ⑥4부 시즌 한 바퀴 ⑦세이브 왕복 ⑧라인업이 로스터 밖을 가리키면 실패
public class LeagueSeasonTests
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

    [Test]
    public void 같은_시드면_같은_팀이_나온다()
    {
        List<GeneratedTeam> a = TeamGenerator.Generate(7, Tier(3), table, formations, names);
        List<GeneratedTeam> b = TeamGenerator.Generate(7, Tier(3), table, formations, names);
        Assert.AreEqual(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(a[i].Signature, b[i].Signature);
            Assert.AreEqual(a[i].Name, b[i].Name);
            Assert.AreEqual(a[i].Players[0].Speed, b[i].Players[0].Speed);
        }
        List<GeneratedTeam> c = TeamGenerator.Generate(8, Tier(3), table, formations, names);
        bool anyDifferent = false;
        for (int i = 0; i < a.Count; i++) { if (a[i].Signature != c[i].Signature || a[i].Name != c[i].Name) { anyDifferent = true; } }
        Assert.IsTrue(anyDifferent, "다른 시드면 달라진다");
    }

    [Test]
    public void 사부는_사사이_balanced_기본_변형_총점_260이고_수비_자리가_공격_자리와_같다()
    {
        List<GeneratedTeam> teams = TeamGenerator.Generate(1, Tier(4), table, formations, names);
        Assert.AreEqual(5, teams.Count, "6팀 중 나를 뺀 5팀");
        var usedNames = new HashSet<string>();
        for (int t = 0; t < teams.Count; t++)
        {
            GeneratedTeam team = teams[t];
            Assert.AreEqual("4-4-2", team.FormationId);
            Assert.AreEqual("balanced", team.PresetId);
            Assert.AreEqual(t + 1, team.TeamId, "팀 id는 1부터(0은 나)");
            Assert.IsTrue(usedNames.Add(team.Name), $"이름 중복 {team.Name}");
            Assert.AreEqual(11, team.Rows.Count);
            for (int i = 0; i < team.Rows.Count; i++)
            {
                StageEntry row = team.Rows[i];
                Assert.AreEqual(StageEntry.SideEnemy, row.Side);
                Assert.AreEqual(TeamGenerator.DefaultVariant(table, team.Players[i].RoleId).VariantId, row.Id, "기본 변형");
                Assert.AreEqual(row.PosX, row.DefendX, 1e-4f, "4부는 자리 2쌍이 같다");
                Assert.Greater(row.PosX, 0f, "상대는 +X 진영");
                Assert.AreEqual(260, team.Players[i].BuildTotal, "부 총점");
            }
        }
    }

    [Test]
    public void 삼부는_슬롯_한두_개만_다른_변형이고_같은_부_안에서_구성이_유일하다()
    {
        List<GeneratedTeam> teams = TeamGenerator.Generate(3, Tier(3), table, formations, names);
        Assert.AreEqual(7, teams.Count);
        var signatures = new HashSet<string>();
        for (int t = 0; t < teams.Count; t++)
        {
            GeneratedTeam team = teams[t];
            Assert.IsTrue(signatures.Add(team.Signature), "구성 중복");
            int varied = 0;
            for (int i = 0; i < team.Rows.Count; i++)
            {
                if (team.Rows[i].Id != TeamGenerator.DefaultVariant(table, team.Players[i].RoleId).VariantId) { varied++; }
                Assert.AreEqual(275, team.Players[i].BuildTotal);
            }
            Assert.That(varied, Is.InRange(0, 2), "dialVariance 1 = 슬롯 1~2개(무작위가 기본 변형을 다시 뽑으면 0)");
            Assert.AreEqual(team.Rows[0].PosX, team.Rows[0].DefendX, 1e-4f, "3부도 자리 2쌍 같음(dualPositions 0)");
        }
    }

    [Test]
    public void 이부는_자리_2쌍을_템플릿대로_쓴다()
    {
        List<GeneratedTeam> teams = TeamGenerator.Generate(5, Tier(2), table, formations, names);
        Assert.AreEqual(9, teams.Count);
        bool anyDual = false;
        for (int t = 0; t < teams.Count; t++)
        {
            for (int i = 0; i < teams[t].Rows.Count; i++) { if (Math.Abs(teams[t].Rows[i].PosX - teams[t].Rows[i].DefendX) > 1e-4f) { anyDual = true; } }
        }
        Assert.IsTrue(anyDual, "템플릿 posX2가 posX와 다른 슬롯이 있으니 어느 팀이든 수비 자리가 다르다");
    }

    [Test]
    public void 일부는_12팀_전_슬롯_무작위라도_100번_안에_유일하게_만들어진다()
    {
        for (int seed = 1; seed <= 5; seed++)
        {
            List<GeneratedTeam> teams = TeamGenerator.Generate(seed, Tier(1), table, formations, names);
            Assert.AreEqual(11, teams.Count);
            var signatures = new HashSet<string>(); var usedNames = new HashSet<string>();
            foreach (GeneratedTeam team in teams)
            {
                Assert.IsTrue(signatures.Add(team.Signature)); Assert.IsTrue(usedNames.Add(team.Name));
                foreach (PlayerStats p in team.Players) { Assert.AreEqual(300, p.BuildTotal); }
            }
        }
    }

    [Test]
    public void 상대끼리_경기는_홈이_마이너스_진영에서_시작하고_같은_시드면_같은_결과다()
    {
        // 09-26 리뷰: 생성 팀 행은 전부 +X 관례라 홈 쪽을 안 뒤집으면 두 팀이 같은 자리에 겹쳐 스폰됐다
        TierRule tier = Tier(4);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(3, tier, table, formations, names);
        var fixture = new Fixture(0, 1, 2);
        Game.Core.Match.MatchSimulation sim = SeasonRunner.Assemble(3, fixture, opponents, presets);
        foreach (Game.Core.Match.PlayerState p in sim.Players)
        {
            if (p.Team == 0) { Assert.Less(p.AttackHomeX, 0f, $"홈 #{p.PlayerId}는 -X 진영"); Assert.Less(p.DefendHomeX, 0f); }
            else { Assert.Greater(p.AttackHomeX, 0f, $"원정 #{p.PlayerId}는 +X 진영"); }
        }
        Game.Core.Match.MatchSimulation a = SeasonRunner.Assemble(3, fixture, opponents, presets);
        Game.Core.Match.MatchSimulation b = SeasonRunner.Assemble(3, fixture, opponents, presets);
        for (int i = 0; i < MatchTuning.MatchTicks; i++) { a.Tick(0.02f); b.Tick(0.02f); }
        Assert.AreEqual(a.HomeGoals, b.HomeGoals); Assert.AreEqual(a.AwayGoals, b.AwayGoals); Assert.AreEqual(a.PassCount, b.PassCount);
    }

    [Test]
    public void 총점_축소는_합계가_정확하고_다이얼은_그대로다()
    {
        PlayerStats st = TeamGenerator.DefaultVariant(table, "ST");
        PlayerStats scaled = BuildScaler.Scale(st, 260);
        Assert.AreEqual(260, scaled.BuildTotal);
        Assert.AreEqual(st.ShotBias, scaled.ShotBias);
        Assert.AreEqual(st.PushUp, scaled.PushUp);
        Assert.AreEqual(300, st.BuildTotal, "원본은 안 바뀐다");
        Assert.AreSame(st, BuildScaler.Scale(st, 300), "300이면 복사 없이 원본");
        for (int total = 250; total <= 300; total += 5)
        {
            foreach (PlayerStats p in table) { Assert.AreEqual(total, BuildScaler.Scale(p, total).BuildTotal, $"{p.VariantId} {total}"); }
        }
    }

    [Test]
    public void 라운드로빈은_팀마다_라운드당_한_경기_모든_쌍_한_번이다()
    {
        foreach (int n in new[] { 6, 8, 10, 12 })
        {
            List<Fixture> fixtures = SeasonSchedule.RoundRobin(n);
            Assert.AreEqual(n * (n - 1) / 2, fixtures.Count);
            var pairs = new HashSet<(int, int)>();
            for (int round = 0; round < n - 1; round++)
            {
                var seen = new HashSet<int>();
                foreach (Fixture f in fixtures)
                {
                    if (f.Round != round) { continue; }
                    Assert.IsTrue(seen.Add(f.HomeTeamId) && seen.Add(f.AwayTeamId), $"{n}팀 라운드 {round}에 같은 팀이 두 번");
                    Assert.IsTrue(pairs.Add((Math.Min(f.HomeTeamId, f.AwayTeamId), Math.Max(f.HomeTeamId, f.AwayTeamId))), "같은 쌍이 두 번");
                }
                Assert.AreEqual(n, seen.Count, $"{n}팀 라운드 {round}에 전원 출전");
            }
            // 홈 횟수는 팀마다 (n-1)/2 ± 1(09-26 리뷰: 라운드 홀짝으로만 뒤집으면 6팀에서 4홈·1원정이 났다)
            var homes = new int[n];
            foreach (Fixture f in fixtures) { homes[f.HomeTeamId]++; }
            for (int t = 0; t < n; t++) { Assert.That(homes[t], Is.InRange((n - 1) / 2, (n - 1) / 2 + 1), $"{n}팀 팀 {t} 홈 {homes[t]}회"); }
        }
        Assert.Throws<ArgumentException>(() => SeasonSchedule.RoundRobin(5));
    }

    [Test]
    public void 사부_시즌_한_바퀴가_돌고_승점표가_쌓이고_승강_판정이_난다()
    {
        TierRule tier = Tier(4);
        SeasonState state = SeasonState.NewSeason(4, 11, myRows, table);
        Assert.AreEqual(11, state.Roster.Count);
        Assert.AreEqual(11, state.Lineup.Count);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(state.SeasonSeed, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);

        for (int round = 0; round < tier.Matches; round++)
        {
            Fixture mine = SeasonSchedule.MyFixture(schedule, round, SeasonState.MyTeamId);
            bool home = mine.HomeTeamId == SeasonState.MyTeamId;
            var myResult = new MatchResult(mine.HomeTeamId, mine.AwayTeamId, home ? 2 : 0, home ? 0 : 2);   // 내가 다 이긴 걸로
            SeasonRunner.PlayRound(state, opponents, schedule, presets, myResult);
        }

        Assert.AreEqual(tier.Matches, state.RoundsPlayed);
        Assert.AreEqual(tier.Teams * (tier.Teams - 1) / 2, state.Results.Count, "전 경기가 표에 들어간다");
        LeagueTable standings = state.Table(tier.Teams);
        Assert.AreEqual(tier.Teams, standings.Count);
        for (int i = 0; i < standings.Count; i++) { Assert.AreEqual(tier.Matches, standings[i].Played, $"팀 {standings[i].TeamId} 경기 수"); }
        Assert.AreEqual(SeasonState.MyTeamId, standings[0].TeamId, "5승이면 1위");
        PromotionDecision decision = LeagueRules.Decide(standings, 4);
        Assert.AreEqual(SeasonState.MyTeamId, decision.AutoPromoted);
        Assert.AreEqual(PromotionDecision.None, decision.Relegated, "4부는 강등 없음");

    }

    [Test]
    public void 내_경기_결과가_일정과_안_맞으면_거부한다()
    {
        TierRule tier = Tier(4);
        SeasonState state = SeasonState.NewSeason(4, 1, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(1, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);
        Assert.Throws<InvalidOperationException>(() => SeasonRunner.PlayRound(state, opponents, schedule, presets, new MatchResult(3, 4, 1, 0)));
    }

    [Test]
    public void 세이브_왕복은_상태를_그대로_돌려준다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        state.AddRound(new List<MatchResult> { new MatchResult(0, 3, 2, 1), new MatchResult(1, 2, 0, 0) });
        state.ScoutAttempts = 2;

        SeasonSave save = state.ToSave();
        SeasonState back = SeasonState.FromSave(save, table);

        Assert.AreEqual(4, back.Tier);
        Assert.AreEqual(42, back.SeasonSeed);
        Assert.AreEqual(TeamGenerator.Version, back.GeneratorVersion);
        Assert.IsTrue(back.MatchesGenerator);
        Assert.AreEqual(1, back.RoundsPlayed);
        Assert.AreEqual(2, back.ScoutAttempts);
        Assert.AreEqual(2, back.Results.Count);
        Assert.AreEqual(2, back.Results[0].HomeGoals);
        Assert.AreEqual(11, back.Roster.Count);
        for (int i = 0; i < 11; i++)
        {
            Assert.AreEqual(state.Roster[i].PlayerId, back.Roster[i].PlayerId);
            Assert.AreEqual(state.Roster[i].Stats.VariantId, back.Roster[i].Stats.VariantId);
            Assert.AreEqual(state.Roster[i].Stats.BuildTotal, back.Roster[i].Stats.BuildTotal);
            Assert.AreEqual(state.Roster[i].Stats.ShotBias, back.Roster[i].Stats.ShotBias, "다이얼은 PlayerTable에서 다시");
            Assert.AreEqual(state.Lineup[i].PlayerId, back.Lineup[i].PlayerId);
            Assert.AreEqual(state.Lineup[i].DefendX, back.Lineup[i].DefendX, 1e-4f);
        }
        Assert.AreEqual(11, back.MyLineup().Count);
    }

    [Test]
    public void 라인업이_로스터_밖_선수를_가리키면_로드에_실패한다()
    {
        SeasonSave save = SeasonState.NewSeason(4, 1, myRows, table).ToSave();
        save.lineup[0].playerId = 99;
        Assert.Throws<InvalidOperationException>(() => SeasonState.FromSave(save, table));

        SeasonSave bad = SeasonState.NewSeason(4, 1, myRows, table).ToSave();
        bad.roster[0].variantId = "없는_변형";
        Assert.Throws<InvalidOperationException>(() => SeasonState.FromSave(bad, table));

        SeasonSave dup = SeasonState.NewSeason(4, 1, myRows, table).ToSave();
        dup.roster[1].playerId = dup.roster[0].playerId;
        Assert.Throws<InvalidOperationException>(() => SeasonState.FromSave(dup, table), "선수 id 중복");

        SeasonSave tampered = SeasonState.NewSeason(4, 1, myRows, table).ToSave();
        tampered.roster[0].build[0] += 50;
        Assert.Throws<InvalidOperationException>(() => SeasonState.FromSave(tampered, table), "빌드 합계 300 아님(강화 없음)");

        SeasonSave shortLineup = SeasonState.NewSeason(4, 1, myRows, table).ToSave();
        shortLineup.lineup.RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() => SeasonState.FromSave(shortLineup, table), "11명 아님");
    }
}
