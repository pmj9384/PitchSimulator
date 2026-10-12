using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Tactics;
using NUnit.Framework;

// 스카우트(스펙 §4-5, 10-12 구현 설계). ①별: 4부 전원 ★1 / 기본 아닌 변형 ★2~3 / 자리 2쌍 ★3 / 재분배 ★4 / 겹침 +1 / 상위 부 +1, 상한 5
// ②확률 경계·보정 ③주사위 결정성 ④상대해 봤는지 ⑤시도 소모·추가 ⑥영입 교체(총점 300·같은 자리·지시 초기화) ⑦세이브 왕복·옛 세이브
public class ScoutRulesTests
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

    private static int MaxStarsIn(int tier, int myTier)
    {
        int max = 0;
        foreach (GeneratedTeam team in TeamGenerator.Generate(5, Tier(tier), table, formations, names))
        {
            for (int i = 0; i < team.Players.Count; i++)
            {
                max = System.Math.Max(max, ScoutRules.Stars(team.Players[i], team.Rows[i], Tier(tier), myTier, table));
            }
        }
        return max;
    }

    [Test]
    public void 사부_선수는_전원_별_1이고_부가_오를수록_별_분포가_넓어진다()
    {
        foreach (GeneratedTeam team in TeamGenerator.Generate(5, Tier(4), table, formations, names))
        {
            for (int i = 0; i < team.Players.Count; i++) { Assert.AreEqual(1, ScoutRules.Stars(team.Players[i], team.Rows[i], Tier(4), 4, table), team.Players[i].VariantId); }
        }
        Assert.GreaterOrEqual(MaxStarsIn(3, 3), 2, "3부: 기본 아닌 변형이 있다");
        Assert.GreaterOrEqual(MaxStarsIn(2, 2), 3, "2부: 자리 2쌍");
        Assert.GreaterOrEqual(MaxStarsIn(1, 1), 4, "1부: 재분배");
        Assert.AreEqual(5, MaxStarsIn(1, 2), "2부에서 1부 선수를 노리면 상위 부 +1로 5까지");
    }

    [Test]
    public void 별_규칙은_항목_최댓값에_겹침과_상위_부를_더하고_5에서_멈춘다()
    {
        PlayerStats poacher = PlayerTableLookup.FindVariant(table, "st_poacher")!;
        PlayerStats pressing = PlayerTableLookup.FindVariant(table, "st_pressing")!;
        var samePos = new StageEntry { PosX = -30f, PosZ = 0f, PosX2 = -30f, PosZ2 = 0f };
        var dualPos = new StageEntry { PosX = -30f, PosZ = 0f, PosX2 = -40f, PosZ2 = 0f };

        Assert.AreEqual(1, ScoutRules.Stars(poacher, samePos, Tier(4), 4, table), "기본 변형·4부");
        Assert.GreaterOrEqual(ScoutRules.Stars(pressing, samePos, Tier(3), 3, table), 2, "기본 아닌 변형");
        Assert.AreEqual(3, ScoutRules.Stars(poacher, dualPos, Tier(2), 2, table), "자리 2쌍만");
        Assert.AreEqual(4, ScoutRules.Stars(poacher, samePos, Tier(1), 1, table), "재분배만(ST는 적용 자리)");
        Assert.AreEqual(5, ScoutRules.Stars(pressing, dualPos, Tier(1), 1, table), "셋 겹침 → 4 + 1 = 5");
        Assert.AreEqual(2, ScoutRules.Stars(poacher, samePos, Tier(3), 4, table), "4부에서 3부 기본 변형: 상위 부 +1");
        PlayerStats keeper = PlayerTableLookup.FindVariant(table, "gk_standard")!;
        Assert.AreEqual(1, ScoutRules.Stars(keeper, samePos, Tier(1), 1, table), "GK는 재분배 자리가 아니다");
    }

    [Test]
    public void 성공_확률은_별_순위_상대해_봤는지_재시도로_움직이고_경계에서_멈춘다()
    {
        Assert.AreEqual(0.9f, ScoutRules.SuccessChance(1, 0, 6, true, 0), 0.0001f, "★1 0.8 + 상위 절반 0.1");
        Assert.AreEqual(0.7f, ScoutRules.SuccessChance(1, 5, 6, true, 0), 0.0001f, "하위 절반 −0.1");
        Assert.AreEqual(0.35f, ScoutRules.SuccessChance(1, 5, 6, false, 0), 0.0001f, "안 만난 팀 ×0.5");
        Assert.AreEqual(0.05f, ScoutRules.SuccessChance(5, 5, 6, false, 0), 0.0001f, "0.15 − 0.1 = 0.05 × 0.5 → 아래 경계 0.05");
        Assert.AreEqual(0.95f, ScoutRules.SuccessChance(1, 0, 6, true, 3), 0.0001f, "재시도 +0.3이면 위 경계 0.95");
        Assert.Greater(ScoutRules.SuccessChance(3, 3, 6, true, 1), ScoutRules.SuccessChance(3, 3, 6, true, 0), "재시도마다 오른다");
    }

    [Test]
    public void 주사위는_시드_팀_선수_시도에서만_나오고_상대해_봤는지는_결과_목록에서_센다()
    {
        Assert.AreEqual(ScoutRules.Roll(42, 3, 7, 0), ScoutRules.Roll(42, 3, 7, 0));
        Assert.AreNotEqual(ScoutRules.Roll(42, 3, 7, 0), ScoutRules.Roll(42, 3, 7, 1), "시도 번호가 다르면 다른 값");
        Assert.AreNotEqual(ScoutRules.Roll(42, 3, 7, 0), ScoutRules.Roll(42, 4, 7, 0));
        Assert.IsTrue(ScoutRules.Succeeds(0.6f, 0.59f));
        Assert.IsFalse(ScoutRules.Succeeds(0.6f, 0.6f), "경계는 실패");

        var results = new List<MatchResult> { new MatchResult(0, 3, 1, 0), new MatchResult(1, 2, 0, 0) };
        Assert.IsTrue(ScoutRules.MetBefore(results, 0, 3));
        Assert.IsFalse(ScoutRules.MetBefore(results, 0, 2), "2번 팀과는 안 붙었다");
        Assert.AreEqual(30, ScoutRules.CoinsFor(2, 1));
        Assert.AreEqual(10, ScoutRules.CoinsFor(1, 1));
        Assert.AreEqual(0, ScoutRules.CoinsFor(0, 1));
    }

    [Test]
    public void 시도는_시즌당_2회에_코인_추가분이_더해지고_다_쓰면_던진다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        Assert.AreEqual(ScoutRules.AttemptsPerSeason, state.RemainingScoutAttempts);
        state.SpendScoutAttempt(1, 3);
        state.SpendScoutAttempt(1, 3);
        Assert.AreEqual(0, state.RemainingScoutAttempts);
        Assert.AreEqual(2, state.RetriesOn(1, 3), "같은 선수 2번");
        Assert.AreEqual(0, state.RetriesOn(2, 3));
        Assert.Throws<System.InvalidOperationException>(() => state.SpendScoutAttempt(1, 3));
        state.AddScoutAttempt();
        Assert.AreEqual(1, state.RemainingScoutAttempts);
    }

    [Test]
    public void 영입은_같은_자리의_내_선수와_교체하고_총점은_300으로_재분배하며_지시를_지운다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        List<GeneratedTeam> opponents = TeamGenerator.Generate(42, Tier(4), table, formations, names);
        GeneratedTeam team = opponents[0];
        int stIndex = -1;
        for (int i = 0; i < team.Players.Count; i++) { if (team.Players[i].RoleId == "ST") { stIndex = i; } }
        PlayerStats target = team.Players[stIndex];
        Assert.AreEqual(260, target.BuildTotal, "4부 상대는 260점");

        int myStriker = -1;
        foreach (RosterPlayer p in state.Roster) { if (p.Stats.RoleId == "ST") { myStriker = p.PlayerId; } }
        state.SetPlayerInstruction(myStriker, PlayerDial.PushUp, 1);
        LineupEntry before = state.Lineup[state.Lineup.Count - 1];

        state.ReplacePlayer(myStriker, target);
        RosterPlayer after = state.FindRosterPlayer(myStriker);
        Assert.AreEqual(target.VariantId, after.Stats.VariantId, "역할(변형)을 가져온다");
        Assert.AreEqual(300, after.Stats.BuildTotal, "빌드는 내 총점으로");
        Assert.IsFalse(after.HasInstructions, "개인 지시는 지운다");
        Assert.AreEqual(260, target.BuildTotal, "상대 선수 객체는 안 바뀐다");
        Assert.AreEqual(before.AttackX, state.Lineup[state.Lineup.Count - 1].AttackX, "라인업 자리는 그대로");

        int myKeeper = -1;
        foreach (RosterPlayer p in state.Roster) { if (p.Stats.RoleId == "GK") { myKeeper = p.PlayerId; } }
        Assert.Throws<System.InvalidOperationException>(() => state.ReplacePlayer(myKeeper, target), "다른 자리엔 못 넣는다");
    }

    [Test]
    public void 스카우트_상태는_세이브로_왕복하고_옛_세이브는_시도_0이다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        state.SpendScoutAttempt(2, 5);
        state.AddScoutAttempt();
        SeasonState back = SeasonState.FromSave(state.ToSave(), table);
        Assert.AreEqual(1, back.ScoutAttempts);
        Assert.AreEqual(1, back.ScoutExtraAttempts);
        Assert.AreEqual(1, back.RetriesOn(2, 5));
        Assert.AreEqual(2, back.RemainingScoutAttempts);

        SeasonSave old = state.ToSave();
        old.scoutTargets = null;
        old.scoutExtra = 0;
        SeasonState fromOld = SeasonState.FromSave(old, table);
        Assert.AreEqual(0, fromOld.RetriesOn(2, 5));
        Assert.AreEqual(ScoutRules.AttemptsPerSeason - 1, fromOld.RemainingScoutAttempts, "scoutAttempts는 09-26부터 있던 필드라 그대로");
    }
}
