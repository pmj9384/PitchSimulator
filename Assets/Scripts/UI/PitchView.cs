using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using UnityEngine;
using UnityEngine.UI;

// 전술 화면의 필드(09-30, FM·EA FC 전술 화면이 화면 절반 넘게를 필드로 쓰는 것을 따름). 내 11명을 칩으로 세운다.
// 공격할 때 / 수비할 때 탭은 각각 공격 포메이션·수비 포메이션의 격자다(10-26 슬롯 격자 ②, FM26 In/Out of Possession 모양 2장).
// 칩은 미터 좌표가 아니라 격자(FormationGrid)에 선다: 10-08~25의 미터 그리기는 22m 폭 칩이 라인 간격 12~17m에 겹쳤다(10-25 유저 Play). 그래서 슬라이더·다이얼로 칩이 움직이는 표시도 없다(FM 격자와 같다).
// 10-27 ③b 조작은 탭만: 칩을 누르면 그 선수(가 선 슬롯)를 고르고, 같은 칩을 다시 누르거나 필드 빈 곳을 누르면 선택을 푼다. 교체는 자리 패널의 후보 목록이 한다(끌기는 뺐다).
// 칩 글자: 공격 탭 = 공격 슬롯의 자리 + 슬롯 변형(선수의 자연 변형이 아니다, 10-27 슬롯 중심), 수비 탭 = 수비 슬롯의 자리 + 수비 역할.
// 어긋남 "!" = SeasonState.WarningsOf(적성·역할군·전환 거리)가 None이 아닐 때. 자리 패널 경고와 같은 판정 한 곳
public class PitchView : MonoBehaviour
{
    private const int DefendPhase = 1;   // phaseTabs: 0 = 공격할 때, 1 = 수비할 때
    public const int NoPlayer = SeasonState.NoPlayerId;

    [SerializeField] private PitchChip[] chips;   // 11개 고정(FormationTemplate.SlotCount). chips[i] = Lineup[i]
    [SerializeField] private TabGroup phaseTabs;
    [SerializeField] private Button fieldButton;  // 필드 바탕. 빈 곳을 누르면 선택을 푼다(명단으로 돌아감)

    // 칩을 눌러 선수를 고를 때(playerId). 전술 화면(TacticsBoardView)이 자리 패널을 그 선수의 슬롯으로 바꾼다
    public event Action<int> PlayerSelected;
    // 빈 곳을 눌러 선택을 풀 때. 전술 화면이 명단을 보인다
    public event Action Deselected;
    // 공격/수비 탭을 바꿀 때. 자리 패널이 변형 쪽/수비 역할 쪽을 바꿔 보인다
    public event Action PhaseChanged;

    public int SelectedPlayerId { get; private set; } = NoPlayer;
    public bool Defending => phaseTabs.SelectedIndex == DefendPhase;

    private bool initialized;

    private static SeasonState State => GameDataManager.Instance.Season.State;

    // 화면을 열 때와 전술이 바뀔 때 부른다
    public void Refresh()
    {
        EnsureInitialized();
        KeepSelectionInLineup();
        PlaceChips();
    }

    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        if (chips.Length != FormationTemplate.SlotCount)
        {
            Debug.LogError($"[PitchView] 칩 {chips.Length}개 ≠ 라인업 {FormationTemplate.SlotCount}명. 프리팹 배선을 확인한다");
        }
        phaseTabs.Initialize();
        phaseTabs.Selected += OnPhaseSelected;
        fieldButton.onClick.AddListener(OnFieldClicked);
        for (int i = 0; i < chips.Length; i++)
        {
            chips[i].Initialize();
            chips[i].Clicked += OnChipClicked;
        }
    }

    // 고른 선수가 라인업에 없으면(시즌이 바뀌어 선수 id가 달라졌을 때) 선택을 푼다. 화면을 처음 열 때도 고른 선수 없음 = 명단부터
    private void KeepSelectionInLineup()
    {
        if (SelectedPlayerId == NoPlayer) { return; }
        IReadOnlyList<LineupEntry> lineup = State.Lineup;
        for (int i = 0; i < lineup.Count; i++)
        {
            if (lineup[i].PlayerId == SelectedPlayerId) { return; }
        }
        SelectedPlayerId = NoPlayer;
    }

    // 코드가 고를 때(명단에서 선발을 눌렀을 때, 교체로 들어온 선수). 알림은 안 쏜다: 부른 쪽이 이미 안다
    public void Select(int playerId)
    {
        SelectedPlayerId = playerId;
        KeepSelectionInLineup();
        PlaceChips();
    }

    public void ClearSelection()
    {
        SelectedPlayerId = NoPlayer;
        PlaceChips();
    }

    // 같은 칩을 다시 누르면 선택 해제(명단으로)
    private void OnChipClicked(PitchChip chip)
    {
        int index = Array.IndexOf(chips, chip);
        int playerId = State.Lineup[index].PlayerId;
        if (playerId == SelectedPlayerId)
        {
            ClearSelection();
            Deselected?.Invoke();
            return;
        }
        SelectedPlayerId = playerId;
        PlaceChips();
        PlayerSelected?.Invoke(SelectedPlayerId);
    }

    private void OnFieldClicked()
    {
        if (SelectedPlayerId == NoPlayer) { return; }
        ClearSelection();
        Deselected?.Invoke();
    }

    private void OnPhaseSelected(int _)
    {
        PlaceChips();
        PhaseChanged?.Invoke();
    }

    // 탭의 포메이션 격자를 깔고, 각 선수를 자기 슬롯 칸에 세운다
    private void PlaceChips()
    {
        SeasonState state = State;
        IReadOnlyList<LineupEntry> lineup = state.Lineup;
        IReadOnlyList<FormationTemplate> formations = FormationTemplateRepository.All;
        bool defending = Defending;
        FormationTemplate attack = FormationRules.Find(formations, state.AttackFormationId);
        FormationTemplate defense = FormationRules.Find(formations, state.DefenseFormationId);
        FormationTemplate template = defending ? defense : attack;
        (float u, float v)[] grid = FormationGrid.Layout(template, defending);

        for (int i = 0; i < chips.Length; i++)
        {
            bool hasPlayer = i < lineup.Count;
            chips[i].gameObject.SetActive(hasPlayer);
            if (!hasPlayer) { continue; }

            LineupEntry entry = lineup[i];
            (float u, float v) cell = grid[(defending ? entry.DefenseSlot : entry.AttackSlot) - 1];
            string roleLabel;
            string nameLabel;
            if (defending)
            {
                roleLabel = FormationRules.SlotOf(defense, entry.DefenseSlot).RoleId;
                nameLabel = DefenseRoleRepository.Get(state.Slots.DefenseRoleOf(entry.DefenseSlot)).DisplayName;
            }
            else
            {
                roleLabel = FormationRules.SlotOf(attack, entry.AttackSlot).RoleId;
                nameLabel = state.SlotVariantOf(entry.AttackSlot, PlayerTableRepository.All).DisplayName;
            }
            bool warn = state.WarningsOf(entry.PlayerId, formations, PlayerTableRepository.All) != LineupWarning.None;
            chips[i].Show(roleLabel, nameLabel, new Vector2(cell.u, cell.v), entry.PlayerId == SelectedPlayerId, warn);
        }
    }
}
