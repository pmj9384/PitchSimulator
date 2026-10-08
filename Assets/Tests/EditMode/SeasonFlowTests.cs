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

        Assert.IsTrue(SeasonProgress.IsOver(state, tier), "1위는 승강전 없이 끝");
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

    // 시즌 끝 판정(09-28 시즌 종료 표시). 헤드리스 경기 없이 결과만 넣어 판정 규칙을 잠근다. 라운드마다 내 경기 1개(상대 id = 라운드 + 1) + extra
    private static SeasonState FinishedSeason(int tier, int myGoals, int opponentGoals, List<MatchResult> extra = null)
    {
        TierRule rule = Tier(tier);
        SeasonState state = SeasonState.NewSeason(tier, 3, myRows, table);
        for (int round = 0; round < rule.Matches; round++)
        {
            var results = new List<MatchResult> { new MatchResult(SeasonState.MyTeamId, round + 1, myGoals, opponentGoals) };
            if (round == 0 && extra != null) { results.AddRange(extra); }
            state.AddRound(results);
        }
        return state;
    }

    [Test]
    public void 시즌_끝_판정은_승격_잔류_강등_우승을_가르고_다음_부와_같다()
    {
        SeasonOutcome promoted = SeasonProgress.Outcome(FinishedSeason(4, 2, 0), Tier(4));
        Assert.AreEqual(SeasonOutcomeKind.Promoted, promoted.Kind, "4부 전승 1위");
        Assert.AreEqual(1, promoted.FinalRank);
        Assert.AreEqual(3, promoted.NextTier);

        SeasonOutcome bottom = SeasonProgress.Outcome(FinishedSeason(4, 0, 2), Tier(4));
        Assert.AreEqual(SeasonOutcomeKind.Stayed, bottom.Kind, "4부 꼴찌는 강등 없이 잔류");
        Assert.AreEqual(Tier(4).Teams, bottom.FinalRank);
        Assert.AreEqual(4, bottom.NextTier);

        SeasonOutcome relegated = SeasonProgress.Outcome(FinishedSeason(3, 0, 2), Tier(3));
        Assert.AreEqual(SeasonOutcomeKind.Relegated, relegated.Kind, "3부 꼴찌 강등");
        Assert.AreEqual(4, relegated.NextTier);

        SeasonOutcome champion = SeasonProgress.Outcome(FinishedSeason(1, 2, 0), Tier(1));
        Assert.AreEqual(SeasonOutcomeKind.Champion, champion.Kind, "1부 1위는 우승(부 그대로)");
        Assert.AreEqual(1, champion.NextTier);

        // 전부 비겨 7점, 1번 팀이 2번 팀을 세 번 이겨 9점 → 내가 2위(승강전 대상). 승강전(10-08)이 남아 시즌은 아직 안 끝났고 판정도 못 낸다
        var extra = new List<MatchResult> { new MatchResult(1, 2, 1, 0), new MatchResult(1, 2, 1, 0), new MatchResult(1, 2, 1, 0) };
        SeasonState second = FinishedSeason(3, 1, 1, extra);
        Assert.IsTrue(SeasonProgress.IsRegularSeasonOver(second, Tier(3)));
        Assert.IsFalse(SeasonProgress.IsOver(second, Tier(3)), "2위는 승강 PO가 남았다");
        Assert.AreEqual(PlayoffStage.Semifinal, SeasonProgress.PendingStage(second, Tier(3)));
        Assert.Throws<System.InvalidOperationException>(() => SeasonProgress.Outcome(second, Tier(3)), "끝나기 전엔 판정이 없다");
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

        List<MatchResult> results = SeasonRunner.ComputeRound(computed.RoundsPlayed, computed.SeasonSeed, opponents, schedule, presets, myResult);
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
    public void 없는_프리셋은_거부하고_내_프리셋을_바꾸지_않는다()
    {
        // 09-30 팀 전술 설정창의 카드 선택. 조용히 balanced로 바꾸지 않는다(SeasonRunner.FindPreset과 같은 방식)
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        Assert.Throws<System.InvalidOperationException>(() => state.SetMyPreset("no_such_preset", presets));
        Assert.AreEqual(SeasonState.DefaultPresetId, state.MyPresetId);

        state.SetMyPreset("pressing", presets);
        Assert.AreEqual("pressing", state.MyPresetId);
    }

    [Test]
    public void 같은_라운드에서_프리셋만_바꾸면_내_전술만_바뀌고_시드_상대_킥오프는_같다()
    {
        // "막히면 재세팅"(스펙 축): 같은 경기를 다른 세팅으로 다시 치를 수 있어야 차이가 세팅 때문이라고 말할 수 있다
        TierRule tier = Tier(4);
        SeasonState state = SeasonState.NewSeason(4, 5, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(5, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);

        MatchSetup before = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, 0);
        state.SetMyPreset("pressing", presets);
        MatchSetup after = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, 0);

        Assert.AreEqual("balanced", before.Tactics0.PresetId);
        Assert.AreEqual("pressing", after.Tactics0.PresetId);
        Assert.AreEqual(before.Seed, after.Seed);
        Assert.AreEqual(before.KickoffTeam, after.KickoffTeam);
        Assert.AreEqual(before.OpponentName, after.OpponentName);
        Assert.AreEqual(before.Tactics1.PresetId, after.Tactics1.PresetId, "상대 전술은 그대로");
    }

    [Test]
    public void 표로_바꾼_내_전술은_경기에_들어가고_세이브를_왕복한다()
    {
        // 09-30 팀 전술 설정창: 세이브 = 기준 카드 id + 바꾼 값 전체(myTactics). 경기 재료(Tactics0)는 바꾼 값
        TierRule tier = Tier(4);
        SeasonState state = SeasonState.NewSeason(4, 5, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(5, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);

        TeamTactics balanced = SeasonRunner.FindPreset(presets, "balanced");
        TeamTactics edited = TacticsEditing.ApplySlider(balanced, balanced, TacticSlider.Speed, 2);
        state.SetMyTactics(edited, presets);
        MatchSetup setup = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, 0);
        CollectionAssert.AreEqual(new[] { 2, 2, 2 }, setup.Tactics0.Tempo, "표로 바꾼 속도가 경기에");
        Assert.AreEqual("balanced", state.MyPresetId, "기준 카드는 그대로");

        SeasonState loaded = SeasonState.FromSave(state.ToSave(), table);
        CollectionAssert.AreEqual(new[] { 2, 2, 2 }, loaded.MyTactics(presets).Tempo, "세이브 왕복 뒤에도");
    }

    [Test]
    public void 옛_세이브엔_바꾼_값이_없어_카드_값을_쓰고_카드를_고르면_바꾼_값이_지워진다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table, "counter");
        SeasonSave save = state.ToSave();
        Assert.IsNull(save.myTactics, "안 바꿨으면 저장하지 않는다(09-27 세이브와 같은 모양)");
        Assert.IsTrue(TacticsEditing.SameValues(SeasonRunner.FindPreset(presets, "counter"), SeasonState.FromSave(save, table).MyTactics(presets)));

        TeamTactics counter = SeasonRunner.FindPreset(presets, "counter");
        state.SetMyTactics(TacticsEditing.ApplySlider(counter, counter, TacticSlider.Forward, 1), presets);
        Assert.IsNotNull(state.MyCustomTactics);
        state.SetMyPreset("pressing", presets);
        Assert.IsNull(state.MyCustomTactics, "카드를 고르면 아래 전부가 그 카드 값으로(목업 규칙)");
        Assert.IsTrue(TacticsEditing.SameValues(SeasonRunner.FindPreset(presets, "pressing"), state.MyTactics(presets)));
    }

    [Test]
    public void 범위_밖_전술_값이_든_세이브는_로드에서_거부한다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        TeamTactics balanced = SeasonRunner.FindPreset(presets, "balanced");
        state.SetMyTactics(TacticsEditing.ApplySlider(balanced, balanced, TacticSlider.Forward, 1), presets);
        SeasonSave save = state.ToSave();
        save.myTactics!.tempo[0] = 5;
        Assert.Throws<System.InvalidOperationException>(() => SeasonState.FromSave(save, table));
    }

    [Test]
    public void 슬라이더로_바꾼_값이_기준_카드와_같아지면_바꾼_값이_없는_상태다()
    {
        // 불변식: MyCustomTactics가 있으면 값이 기준 카드와 다르다. 슬라이더를 가운데로 되돌리면 카드 그대로
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        TeamTactics balanced = SeasonRunner.FindPreset(presets, "balanced");
        state.SetMyTactics(TacticsEditing.ApplySlider(balanced, balanced, TacticSlider.Speed, 1), presets);
        Assert.IsNotNull(state.MyCustomTactics);
        state.SetMyTactics(TacticsEditing.ApplySlider(state.MyTactics(presets), balanced, TacticSlider.Speed, 0), presets);
        Assert.IsNull(state.MyCustomTactics);
    }

    [Test]
    public void 표로_바꾼_값이_다른_카드와_같아지면_그_카드를_고른_것이_되고_슬라이더는_기준을_지킨다()
    {
        // 09-30 리뷰: 화면이 강조하는 카드와 슬라이더가 기준으로 삼는 카드가 어긋나면 안 된다
        TeamTactics pressing = SeasonRunner.FindPreset(presets, "pressing");

        SeasonState byTable = SeasonState.NewSeason(4, 42, myRows, table);
        byTable.SetMyTacticsFromTable(TacticsEditing.Copy(pressing), presets);
        Assert.AreEqual("pressing", byTable.MyPresetId, "표: 그 카드를 고른 것으로");
        Assert.IsNull(byTable.MyCustomTactics);

        SeasonState bySlider = SeasonState.NewSeason(4, 42, myRows, table);
        bySlider.SetMyTactics(TacticsEditing.Copy(pressing), presets);
        Assert.AreEqual("balanced", bySlider.MyPresetId, "슬라이더: 기준 카드는 그대로");
        Assert.IsNotNull(bySlider.MyCustomTactics, "기준 카드와 값이 다르니 사용자 설정");
    }

    // ── 개인 전술(09-30): 필드의 선수 칩을 눌러 역할과 개인 지시를 정한다

    private static int FirstPlayerOfRole(SeasonState state, string roleId)
    {
        for (int i = 0; i < state.Roster.Count; i++)
        {
            if (state.Roster[i].Stats.RoleId == roleId) { return state.Roster[i].PlayerId; }
        }
        Assert.Fail($"{roleId} 선수가 없다");
        return -1;
    }

    [Test]
    public void 역할을_바꾸면_지시_값은_새_역할_것이고_능력치는_그대로며_개인_지시는_지워진다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        int striker = FirstPlayerOfRole(state, "ST");
        PlayerStats before = state.FindRosterPlayer(striker).Stats;
        state.SetPlayerInstruction(striker, PlayerDial.PressRange, 2);

        state.SetPlayerRole(striker, "st_advanced", table);

        RosterPlayer after = state.FindRosterPlayer(striker);
        PlayerStats advanced = PlayerTableLookup.FindVariant(table, "st_advanced")!;
        Assert.AreEqual("st_advanced", after.Stats.VariantId);
        Assert.AreEqual(advanced.HoldUp, after.Stats.HoldUp, "지시 값은 새 역할 것");
        Assert.AreEqual(before.Shot, after.Stats.Shot, "능력치는 그대로(스탯은 전술 창에서 안 바뀐다)");
        Assert.AreEqual(before.Speed, after.Stats.Speed);
        Assert.IsFalse(after.HasInstructions, "개인 지시는 역할 기준이라 역할을 바꾸면 지워진다");
    }

    [Test]
    public void 다른_자리_역할과_없는_역할과_잠긴_역할은_거부한다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        int striker = FirstPlayerOfRole(state, "ST");
        string original = state.FindRosterPlayer(striker).Stats.VariantId;

        var withLocked = new List<PlayerStats>(table);
        PlayerStats locked = BuildScaler.Copy(PlayerTableLookup.FindVariant(table, "st_poacher")!);
        locked.VariantId = "st_locked_test";
        locked.Exposed = false;
        withLocked.Add(locked);

        Assert.Throws<System.InvalidOperationException>(() => state.SetPlayerRole(striker, "cb_centreback", table), "다른 자리");
        Assert.Throws<System.InvalidOperationException>(() => state.SetPlayerRole(striker, "no_such_role", table), "없는 역할");
        Assert.Throws<System.InvalidOperationException>(() => state.SetPlayerRole(striker, "st_locked_test", withLocked), "잠긴 역할");
        Assert.AreEqual(original, state.FindRosterPlayer(striker).Stats.VariantId, "거부하면 아무것도 안 바뀐다");
    }

    [Test]
    public void 개인_지시는_경기에_들어가고_세이브를_왕복하며_시드는_그대로다()
    {
        TierRule tier = Tier(4);
        SeasonState state = SeasonState.NewSeason(4, 5, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(5, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);
        int striker = FirstPlayerOfRole(state, "ST");
        float rolePress = state.FindRosterPlayer(striker).Stats.PressRange;
        MatchSetup before = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, 0);

        state.SetPlayerInstruction(striker, PlayerDial.PressRange, 2);
        MatchSetup after = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, 0);

        Assert.AreEqual(rolePress + 4f, after.Team0[striker].Stats.PressRange, 1e-4f, "경기 재료에는 지시가 반영된 값");
        Assert.AreEqual(rolePress, state.FindRosterPlayer(striker).Stats.PressRange, "로스터의 역할 값은 그대로(지시는 따로 든다)");
        Assert.AreEqual(before.Seed, after.Seed);

        SeasonSave save = state.ToSave();
        Assert.IsNull(save.roster[0].instructions, "지시가 없는 선수는 저장하지 않는다(09-27 세이브와 같은 모양)");
        SeasonState loaded = SeasonState.FromSave(save, table);
        Assert.AreEqual(2, loaded.FindRosterPlayer(striker).Instructions[(int)PlayerDial.PressRange]);
        Assert.AreEqual(rolePress + 4f, loaded.MyLineup()[striker].Stats.PressRange, 1e-4f);
    }

    [Test]
    public void 범위_밖_개인_지시나_골키퍼의_필드_지시는_거부하고_세이브에서도_거부한다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        int striker = FirstPlayerOfRole(state, "ST");
        int keeper = FirstPlayerOfRole(state, "GK");
        Assert.Throws<System.InvalidOperationException>(() => state.SetPlayerInstruction(striker, PlayerDial.PushUp, 3));
        Assert.Throws<System.InvalidOperationException>(() => state.SetPlayerInstruction(keeper, PlayerDial.PushUp, 1));
        Assert.Throws<System.InvalidOperationException>(() => state.SetPlayerInstruction(99, PlayerDial.PushUp, 1), "없는 선수");

        state.SetPlayerInstruction(striker, PlayerDial.PushUp, 1);
        SeasonSave save = state.ToSave();
        save.roster[striker].instructions![(int)PlayerDial.PushUp] = 7;
        Assert.Throws<System.InvalidOperationException>(() => SeasonState.FromSave(save, table));
    }

    [Test]
    public void 새_시즌은_이전_시즌의_역할과_개인_지시를_이어받는다()
    {
        // 새 시즌의 로스터는 기본 편성에서 다시 만든다. 그대로 두면 승격하자마자 개인 전술이 풀린다
        SeasonState previous = SeasonState.NewSeason(4, 42, myRows, table);
        int striker = FirstPlayerOfRole(previous, "ST");
        previous.SetPlayerRole(striker, "st_advanced", table);
        previous.SetPlayerInstruction(striker, PlayerDial.HoldUp, -1);

        SeasonState next = SeasonState.NewSeason(3, 43, myRows, table);
        next.AdoptPlayerTactics(previous.Roster, table);

        RosterPlayer carried = next.FindRosterPlayer(striker);
        Assert.AreEqual("st_advanced", carried.Stats.VariantId);
        Assert.AreEqual(-1, carried.Instructions[(int)PlayerDial.HoldUp]);
    }

    [Test]
    public void 업데이트로_잠긴_역할을_쓰던_선수는_새_시즌에도_그_역할과_개인_지시를_지킨다()
    {
        // 10-08: 타깃맨·폴스 9를 잠갔다. 잠그기 전 세이브의 선수가 새 시즌에서 포처로 조용히 돌아가면 "내 타깃맨이 사라졌다"가 된다(FM·FC는 준 것을 지운 적 없다)
        var tableWhenExposed = new List<PlayerStats>();
        foreach (PlayerStats row in table)
        {
            PlayerStats copy = row.Clone();
            if (copy.VariantId == "st_targetman") { copy.Exposed = true; }
            tableWhenExposed.Add(copy);
        }
        SeasonState previous = SeasonState.NewSeason(4, 42, myRows, tableWhenExposed);
        int striker = FirstPlayerOfRole(previous, "ST");
        previous.SetPlayerRole(striker, "st_targetman", tableWhenExposed);
        previous.SetPlayerInstruction(striker, PlayerDial.HoldUp, 2);

        SeasonState next = SeasonState.NewSeason(3, 43, myRows, table);
        next.AdoptPlayerTactics(previous.Roster, table);

        RosterPlayer carried = next.FindRosterPlayer(striker);
        Assert.AreEqual("st_targetman", carried.Stats.VariantId, "잠겨도 쓰던 역할은 남는다");
        Assert.AreEqual(2, carried.Instructions[(int)PlayerDial.HoldUp]);
        Assert.Throws<System.InvalidOperationException>(() => next.SetPlayerRole(striker, "st_targetman", table), "새로 고르는 건 여전히 막힌다");
    }

    // 다음 시즌으로 바꾼 전술을 이어 가는 것(SeasonSystem.StartNextSeason)은 여기서 못 잠근다: SeasonSystem이 SaveLoadSystem 싱글턴에 묶여 EditMode에서 못 만든다

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
