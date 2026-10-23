using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 유니폼 번호·경기 얼굴(10-21 선수 모델 교체): ①내 팀은 0, 상대는 1~KitCount-1 ②같은 (시드, 부)의 상대 7팀까지 서로 다르다 ③같은 입력 = 같은 번호
// ④이번 경기 세팅의 Portrait는 내 선수 = 로스터 개체 값, 상대 = 스카우트와 같은 (팀, 번호) 규칙, Kit1 = KitRules
public class KitRulesTests
{
    private static List<PlayerStats> table;
    private static List<FormationTemplate> formations;
    private static TeamNameTable names;
    private static List<TierRule> tiers;
    private static List<StageEntry> myRows;
    private static List<TeamTactics> presets;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        formations = FormationTemplateParser.Parse(File.ReadAllText("Assets/Resources/Tables/FormationTemplates.csv"));
        names = TeamNameTable.Parse(File.ReadAllText("Assets/Resources/Tables/TeamNames.csv"));
        tiers = TierRuleParser.Parse(File.ReadAllText("Assets/Resources/Tables/TierRules.csv"));
        myRows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1 && r.Team == 0);
        presets = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));
    }

    [Test]
    public void 상대_번호는_1부터이고_같은_부_7팀까지_서로_다르다()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            for (int tier = 1; tier <= 4; tier++)
            {
                var seen = new HashSet<int>();
                for (int teamId = 1; teamId <= KitRules.KitCount - 1; teamId++)
                {
                    int kit = KitRules.Of(seed, tier, teamId);
                    Assert.AreNotEqual(KitRules.MyKit, kit, $"seed {seed} tier {tier} team {teamId}: 상대가 내 번호");
                    Assert.IsTrue(KitRules.IsValid(kit));
                    Assert.IsTrue(seen.Add(kit), $"seed {seed} tier {tier}: 유니폼 번호 {kit} 중복");
                }
            }
        }
        Assert.AreEqual(KitRules.Of(7, 4, 3), KitRules.Of(7, 4, 3));
    }

    [Test]
    public void 같은_부_7팀_밖의_팀_번호와_음수도_유효한_상대_번호가_된다()
    {
        // 승강전 상대는 다른 부 팀이라 번호가 8 이상일 수 있다(SeasonRunner). 1~7 안으로 돌아야지 범위 밖이나 내 번호(0)가 나오면 안 된다
        for (int teamId = -3; teamId <= 20; teamId++)
        {
            int kit = KitRules.Of(11, 2, teamId);
            Assert.IsTrue(KitRules.IsValid(kit), $"team {teamId}: {kit}");
            Assert.AreNotEqual(KitRules.MyKit, kit, $"team {teamId}: 내 번호");
        }
        Assert.AreEqual(KitRules.Of(11, 2, 1), KitRules.Of(11, 2, 8), "7팀마다 같은 번호로 돈다");
    }

    [Test]
    public void 경기_세팅의_얼굴과_유니폼은_로스터와_규칙을_따른다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        TierRule tier = tiers.Find(t => t.Tier == 4);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(42, tier, table, formations, names);
        List<Fixture> schedule = SeasonSchedule.RoundRobin(tier.Teams);
        MatchSetup setup = SeasonRunner.SetupMyMatch(state, opponents, schedule, presets, 0);

        Assert.AreEqual(KitRules.MyKit, setup.Kit0);
        int opponentId = setup.Fixture.HomeTeamId == SeasonState.MyTeamId ? setup.Fixture.AwayTeamId : setup.Fixture.HomeTeamId;
        Assert.AreEqual(KitRules.Of(42, 4, opponentId), setup.Kit1);

        for (int i = 0; i < setup.Team0.Count; i++)
        {
            Assert.AreEqual(state.FindRosterPlayer(state.Lineup[i].PlayerId).Portrait, setup.Team0[i].Portrait);
        }
        for (int i = 0; i < setup.Team1.Count; i++)
        {
            Assert.AreEqual(PortraitRules.Of(opponentId, i), setup.Team1[i].Portrait);
            Assert.IsTrue(PortraitRules.IsValid(setup.Team1[i].Portrait));
        }
    }
}
