using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Tactics;

namespace Game.Core.League
{
    // 전술의 슬롯 층(10-27 슬롯 중심, 스펙 §4-2): 공격 슬롯 11칸 × (공격 역할 변형 · 개인 지시) + 수비 슬롯 11칸 × 수비 역할.
    // 역할·지시는 자리의 것이고 선수의 것이 아니다(FM26·EA FC "슬롯 = 역할·지시 / 선수 = 적성·능력치"). 칩을 바꿔도 여기는 안 바뀐다.
    // 어느 변형이 어느 슬롯에 설 수 있는지는 그 슬롯의 자리(FormationSlot.RoleId)로 가린다. 포메이션은 SeasonState가 들고 있으므로 자리를 인자로 받는다
    public sealed class SlotTactics
    {
        public const int SlotCount = FormationTemplate.SlotCount;

        private readonly string[] attackVariants = new string[SlotCount];   // [공격 슬롯 − 1] = PlayerTable variantId
        private readonly int[][] instructions = new int[SlotCount][];     // [공격 슬롯 − 1] = PlayerDial 순서 오프셋
        private readonly string[] defenseRoles = new string[SlotCount];   // [수비 슬롯 − 1] = DefenseRoles.csv id

        private SlotTactics()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                attackVariants[i] = string.Empty;
                instructions[i] = PlayerInstructions.Empty();
                defenseRoles[i] = DefenseRole.DefaultId;
            }
        }

        // 공격 템플릿의 자리마다 기본 변형(PlayerTable 첫 행), 지시 0, 수비 역할 균형
        public static SlotTactics Default(FormationTemplate attack, IReadOnlyList<PlayerStats> table)
        {
            var slots = new SlotTactics();
            for (int i = 0; i < SlotCount; i++)
            {
                slots.attackVariants[i] = TeamGenerator.DefaultVariant(table, attack.Slots[i].RoleId).VariantId;
            }
            return slots;
        }

        // 선 선수의 변형을 그 공격 슬롯의 변형으로(새 시즌의 편성 행·10-27 전 세이브 이행). 선수 i가 선 공격 슬롯 ← 선수 i의 변형
        public static SlotTactics FromStarters(IReadOnlyList<RosterPlayer> roster, IReadOnlyList<LineupEntry> lineup)
        {
            var slots = new SlotTactics();
            for (int i = 0; i < lineup.Count; i++)
            {
                slots.attackVariants[lineup[i].AttackSlot - 1] = FindPlayer(roster, lineup[i].PlayerId).Stats.VariantId;
            }
            for (int i = 0; i < SlotCount; i++)
            {
                if (slots.attackVariants[i].Length == 0) { throw new InvalidOperationException($"[SlotTactics] 공격 슬롯 {i + 1}에 선 선수가 없다"); }
            }
            return slots;
        }

        // 10-27 전 세이브(선수에 지시, 라인업 항목에 수비 역할)를 슬롯 설정 DTO로. 변형은 선 선수의 variantId
        public static SlotTacticsSave FromStartersSave(SeasonSave save, IReadOnlyList<LineupEntry> lineup)
        {
            var result = new SlotTacticsSave();
            for (int i = 0; i < SlotCount; i++)
            {
                result.attackVariants.Add(string.Empty);
                result.instructions.Add(PlayerInstructions.Empty());
                result.defenseRoles.Add(DefenseRole.DefaultId);
            }
            for (int i = 0; i < lineup.Count; i++)
            {
                SeasonSave.Player player = FindSavedPlayer(save, lineup[i].PlayerId);
                result.attackVariants[lineup[i].AttackSlot - 1] = player.variantId;
                result.instructions[lineup[i].AttackSlot - 1] = PlayerInstructions.FromLegacy(player.instructions);
                string? role = save.lineup[i].defenseRole;
                result.defenseRoles[lineup[i].DefenseSlot - 1] = string.IsNullOrEmpty(role) ? DefenseRole.DefaultId : role!;
            }
            return result;
        }

        private static RosterPlayer FindPlayer(IReadOnlyList<RosterPlayer> roster, int playerId)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].PlayerId == playerId) { return roster[i]; }
            }
            throw new InvalidOperationException($"[SlotTactics] 로스터에 없는 선수 id {playerId}");
        }

        private static SeasonSave.Player FindSavedPlayer(SeasonSave save, int playerId)
        {
            for (int i = 0; i < save.roster.Count; i++)
            {
                if (save.roster[i].playerId == playerId) { return save.roster[i]; }
            }
            throw new InvalidOperationException($"[SlotTactics] 세이브 로스터에 없는 선수 id {playerId}");
        }

        public string AttackVariantOf(int attackSlot)
        {
            return attackVariants[Index(attackSlot)];
        }

        public IReadOnlyList<int> InstructionsOf(int attackSlot)
        {
            return instructions[Index(attackSlot)];
        }

        public bool HasInstructions(int attackSlot)
        {
            int[] offsets = instructions[Index(attackSlot)];
            for (int i = 0; i < offsets.Length; i++)
            {
                if (offsets[i] != 0) { return true; }
            }
            return false;
        }

        public string DefenseRoleOf(int defenseSlot)
        {
            return defenseRoles[Index(defenseSlot)];
        }

        // 공격 슬롯의 역할 변형. 그 자리의 노출된 변형만 새로 줄 수 있고, 이미 들고 있는(잠긴) 변형은 그대로 둘 수 있다(PlayerTableLookup.IsSamePosition 주석).
        // 지시는 역할 기준 오프셋이라 역할이 바뀌면 지운다(팀 전술에서 카드를 고르면 바꾼 값이 지워지는 것과 같은 규칙)
        public void SetVariant(FormationSlot slot, string variantId, IReadOnlyList<PlayerStats> table)
        {
            int i = Index(slot.Slot);
            if (string.Equals(attackVariants[i], variantId, StringComparison.OrdinalIgnoreCase)) { return; }
            PlayerStats? variant = PlayerTableLookup.FindVariant(table, variantId);
            if (variant == null) { throw new InvalidOperationException($"[SlotTactics] PlayerTable에 없는 역할: {variantId}"); }
            if (!PlayerTableLookup.IsAssignable(variant, slot.RoleId))
            {
                throw new InvalidOperationException($"[SlotTactics] 슬롯 {slot.Slot}({slot.RoleId})에 줄 수 없는 역할: {variantId}(자리 {variant.RoleId}, 노출 {variant.Exposed})");
            }
            attackVariants[i] = variant.VariantId;
            instructions[i] = PlayerInstructions.Empty();
        }

        // 지시 한 칸. 범위 밖·그 자리에 해당 없는 다이얼이면 던지고 아무것도 안 바뀐다
        public void SetInstruction(FormationSlot slot, PlayerDial dial, int offset)
        {
            int i = Index(slot.Slot);
            int[] next = (int[])instructions[i].Clone();
            next[(int)dial] = offset;
            PlayerInstructions.Validate(next, slot.RoleId);
            instructions[i] = next;
        }

        public void ClearInstructions(int attackSlot)
        {
            instructions[Index(attackSlot)] = PlayerInstructions.Empty();
        }

        public void SetDefenseRole(int defenseSlot, string defenseRoleId, IReadOnlyList<DefenseRole> roles)
        {
            if (DefenseRoleParser.Find(roles, defenseRoleId) == null) { throw new InvalidOperationException($"[SlotTactics] 없는 수비 역할 id {defenseRoleId}"); }
            defenseRoles[Index(defenseSlot)] = defenseRoleId;
        }

        // 공격 포메이션이 바뀔 때: 자리(roleId)가 같은 슬롯 번호는 변형·지시를 지키고, 자리가 바뀐 슬롯은 새 자리의 기본 변형·지시 0으로 [가정, 스펙 §4-2].
        // 수비 역할 5종은 자리와 무관한 성향이라 수비 포메이션이 바뀌어도 그대로 둔다
        public void ResetAttackFor(FormationTemplate previousAttack, FormationTemplate nextAttack, IReadOnlyList<PlayerStats> table)
        {
            var defaults = new string?[SlotCount];   // 먼저 전부 구한다: 표에 없는 자리로 중간에 던지면 반쪽만 바뀐 채 남는다(10-27 리뷰 🟢)
            for (int i = 0; i < SlotCount; i++)
            {
                if (string.Equals(previousAttack.Slots[i].RoleId, nextAttack.Slots[i].RoleId, StringComparison.OrdinalIgnoreCase)) { continue; }
                defaults[i] = TeamGenerator.DefaultVariant(table, nextAttack.Slots[i].RoleId).VariantId;
            }
            for (int i = 0; i < SlotCount; i++)
            {
                if (defaults[i] == null) { continue; }
                attackVariants[i] = defaults[i]!;
                instructions[i] = PlayerInstructions.Empty();
            }
        }

        // 세이브·이행에서 온 설정을 표와 자리에 맞게 고친다(로드 때 한 번, SeasonSystem.Load·TacticLibrary.FromSave). 고친 내용을 문장으로 돌려줘 호출처가 알린다.
        // 표에서 사라진 변형·자리가 어긋난 변형(4-3-3에서 군이 어긋나 선 선수의 변형이 이행된 경우) → 그 자리 기본 변형·지시 0 / 지시는 범위·해당 여부로 정규화 / 없는 수비 역할 → 기본.
        // 던지지 않는 이유: 로드에서 던지면 시즌 전체를 잃는다(SeasonSystem.Load 정책). Validate(저장 전술 입히기)는 던진다: 그건 유저가 고른 한 장이라 거부하면 된다
        public List<string> Sanitize(FormationTemplate attack, IReadOnlyList<PlayerStats> table, IReadOnlyList<DefenseRole> roles)
        {
            var fixes = new List<string>();
            for (int i = 0; i < SlotCount; i++)
            {
                FormationSlot slot = attack.Slots[i];
                PlayerStats? variant = PlayerTableLookup.FindVariant(table, attackVariants[i]);
                if (variant == null || !PlayerTableLookup.IsSamePosition(variant, slot.RoleId))
                {
                    string fallback = TeamGenerator.DefaultVariant(table, slot.RoleId).VariantId;
                    fixes.Add($"공격 슬롯 {slot.Slot}({slot.RoleId})의 변형 {attackVariants[i]} → {fallback}");
                    attackVariants[i] = fallback;
                    instructions[i] = PlayerInstructions.Empty();
                    continue;
                }
                int[] normalized = PlayerInstructions.Normalize(instructions[i], slot.RoleId);
                if (!SameOffsets(normalized, instructions[i])) { fixes.Add($"공격 슬롯 {slot.Slot}({slot.RoleId})의 지시를 자리에 맞게 다듬음"); }
                instructions[i] = normalized;
            }
            for (int i = 0; i < SlotCount; i++)
            {
                if (DefenseRoleParser.Find(roles, defenseRoles[i]) != null) { continue; }
                fixes.Add($"수비 슬롯 {i + 1}의 수비 역할 {defenseRoles[i]} → {DefenseRole.DefaultId}");
                defenseRoles[i] = DefenseRole.DefaultId;
            }
            return fixes;
        }

        private static bool SameOffsets(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) { return false; }
            }
            return true;
        }

        // 저장 전술을 입히거나 로드할 때: 변형이 표에 있고 공격 템플릿의 자리와 맞는지, 지시가 범위 안인지, 수비 역할이 표에 있는지. 하나라도 틀리면 던진다
        public void Validate(FormationTemplate attack, IReadOnlyList<PlayerStats> table, IReadOnlyList<DefenseRole> roles)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                FormationSlot slot = attack.Slots[i];
                PlayerStats? variant = PlayerTableLookup.FindVariant(table, attackVariants[i]);
                if (variant == null) { throw new InvalidOperationException($"[SlotTactics] 공격 슬롯 {slot.Slot}의 변형 {attackVariants[i]}가 PlayerTable에 없다"); }
                if (!PlayerTableLookup.IsSamePosition(variant, slot.RoleId)) { throw new InvalidOperationException($"[SlotTactics] 공격 슬롯 {slot.Slot}({slot.RoleId})에 {attackVariants[i]}(자리 {variant.RoleId})는 설 수 없다"); }
                PlayerInstructions.Validate(instructions[i], slot.RoleId);
                if (DefenseRoleParser.Find(roles, defenseRoles[i]) == null) { throw new InvalidOperationException($"[SlotTactics] 수비 슬롯 {i + 1}의 수비 역할 id {defenseRoles[i]}가 표에 없다"); }
            }
        }

        public SlotTactics Clone()
        {
            var copy = new SlotTactics();
            for (int i = 0; i < SlotCount; i++)
            {
                copy.attackVariants[i] = attackVariants[i];
                copy.instructions[i] = (int[])instructions[i].Clone();
                copy.defenseRoles[i] = defenseRoles[i];
            }
            return copy;
        }

        // 테스트 전용(왕복·입히기 단언). 프로덕션 호출처 없음
        public bool SameAs(SlotTactics other)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!string.Equals(attackVariants[i], other.attackVariants[i], StringComparison.OrdinalIgnoreCase)) { return false; }
                if (!string.Equals(defenseRoles[i], other.defenseRoles[i], StringComparison.OrdinalIgnoreCase)) { return false; }
                for (int d = 0; d < PlayerInstructions.DialCount; d++)
                {
                    if (instructions[i][d] != other.instructions[i][d]) { return false; }
                }
            }
            return true;
        }

        public SlotTacticsSave ToSave()
        {
            var save = new SlotTacticsSave();
            for (int i = 0; i < SlotCount; i++)
            {
                save.attackVariants.Add(attackVariants[i]);
                save.instructions.Add((int[])instructions[i].Clone());
                save.defenseRoles.Add(defenseRoles[i]);
            }
            return save;
        }

        // 칸 수만 본다. id·자리·범위는 로드 뒤 Sanitize(고침)나 입히기 전 Validate(거부)가 본다(포메이션·표가 필요해서 호출처가 한다)
        public static SlotTactics FromSave(SlotTacticsSave save)
        {
            if (save.attackVariants.Count != SlotCount || save.instructions.Count != SlotCount || save.defenseRoles.Count != SlotCount)
            {
                throw new InvalidOperationException($"[SlotTactics] 세이브의 슬롯 설정이 {SlotCount}칸이 아니다({save.attackVariants.Count}/{save.instructions.Count}/{save.defenseRoles.Count})");
            }
            var slots = new SlotTactics();
            for (int i = 0; i < SlotCount; i++)
            {
                slots.attackVariants[i] = save.attackVariants[i] ?? string.Empty;
                slots.instructions[i] = PlayerInstructions.FromLegacy(save.instructions[i]);
                slots.defenseRoles[i] = string.IsNullOrEmpty(save.defenseRoles[i]) ? DefenseRole.DefaultId : save.defenseRoles[i];
            }
            return slots;
        }

        private static int Index(int slot)
        {
            if (slot < 1 || slot > SlotCount) { throw new ArgumentOutOfRangeException(nameof(slot), $"[SlotTactics] 슬롯은 1~{SlotCount} ({slot})"); }
            return slot - 1;
        }
    }

    // 세이브 DTO(Json.NET, 필드 camelCase 관례). 시즌 세이브(현재 전술)와 저장 전술 목록이 같은 꼴을 쓴다
    [Serializable]
    public sealed class SlotTacticsSave
    {
        public List<string> attackVariants = new List<string>();
        public List<int[]> instructions = new List<int[]>();
        public List<string> defenseRoles = new List<string>();
    }
}
