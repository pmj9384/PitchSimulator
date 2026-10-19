using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using NUnit.Framework;

// 스카우트 일괄 제시(스펙 §4-5 10-19). ①후보 4명·결정성·중복 없음·내 자리에 들어감 ②4부는 내 부 2 + 위 부 2, 1부는 전부 내 부
// ③위 부 후보의 별이 내 부(전원 ★1)보다 높다 = 4부의 변별 ④실패 기록은 후보를 안 바꾸고 성공한 자리만 바뀐다 ⑤같은 자리 성공 2회에 영입한 선수는 다시 안 뜬다
// ⑥후보에 없던 기록은 무시 ⑦위 부에 들어갈 선수가 2명 미만이면 전부 내 부 ⑧성공 비트는 세이브로 왕복
public class ScoutPoolTests
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
        return tiers.Find(t => t.Tier == tier);
    }

    private static List<GeneratedTeam> Own(int seed, int tier)
    {
        return TeamGenerator.Generate(seed, Tier(tier), table, formations, names);
    }

    private static GeneratedTeam Upper(int seed, int myTier)
    {
        return SeasonRunner.ScoutUpperTeam(seed, Tier(myTier - 1), table, formations, names);
    }

    private static PlayerStats PlayerOf(List<GeneratedTeam> own, GeneratedTeam upper, ScoutCandidate c)
    {
        GeneratedTeam team = c.UpperTier ? upper : SeasonRunner.Find(own, c.TeamId);
        return team.Players[c.PlayerIndex];
    }

    [Test]
    public void 후보는_4명이고_같은_시드면_같고_겹치지_않고_내_자리에_들어간다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        List<GeneratedTeam> own = Own(42, 4);
        GeneratedTeam upper = Upper(42, 4);
        List<ScoutCandidate> a = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);
        List<ScoutCandidate> b = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);

        Assert.AreEqual(ScoutPool.CandidateCount, a.Count);
        var seen = new HashSet<(int, int)>();
        for (int i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(i, a[i].Slot);
            Assert.IsTrue(a[i].Is(b[i].TeamId, b[i].PlayerIndex), $"자리 {i}는 같은 시드면 같은 후보");
            Assert.IsTrue(seen.Add((a[i].TeamId, a[i].PlayerIndex)), $"자리 {i}가 다른 자리와 겹친다");
            PlayerStats p = PlayerOf(own, upper, a[i]);
            Assert.IsTrue(state.FitsLineupRole(p), $"자리 {i}({p.RoleId})는 내 어느 자리에도 못 들어간다");
        }
    }

    [Test]
    public void 사부는_내_부_2_위_부_2이고_일부는_위_부가_없어_전부_내_부다()
    {
        SeasonState four = SeasonState.NewSeason(4, 7, myRows, table);
        List<ScoutCandidate> a = ScoutPool.Pick(7, four.Roster, Own(7, 4), Upper(7, 4), four.ScoutTargets);
        Assert.IsFalse(a[0].UpperTier);
        Assert.IsFalse(a[1].UpperTier);
        Assert.IsTrue(a[2].UpperTier);
        Assert.IsTrue(a[3].UpperTier);
        Assert.GreaterOrEqual(a[2].TeamId, PlayoffRules.ForeignTeamIdBase, "위 부 팀 id는 승점표 id와 안 겹친다");

        SeasonState one = SeasonState.NewSeason(1, 7, myRows, table);
        List<ScoutCandidate> b = ScoutPool.Pick(7, one.Roster, Own(7, 1), null, one.ScoutTargets);
        Assert.AreEqual(4, b.Count);
        for (int i = 0; i < b.Count; i++) { Assert.IsFalse(b[i].UpperTier, $"1부 자리 {i}"); }
    }

    [Test]
    public void 위_부_후보의_별은_내_부_후보보다_높다()
    {
        // 4부의 변별: TierRules 4부는 전원 ★1이고, 3부 선수는 상위 부 +1을 받아 최소 ★2
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        List<GeneratedTeam> own = Own(42, 4);
        GeneratedTeam upper = Upper(42, 4);
        List<ScoutCandidate> picked = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);

        for (int i = 0; i < picked.Count; i++)
        {
            ScoutCandidate c = picked[i];
            GeneratedTeam team = c.UpperTier ? upper : SeasonRunner.Find(own, c.TeamId);
            TierRule targetTier = Tier(c.UpperTier ? 3 : 4);
            int stars = ScoutRules.Stars(team.Players[c.PlayerIndex], team.Rows[c.PlayerIndex], targetTier, 4, table);
            if (c.UpperTier) { Assert.GreaterOrEqual(stars, 2, $"위 부 자리 {i}"); }
            else { Assert.AreEqual(1, stars, $"내 부 자리 {i}"); }
        }
    }

    [Test]
    public void 실패_기록은_후보를_안_바꾸고_성공한_자리만_다음_후보로_바뀐다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        List<GeneratedTeam> own = Own(42, 4);
        GeneratedTeam upper = Upper(42, 4);
        List<ScoutCandidate> before = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);

        state.SpendScoutAttempt(before[2].TeamId, before[2].PlayerIndex, success: false);
        List<ScoutCandidate> afterFail = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);
        for (int i = 0; i < 4; i++) { Assert.IsTrue(afterFail[i].Is(before[i].TeamId, before[i].PlayerIndex), $"실패 뒤 자리 {i}"); }

        state.SpendScoutAttempt(before[2].TeamId, before[2].PlayerIndex, success: true);
        state.AddToBench(upper.Players[before[2].PlayerIndex], 3);   // 실제 영입(벤치 추가)까지: 로스터가 늘어도 풀(라인업 자리 집합)은 그대로여야 한다
        List<ScoutCandidate> afterSuccess = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);
        Assert.IsFalse(afterSuccess[2].Is(before[2].TeamId, before[2].PlayerIndex), "성공한 자리는 다음 후보");
        Assert.IsTrue(afterSuccess[2].UpperTier, "같은 풀(위 부)에서 뽑는다");
        for (int i = 0; i < 4; i++)
        {
            if (i == 2) { continue; }
            Assert.IsTrue(afterSuccess[i].Is(before[i].TeamId, before[i].PlayerIndex), $"성공 뒤 다른 자리 {i}는 그대로");
        }
    }

    [Test]
    public void 후보에_없던_선수의_성공_기록은_후보를_안_바꾼다()
    {
        // 일괄 제시 전(10-12 설계) 세이브의 기록: 팀 11명 중 아무나 지목할 수 있었다
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        List<GeneratedTeam> own = Own(42, 4);
        GeneratedTeam upper = Upper(42, 4);
        List<ScoutCandidate> before = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);

        int strangerTeam = own[0].TeamId;
        int strangerIndex = 0;
        while (IsCandidate(before, strangerTeam, strangerIndex)) { strangerIndex++; }
        state.SpendScoutAttempt(strangerTeam, strangerIndex, success: true);
        List<ScoutCandidate> after = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);
        for (int i = 0; i < 4; i++) { Assert.IsTrue(after[i].Is(before[i].TeamId, before[i].PlayerIndex), $"자리 {i}"); }
    }

    private static bool IsCandidate(List<ScoutCandidate> picked, int teamId, int playerIndex)
    {
        for (int i = 0; i < picked.Count; i++)
        {
            if (picked[i].Is(teamId, playerIndex)) { return true; }
        }
        return false;
    }

    [Test]
    public void 같은_자리에서_두_번_성공하면_영입한_선수는_다시_안_뜨고_다른_자리는_그대로다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        state.AddExtraScoutAttempt();   // 시도 3회
        List<GeneratedTeam> own = Own(42, 4);
        GeneratedTeam upper = Upper(42, 4);
        List<ScoutCandidate> first = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);

        state.SpendScoutAttempt(first[3].TeamId, first[3].PlayerIndex, success: true);
        List<ScoutCandidate> second = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);
        state.SpendScoutAttempt(second[3].TeamId, second[3].PlayerIndex, success: true);
        List<ScoutCandidate> third = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);

        Assert.IsFalse(IsCandidate(third, first[3].TeamId, first[3].PlayerIndex), "첫 영입 선수가 다시 뜬다");
        Assert.IsFalse(IsCandidate(third, second[3].TeamId, second[3].PlayerIndex), "둘째 영입 선수가 다시 뜬다");
        Assert.IsTrue(third[3].UpperTier);
        for (int i = 0; i < 3; i++) { Assert.IsTrue(third[i].Is(first[i].TeamId, first[i].PlayerIndex), $"자리 {i}는 그대로"); }
    }

    [Test]
    public void 위_부에_내_자리와_맞는_선수가_2명_미만이면_후보_4명이_전부_내_부다()
    {
        // 위 부 팀을 전원 AM(내 4-4-2 로스터엔 없는 자리)으로 만든다
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        List<GeneratedTeam> own = Own(42, 4);
        PlayerStats am = table.Find(p => p.RoleId == "AM");
        Assert.IsNotNull(am, "PlayerTable에 AM이 있어야 한다");
        var rows = new List<StageEntry>(own[0].Rows);
        var players = new List<PlayerStats>();
        for (int i = 0; i < rows.Count; i++) { players.Add(am); }
        var upper = new GeneratedTeam(PlayoffRules.ForeignTeamIdBase + 1, "AM만 (3부)", own[0].FormationId, own[0].PresetId, rows, players);

        List<ScoutCandidate> picked = ScoutPool.Pick(42, state.Roster, own, upper, state.ScoutTargets);
        Assert.AreEqual(4, picked.Count);
        for (int i = 0; i < picked.Count; i++) { Assert.IsFalse(picked[i].UpperTier, $"자리 {i}"); }
    }

    [Test]
    public void 시도_기록의_성공_비트는_세이브로_왕복한다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        state.SpendScoutAttempt(1, 3, success: false);
        state.SpendScoutAttempt(2, 5, success: true);
        SeasonState loaded = SeasonState.FromSave(state.ToSave(), table);

        Assert.AreEqual(2, loaded.ScoutTargets.Count);
        Assert.IsFalse(loaded.ScoutTargets[0].Success);
        Assert.IsTrue(loaded.ScoutTargets[1].Success);
        Assert.AreEqual(2, loaded.ScoutTargets[1].TeamId);
        Assert.AreEqual(5, loaded.ScoutTargets[1].PlayerIndex);
    }
}
