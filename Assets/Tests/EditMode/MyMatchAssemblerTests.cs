using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 내 경기 조립 단일화(11-06 S4-1). 인게임(MatchManager)·치트 헤드리스(SeasonSystem)가 둘 다 MyMatchAssembler.Create를 부른다.
// 여기서는 그 함수가 ① 같은 MatchSetup이면 같은 경기를 내고 ② 킥오프·선수 번호를 일정·라인업에서 정하고 ③ 인게임처럼 관측(TacticsProbe)을 붙여 돌려도 헤드리스와 같은 결과인지 잠근다
public class MyMatchAssemblerTests
{
    private static List<PlayerStats> table;
    private static List<FormationTemplate> formations;
    private static TeamNameTable names;
    private static List<TierRule> tiers;
    private static List<TeamTactics> presets;
    private static List<StageEntry> myRows;
    private static List<DefenseRole> defenseRoles;
    private static ThreatGrid threat;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        formations = FormationTemplateParser.Parse(File.ReadAllText("Assets/Resources/Tables/FormationTemplates.csv"));
        names = TeamNameTable.Parse(File.ReadAllText("Assets/Resources/Tables/TeamNames.csv"));
        tiers = TierRuleParser.Parse(File.ReadAllText("Assets/Resources/Tables/TierRules.csv"));
        presets = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));
        myRows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(IsMyStageOneRow);
        threat = ThreatTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/ThreatTable.csv"));
        defenseRoles = DefenseRoleParser.Parse(File.ReadAllText("Assets/Resources/Tables/DefenseRoles.csv"));   // 인게임(SeasonSystem.CurrentMatch)과 같은 재료로 내 라인업을 만든다
    }

    private static bool IsMyStageOneRow(StageEntry row)
    {
        return row.Stage == 1 && row.Team == 0;
    }

    private static TierRule Tier(int tier)
    {
        for (int i = 0; i < tiers.Count; i++)
        {
            if (tiers[i].Tier == tier) { return tiers[i]; }
        }
        Assert.Fail($"TierRules에 {tier}부가 없다");
        return null;
    }

    // 원정 경기 중 시드 홀짝 킥오프(옛 헤드리스 규칙)와 일정상 홈 킥오프가 갈리는 판. 두 규칙을 헷갈리면 테스트가 깨지게 이런 판을 고른다.
    // 경기 시드 홀짝 = 시즌 시드 + 라운드 + 상대 id(계수가 전부 홀수)라 시즌 시드 5의 원정 판은 전부 홀수로 우연히 맞는다. 그래서 시즌 시드를 돌며 찾는다
    private static MatchSetup AwaySetupWhereSeedParityDisagrees()
    {
        TierRule tier = Tier(4);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);
        for (int seasonSeed = 1; seasonSeed <= 10; seasonSeed++)
        {
            SeasonState state = SeasonState.NewSeason(4, seasonSeed, myRows, table);
            List<GeneratedTeam> opponents = TeamGenerator.Generate(seasonSeed, tier, table, formations, names);
            for (int round = 0; round < tier.Matches; round++)
            {
                MatchSetup setup = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, round, table, defenseRoles);
                if (setup.MyTeamIsHome) { continue; }
                if (setup.KickoffTeam == MatchAssembler.KickoffTeamBySeed(setup.Seed)) { continue; }
                return setup;
            }
        }
        Assert.Fail("원정이면서 시드 홀짝이 다른 판이 시즌 시드 1~10에 없다");
        return null;
    }

    // 슛 수(양 팀 합). 셈은 러너와 같은 MatchProbe에 맡긴다
    private static int RunAndCountShots(MatchSimulation sim, int ticks)
    {
        var probe = new MatchProbe(sim);
        probe.Run(ticks, MatchTuning.FixedStep);
        MatchSummary summary = probe.Summarize(0, string.Empty, string.Empty);
        return summary.HomeShots + summary.AwayShots;
    }

    [Test]
    public void 같은_MatchSetup을_두_번_조립하면_같은_틱_뒤_골_슛_좌표가_같다()
    {
        MatchSetup setup = AwaySetupWhereSeedParityDisagrees();
        MatchSimulation a = MyMatchAssembler.Create(setup, threat);
        MatchSimulation b = MyMatchAssembler.Create(setup, threat);
        int ticks = a.TotalTicks / 2;   // 하프타임 직전까지. 전반 내내 같은 궤도면 조립의 모든 입력이 같다
        int shotsA = RunAndCountShots(a, ticks);
        int shotsB = RunAndCountShots(b, ticks);

        Assert.Greater(shotsA, 0, "슛이 0이면 주사위를 안 굴려 비교가 약하다");
        Assert.AreEqual(shotsA, shotsB, "슛 수");
        Assert.AreEqual(a.HomeGoals, b.HomeGoals, "팀 0 골");
        Assert.AreEqual(a.AwayGoals, b.AwayGoals, "팀 1 골");
        Assert.AreEqual(a.PassCount, b.PassCount, "패스 수");
        Assert.AreEqual(a.Ball.X, b.Ball.X, "공 X");
        Assert.AreEqual(a.Ball.Z, b.Ball.Z, "공 Z");
        Assert.AreEqual(a.Players.Count, b.Players.Count);
        for (int i = 0; i < a.Players.Count; i++)
        {
            Assert.AreEqual(a.Players[i].X, b.Players[i].X, $"#{i} X");
            Assert.AreEqual(a.Players[i].Z, b.Players[i].Z, $"#{i} Z");
        }
    }

    [Test]
    public void 조립은_일정상_홈에게_킥오프를_주고_선수_번호는_팀0_라인업_순이다()
    {
        MatchSetup setup = AwaySetupWhereSeedParityDisagrees();
        MatchSimulation sim = MyMatchAssembler.Create(setup, threat);

        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase, "킥오프는 조립에서 끝난다(인게임 StartMatch는 화면만 맞춘다)");
        Assert.AreEqual(setup.KickoffTeam, sim.Players[sim.Ball.OwnerId].Team, "원정이면 상대(팀 1)가 킥오프. 시드 홀짝이 아니다");
        Assert.AreSame(setup.Tactics0, sim.TacticsOf(0), "내 전술");
        Assert.AreSame(setup.Tactics1, sim.TacticsOf(1), "상대 전술");
        Assert.AreEqual(setup.Team0.Count + setup.Team1.Count, sim.Players.Count);
        for (int i = 0; i < sim.Players.Count; i++)
        {
            PlayerState player = sim.Players[i];
            Assert.AreEqual(i, player.PlayerId, "번호 = 등록 순서");
            Assert.AreEqual(i < setup.Team0.Count ? 0 : 1, player.Team, "팀 0(나) 먼저");
            LineupSlot slot = MyMatchAssembler.SlotOf(setup, player);
            Assert.AreSame(slot.Stats, player.Stats, $"#{i} 뷰가 붙는 칸 = 상태를 만든 칸");
            Assert.AreEqual(slot.AttackX, player.AttackHomeX, $"#{i} 공격 자리 X");
            Assert.AreEqual(slot.DefendZ, player.DefendHomeZ, $"#{i} 수비 자리 Z");
        }
    }

    // 인게임 루프(MatchManager.FixedUpdate)는 조립 뒤 TacticsProbe를 붙이고 틱마다 Sample을 부른다. 치트·러너는 MatchProbe.Run.
    // 관측은 규칙을 안 건드리므로 두 경로의 끝 결과가 같아야 한다(1차 헤드리스 = 2차 재생의 전제)
    [Test]
    public void 인게임처럼_관측을_붙여_돌려도_헤드리스와_같은_경기가_끝난다()
    {
        MatchSetup setup = AwaySetupWhereSeedParityDisagrees();

        MatchSimulation inGame = MyMatchAssembler.Create(setup, threat);
        var myProbe = new TacticsProbe(inGame, 0);
        for (int i = 0; i < inGame.TotalTicks; i++)
        {
            inGame.Tick(MatchTuning.FixedStep);
            myProbe.Sample();
        }

        MatchSimulation headless = MyMatchAssembler.Create(setup, threat);
        var probe = new MatchProbe(headless);
        probe.Run(headless.TotalTicks, MatchTuning.FixedStep);

        Assert.AreEqual(headless.TotalTicks, inGame.TotalTicks);
        Assert.AreEqual(headless.HomeGoals, inGame.HomeGoals, "팀 0 골");
        Assert.AreEqual(headless.AwayGoals, inGame.AwayGoals, "팀 1 골");
        Assert.AreEqual(headless.PassCount, inGame.PassCount, "패스 수");
        Assert.AreEqual(headless.Ball.X, inGame.Ball.X, "공 X");
        Assert.AreEqual(headless.Ball.Z, inGame.Ball.Z, "공 Z");
    }
}
