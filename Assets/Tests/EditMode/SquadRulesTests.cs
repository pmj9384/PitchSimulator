using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Tactics;
using NUnit.Framework;

// 스쿼드(스펙 §4-5 「10-19 스쿼드」): ①벤치는 7까지, 차면 던진다 ②방출은 벤치만, 선발은 던진다, 코인 = ★ × 30 ③선발 교체는 같은 자리만, 자리 2쌍은 슬롯에 남고 개인 지시는 선수를 따라간다
// ④옛 세이브(★ 없음)는 벤치 빔·★1 ⑤다음 시즌 벤치 이월은 벤치 상한을 넘기지 않는다
public class SquadRulesTests
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

    private static PlayerStats OpponentPlayer(string roleId)
    {
        List<GeneratedTeam> teams = TeamGenerator.Generate(42, tiers.Find(t => t.Tier == 4), table, formations, names);
        foreach (PlayerStats p in teams[0].Players)
        {
            if (p.RoleId == roleId) { return p; }
        }
        Assert.Fail($"상대 팀에 {roleId}가 없다");
        return null;
    }

    private static int MyStarterOf(SeasonState state, string roleId)
    {
        for (int i = 0; i < state.Lineup.Count; i++)
        {
            int id = state.Lineup[i].PlayerId;
            if (state.FindRosterPlayer(id).Stats.RoleId == roleId) { return id; }
        }
        Assert.Fail($"내 선발에 {roleId}가 없다");
        return -1;
    }

    [Test]
    public void 벤치는_7명까지고_차면_던진다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        PlayerStats st = OpponentPlayer("ST");
        for (int i = 0; i < SquadRules.MaxBench; i++) { state.AddToBench(st, 1, portrait: 1); }
        Assert.AreEqual(SquadRules.MaxBench, state.BenchCount);
        Assert.IsFalse(SquadRules.BenchHasRoom(state.BenchCount));
        Assert.Throws<System.InvalidOperationException>(() => state.AddToBench(st, 1, portrait: 1));
        Assert.AreEqual(11 + SquadRules.MaxBench, state.Roster.Count);
    }

    [Test]
    public void 방출은_벤치만_되고_선발은_던지며_코인은_별_곱_30이다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        RosterPlayer added = state.AddToBench(OpponentPlayer("CB"), 4, portrait: 1);
        int starter = MyStarterOf(state, "CB");

        Assert.Throws<System.InvalidOperationException>(() => state.Release(starter), "선발은 먼저 벤치로 내린다");
        RosterPlayer released = state.Release(added.PlayerId);
        Assert.AreEqual(added.PlayerId, released.PlayerId);
        Assert.AreEqual(0, state.BenchCount);
        Assert.AreEqual(11, state.Roster.Count);
        Assert.AreEqual(120, SquadRules.ReleaseCoins(released.Stars));
        Assert.Throws<System.InvalidOperationException>(() => state.FindRosterPlayer(added.PlayerId), "로스터에서 빠졌다");
    }

    [Test]
    public void 선발_교체는_같은_자리만_되고_자리_2쌍은_슬롯에_남고_개인_지시는_선수를_따라간다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        RosterPlayer benchSt = state.AddToBench(OpponentPlayer("ST"), 1, portrait: 1);
        int starterSt = MyStarterOf(state, "ST");
        int starterGk = MyStarterOf(state, "GK");
        state.SetPlayerInstruction(starterSt, PlayerDial.PushUp, 1);
        int slot = -1;
        for (int i = 0; i < state.Lineup.Count; i++) { if (state.Lineup[i].PlayerId == starterSt) { slot = i; } }
        LineupEntry before = state.Lineup[slot];

        Assert.Throws<System.InvalidOperationException>(() => state.SwapStarter(starterGk, benchSt.PlayerId), "GK 자리에 ST는 못 선다");
        state.SwapStarter(starterSt, benchSt.PlayerId);

        Assert.AreEqual(benchSt.PlayerId, state.Lineup[slot].PlayerId, "그 슬롯에 벤치 선수가 선다");
        Assert.AreEqual(before.AttackX, state.Lineup[slot].AttackX, "자리 2쌍은 슬롯에 남는다");
        Assert.AreEqual(before.DefendZ, state.Lineup[slot].DefendZ);
        Assert.IsTrue(state.IsStarter(benchSt.PlayerId));
        Assert.IsFalse(state.IsStarter(starterSt), "내려간 선수는 벤치");
        Assert.IsTrue(state.FindRosterPlayer(starterSt).HasInstructions, "개인 지시는 선수를 따라간다");
        Assert.IsFalse(state.FindRosterPlayer(benchSt.PlayerId).HasInstructions);
        Assert.AreEqual(1, state.BenchCount);
        Assert.AreEqual(11, state.MyLineup().Count, "경기에 들어가는 라인업은 여전히 11");

        Assert.Throws<System.InvalidOperationException>(() => state.SwapStarter(starterSt, benchSt.PlayerId), "벤치 선수를 선발 자리에 넣는 방향만 된다");
    }

    [Test]
    public void 옛_세이브는_별이_없어_1이고_벤치가_비며_새_세이브는_벤치와_별이_왕복한다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        SeasonSave old = state.ToSave();
        for (int i = 0; i < old.roster.Count; i++) { old.roster[i].stars = 0; }   // 10-19 전 세이브엔 이 필드가 없다
        SeasonState fromOld = SeasonState.FromSave(old, table);
        Assert.AreEqual(0, fromOld.BenchCount);
        for (int i = 0; i < fromOld.Roster.Count; i++) { Assert.AreEqual(SquadRules.DefaultStars, fromOld.Roster[i].Stars); }

        state.AddToBench(OpponentPlayer("ST"), 3, portrait: 1);
        state.AddToBench(OpponentPlayer("CB"), 2, portrait: 1);
        SeasonState back = SeasonState.FromSave(state.ToSave(), table);
        Assert.AreEqual(2, back.BenchCount);
        Assert.AreEqual(13, back.Roster.Count);
        List<RosterPlayer> bench = back.Bench();
        Assert.AreEqual(3, bench[0].Stars);
        Assert.AreEqual(2, bench[1].Stars);
    }

    [Test]
    public void 역할_변경과_개인_지시_설정_해제를_거쳐도_별은_남는다()
    {
        // 10-19 리뷰 🔴: RosterPlayer를 손으로 다시 만들던 4곳이 ★을 빠뜨려 1로 떨어졌다
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        RosterPlayer added = state.AddToBench(OpponentPlayer("ST"), 3, portrait: 1);
        state.SetPlayerInstruction(added.PlayerId, PlayerDial.PushUp, 1);
        Assert.AreEqual(3, state.FindRosterPlayer(added.PlayerId).Stars, "지시 설정 뒤");
        state.ClearPlayerInstructions(added.PlayerId);
        Assert.AreEqual(3, state.FindRosterPlayer(added.PlayerId).Stars, "지시 해제 뒤");
        state.SetPlayerRole(added.PlayerId, "st_advanced", table);
        Assert.AreEqual(3, state.FindRosterPlayer(added.PlayerId).Stars, "역할 변경 뒤");
    }

    [Test]
    public void 스왑으로_선발이_된_영입_선수는_슬롯_기준으로_다음_시즌에_이월되고_내려간_선수는_벤치로만_간다()
    {
        // 10-19 리뷰 🔴: 이월이 선수 id 기준이라 선발에 선 영입 선수(id ≥ 11)가 사라지고 내려간 기본 선수가 선발과 벤치에 중복됐다
        SeasonState previous = SeasonState.NewSeason(4, 42, myRows, table);
        List<GeneratedTeam> upperTeams = TeamGenerator.Generate(42, tiers.Find(t => t.Tier == 3), table, formations, names);
        PlayerStats otherSt = null;
        foreach (GeneratedTeam team in upperTeams)
        {
            foreach (PlayerStats p in team.Players)
            {
                if (p.RoleId == "ST" && p.VariantId != previous.FindRosterPlayer(MyStarterOf(previous, "ST")).Stats.VariantId) { otherSt = p; }
            }
        }
        Assert.IsNotNull(otherSt, "3부에 다른 변형의 ST가 있어야 한다");
        RosterPlayer recruit = previous.AddToBench(otherSt, 4, portrait: 1);
        int starterSt = MyStarterOf(previous, "ST");
        int slot = -1;
        for (int i = 0; i < previous.Lineup.Count; i++) { if (previous.Lineup[i].PlayerId == starterSt) { slot = i; } }
        previous.SwapStarter(starterSt, recruit.PlayerId);
        previous.SetPlayerInstruction(recruit.PlayerId, PlayerDial.PushUp, 1);
        int attackSlot = previous.Lineup[slot].AttackSlot;

        SeasonState next = SeasonState.NewSeason(3, 43, myRows, table);
        next.AdoptStarters(previous.Roster, previous.Lineup, table);
        next.AdoptBench(previous.Bench(), table);

        RosterPlayer carried = next.FindRosterPlayer(next.Lineup[slot].PlayerId);
        Assert.AreEqual(otherSt.VariantId, carried.Stats.VariantId, "그 슬롯의 역할은 영입 선수 것");
        Assert.AreEqual(4, carried.Stars, "★도 따라온다");
        Assert.AreEqual(1, carried.Instructions[(int)PlayerDial.PushUp], "개인 지시도");
        Assert.AreEqual(attackSlot, next.Lineup[slot].AttackSlot, "공격 슬롯도 슬롯 기준");
        Assert.AreEqual(1, next.BenchCount, "내려간 기본 선수 한 명만 벤치로");
        Assert.AreEqual(1, next.Bench()[0].Stars);
        Assert.AreEqual(12, next.Roster.Count);
    }

    [Test]
    public void 다음_시즌_벤치_이월은_상한을_넘기지_않고_라인업에_없는_자리는_두고_온다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        PlayerStats st = OpponentPlayer("ST");
        for (int i = 0; i < SquadRules.MaxBench; i++) { state.AddToBench(st, 1, portrait: 1); }

        SeasonState next = SeasonState.NewSeason(3, 43, myRows, table);
        next.AddToBench(st, 1, portrait: 1);   // 새 시즌에 이미 한 명 있다고 치면
        next.AdoptBench(state.Bench(), table);
        Assert.AreEqual(SquadRules.MaxBench, next.BenchCount, "상한에서 멈춘다");
    }
}
