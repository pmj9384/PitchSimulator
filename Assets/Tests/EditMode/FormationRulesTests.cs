using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 슬롯 격자 판정(10-25, FM26 공수 모양 2장): 슬롯 자리 파생 · 역할군 · 전환 거리와 경고 · 수비 포메이션 추천 순위 · 가장 가까운 슬롯 · 시즌 상태의 포메이션 선택·슬롯 교체·수비 역할·세이브 왕복
public class FormationRulesTests
{
    private static List<FormationTemplate> Formations()
    {
        return FormationTemplateParser.Parse(File.ReadAllText("Assets/Resources/Tables/FormationTemplates.csv"));
    }

    private static List<DefenseRole> Roles()
    {
        return DefenseRoleParser.Parse(File.ReadAllText("Assets/Resources/Tables/DefenseRoles.csv"));
    }

    private static List<StageEntry> StageRows()
    {
        return StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1 && r.Team == 0);
    }

    private static SeasonState NewState()
    {
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        return SeasonState.NewSeason(4, 42, StageRows(), table);
    }

    [Test]
    public void 슬롯_자리는_공격이면_posX_posZ_수비면_posX2_posZ2다()
    {
        FormationTemplate f442 = FormationRules.Find(Formations(), "4-4-2");
        Assert.AreEqual((-35f, -22f), FormationRules.Position(f442, 2, defending: false), "슬롯 2 FB 공격 자리");
        Assert.AreEqual((-40f, -22f), FormationRules.Position(f442, 2, defending: true), "슬롯 2 FB 수비 자리");
        Assert.Throws<System.ArgumentOutOfRangeException>(() => FormationRules.Position(f442, 12, false));
        Assert.Throws<System.InvalidOperationException>(() => FormationRules.Find(Formations(), "9-9-9"));
    }

    [Test]
    public void 역할군은_GK_수비_미드_공격_넷이고_교체는_같은_군끼리만이다()
    {
        Assert.AreEqual(RoleGroup.Defender, RoleGroups.Of("fb"));
        Assert.AreEqual(RoleGroup.Midfielder, RoleGroups.Of("AM"));
        Assert.AreEqual(RoleGroup.Attacker, RoleGroups.Of("W"));
        FormationTemplate f433 = FormationRules.Find(Formations(), "4-3-3");
        Assert.IsTrue(FormationRules.CanOccupy("ST", FormationRules.SlotOf(f433, 9)), "W 슬롯에 ST: 같은 공격군");
        Assert.IsFalse(FormationRules.CanOccupy("CM", FormationRules.SlotOf(f433, 9)), "W 슬롯에 CM: 다른 군");
        Assert.Throws<System.ArgumentException>(() => RoleGroups.Of("XX"));
    }

    [Test]
    public void 전환_거리는_공격_자리와_수비_자리_사이이고_15m_넘으면_경고다()
    {
        List<FormationTemplate> formations = Formations();
        FormationTemplate f442 = FormationRules.Find(formations, "4-4-2");
        FormationTemplate f352 = FormationRules.Find(formations, "3-5-2");
        Assert.AreEqual(5f, FormationRules.TransitionDistance(f442, 2, f442, 2), 1e-4f, "4-4-2 FB: 공수 5m 차이");
        Assert.IsFalse(FormationRules.IsFarTransition(5f));
        Assert.IsTrue(FormationRules.IsFarTransition(MatchTuning.FormationTransitionWarnMeters + 0.1f));
        Assert.Greater(FormationRules.MeanTransitionDistance(f442, f352), FormationRules.MeanTransitionDistance(f442, f442), "다른 모양이면 평균 이동이 길다");
    }

    [Test]
    public void 수비_포메이션_추천은_평균_이동_거리_짧은_순이고_같은_모양이_1위다()
    {
        List<FormationTemplate> formations = Formations();
        FormationTemplate f442 = FormationRules.Find(formations, "4-4-2");
        List<FormationTemplate> ranked = FormationRules.RankDefenseFormations(f442, formations);
        Assert.AreEqual(formations.Count, ranked.Count);
        Assert.AreEqual("4-4-2", ranked[0].Id, "자기 모양(이동 0)이 1위");
        for (int i = 1; i < ranked.Count; i++)
        {
            Assert.GreaterOrEqual(FormationRules.MeanTransitionDistance(f442, ranked[i]), FormationRules.MeanTransitionDistance(f442, ranked[i - 1]));
        }
    }

    [Test]
    public void 가장_가까운_슬롯은_좌표에서_거리가_최소인_번호다()
    {
        FormationTemplate f442 = FormationRules.Find(Formations(), "4-4-2");
        Assert.AreEqual(2, FormationRules.NearestSlot(f442, defending: false, -33f, -20f), "FB 자리 근처");
        Assert.AreEqual(10, FormationRules.NearestSlot(f442, defending: true, -12f, -7f), "수비 모양의 ST");
    }

    [Test]
    public void 수비_역할_표는_기본_역할을_포함하고_다이얼_3개만_덮는다()
    {
        List<DefenseRole> roles = Roles();
        Assert.IsNotNull(DefenseRoleParser.Find(roles, DefenseRole.DefaultId));
        DefenseRole pressing = DefenseRoleParser.Find(roles, "pressing");
        var stats = new PlayerStats { RoleId = "CM", PressRange = 10f, LineHeight = 4f, RoamRadius = 6f, Speed = 50, PushUp = 15f };
        PlayerStats applied = pressing.Apply(stats);
        Assert.AreEqual(15f, applied.PressRange, 1e-4f, "×1.5");
        Assert.AreEqual(7f, applied.LineHeight, 1e-4f, "+3");
        Assert.AreEqual(7.8f, applied.RoamRadius, 1e-4f, "×1.3");
        Assert.AreEqual(15f, applied.PushUp, 1e-4f, "공격 다이얼은 그대로");
        Assert.AreEqual(10f, stats.PressRange, "원본 불변");
        Assert.Throws<System.FormatException>(() => DefenseRoleParser.Parse("defenseRoleId,displayName,description,pressRangeScale,lineHeightOffset,roamRadiusScale\npressing,압박,,1.5,3,1.3\n"), "기본 역할 없음");
    }

    [Test]
    public void 새_시즌은_4_4_2_두_장이고_슬롯은_라인업_순서다()
    {
        SeasonState state = NewState();
        Assert.AreEqual("4-4-2", state.AttackFormationId);
        Assert.AreEqual("4-4-2", state.DefenseFormationId);
        for (int i = 0; i < state.Lineup.Count; i++)
        {
            Assert.AreEqual(i + 1, state.Lineup[i].AttackSlot);
            Assert.AreEqual(i + 1, state.Lineup[i].DefenseSlot);
            Assert.AreEqual(DefenseRole.DefaultId, state.Lineup[i].DefenseRoleId);
        }
    }

    [Test]
    public void 포메이션을_고르면_선발_전원의_자리_2쌍이_새_템플릿_슬롯_값으로_바뀐다()
    {
        SeasonState state = NewState();
        List<FormationTemplate> formations = Formations();
        state.SetFormations("4-3-3", "5-3-2", formations);
        Assert.AreEqual("4-3-3", state.AttackFormationId);
        Assert.AreEqual("5-3-2", state.DefenseFormationId);
        FormationTemplate f433 = FormationRules.Find(formations, "4-3-3");
        FormationTemplate f532 = FormationRules.Find(formations, "5-3-2");
        LineupEntry slot6 = state.Lineup[5];
        Assert.AreEqual(FormationRules.Position(f433, 6, false), (slot6.AttackX, slot6.AttackZ), "공격 자리 = 4-3-3 슬롯 6");
        Assert.AreEqual(FormationRules.Position(f532, 6, true), (slot6.DefendX, slot6.DefendZ), "수비 자리 = 5-3-2 슬롯 6");
        Assert.Throws<System.InvalidOperationException>(() => state.SetFormations("9-9-9", "4-4-2", formations));
        Assert.AreEqual("4-3-3", state.AttackFormationId, "실패하면 안 바뀐다");
    }

    [Test]
    public void 슬롯_교체는_같은_역할군끼리만_되고_자리가_따라간다()
    {
        SeasonState state = NewState();
        List<FormationTemplate> formations = Formations();
        int leftFb = state.Lineup[1].PlayerId;    // 4-4-2 슬롯 2 FB(-35, -22)
        int rightFb = state.Lineup[4].PlayerId;   // 슬롯 5 FB(-35, 22)
        int striker = state.Lineup[9].PlayerId;   // 슬롯 10 ST
        state.SwapLineupSlots(leftFb, rightFb, defending: false, formations);
        Assert.AreEqual(5, state.Lineup[1].AttackSlot);
        Assert.AreEqual(22f, state.Lineup[1].AttackZ, 1e-4f, "왼쪽 FB가 오른쪽 자리로");
        Assert.AreEqual(2, state.Lineup[4].AttackSlot);
        Assert.AreEqual(-22f, state.Lineup[1].DefendZ, 1e-4f, "수비 슬롯은 안 건드림(왼쪽 FB의 수비 자리 그대로)");
        Assert.Throws<System.InvalidOperationException>(() => state.SwapLineupSlots(leftFb, striker, defending: false, formations), "수비군 ↔ 공격군은 안 됨");
        state.SwapLineupSlots(leftFb, striker, defending: true, formations);
        Assert.AreEqual(10, state.Lineup[1].DefenseSlot, "수비 슬롯은 필드 선수끼리 역할군을 안 본다(수비 역할이 따로 있다)");
        int keeper = state.Lineup[0].PlayerId;
        Assert.Throws<System.InvalidOperationException>(() => state.SwapLineupSlots(keeper, rightFb, defending: true, formations), "GK 슬롯은 GK만: 수비 자리가 GK 라인의 기준");
        Assert.AreEqual(1, state.Lineup[0].DefenseSlot, "실패하면 안 바뀐다");
    }

    [Test]
    public void 수비_역할의_라인_높이_오프셋은_0_아래로_내리지_않는다()
    {
        DefenseRole covering = DefenseRoleParser.Find(Roles(), "covering");
        var stats = new PlayerStats { RoleId = "ST", PressRange = 10f, LineHeight = 0f, RoamRadius = 6f };
        Assert.AreEqual(0f, covering.Apply(stats).LineHeight, "ST 라인 높이 0 + (-3) → 0 (PlayerTable 불변식 ≥ 0)");
        Assert.AreEqual(2f, covering.Apply(new PlayerStats { RoleId = "CB", LineHeight = 5f }).LineHeight, 1e-4f, "5 - 3 = 2");
    }

    [Test]
    public void 다음_시즌은_이전_공수_포메이션과_슬롯을_그대로_이어_간다()
    {
        SeasonState previous = NewState();
        List<FormationTemplate> formations = Formations();
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        previous.SetFormations("4-3-3", "5-3-2", formations);
        previous.SwapLineupSlots(previous.Lineup[1].PlayerId, previous.Lineup[4].PlayerId, defending: false, formations);

        SeasonState next = SeasonState.NewSeason(3, 43, StageRows(), table);
        next.SetFormations(previous.AttackFormationId, previous.DefenseFormationId, formations);   // SeasonSystem.StartNextSeason의 순서 그대로
        next.AdoptStarters(previous.Roster, previous.Lineup, table);
        Assert.AreEqual("4-3-3", next.AttackFormationId);
        Assert.AreEqual("5-3-2", next.DefenseFormationId);
        Assert.AreEqual(5, next.Lineup[1].AttackSlot, "바꿔 둔 슬롯도 이월");
        FormationTemplate f433 = FormationRules.Find(formations, "4-3-3");
        Assert.AreEqual(FormationRules.Position(f433, 5, false), (next.Lineup[1].AttackX, next.Lineup[1].AttackZ), "좌표가 id·슬롯과 맞는다");
    }

    [Test]
    public void 저장_전술은_공수_포메이션_슬롯별_수비_역할_팀_전술을_담고_입히면_선수_배정은_그대로다()
    {
        SeasonState state = NewState();
        List<FormationTemplate> formations = Formations();
        List<DefenseRole> roles = Roles();
        List<TeamTactics> presets = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));
        state.SetFormations("4-3-3", "5-3-2", formations);
        state.SetDefenseRole(state.Lineup[6].PlayerId, "pressing", roles);   // 수비 슬롯 7
        state.SetMyPreset("pressing", presets);
        SavedTactic tactic = state.SnapshotTactic("압박 433");
        Assert.AreEqual("4-3-3", tactic.AttackFormationId);
        Assert.AreEqual("pressing", tactic.DefenseRoleOf(7), "역할은 선수가 아니라 수비 슬롯에 붙는다");
        Assert.AreEqual(DefenseRole.DefaultId, tactic.DefenseRoleOf(1));
        Assert.IsNull(tactic.CustomTactics, "카드 값 그대로면 바꾼 값 없음");

        SeasonState other = NewState();
        int keeperId = other.Lineup[0].PlayerId;
        other.ApplyTactic(tactic, formations, roles, presets);
        Assert.AreEqual("5-3-2", other.DefenseFormationId);
        Assert.AreEqual("pressing", other.Lineup[6].DefenseRoleId, "슬롯 7에 선 선수가 압박 역할을 받는다");
        Assert.AreEqual("pressing", other.MyPresetId);
        Assert.AreEqual(keeperId, other.Lineup[0].PlayerId, "선수 배정은 안 바뀐다");
        Assert.Throws<System.ArgumentException>(() => new SavedTactic("", "4-4-2", "4-4-2", tactic.DefenseRoleBySlot, "balanced", null), "이름 없음");
    }

    [Test]
    public void 전술_목록은_이름으로_덮어쓰고_5장이_넘으면_던지며_세이브를_왕복한다()
    {
        SeasonState state = NewState();
        var library = new TacticLibrary();
        for (int i = 0; i < TacticLibrary.MaxCount; i++)
        {
            library.Put(state.SnapshotTactic($"전술{i}"));
        }
        Assert.AreEqual(TacticLibrary.MaxCount, library.All.Count);
        Assert.Throws<System.InvalidOperationException>(() => library.Put(state.SnapshotTactic("여섯째")), "꽉 차면 던진다(화면이 먼저 막는다)");
        state.SetFormations("4-3-3", "4-4-2", Formations());
        library.Put(state.SnapshotTactic("전술2"));
        Assert.AreEqual(TacticLibrary.MaxCount, library.All.Count, "같은 이름은 덮어쓴다");
        Assert.AreEqual("4-3-3", library.All[2].AttackFormationId, "그 자리에");
        Assert.IsTrue(library.Remove("전술0"));
        Assert.IsFalse(library.Remove("없는 이름"));

        TacticLibrary back = TacticLibrary.FromSave(library.ToSave());
        Assert.AreEqual(library.All.Count, back.All.Count);
        Assert.AreEqual("4-3-3", back.Find("전술2").AttackFormationId);
        Assert.AreEqual(0, TacticLibrary.FromSave(null).All.Count, "옛 세이브(없음)는 빈 목록");
    }

    [Test]
    public void 수비_역할은_선발마다_고르고_경기_재료의_수비_다이얼을_덮으며_세이브를_왕복한다()
    {
        SeasonState state = NewState();
        List<DefenseRole> roles = Roles();
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        int cm = state.Lineup[6].PlayerId;
        float basePress = state.MyLineup()[6].Stats.PressRange;
        state.SetDefenseRole(cm, "pressing", roles);
        Assert.AreEqual(basePress * 1.5f, state.MyLineup(roles)[6].Stats.PressRange, 1e-4f, "압박 역할이면 압박 거리 ×1.5");
        Assert.AreEqual(basePress, state.MyLineup()[6].Stats.PressRange, 1e-4f, "표를 안 주면 역할 값 그대로");
        Assert.Throws<System.InvalidOperationException>(() => state.SetDefenseRole(cm, "nope", roles));

        state.SetFormations("4-3-3", "4-4-2", Formations());
        SeasonState back = SeasonState.FromSave(state.ToSave(), table);
        Assert.AreEqual("4-3-3", back.AttackFormationId);
        Assert.AreEqual("pressing", back.Lineup[6].DefenseRoleId);
        Assert.AreEqual(state.Lineup[6].AttackX, back.Lineup[6].AttackX, 1e-4f);
    }

    [Test]
    public void 옛_세이브는_슬롯이_없어도_라인업_순서로_흡수하고_포메이션은_4_4_2다()
    {
        SeasonState state = NewState();
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        SeasonSave old = state.ToSave();
        old.attackFormation = null;
        old.defenseFormation = null;
        for (int i = 0; i < old.lineup.Count; i++)
        {
            old.lineup[i].attackSlot = 0;
            old.lineup[i].defenseSlot = 0;
            old.lineup[i].defenseRole = null;
        }
        SeasonState back = SeasonState.FromSave(old, table);
        Assert.AreEqual("4-4-2", back.DefenseFormationId);
        Assert.AreEqual(7, back.Lineup[6].AttackSlot, "순서 i+1");
        Assert.AreEqual(DefenseRole.DefaultId, back.Lineup[6].DefenseRoleId);
    }
}
