using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;
using UnityEngine;

// 선수 초상(10-21): 얼굴 번호는 선수 개체가 들고 다닌다.
// ①번호 규칙은 결정적이고 1~VariantCount 안 ②영입은 스카우트에서 본 얼굴을 그대로 가져온다 ③역할 변경·지시·이월을 거쳐도 남는다
// ④세이브가 왕복하고, 옛 세이브(번호 0)는 playerId로 정한다 ⑤Resources 스프라이트 수 = VariantCount(에셋과 상수가 어긋나면 여기서 깨진다)
public class PortraitTests
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

    [Test]
    public void 번호는_같은_입력에_같고_범위_안이다()
    {
        for (int seed = -3; seed < 40; seed++)
        {
            int portrait = PortraitRules.Of(seed);
            Assert.AreEqual(portrait, PortraitRules.Of(seed));
            Assert.IsTrue(PortraitRules.IsValid(portrait), $"seed {seed} → {portrait}");
        }
        Assert.AreEqual(PortraitRules.Of(3, 7), PortraitRules.Of(3, 7));
        Assert.IsTrue(PortraitRules.IsValid(PortraitRules.Of(3, 7)));
        Assert.IsFalse(PortraitRules.IsValid(PortraitRules.None));
        Assert.IsFalse(PortraitRules.IsValid(PortraitRules.VariantCount + 1));
    }

    [Test]
    public void 한_팀_11명은_얼굴이_한_가지로_몰리지_않고_팀마다_시작_얼굴이_다르다()
    {
        var seen = new HashSet<int>();
        for (int index = 0; index < 11; index++) { seen.Add(PortraitRules.Of(0, index)); }
        Assert.GreaterOrEqual(seen.Count, 5, "같은 팀 선수 얼굴이 너무 같다");
        Assert.AreNotEqual(PortraitRules.Of(0, 0), PortraitRules.Of(1, 0), "옆 팀 첫 선수가 같은 얼굴이면 팀 간 섞임(TeamStride)이 없는 것");
    }

    [Test]
    public void 기본_로스터는_playerId로_정하고_영입은_받은_번호를_그대로_든다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        for (int i = 0; i < state.Roster.Count; i++)
        {
            Assert.AreEqual(PortraitRules.Of(state.Roster[i].PlayerId), state.Roster[i].Portrait);
        }
        RosterPlayer recruit = state.AddToBench(OpponentPlayer("ST"), 3, portrait: 7);
        Assert.AreEqual(7, recruit.Portrait);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => state.AddToBench(OpponentPlayer("CB"), 2, portrait: PortraitRules.VariantCount + 1));
    }

    [Test]
    public void 역할_변경_지시_설정_해제_이월을_거쳐도_초상은_남는다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        RosterPlayer recruit = state.AddToBench(OpponentPlayer("ST"), 3, portrait: 9);
        int id = recruit.PlayerId;
        state.SetPlayerInstruction(id, PlayerDial.PushUp, 1);
        Assert.AreEqual(9, state.FindRosterPlayer(id).Portrait);
        state.ClearPlayerInstructions(id);
        Assert.AreEqual(9, state.FindRosterPlayer(id).Portrait);
        List<PlayerStats> others = PlayerTableLookup.ExposedVariants(table, "ST");
        state.SetPlayerRole(id, others[0].VariantId == recruit.Stats.VariantId ? others[1].VariantId : others[0].VariantId, table);
        Assert.AreEqual(9, state.FindRosterPlayer(id).Portrait);

        SeasonState next = SeasonState.NewSeason(4, 43, myRows, table);
        next.AdoptBench(state.Bench(), table);
        Assert.AreEqual(1, next.BenchCount);
        Assert.AreEqual(9, next.Bench()[0].Portrait);
    }

    [Test]
    public void 세이브가_왕복하고_옛_세이브는_playerId로_정한다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        state.AddToBench(OpponentPlayer("ST"), 3, portrait: 4);
        SeasonState back = SeasonState.FromSave(state.ToSave(), table);
        Assert.AreEqual(4, back.Bench()[0].Portrait);

        SeasonSave old = state.ToSave();
        for (int i = 0; i < old.roster.Count; i++) { old.roster[i].portrait = PortraitRules.None; }   // 10-21 전 세이브엔 이 필드가 없다
        SeasonState fromOld = SeasonState.FromSave(old, table);
        for (int i = 0; i < fromOld.Roster.Count; i++)
        {
            Assert.AreEqual(PortraitRules.Of(fromOld.Roster[i].PlayerId), fromOld.Roster[i].Portrait);
        }
    }

    [Test]
    public void Resources_초상_스프라이트_수가_VariantCount와_같다()
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>("Sprites/Portrait");
        Assert.AreEqual(PortraitRules.VariantCount, sprites.Length);
        for (int portrait = 1; portrait <= PortraitRules.VariantCount; portrait++)
        {
            Assert.IsNotNull(Resources.Load<Sprite>($"Sprites/Portrait/portrait_{portrait:000}"), $"portrait_{portrait:000} 없음");
        }
    }
}
