using System;
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

    // 전술판 칩 크기(80×50px)를 필드 그림(약 424×342px)으로 나눈 값. 이보다 가까우면 칩이 겹친다. 프리팹 치수를 바꾸면 같이 바꾼다
    private const float ChipWidthU = 80f / 424f;
    private const float ChipHeightV = 50f / 342f;

    [Test]
    public void 격자는_모든_포메이션_양쪽_모양에서_칩이_겹치지_않고_GK는_왼쪽_가운데다()
    {
        foreach (FormationTemplate template in Formations())
        {
            foreach (bool defending in new[] { false, true })
            {
                (float u, float v)[] grid = FormationGrid.Layout(template, defending);
                string label = $"{template.Id} {(defending ? "수비" : "공격")}";
                Assert.AreEqual(FormationGrid.EdgeInset, grid[0].u, 1e-4f, label + " GK 열");
                Assert.AreEqual(0.5f, grid[0].v, 1e-4f, label + " GK 가운데");
                for (int i = 0; i < grid.Length; i++)
                {
                    Assert.That(grid[i].u, Is.InRange(FormationGrid.EdgeInset - 1e-4f, 1f - FormationGrid.EdgeInset + 1e-4f), label + " u 범위");
                    Assert.That(grid[i].v, Is.InRange(FormationGrid.EdgeInset - 1e-4f, 1f - FormationGrid.EdgeInset + 1e-4f), label + " v 범위");
                    for (int j = i + 1; j < grid.Length; j++)
                    {
                        bool apart = Math.Abs(grid[i].u - grid[j].u) >= ChipWidthU || Math.Abs(grid[i].v - grid[j].v) >= ChipHeightV;
                        Assert.IsTrue(apart, $"{label}: 슬롯 {i + 1}·{j + 1}이 겹친다 ({grid[i]} / {grid[j]})");
                    }
                }
            }
        }
    }

    [Test]
    public void 격자는_x가_가까운_슬롯을_한_열로_묶고_열_안은_z_순서로_균등하게_둔다()
    {
        FormationTemplate f442 = FormationRules.Find(Formations(), "4-4-2");
        (float u, float v)[] grid = FormationGrid.Layout(f442, defending: false);
        Assert.AreEqual(grid[1].u, grid[2].u, 1e-4f, "FB −35와 CB −36은 한 열");
        Assert.Less(grid[1].v, grid[2].v, "왼쪽 FB(z −22)가 아래");
        Assert.AreEqual(1f - FormationGrid.EdgeInset, grid[9].u, 1e-4f, "ST 열이 맨 오른쪽");
        Assert.AreEqual(0.3f, grid[9].v, 1e-4f, "둘인 열은 0.3·0.7");
        Assert.AreEqual(0.7f, grid[10].v, 1e-4f);
    }

    [Test]
    public void 포메이션을_바꾸면_선수는_역할군이_맞는_슬롯에_다시_앉고_넘치는_선수만_어긋난_슬롯에_선다()
    {
        List<FormationTemplate> formations = Formations();
        FormationTemplate f442 = FormationRules.Find(formations, "4-4-2");
        FormationTemplate f433 = FormationRules.Find(formations, "4-3-3");
        // 4-4-2 선발: GK·수비 4·미드 4·공격 2(슬롯 1~11 순)
        var roles = new List<string> { "GK", "FB", "CB", "CB", "FB", "W", "CM", "CM", "W", "ST", "ST" };
        var slots = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        int[] reseated = FormationRules.ReseatSlots(roles, slots, f433);
        for (int i = 0; i < 5; i++) { Assert.AreEqual(slots[i], reseated[i], $"GK·수비는 같은 번호 유지 ({roles[i]})"); }
        int mismatched = 0;
        var seen = new HashSet<int>();
        for (int i = 0; i < reseated.Length; i++)
        {
            Assert.IsTrue(seen.Add(reseated[i]), "슬롯은 한 명씩");
            if (!FormationRules.CanOccupy(roles[i], FormationRules.SlotOf(f433, reseated[i]))) { mismatched++; }
        }
        Assert.AreEqual(1, mismatched, "4-4-2의 W 2명은 4-3-3에서 공격군(W·ST) 3칸을 ST 2명과 나눠 1명만 미드 칸에 어긋나 선다");
        CollectionAssert.AreEqual(slots, FormationRules.ReseatSlots(roles, slots, f442), "같은 모양이면 그대로");

        SeasonState state = NewState();
        state.SetFormations("4-3-3", "4-3-3", formations);
        for (int i = 0; i < state.Lineup.Count; i++)
        {
            LineupEntry e = state.Lineup[i];
            Assert.AreEqual(FormationRules.Position(f433, e.AttackSlot, false), (e.AttackX, e.AttackZ), "좌표는 새 슬롯 값");
        }
    }

    [Test]
    public void 저장_전술_입히기는_id_하나라도_표에_없으면_아무것도_바꾸지_않는다()
    {
        SeasonState state = NewState();
        List<FormationTemplate> formations = Formations();
        List<DefenseRole> roles = Roles();
        List<TeamTactics> presets = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));
        var badRoles = new List<string>();
        for (int i = 0; i < FormationTemplate.SlotCount; i++) { badRoles.Add(i == 6 ? "ghost" : DefenseRole.DefaultId); }
        var tactic = new SavedTactic("깨진 전술", "4-3-3", "5-3-2", badRoles, "balanced", null);
        Assert.Throws<System.InvalidOperationException>(() => state.ApplyTactic(tactic, formations, roles, presets));
        Assert.AreEqual("4-4-2", state.AttackFormationId, "포메이션도 안 바뀐다(반쪽 적용 없음)");
    }

    [Test]
    public void 교체_가능_판정은_교체와_같은_규칙이다()
    {
        SeasonState state = NewState();
        List<FormationTemplate> formations = Formations();
        int leftFb = state.Lineup[1].PlayerId;
        int rightFb = state.Lineup[4].PlayerId;
        int striker = state.Lineup[9].PlayerId;
        int keeper = state.Lineup[0].PlayerId;
        Assert.IsTrue(state.CanSwapLineupSlots(leftFb, rightFb, defending: false, formations));
        Assert.IsFalse(state.CanSwapLineupSlots(leftFb, striker, defending: false, formations), "공격: 다른 군");
        Assert.IsTrue(state.CanSwapLineupSlots(leftFb, striker, defending: true, formations), "수비: 필드 선수끼리 자유");
        Assert.IsFalse(state.CanSwapLineupSlots(keeper, striker, defending: true, formations), "수비: GK 슬롯 보호");
        Assert.AreEqual("전술 3", TacticLibrary.SlotName(2));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => TacticLibrary.SlotName(TacticLibrary.MaxCount));
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

        // 10-27 ④: SeasonSystem.Load가 옛 세이브에 하는 1회 변환. 자유 배치로 옮겨 둔 좌표가 템플릿 슬롯 값으로 돌아온다
        List<FormationTemplate> formations = Formations();
        back.SetFormations(back.AttackFormationId, back.DefenseFormationId, formations);
        (float x, float z) expected = FormationRules.Position(FormationRules.Find(formations, "4-4-2"), 7, defending: false);
        Assert.AreEqual(expected.x, back.Lineup[6].AttackX, 0.001f, "좌표는 템플릿 슬롯 값");
        Assert.AreEqual(expected.z, back.Lineup[6].AttackZ, 0.001f);
    }
}
