using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Tactics;
using NUnit.Framework;

// 스카우트(스펙 §4-5, 10-12 구현 설계). ①별: 4부 전원 ★1 / 기본 아닌 변형 ★2~3 / 자리 2쌍 ★3 / 재분배 ★4 / 겹침 +1 / 상위 부 +1, 상한 5
// ②확률 경계·보정 ③주사위 결정성 ④상대해 봤는지·코인 수입 ⑤시도 소모·추가 ⑥영입 교체(총점 300·같은 자리·지시 초기화) ⑦세이브 왕복·옛 세이브 ⑧★2/★3 다이얼 거리 경계
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

    private static int MyPlayerIdOf(SeasonState state, string roleId)
    {
        foreach (RosterPlayer p in state.Roster)
        {
            if (p.Stats.RoleId == roleId) { return p.PlayerId; }
        }
        throw new System.InvalidOperationException($"로스터에 {roleId}가 없다");
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
        Assert.IsTrue(ScoutRules.AllStarsAreOne(Tier(4)), "4부 안내의 근거");
        Assert.IsFalse(ScoutRules.AllStarsAreOne(Tier(3)));
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
    public void 기본_아닌_변형은_다이얼_거리_6칸부터_별_3이다()
    {
        PlayerStats baseline = TeamGenerator.DefaultVariant(table, "ST");
        var samePos = new StageEntry { PosX = -30f, PosZ = 0f, PosX2 = -30f, PosZ2 = 0f };
        float step = PlayerInstructions.StepOf(PlayerDial.PushUp);

        PlayerStats near = baseline.Clone();
        near.VariantId = "st_test_near";
        near.PushUp = baseline.PushUp + step * (ScoutRules.BigDialDistanceCells - 1f);
        Assert.AreEqual(ScoutRules.BigDialDistanceCells - 1f, ScoutRules.DialDistanceCells(near, baseline), 0.001f);
        Assert.AreEqual(2, ScoutRules.Stars(near, samePos, Tier(3), 3, table), "5칸 → ★2");

        PlayerStats far = baseline.Clone();
        far.VariantId = "st_test_far";
        far.PushUp = baseline.PushUp + step * ScoutRules.BigDialDistanceCells;
        Assert.AreEqual(ScoutRules.BigDialDistanceCells, ScoutRules.DialDistanceCells(far, baseline), 0.001f);
        Assert.AreEqual(3, ScoutRules.Stars(far, samePos, Tier(3), 3, table), "6칸 → ★3");

        PlayerStats shot = baseline.Clone();
        shot.VariantId = "st_test_shot";
        shot.ShotBias = baseline.ShotBias + PlayerInstructions.StepOf(PlayerDial.ShotBias) * 2f;   // 음수 칸(−0.05)도 절댓값으로 센다
        Assert.AreEqual(2f, ScoutRules.DialDistanceCells(shot, baseline), 0.001f);
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
        Assert.AreEqual(0.7f, ScoutRules.SuccessChance(1, 2, 5, true, 0), 0.0001f, "5팀이면 상위 절반은 0~1위, 2위는 하위");
        Assert.AreEqual(ScoutRules.SuccessChance(1, 0, 6, true, 0), ScoutRules.SuccessChance(0, 0, 6, true, 0), "별 0은 1로 묶인다");
        Assert.AreEqual(ScoutRules.SuccessChance(5, 0, 6, true, 0), ScoutRules.SuccessChance(6, 0, 6, true, 0), "별 6은 5로 묶인다");
    }

    [Test]
    public void 주사위는_시드_팀_선수_시도에서만_나오고_연속_시도가_한_방향으로_늘지_않는다()
    {
        Assert.AreEqual(ScoutRules.Roll(42, 3, 7, 0), ScoutRules.Roll(42, 3, 7, 0));
        Assert.AreNotEqual(ScoutRules.Roll(42, 3, 7, 0), ScoutRules.Roll(42, 3, 7, 1), "시도 번호가 다르면 다른 값");
        Assert.AreNotEqual(ScoutRules.Roll(42, 3, 7, 0), ScoutRules.Roll(42, 4, 7, 0));
        Assert.IsTrue(ScoutRules.Succeeds(0.6f, 0.59f));
        Assert.IsFalse(ScoutRules.Succeeds(0.6f, 0.6f), "경계는 실패");

        // System.Random(seed)로 만들던 때는 시도 +1마다 약 +0.195씩 올라갔다(10-12 리뷰). 해시 기반이면 연속 차이가 한 부호로 몰리지 않는다
        int ups = 0;
        for (int seed = 0; seed < 50; seed++)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                float a = ScoutRules.Roll(seed, 3, 7, attempt);
                float b = ScoutRules.Roll(seed, 3, 7, attempt + 1);
                Assert.That(a, Is.InRange(0f, 1f));
                if (b > a) { ups++; }
            }
        }
        Assert.That(ups, Is.InRange(75, 175), "250쌍 중 오른 쌍이 절반 근처여야 한다(선형이면 거의 전부)");
    }

    [Test]
    public void 상대해_봤는지는_결과_목록에서_세고_코인은_내_팀이_홈이든_원정이든_같은_결과를_낸다()
    {
        var results = new List<MatchResult> { new MatchResult(0, 3, 1, 0), new MatchResult(1, 2, 0, 0) };
        Assert.IsTrue(ScoutRules.MetBefore(results, 0, 3));
        Assert.IsFalse(ScoutRules.MetBefore(results, 0, 2), "2번 팀과는 안 붙었다");

        Assert.AreEqual(30, ScoutRules.CoinsFor(2, 1));
        Assert.AreEqual(10, ScoutRules.CoinsFor(1, 1));
        Assert.AreEqual(0, ScoutRules.CoinsFor(0, 1));
        Assert.AreEqual(ScoutRules.WinCoins, ScoutRules.CoinsFor(new MatchResult(0, 3, 2, 1), 0), "홈 승");
        Assert.AreEqual(ScoutRules.WinCoins, ScoutRules.CoinsFor(new MatchResult(3, 0, 1, 2), 0), "원정 승");
        Assert.AreEqual(ScoutRules.LossCoins, ScoutRules.CoinsFor(new MatchResult(3, 0, 2, 1), 0), "원정 패");
        Assert.AreEqual(ScoutRules.DrawCoins, ScoutRules.CoinsFor(new MatchResult(0, 3, 1, 1), 0));
        Assert.Throws<System.ArgumentException>(() => ScoutRules.CoinsFor(new MatchResult(1, 2, 0, 0), 0), "내 경기가 아니면 던진다");
    }

    [Test]
    public void 시도는_시즌당_2회에_코인_추가분이_더해지고_다_쓰면_던진다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        Assert.AreEqual(ScoutRules.AttemptsPerSeason, state.RemainingScoutAttempts);
        state.SpendScoutAttempt(1, 3);
        state.SpendScoutAttempt(1, 3);
        Assert.AreEqual(0, state.RemainingScoutAttempts);
        Assert.AreEqual(2, state.ScoutRetriesOn(1, 3), "같은 선수 2번");
        Assert.AreEqual(0, state.ScoutRetriesOn(2, 3));
        Assert.Throws<System.InvalidOperationException>(() => state.SpendScoutAttempt(1, 3));
        state.AddExtraScoutAttempt();
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

        int myStriker = MyPlayerIdOf(state, "ST");
        Assert.IsTrue(state.CanReplacePlayer(myStriker, target));
        state.SetPlayerInstruction(myStriker, PlayerDial.PushUp, 1);
        LineupEntry before = state.Lineup[state.Lineup.Count - 1];

        state.ReplacePlayer(myStriker, target);
        RosterPlayer after = state.FindRosterPlayer(myStriker);
        Assert.AreEqual(target.VariantId, after.Stats.VariantId, "역할(변형)을 가져온다");
        Assert.AreEqual(300, after.Stats.BuildTotal, "빌드는 내 총점으로");
        Assert.IsFalse(after.HasInstructions, "개인 지시는 지운다");
        Assert.AreEqual(260, target.BuildTotal, "상대 선수 객체는 안 바뀐다");
        Assert.AreEqual(before.AttackX, state.Lineup[state.Lineup.Count - 1].AttackX, "라인업 자리는 그대로");

        int myKeeper = MyPlayerIdOf(state, "GK");
        Assert.IsFalse(state.CanReplacePlayer(myKeeper, target), "다른 자리는 미리 거른다");
        Assert.Throws<System.InvalidOperationException>(() => state.ReplacePlayer(myKeeper, target), "다른 자리엔 못 넣는다");
    }

    [Test]
    public void 영입한_선수는_세이브로_왕복하고_다음_시즌엔_역할만_남고_빌드는_기본으로_돌아간다()
    {
        // 10-13 재검증 F2: 교체 결과만 잠겨 있고 세이브·이월 경로는 비어 있었다. 이월 규칙은 스펙 §4-5 [가정] ④
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        GeneratedTeam team = TeamGenerator.Generate(42, Tier(4), table, formations, names)[0];
        int stIndex = -1;
        for (int i = 0; i < team.Players.Count; i++) { if (team.Players[i].RoleId == "ST") { stIndex = i; } }
        PlayerStats target = team.Players[stIndex];
        int myStriker = MyPlayerIdOf(state, "ST");
        state.ReplacePlayer(myStriker, target);
        PlayerStats scouted = state.FindRosterPlayer(myStriker).Stats;

        SeasonState back = SeasonState.FromSave(state.ToSave(), table);
        PlayerStats loaded = back.FindRosterPlayer(myStriker).Stats;
        Assert.AreEqual(scouted.VariantId, loaded.VariantId);
        Assert.AreEqual(300, loaded.BuildTotal, "FromSave의 총점 검사를 통과한다");
        Assert.AreEqual(scouted.Speed, loaded.Speed);
        Assert.AreEqual(scouted.Shot, loaded.Shot);

        SeasonState next = SeasonState.NewSeason(3, 43, myRows, table);
        PlayerStats defaultBuild = next.FindRosterPlayer(myStriker).Stats.Clone();
        next.AdoptPlayerTactics(back.Roster, table);
        PlayerStats carried = next.FindRosterPlayer(myStriker).Stats;
        Assert.AreEqual(scouted.VariantId, carried.VariantId, "역할(변형)은 이어 간다");
        Assert.AreEqual(defaultBuild.Speed, carried.Speed, "빌드는 기본 로스터 값");
        Assert.AreEqual(defaultBuild.Shot, carried.Shot);
        Assert.IsFalse(next.FindRosterPlayer(myStriker).HasInstructions);
    }

    [Test]
    public void 스카우트_상태는_세이브로_왕복하고_옛_세이브는_시도_0이다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        state.SpendScoutAttempt(2, 5);
        state.AddExtraScoutAttempt();
        SeasonState back = SeasonState.FromSave(state.ToSave(), table);
        Assert.AreEqual(1, back.ScoutAttempts);
        Assert.AreEqual(1, back.ScoutExtraAttempts);
        Assert.AreEqual(1, back.ScoutRetriesOn(2, 5));
        Assert.AreEqual(2, back.RemainingScoutAttempts);

        SeasonSave old = state.ToSave();
        old.scoutTargets = null;
        old.scoutExtra = 0;
        SeasonState fromOld = SeasonState.FromSave(old, table);
        Assert.AreEqual(0, fromOld.ScoutRetriesOn(2, 5));
        Assert.AreEqual(ScoutRules.AttemptsPerSeason - 1, fromOld.RemainingScoutAttempts, "scoutAttempts는 09-26부터 있던 필드라 그대로");
    }
}
