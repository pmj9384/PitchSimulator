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
        TacticLibrary back = TacticLibrary.FromSave(library.ToSave(), formations, table, roles);
        Assert.IsTrue(back.Find("전술 1")!.Slots.SameAs(state.Slots), "저장 전술 목록 세이브 왕복");

        SeasonState loaded = SeasonState.FromSave(state.ToSave(), table);
        Assert.IsTrue(loaded.Slots.SameAs(state.Slots), "시즌 세이브 왕복");
        Assert.IsFalse(loaded.Slots.SameAs(NewState().Slots));
    }

    [Test]
    public void 옛_세이브의_선수_지시와_라인업_수비_역할은_슬롯으로_옮겨지고_경기_입력이_같다()
    {
        // 10-27 전: 선수에 instructions 10칸(라인 높이 포함), 라인업 항목에 defenseRole. 옛 PlayerDial 순서 = 전진·측면·압박·라인높이·슛·패스길이·패스리스크·드리블·볼끌기·출격
        SeasonState state = NewState();
        SeasonSave old = state.ToSave();
        old.slotTactics = null;
        int striker = state.Lineup[9].PlayerId;
        int attackSlot = state.Lineup[9].AttackSlot;
        int defenseSlot = state.Lineup[9].DefenseSlot;
        int[] legacy = { 0, 0, 2, -2, 1, 0, 0, 0, 1, 0 };   // 압박 +2, 라인 높이 −2(버려진다), 슛 성향 +1, 볼 끌기 +1
        int cm = state.Lineup[6].PlayerId;
        int cmSlot = state.Lineup[6].AttackSlot;
        int[] nineCells = PlayerInstructions.Empty();
        nineCells[(int)PlayerDial.PassRisk] = -1;
        RosterPlayer benchSt = state.AddToBench(PlayerTableLookup.FindVariant(table, "st_advanced")!, 1, portrait: 1);
        old = state.ToSave();
        old.slotTactics = null;
        for (int i = 0; i < old.roster.Count; i++)
        {
            if (old.roster[i].playerId == striker) { old.roster[i].instructions = legacy; }
            if (old.roster[i].playerId == cm) { old.roster[i].instructions = nineCells; }
            if (old.roster[i].playerId == benchSt.PlayerId) { old.roster[i].instructions = new[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 }; }
        }
        old.lineup[9].defenseRole = "pressing";

        SeasonState migrated = SeasonState.FromSave(old, table);

        Assert.AreEqual(state.FindRosterPlayer(striker).Stats.VariantId, migrated.Slots.AttackVariantOf(attackSlot), "변형은 선 선수 것");
        IReadOnlyList<int> moved = migrated.Slots.InstructionsOf(attackSlot);
        Assert.AreEqual(9, moved.Count);
        Assert.AreEqual(2, moved[(int)PlayerDial.PressRange], "압박 거리 칸은 자리가 그대로");
        Assert.AreEqual(1, moved[(int)PlayerDial.ShotBias], "라인 높이 칸이 사라져 슛 성향이 한 칸 당겨진다(값이 새지 않음)");
        Assert.AreEqual(1, moved[(int)PlayerDial.HoldUp], "볼 끌기도 한 칸 당겨진다");
        Assert.AreEqual(0, moved[(int)PlayerDial.PassLength]);
        Assert.AreEqual(0, moved[(int)PlayerDial.GkRushRadius]);
        Assert.AreEqual(-1, migrated.Slots.InstructionsOf(cmSlot)[(int)PlayerDial.PassRisk], "이미 9칸이면 그대로");
        Assert.AreEqual("pressing", migrated.Slots.DefenseRoleOf(defenseSlot));
        for (int slot = 1; slot <= SlotTactics.SlotCount; slot++)
        {
            if (slot == attackSlot || slot == cmSlot) { continue; }
            Assert.IsFalse(migrated.Slots.HasInstructions(slot), $"지시가 null인 선수의 슬롯 {slot}은 0. 벤치 선수의 지시는 슬롯이 없어 버린다");
        }

        // 경기 입력 = 변형(선수 빌드) + 지시 9칸 + 수비 역할. 리터럴로 조립한다(구현 함수로 기대값을 만들면 동어반복)
        PlayerStats variant = PlayerTableLookup.FindVariant(table, migrated.Slots.AttackVariantOf(attackSlot))!;
        DefenseRole pressing = DefenseRoleParser.Find(roles, "pressing")!;
        LineupSlot composed = migrated.MyLineup(table, roles)[9];
        Assert.AreEqual((variant.PressRange + 2f * 2f) * pressing.PressRangeScale, composed.Stats.PressRange, 1e-4f, "(역할 + 2칸 × 2m) × 1.5");
        Assert.AreEqual(variant.ShotBias - 0.05f, composed.Stats.ShotBias, 1e-4f, "슛 성향 +1칸 = −0.05");
        Assert.AreEqual(variant.HoldUp + 0.15f, composed.Stats.HoldUp, 1e-4f, "볼 끌기 +1칸 = +0.15");
        Assert.AreEqual(System.Math.Max(0f, variant.LineHeight + pressing.LineHeightOffset), composed.Stats.LineHeight, 1e-4f, "라인 높이는 수비 역할만(옛 −2칸은 버려짐)");
        Assert.AreEqual(state.FindRosterPlayer(striker).Stats.Shot, composed.Stats.Shot, "빌드는 선수 것");
    }

    [Test]
    public void 로드_뒤_고치기는_자리가_어긋난_변형_사라진_변형_해당_없는_지시_없는_수비_역할을_기본값으로_돌린다()
    {
        // 10-27 리뷰 🟡: 4-3-3에서 군이 어긋나 선 선수의 변형이 이행되면 슬롯 자리와 다른 변형이 들어가 저장 전술·역할 목록이 던졌다. 로드에서 던지면 시즌을 잃으니 고치고 알린다
        FormationTemplate f442 = FormationRules.Find(formations, "4-4-2");
        SlotTacticsSave bad = SlotTactics.Default(f442, table).ToSave();
        bad.attackVariants[5] = "st_poacher";      // 슬롯 6은 W. 자리가 다르다
        bad.attackVariants[9] = "st_vanished";     // 표에 없음
        bad.instructions[2][(int)PlayerDial.ShotBias] = 2;   // 슬롯 3은 CB: 슛 성향은 해당 없음
        bad.instructions[2][(int)PlayerDial.PressRange] = 7;  // 범위 밖
        bad.defenseRoles[0] = "ghost";
        SlotTactics slots = SlotTactics.FromSave(bad);

        List<string> fixes = slots.Sanitize(f442, table, roles);

        Assert.AreEqual(4, fixes.Count, string.Join(" / ", fixes));
        Assert.AreEqual(TeamGenerator.DefaultVariant(table, "W").VariantId, slots.AttackVariantOf(6));
        Assert.AreEqual(TeamGenerator.DefaultVariant(table, "ST").VariantId, slots.AttackVariantOf(10));
        Assert.AreEqual(0, slots.InstructionsOf(3)[(int)PlayerDial.ShotBias], "해당 없는 칸은 0");
        Assert.AreEqual(2, slots.InstructionsOf(3)[(int)PlayerDial.PressRange], "범위 밖은 +2로");
        Assert.AreEqual(DefenseRole.DefaultId, slots.DefenseRoleOf(1));
        Assert.DoesNotThrow(() => slots.Validate(f442, table, roles), "고친 뒤엔 검증을 통과한다");
        Assert.AreEqual(0, slots.Sanitize(f442, table, roles).Count, "두 번째는 고칠 게 없다");

        SeasonState state = NewState();
        Assert.AreEqual(0, state.SanitizeSlots(formations, table, roles).Count, "새 시즌은 고칠 게 없다");
        state.SetFormations(state.AttackFormationId, state.DefenseFormationId, formations, table);
        Assert.IsTrue(state.Slots.SameAs(NewState().Slots), "같은 id로 다시 골라도 슬롯 설정은 그대로(옛 세이브 좌표 변환 경로)");
    }

    [Test]
    public void 옛_저장_전술_항목은_수비_역할만_있어_공격_변형이_기본값으로_채워진다()
    {
        var save = new TacticLibrarySave();
        var entry = new TacticLibrarySave.Entry { name = "옛 전술", attackFormation = "4-3-3", defenseFormation = "4-4-2", presetId = "balanced", defenseRoles = new List<string>() };
        for (int i = 0; i < SlotTactics.SlotCount; i++) { entry.defenseRoles.Add(i == 4 ? "screening" : DefenseRole.DefaultId); }
        save.tactics.Add(entry);

        TacticLibrary library = TacticLibrary.FromSave(save, formations, table, roles);
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

    [Test]
    public void 어긋남_판정은_적성_역할군_전환_거리_셋을_플래그로_돌려주고_슬롯_변형_조회는_표에서_읽는다()
    {
        // 10-27 ③b: 칩의 "!"와 자리 패널 경고가 같은 한 판정(SeasonState.WarningsOf)을 읽는다(리뷰 🟡: UI 두 곳에 따로 적혀 있었다)
        SeasonState state = NewState();
        int striker = state.Lineup[9].PlayerId;
        Assert.AreEqual(LineupWarning.None, state.WarningsOf(striker, formations, table), "새 시즌 4-4-2는 전원 어긋남 없음");
        Assert.AreEqual(state.FindRosterPlayer(striker).Stats.VariantId, state.SlotVariantOf(state.Lineup[9].AttackSlot, table).VariantId);

        state.SetSlotVariant(state.Lineup[9].AttackSlot, "st_advanced", formations, table);
        Assert.AreEqual(LineupWarning.Aptitude, state.WarningsOf(striker, formations, table), "슬롯 변형 ≠ 자연 변형");
        Assert.AreEqual("st_advanced", state.SlotVariantOf(state.Lineup[9].AttackSlot, table).VariantId);

        state.SetFormations("4-3-3", "5-3-2", formations, table);   // 4-4-2 W 한 명이 미드 칸에 어긋나 선다(ReseatSlots 3단계)
        int mismatched = 0;
        foreach (LineupEntry e in state.Lineup)
        {
            if ((state.WarningsOf(e.PlayerId, formations, table) & LineupWarning.RoleGroup) != 0) { mismatched++; }
        }
        Assert.AreEqual(1, mismatched, "역할군 어긋남은 포메이션 전환으로 생긴 한 명");
        foreach (LineupEntry e in state.Lineup)
        {
            bool flagged = (state.WarningsOf(e.PlayerId, formations, table) & LineupWarning.FarTransition) != 0;
            Assert.AreEqual(FormationRules.IsFarTransition(state.TransitionDistanceOf(e.PlayerId)), flagged, $"전환 거리 플래그는 IsFarTransition과 같은 문턱(선수 {e.PlayerId})");
        }
        Assert.Throws<System.InvalidOperationException>(() => state.WarningsOf(999, formations, table), "라인업에 없는 선수");
    }
}
