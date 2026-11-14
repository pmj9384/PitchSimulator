using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using NUnit.Framework;

// 내 팀 총점 = 소속 부 총점(스펙 §4-1 "같은 부 양 팀의 총점이 같다", 11-12 설계 `실험/2026-11-12-설계-내팀-총점.md`).
// 잠그는 것: 새 시즌·영입·승강 이월·세이브 로드 어디서나 내 선수 전원의 빌드 합계가 SeasonState.BuildTotal이고, 그 값은 TierRules.csv에서 온다.
// 1부에서도 내 팀에는 Rebuild(역할 묶음별 재분배)를 얹지 않는다(설계 D1): 재분배는 "상대 세팅의 질"이고 스카우트 ★4의 값이다
public class MyTeamTierTotalTests
{
    private static List<PlayerStats> table;
    private static List<FormationTemplate> formations;
    private static TeamNameTable names;
    private static List<TierRule> tiers;
    private static List<StageEntry> myRows;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        formations = FormationTemplateParser.Parse(File.ReadAllText("Assets/Resources/Tables/FormationTemplates.csv"));
        names = TeamNameTable.Parse(File.ReadAllText("Assets/Resources/Tables/TeamNames.csv"));
        tiers = TierRuleParser.Parse(File.ReadAllText("Assets/Resources/Tables/TierRules.csv"));
        myRows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1 && r.Team == 0);
    }

    private static TierRule Tier(int tier)
    {
        for (int i = 0; i < tiers.Count; i++)
        {
            if (tiers[i].Tier == tier) { return tiers[i]; }
        }
        Assert.Fail($"TierRules.csv에 {tier}부가 없다");
        return null;
    }

    private static PlayerStats TableVariant(string variantId)
    {
        for (int i = 0; i < table.Count; i++)
        {
            if (table[i].VariantId == variantId) { return table[i]; }
        }
        Assert.Fail($"PlayerTable에 {variantId}가 없다");
        return null;
    }

    private static int[] BuildOf(PlayerStats p)
    {
        return new[] { p.Speed, p.Stamina, p.Pass, p.Shot, p.Tackle, p.Positioning, p.Reflexes, p.Handling, p.Diving };
    }

    private static void AssertRosterTotal(SeasonState state, int expected, string label)
    {
        Assert.AreEqual(expected, state.BuildTotal, $"{label}: BuildTotal");
        foreach (RosterPlayer p in state.Roster)
        {
            Assert.AreEqual(expected, p.Stats.BuildTotal, $"{label}: 선수 {p.PlayerId}({p.Stats.VariantId})");
        }
    }

    private static PlayerStats StrikerOf(GeneratedTeam team)
    {
        for (int i = 0; i < team.Players.Count; i++)
        {
            if (team.Players[i].RoleId == "ST") { return team.Players[i]; }
        }
        Assert.Fail($"{team.Name}에 ST가 없다");
        return null;
    }

    [TestCase(4)]
    [TestCase(3)]
    [TestCase(2)]
    [TestCase(1)]
    public void 새_시즌_내_선수_전원의_빌드_합계는_소속_부_총점이다(int tier)
    {
        SeasonState state = SeasonState.NewSeason(tier, TestTierTotals.Of(tier), 7, myRows, table);

        AssertRosterTotal(state, Tier(tier).TotalPoints, $"{tier}부");
    }

    [Test]
    public void 같은_부에서_내_기본_선수의_빌드는_같은_변형의_상대_선수와_9칸_모두_같다()
    {
        SeasonState state = SeasonState.NewSeason(4, TestTierTotals.Of(4), 42, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(42, Tier(4), table, formations, names);

        int compared = 0;
        foreach (RosterPlayer mine in state.Roster)
        {
            foreach (PlayerStats theirs in opponents[0].Players)
            {
                if (theirs.VariantId != mine.Stats.VariantId) { continue; }
                Assert.AreEqual(BuildOf(theirs), BuildOf(mine.Stats), $"{mine.Stats.VariantId}: 같은 부 양 팀이 같다(스펙 §4-1)");
                compared++;
                break;
            }
        }
        Assert.Greater(compared, 0, "비교한 변형이 하나도 없으면 단언이 공허하다");
    }

    [Test]
    public void 위_부_선수를_영입하면_내_부_총점으로_줄어든다()
    {
        SeasonState state = SeasonState.NewSeason(4, TestTierTotals.Of(4), 42, myRows, table);
        PlayerStats upper = StrikerOf(TeamGenerator.Generate(42, Tier(3), table, formations, names)[0]);
        Assert.AreEqual(TestTierTotals.Of(3), upper.BuildTotal, "전제: 3부 선수");

        RosterPlayer added = state.AddToBench(upper, 2, portrait: 1);

        Assert.AreEqual(TestTierTotals.Of(4), added.Stats.BuildTotal, "내 부 총점으로");
        Assert.AreEqual(TestTierTotals.Of(3), upper.BuildTotal, "상대 선수 객체는 안 바뀐다");
    }

    [Test]
    public void 승격_이월_뒤_선발과_벤치_전원의_합계는_새_부_총점이다()
    {
        SeasonState previous = SeasonState.NewSeason(4, TestTierTotals.Of(4), 42, myRows, table);
        previous.AddToBench(StrikerOf(TeamGenerator.Generate(42, Tier(4), table, formations, names)[0]), 1, portrait: 1);

        SeasonState next = SeasonState.NewSeason(3, TestTierTotals.Of(3), 43, myRows, table);
        next.AdoptStarters(previous.Roster, previous.Lineup, table);
        next.AdoptBench(previous.Bench(), table);

        Assert.AreEqual(1, next.BenchCount, "전제: 벤치 선수도 넘어왔다");
        AssertRosterTotal(next, TestTierTotals.Of(3), "4 → 3부");
    }

    [Test]
    public void 강등_이월_뒤_전원의_합계는_새_부_총점이다()
    {
        SeasonState previous = SeasonState.NewSeason(3, TestTierTotals.Of(3), 42, myRows, table);

        SeasonState next = SeasonState.NewSeason(4, TestTierTotals.Of(4), 43, myRows, table);
        next.AdoptStarters(previous.Roster, previous.Lineup, table);

        AssertRosterTotal(next, TestTierTotals.Of(4), "3 → 4부");
    }

    [Test]
    public void 합계_300인_옛_4부_세이브는_로드되고_전원_부_총점이며_라인업과_별은_그대로다()
    {
        // 11-12 전 세이브: 내 선수는 표 원본(300)이었다
        SeasonState state = SeasonState.NewSeason(4, TestTierTotals.Of(4), 42, myRows, table);
        state.AddRound(new List<MatchResult> { new MatchResult(0, 1, 2, 1) });
        SeasonSave old = state.ToSave();
        foreach (SeasonSave.Player p in old.roster)
        {
            p.build = BuildOf(TableVariant(p.variantId));
        }
        Assert.AreEqual(PlayerStats.TableTotalPoints, TableVariant(old.roster[0].variantId).BuildTotal, "전제: 옛 세이브의 합계는 300");

        SeasonState loaded = TestTierTotals.Load(old, table);

        Assert.IsTrue(loaded.BuildsRescaledOnLoad, "다시 나눴다고 알린다(SeasonSystem.Load 경고)");
        AssertRosterTotal(loaded, TestTierTotals.Of(4), "옛 세이브");
        Assert.AreEqual(4, loaded.Tier, "부는 그대로");
        Assert.AreEqual(1, loaded.Results.Count, "결과는 그대로");
        Assert.AreEqual(2, loaded.Results[0].HomeGoals);
        Assert.AreEqual(1, loaded.RoundsPlayed);
        for (int i = 0; i < state.Lineup.Count; i++)
        {
            Assert.AreEqual(state.Lineup[i].PlayerId, loaded.Lineup[i].PlayerId, $"라인업 {i}");
            Assert.AreEqual(state.Lineup[i].AttackSlot, loaded.Lineup[i].AttackSlot, $"슬롯 {i}");
        }
        for (int i = 0; i < state.Roster.Count; i++)
        {
            Assert.AreEqual(state.Roster[i].Stars, loaded.Roster[i].Stars, $"★ {i}");
        }
    }

    [Test]
    public void 부_총점보다_합계가_낮은_세이브는_부_총점으로_늘려_받는다()
    {
        // TierRules 총점을 올린 뒤의 세이브: 3부(275) 세이브에 4부 값(260)이 들어 있는 경우
        SeasonSave lower = SeasonState.NewSeason(4, TestTierTotals.Of(4), 42, myRows, table).ToSave();
        lower.tier = 3;
        Assert.Less(TestTierTotals.Of(4), TestTierTotals.Of(3), "전제: 4부 총점 < 3부 총점");

        SeasonState loaded = TestTierTotals.Load(lower, table);

        Assert.IsTrue(loaded.BuildsRescaledOnLoad);
        AssertRosterTotal(loaded, TestTierTotals.Of(3), "늘린 세이브");
    }

    [Test]
    public void 합계가_맞는_세이브는_다시_나누지_않는다()
    {
        SeasonState state = SeasonState.NewSeason(4, TestTierTotals.Of(4), 42, myRows, table);

        SeasonState loaded = TestTierTotals.Load(state.ToSave(), table);

        Assert.IsFalse(loaded.BuildsRescaledOnLoad);
        Assert.AreEqual(BuildOf(state.Roster[0].Stats), BuildOf(loaded.Roster[0].Stats), "값 그대로");
    }

    [Test]
    public void 영입_선발_교체_방출_세이브_왕복_뒤에도_로스터_합계는_BuildTotal이다()
    {
        SeasonState state = SeasonState.NewSeason(4, TestTierTotals.Of(4), 42, myRows, table);
        PlayerStats striker = StrikerOf(TeamGenerator.Generate(42, Tier(3), table, formations, names)[0]);
        RosterPlayer added = state.AddToBench(striker, 2, portrait: 1);
        int starterSt = -1;
        foreach (LineupEntry e in state.Lineup)
        {
            if (state.FindRosterPlayer(e.PlayerId).Stats.RoleId == "ST") { starterSt = e.PlayerId; }
        }
        Assert.GreaterOrEqual(starterSt, 0, "전제: 선발 ST가 있다");

        state.SwapStarter(starterSt, added.PlayerId);
        AssertRosterTotal(state, TestTierTotals.Of(4), "선발 교체");
        state.SetFormations("4-3-3", "5-3-2", formations, table);
        AssertRosterTotal(state, TestTierTotals.Of(4), "포메이션 변경");
        state.Release(starterSt);
        AssertRosterTotal(state, TestTierTotals.Of(4), "방출");
        AssertRosterTotal(TestTierTotals.Load(state.ToSave(), table), TestTierTotals.Of(4), "세이브 왕복");
    }

    [Test]
    public void 최상위_부_내_선수에는_Rebuild를_얹지_않는다()
    {
        // 설계 D1: 1부 총점(300) = 표 합계라 내 선수는 표 원본 빌드 그대로다. 상대 1부 선수만 역할 묶음별로 재분배된다
        SeasonState state = SeasonState.NewSeason(1, TestTierTotals.Of(1), 7, myRows, table);
        Assert.AreEqual(PlayerStats.TableTotalPoints, TestTierTotals.Of(1), "전제: 1부 총점 = 표 합계");

        foreach (RosterPlayer p in state.Roster)
        {
            Assert.AreEqual(BuildOf(TableVariant(p.Stats.VariantId)), BuildOf(p.Stats), $"{p.Stats.VariantId}: 재분배 없음");
            Assert.AreNotSame(TableVariant(p.Stats.VariantId), p.Stats, "표 객체를 공유하지 않는다(복사본)");
        }
    }
}
