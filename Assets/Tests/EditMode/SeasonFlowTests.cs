using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 시즌을 게임 흐름에 잇는 순수 판정(09-27 확정 스펙 ①②③): 경기 시드·내 경기 조립·홈/원정 결과 변환·시즌 종료와 다음 부·세이브의 프리셋·새 시즌 조건.
// 매니저(StageManager·MatchManager·SeasonSystem)는 여기서 잠근 함수를 부르기만 한다
public class SeasonFlowTests
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

    private static float KeeperX(IReadOnlyList<LineupSlot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (string.Equals(slots[i].Stats.RoleId, "GK", System.StringComparison.OrdinalIgnoreCase)) { return slots[i].AttackX; }
        }
        Assert.Fail("GK가 없다");
        return 0f;
    }

    [Test]
    public void 같은_시즌_시드와_일정이면_같은_경기_시드고_경기마다_다르다()
    {
        var a = new Fixture(0, 0, 3);
        Assert.AreEqual(SeasonRunner.MatchSeed(11, a), SeasonRunner.MatchSeed(11, a));
        Assert.AreNotEqual(SeasonRunner.MatchSeed(11, a), SeasonRunner.MatchSeed(12, a), "시즌 시드가 다르면");
        Assert.AreNotEqual(SeasonRunner.MatchSeed(11, a), SeasonRunner.MatchSeed(11, new Fixture(1, 0, 3)), "라운드가 다르면");
        Assert.AreNotEqual(SeasonRunner.MatchSeed(11, a), SeasonRunner.MatchSeed(11, new Fixture(0, 3, 0)), "홈·원정이 바뀌면");
    }

    [Test]
    public void 내_경기_조립은_내가_팀0이고_킥오프는_일정상_홈이_한다()
    {
        TierRule tier = Tier(4);
        SeasonState state = SeasonState.NewSeason(4, 5, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(5, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);

        int sawHome = 0;
        int sawAway = 0;
        for (int round = 0; round < tier.Matches; round++)
        {
            MatchSetup setup = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, round);
            Assert.AreEqual(11, setup.Team0.Count, "팀 0 = 내 라인업");
            Assert.AreEqual(11, setup.Team1.Count, "팀 1 = 상대");
            Assert.AreEqual(state.MyLineup()[0].Stats.VariantId, setup.Team0[0].Stats.VariantId);
            Assert.AreEqual("balanced", setup.Tactics0.PresetId, "새 시즌 기본 프리셋");
            Assert.AreEqual(SeasonRunner.MatchSeed(5, setup.Fixture), setup.Seed);
            Assert.Less(KeeperX(setup.Team0), 0f, "나는 항상 -X 진영에서 시작");
            Assert.Greater(KeeperX(setup.Team1), 0f, "상대는 +X 진영");
            Assert.AreEqual(setup.MyTeamIsHome ? 0 : 1, setup.KickoffTeam);
            Assert.IsFalse(string.IsNullOrEmpty(setup.OpponentName));
            if (setup.MyTeamIsHome) { sawHome++; } else { sawAway++; }
        }
        Assert.Greater(sawHome, 0);
        Assert.Greater(sawAway, 0, "5경기면 홈·원정이 섞인다");
    }

    [Test]
    public void 내가_원정이면_시뮬_골이_일정의_홈_원정으로_뒤집혀_들어간다()
    {
        var away = new MatchSetup(new Fixture(2, 4, SeasonState.MyTeamId), 0, new List<LineupSlot>(), new List<LineupSlot>(), presets[0], presets[0], "상대");
        MatchResult r = away.ResultFor(team0Goals: 0, team1Goals: 1);
        Assert.AreEqual(4, r.HomeTeamId);
        Assert.AreEqual(SeasonState.MyTeamId, r.AwayTeamId);
        Assert.AreEqual(1, r.HomeGoals, "상대(홈) 골 = 시뮬 팀 1 골");
        Assert.AreEqual(0, r.AwayGoals, "내(원정) 골 = 시뮬 팀 0 골");
        Assert.AreEqual(1, away.KickoffTeam, "원정이면 상대가 킥오프");

        var home = new MatchSetup(new Fixture(0, SeasonState.MyTeamId, 2), 0, away.Team0, away.Team1, presets[0], presets[0], "상대");
        MatchResult h = home.ResultFor(3, 2);
        Assert.AreEqual(SeasonState.MyTeamId, h.HomeTeamId);
        Assert.AreEqual(3, h.HomeGoals);
        Assert.AreEqual(2, h.AwayGoals);
        Assert.AreEqual(0, home.KickoffTeam);
    }

    [Test]
    public void 마지막_라운드가_끝나면_시즌이_끝나고_사부_1위는_다음_시즌_삼부다()
    {
        TierRule tier = Tier(4);
        SeasonState state = SeasonState.NewSeason(4, 11, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(11, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);

        for (int round = 0; round < tier.Matches; round++)
        {
            Assert.IsFalse(SeasonProgress.IsOver(state, tier), $"라운드 {round} 전엔 안 끝남");
            MatchSetup setup = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, round);
            SeasonRunner.PlayRound(state, opponents, schedule, presets, setup.ResultFor(2, 0));   // 내가 다 이긴 걸로
        }

        Assert.IsTrue(SeasonProgress.IsOver(state, tier));
        Assert.AreEqual(3, SeasonProgress.NextTier(state, tier), "4부 1위 → 3부");
        Assert.AreEqual(tier.Teams * (tier.Teams - 1) / 2, state.Results.Count, "라운드마다 전 경기가 한 번에 들어간다");
    }

    [Test]
    public void 다_지면_사부는_강등이_없어_그대로_사부다()
    {
        TierRule tier = Tier(4);
        SeasonState state = SeasonState.NewSeason(4, 11, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(11, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);
        for (int round = 0; round < tier.Matches; round++)
        {
            MatchSetup setup = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, round);
            SeasonRunner.PlayRound(state, opponents, schedule, presets, setup.ResultFor(0, 3));
        }
        Assert.AreEqual(4, SeasonProgress.NextTier(state, tier));
    }

    // 인게임은 ComputeRound를 백그라운드 스레드에서 부르고 AddRound는 메인 스레드에서 따로 한다(09-28 G1). 계산이 상태를 건드리면 스레드 경합이 되고,
    // 두 경로의 결과가 다르면 인게임 승점표와 테스트가 잠근 승점표가 갈린다
    [Test]
    public void ComputeRound는_상태를_바꾸지_않고_PlayRound와_같은_결과를_낸다()
    {
        TierRule tier = Tier(4);
        SeasonState computed = SeasonState.NewSeason(4, 7, myRows, table);
        SeasonState played = SeasonState.NewSeason(4, 7, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(7, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);
        MatchResult myResult = SeasonRunner.SetupMyMatch(computed, opponents, schedule, presets, 0).ResultFor(1, 0);

        List<MatchResult> results = SeasonRunner.ComputeRound(computed, opponents, schedule, presets, myResult);
        Assert.AreEqual(0, computed.RoundsPlayed, "계산만 하고 라운드를 넘기지 않는다");
        Assert.AreEqual(0, computed.Results.Count, "결과도 넣지 않는다");
        Assert.AreEqual(tier.Teams / 2, results.Count, "라운드의 전 경기");
        Assert.AreEqual(myResult, results[0], "내 결과가 맨 앞");

        SeasonRunner.PlayRound(played, opponents, schedule, presets, myResult);
        Assert.AreEqual(1, played.RoundsPlayed);
        CollectionAssert.AreEqual(results, played.Results, "같은 시드면 헤드리스 경기 결과도 같다");
    }

    [Test]
    public void 내_프리셋은_세이브를_왕복하고_옛_세이브엔_없어_balanced가_된다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table, "counter");
        Assert.AreEqual("counter", state.MyPresetId);
        SeasonSave save = state.ToSave();
        Assert.AreEqual("counter", save.myPresetId);
        Assert.AreEqual("counter", SeasonState.FromSave(save, table).MyPresetId);

        save.myPresetId = null;   // 09-26 세이브엔 이 필드가 없다
        Assert.AreEqual(SeasonState.DefaultPresetId, SeasonState.FromSave(save, table).MyPresetId);
    }

    [Test]
    public void 세이브가_없거나_생성기_버전이_다르면_새_시즌이_필요하다()
    {
        Assert.IsTrue(SeasonProgress.NeedsNewSeason(null), "세이브 없음");
        SeasonSave save = SeasonState.NewSeason(4, 1, myRows, table).ToSave();
        Assert.IsFalse(SeasonProgress.NeedsNewSeason(save), "버전 일치면 이어 간다");
        save.generatorVersion = TeamGenerator.Version + 1;
        Assert.IsTrue(SeasonProgress.NeedsNewSeason(save), "생성기가 바뀌었으면 상대 팀을 재현 못 하니 새 시즌");
    }
}
