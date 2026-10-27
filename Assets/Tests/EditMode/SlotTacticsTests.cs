using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 슬롯 중심 전술(10-27, 스펙 §4-2): 역할 변형·개인 지시·수비 역할은 선수가 아니라 슬롯 번호의 것이다(FM26·EA FC "슬롯 = 역할·지시 / 선수 = 적성·능력치").
// ①칩(선수)을 바꿔도 슬롯 설정은 제자리 ②적성: 슬롯 변형 ≠ 자연 변형이면 다이얼은 기본 변형과의 중간값 ③포메이션을 바꾸면 자리가 바뀐 공격 슬롯만 기본 변형으로
// ④저장 전술이 슬롯 설정을 통째로 왕복 ⑤10-27 전 세이브(선수의 지시·라인업의 수비 역할)와 옛 저장 전술 항목의 1회 이행 ⑥검증
public class SlotTacticsTests
{
    private static List<PlayerStats> table;
    private static List<FormationTemplate> formations;
    private static List<DefenseRole> roles;
    private static List<StageEntry> myRows;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        formations = FormationTemplateParser.Parse(File.ReadAllText("Assets/Resources/Tables/FormationTemplates.csv"));
        roles = DefenseRoleParser.Parse(File.ReadAllText("Assets/Resources/Tables/DefenseRoles.csv"));
        myRows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1 && r.Team == 0);
    }

    private static SeasonState NewState()
    {
        return SeasonState.NewSeason(4, 42, myRows, table);
    }

    private static int LineupIndexOf(SeasonState state, int playerId)
    {
        for (int i = 0; i < state.Lineup.Count; i++)
        {
            if (state.Lineup[i].PlayerId == playerId) { return i; }
        }
        Assert.Fail($"라인업에 없는 선수 {playerId}");
        return -1;
    }

    [Test]
    public void 새_시즌의_슬롯_변형은_선_선수의_변형이고_지시는_0이며_수비_역할은_균형이다()
    {
        SeasonState state = NewState();
        for (int i = 0; i < state.Lineup.Count; i++)
        {
            LineupEntry e = state.Lineup[i];
            Assert.AreEqual(state.FindRosterPlayer(e.PlayerId).Stats.VariantId, state.Slots.AttackVariantOf(e.AttackSlot));
            Assert.IsFalse(state.Slots.HasInstructions(e.AttackSlot));
            Assert.AreEqual(DefenseRole.DefaultId, state.Slots.DefenseRoleOf(e.DefenseSlot));
        }
    }

    [Test]
    public void 칩을_바꿔도_슬롯의_변형_지시_수비_역할은_제자리고_새로_선_선수에게_얹힌다()
    {
        // 10-26 유저: "역할은 자리에 고정이고 그 자리를 맡는 선수가 바뀌는 게 맞다". 10-27 전엔 수비 역할이 LineupEntry(선수 항목)에 있어 교체 때 선수를 따라갔다
        SeasonState state = NewState();
        int leftFb = state.Lineup[1].PlayerId;
        int rightFb = state.Lineup[4].PlayerId;
        int leftSlot = state.Lineup[1].AttackSlot;
        int leftDefense = state.Lineup[1].DefenseSlot;
        state.SetSlotVariant(leftSlot, "fb_wingback", formations, table);
        state.SetSlotInstruction(leftSlot, PlayerDial.PushUp, 2, formations);
        state.SetDefenseRole(leftDefense, "tracking", roles);

        state.SwapLineupSlots(leftFb, rightFb, defending: false, formations);
        state.SwapLineupSlots(leftFb, rightFb, defending: true, formations);

        Assert.AreEqual(leftSlot, state.Lineup[4].AttackSlot, "오른쪽 FB가 왼쪽 슬롯에 선다");
        Assert.AreEqual("fb_wingback", state.Slots.AttackVariantOf(leftSlot), "변형은 슬롯에 남는다");
        Assert.AreEqual(2, state.Slots.InstructionsOf(leftSlot)[(int)PlayerDial.PushUp], "지시도");
        Assert.AreEqual("tracking", state.Slots.DefenseRoleOf(leftDefense), "수비 역할도");
        LineupSlot composed = state.MyLineup(table, roles)[4];
        PlayerStats wingback = PlayerTableLookup.FindVariant(table, "fb_wingback")!;
        PlayerStats fullback = TeamGenerator.DefaultVariant(table, "FB");
        float expectedPushUp = (wingback.PushUp + fullback.PushUp) * 0.5f + 10f;   // 적성 중간값 + 지시 +2칸(5m × 2)
        Assert.AreEqual(expectedPushUp, composed.Stats.PushUp, 1e-4f, "새로 선 선수가 슬롯 변형·지시를 받는다");
        DefenseRole tracking = DefenseRoleParser.Find(roles, "tracking")!;
        Assert.AreEqual(System.Math.Max(0f, (wingback.LineHeight + fullback.LineHeight) * 0.5f + tracking.LineHeightOffset), composed.Stats.LineHeight, 1e-4f, "수비 역할도");
    }

    [Test]
    public void 적성은_슬롯_변형이_자연_변형과_같으면_100_다르면_기본_변형과의_중간값이다()
    {
        PlayerStats poacher = BuildScaler.Copy(PlayerTableLookup.FindVariant(table, "st_poacher")!);
        PlayerStats advanced = PlayerTableLookup.FindVariant(table, "st_advanced")!;
        int[] none = PlayerInstructions.Empty();

        PlayerStats natural = LineupComposer.Compose(poacher, PlayerTableLookup.FindVariant(table, "st_poacher")!, none, null, table);
        Assert.AreEqual(poacher.ShotBias, natural.ShotBias, 1e-4f, "자연 변형이면 다이얼 그대로");
        Assert.AreEqual(poacher.Shot, natural.Shot, "빌드는 선수 것");

        PlayerStats mismatched = LineupComposer.Compose(poacher, advanced, none, null, table);
        PlayerStats baseline = TeamGenerator.DefaultVariant(table, "ST");
        Assert.AreEqual((advanced.ShotBias + baseline.ShotBias) * 0.5f, mismatched.ShotBias, 1e-4f, "다르면 변형과 기본 변형의 중간");
        Assert.AreEqual((advanced.PushUp + baseline.PushUp) * 0.5f, mismatched.PushUp, 1e-4f);
        Assert.AreEqual("st_advanced", mismatched.VariantId, "경기 값의 이름은 슬롯 변형");
        Assert.AreEqual(poacher.Speed, mismatched.Speed, "빌드는 선수 것");
        Assert.IsTrue(LineupComposer.IsNatural(poacher, poacher));
        Assert.IsFalse(LineupComposer.IsNatural(poacher, advanced));
    }

    [Test]
    public void 포메이션을_바꾸면_자리가_바뀐_공격_슬롯만_기본_변형으로_돌아가고_수비_역할은_그대로다()
    {
        SeasonState state = NewState();
        FormationTemplate f442 = FormationRules.Find(formations, "4-4-2");
        FormationTemplate f433 = FormationRules.Find(formations, "4-3-3");
        state.SetSlotVariant(2, "fb_wingback", formations, table);   // 4-4-2 슬롯 2 = FB, 4-3-3도 FB
        state.SetSlotInstruction(2, PlayerDial.PushUp, 1, formations);
        state.SetDefenseRole(7, "pressing", roles);
        var before = new List<string>();
        for (int slot = 1; slot <= SlotTactics.SlotCount; slot++) { before.Add(state.Slots.AttackVariantOf(slot)); }

        state.SetFormations("4-3-3", "5-3-2", formations, table);

        int changed = 0;
        for (int i = 0; i < SlotTactics.SlotCount; i++)
        {
            int slot = i + 1;
            if (f442.Slots[i].RoleId == f433.Slots[i].RoleId)
            {
                Assert.AreEqual(before[i], state.Slots.AttackVariantOf(slot), $"자리가 같은 슬롯 {slot}은 그대로");
                continue;
            }
            changed++;
            Assert.AreEqual(TeamGenerator.DefaultVariant(table, f433.Slots[i].RoleId).VariantId, state.Slots.AttackVariantOf(slot), $"자리가 바뀐 슬롯 {slot}은 새 자리의 기본 변형");
            Assert.IsFalse(state.Slots.HasInstructions(slot));
        }
        Assert.Greater(changed, 0, "4-4-2 → 4-3-3은 자리가 바뀌는 슬롯이 있어야 뜻이 있다");
        Assert.AreEqual("fb_wingback", state.Slots.AttackVariantOf(2));
        Assert.AreEqual(1, state.Slots.InstructionsOf(2)[(int)PlayerDial.PushUp]);
        Assert.AreEqual("pressing", state.Slots.DefenseRoleOf(7), "수비 역할은 자리와 무관한 성향이라 남는다");
        Assert.DoesNotThrow(() => state.MyLineup(table, roles), "바뀐 뒤에도 전원 조립된다");
    }

    [Test]
    public void 저장_전술은_슬롯_변형_지시_수비_역할을_통째로_담고_입히고_세이브로_왕복한다()
    {
        SeasonState state = NewState();
        state.SetSlotVariant(10, "st_advanced", formations, table);
        state.SetSlotInstruction(10, PlayerDial.PressRange, -1, formations);
        state.SetDefenseRole(3, "covering", roles);
        SavedTactic tactic = state.SnapshotTactic("전술 1");
        List<TeamTactics> presets = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));

        SeasonState other = NewState();
        other.ApplyTactic(tactic, formations, roles, presets, table);
        Assert.AreEqual("st_advanced", other.Slots.AttackVariantOf(10));
        Assert.AreEqual(-1, other.Slots.InstructionsOf(10)[(int)PlayerDial.PressRange]);
        Assert.AreEqual("covering", other.Slots.DefenseRoleOf(3));
        Assert.IsTrue(other.Slots.SameAs(state.Slots));

        var library = new TacticLibrary();
        library.Put(tactic);
        TacticLibrary back = TacticLibrary.FromSave(library.ToSave(), formations, table);
        Assert.IsTrue(back.Find("전술 1")!.Slots.SameAs(state.Slots), "저장 전술 목록 세이브 왕복");

        SeasonState loaded = SeasonState.FromSave(state.ToSave(), table);
        Assert.IsTrue(loaded.Slots.SameAs(state.Slots), "시즌 세이브 왕복");
        Assert.IsFalse(loaded.Slots.SameAs(NewState().Slots));
    }

    [Test]
    public void 옛_세이브의_선수_지시와_라인업_수비_역할은_슬롯으로_옮겨지고_경기_입력이_같다()
    {
        // 10-27 전: 선수에 instructions 10칸(라인 높이 포함), 라인업 항목에 defenseRole. 이행 뒤 경기 입력 = 손으로 조립한 "변형 + 지시 9칸 + 수비 역할"
        SeasonState state = NewState();
        SeasonSave old = state.ToSave();
        old.slotTactics = null;
        int striker = state.Lineup[9].PlayerId;
        int attackSlot = state.Lineup[9].AttackSlot;
        int defenseSlot = state.Lineup[9].DefenseSlot;
        int[] legacy = new int[PlayerInstructions.LegacyDialCount];
        legacy[2] = 2;    // 압박 거리 +2
        legacy[3] = -2;   // 라인 높이(버려진다)
        legacy[8] = 1;    // 볼 끌기 +1
        for (int i = 0; i < old.roster.Count; i++)
        {
            if (old.roster[i].playerId == striker) { old.roster[i].instructions = legacy; }
        }
        old.lineup[9].defenseRole = "pressing";

        SeasonState migrated = SeasonState.FromSave(old, table);

        Assert.AreEqual(state.FindRosterPlayer(striker).Stats.VariantId, migrated.Slots.AttackVariantOf(attackSlot), "변형은 선 선수 것");
        CollectionAssert.AreEqual(PlayerInstructions.FromLegacy(legacy), migrated.Slots.InstructionsOf(attackSlot), "지시는 10칸 → 9칸");
        Assert.AreEqual("pressing", migrated.Slots.DefenseRoleOf(defenseSlot));

        PlayerStats variant = PlayerTableLookup.FindVariant(table, migrated.Slots.AttackVariantOf(attackSlot))!;
        PlayerStats expected = BuildScaler.Copy(variant);
        BuildScaler.CopyBuild(state.FindRosterPlayer(striker).Stats, expected);
        expected = PlayerInstructions.Apply(expected, PlayerInstructions.FromLegacy(legacy));
        expected = DefenseRoleParser.Find(roles, "pressing")!.Apply(expected);
        LineupSlot composed = migrated.MyLineup(table, roles)[9];
        Assert.AreEqual(expected.PressRange, composed.Stats.PressRange, 1e-4f, "압박 거리 = (역할 + 2칸) × 1.5");
        Assert.AreEqual(expected.HoldUp, composed.Stats.HoldUp, 1e-4f);
        Assert.AreEqual(expected.LineHeight, composed.Stats.LineHeight, 1e-4f, "라인 높이는 수비 역할만 반영");
        Assert.AreEqual(expected.Shot, composed.Stats.Shot);
    }

    [Test]
    public void 옛_저장_전술_항목은_수비_역할만_있어_공격_변형이_기본값으로_채워진다()
    {
        var save = new TacticLibrarySave();
        var entry = new TacticLibrarySave.Entry { name = "옛 전술", attackFormation = "4-3-3", defenseFormation = "4-4-2", presetId = "balanced", defenseRoles = new List<string>() };
        for (int i = 0; i < SlotTactics.SlotCount; i++) { entry.defenseRoles.Add(i == 4 ? "screening" : DefenseRole.DefaultId); }
        save.tactics.Add(entry);

        TacticLibrary library = TacticLibrary.FromSave(save, formations, table);
        SavedTactic tactic = library.Find("옛 전술")!;
        FormationTemplate f433 = FormationRules.Find(formations, "4-3-3");
        Assert.AreEqual("screening", tactic.Slots.DefenseRoleOf(5));
        Assert.AreEqual(TeamGenerator.DefaultVariant(table, f433.Slots[6].RoleId).VariantId, tactic.Slots.AttackVariantOf(7));
        Assert.IsFalse(tactic.Slots.HasInstructions(7));
    }

    [Test]
    public void 검증은_자리가_다른_변형_없는_수비_역할_범위_밖_지시를_거부한다()
    {
        FormationTemplate f442 = FormationRules.Find(formations, "4-4-2");
        SlotTactics good = SlotTactics.Default(f442, table);
        Assert.DoesNotThrow(() => good.Validate(f442, table, roles));

        SlotTacticsSave wrongPosition = good.ToSave();
        wrongPosition.attackVariants[0] = "st_poacher";   // 슬롯 1은 GK
        Assert.Throws<System.InvalidOperationException>(() => SlotTactics.FromSave(wrongPosition).Validate(f442, table, roles));

        SlotTacticsSave ghostRole = good.ToSave();
        ghostRole.defenseRoles[3] = "ghost";
        Assert.Throws<System.InvalidOperationException>(() => SlotTactics.FromSave(ghostRole).Validate(f442, table, roles));

        SlotTacticsSave outOfRange = good.ToSave();
        outOfRange.instructions[9][(int)PlayerDial.PushUp] = 5;
        Assert.Throws<System.InvalidOperationException>(() => SlotTactics.FromSave(outOfRange).Validate(f442, table, roles));

        SlotTacticsSave shortSave = good.ToSave();
        shortSave.defenseRoles.RemoveAt(0);
        Assert.Throws<System.InvalidOperationException>(() => SlotTactics.FromSave(shortSave));

        Assert.Throws<System.InvalidOperationException>(() => good.SetVariant(FormationRules.SlotOf(f442, 2), "cb_centreback", table), "FB 슬롯에 CB 변형");
        Assert.Throws<System.InvalidOperationException>(() => good.SetInstruction(FormationRules.SlotOf(f442, 3), PlayerDial.ShotBias, 1), "CB 자리에 슛 성향");
    }
}
