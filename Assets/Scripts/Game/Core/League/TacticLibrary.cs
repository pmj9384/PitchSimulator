using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Tactics;

namespace Game.Core.League
{
    // 저장 전술 한 장(10-25 슬롯 격자, 유저 "전술을 내가 만들고 저장해 둘 수 있어야지"). FM의 전술 파일과 같은 구성: 자리·규칙만 담고 선수 배정은 담지 않는다.
    // 선수는 시즌마다 바뀌어도 "압박 4-3-3"은 그대로 꺼내 쓸 수 있어야 해서다. 수비 역할은 선수가 아니라 **수비 슬롯 번호**에 붙인다(같은 이유)
    public sealed class SavedTactic
    {
        public const int NameMaxLength = 12;   // [가정] 전술판의 탭 한 칸에 들어가는 길이

        public string Name { get; }
        public string AttackFormationId { get; }
        public string DefenseFormationId { get; }
        public IReadOnlyList<string> DefenseRoleBySlot { get; }   // 길이 11, [슬롯 - 1] = DefenseRoles.csv id
        public string PresetId { get; }                            // 팀 전술 기준 카드
        public TeamTactics? CustomTactics { get; }                 // 카드에서 바꾼 값. null = 카드 값 그대로

        public SavedTactic(string name, string attackFormationId, string defenseFormationId, IReadOnlyList<string> defenseRoleBySlot, string presetId, TeamTactics? customTactics)
        {
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("[SavedTactic] 이름이 비었다"); }
            if (name.Length > NameMaxLength) { throw new ArgumentException($"[SavedTactic] 이름은 {NameMaxLength}자까지 ({name})"); }
            if (string.IsNullOrEmpty(attackFormationId) || string.IsNullOrEmpty(defenseFormationId)) { throw new ArgumentException($"[SavedTactic] {name}: 포메이션 id가 비었다"); }
            if (defenseRoleBySlot.Count != FormationTemplate.SlotCount) { throw new ArgumentException($"[SavedTactic] {name}: 수비 역할은 슬롯 수 {FormationTemplate.SlotCount}칸 ({defenseRoleBySlot.Count})"); }
            if (string.IsNullOrEmpty(presetId)) { throw new ArgumentException($"[SavedTactic] {name}: 팀 전술 카드 id가 비었다"); }
            Name = name.Trim();
            AttackFormationId = attackFormationId;
            DefenseFormationId = defenseFormationId;
            DefenseRoleBySlot = new List<string>(defenseRoleBySlot);
            PresetId = presetId;
            CustomTactics = customTactics == null ? null : TacticsEditing.Copy(customTactics);
        }

        public string DefenseRoleOf(int defenseSlot)
        {
            return DefenseRoleBySlot[defenseSlot - 1];
        }

        // 화면에 적는 팀 전술 이름. 바꾼 값이 있어도 기준 카드 이름(SeasonState.MyCardName과 같은 규칙)
        public string CardName(IReadOnlyList<TeamTactics> presets)
        {
            return SeasonRunner.FindPreset(presets, PresetId).DisplayName;
        }
    }

    // 저장 전술 목록. 시즌과 별개로 세이브에 남는다(시즌이 끝나도 전술은 남아야 한다). 이름이 열쇠: 같은 이름으로 저장하면 덮어쓴다
    public sealed class TacticLibrary
    {
        public const int MaxCount = 5;   // [가정] FM은 활성 전술 3장. 저장은 더 두되 전술판 탭이 넘치지 않는 수

        // 전술판의 저장 칸 이름(10-26 ②). 이름 입력 없이 FM 모바일처럼 고정 칸 5개를 쓴다: 프로젝트에 글자 입력 UI가 없고 모바일 키보드보다 칸 하나 누르기가 빠르다
        public static string SlotName(int index)
        {
            if (index < 0 || index >= MaxCount) { throw new ArgumentOutOfRangeException(nameof(index), $"[TacticLibrary] 칸은 0~{MaxCount - 1} ({index})"); }
            return $"전술 {index + 1}";
        }

        private readonly List<SavedTactic> tactics = new List<SavedTactic>();

        public IReadOnlyList<SavedTactic> All => tactics;

        public SavedTactic? Find(string name)
        {
            for (int i = 0; i < tactics.Count; i++)
            {
                if (string.Equals(tactics[i].Name, name, StringComparison.Ordinal)) { return tactics[i]; }
            }
            return null;
        }

        // 같은 이름이 있으면 그 자리에 덮어쓴다(순서 유지). 새 이름인데 꽉 차 있으면 던진다: 화면이 먼저 막아야 하고, 여기서 조용히 버리면 저장했다고 믿게 된다
        public void Put(SavedTactic tactic)
        {
            for (int i = 0; i < tactics.Count; i++)
            {
                if (!string.Equals(tactics[i].Name, tactic.Name, StringComparison.Ordinal)) { continue; }
                tactics[i] = tactic;
                return;
            }
            if (tactics.Count >= MaxCount) { throw new InvalidOperationException($"[TacticLibrary] 저장 전술은 {MaxCount}장까지"); }
            tactics.Add(tactic);
        }

        public bool Remove(string name)
        {
            for (int i = 0; i < tactics.Count; i++)
            {
                if (!string.Equals(tactics[i].Name, name, StringComparison.Ordinal)) { continue; }
                tactics.RemoveAt(i);
                return true;
            }
            return false;
        }

        public TacticLibrarySave ToSave()
        {
            var save = new TacticLibrarySave();
            for (int i = 0; i < tactics.Count; i++)
            {
                SavedTactic t = tactics[i];
                var entry = new TacticLibrarySave.Entry
                {
                    name = t.Name, attackFormation = t.AttackFormationId, defenseFormation = t.DefenseFormationId,
                    defenseRoles = new List<string>(t.DefenseRoleBySlot), presetId = t.PresetId,
                };
                if (t.CustomTactics != null) { entry.tactics = SeasonSave.TacticsValues.From(t.CustomTactics); }
                save.tactics.Add(entry);
            }
            return save;
        }

        // 옛 세이브(10-25 전)엔 없어 null → 빈 목록. 한 장이 깨져 있으면 그 장만 버리지 않고 던진다(SeasonSystem.Load와 같은 정책: 크게 알리고 기본값)
        public static TacticLibrary FromSave(TacticLibrarySave? save)
        {
            var library = new TacticLibrary();
            if (save == null) { return library; }
            for (int i = 0; i < save.tactics.Count; i++)
            {
                TacticLibrarySave.Entry e = save.tactics[i];
                TeamTactics? custom = e.tactics == null ? null : e.tactics.ToTactics(e.presetId ?? SeasonState.DefaultPresetId);
                library.Put(new SavedTactic(e.name ?? string.Empty, e.attackFormation ?? string.Empty, e.defenseFormation ?? string.Empty,
                    e.defenseRoles ?? new List<string>(), e.presetId ?? SeasonState.DefaultPresetId, custom));
            }
            return library;
        }
    }

    // 세이브 DTO. SaveDataV1.tacticLibrary로 얹힌다(Json.NET, 필드 camelCase 관례)
    [Serializable]
    public sealed class TacticLibrarySave
    {
        public List<Entry> tactics = new List<Entry>();

        [Serializable]
        public sealed class Entry
        {
            public string? name;
            public string? attackFormation;
            public string? defenseFormation;
            public List<string>? defenseRoles;
            public string? presetId;
            public SeasonSave.TacticsValues? tactics;   // 카드 값 그대로면 null
        }
    }
}
