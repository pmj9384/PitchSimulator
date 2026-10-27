using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using UnityEngine;

// 전술 화면의 필드(09-30, FM·EA FC 전술 화면이 화면 절반을 필드로 쓰는 것을 따름). 내 11명을 칩으로 세운다.
// 공격할 때 / 수비할 때 탭은 각각 공격 포메이션·수비 포메이션의 격자다(10-26 슬롯 격자 ②, FM26 In/Out of Possession 모양 2장).
// 칩은 미터 좌표가 아니라 격자(FormationGrid)에 선다: 10-08~25의 미터 그리기는 22m 폭 칩이 라인 간격 12~17m에 겹쳤다(10-25 유저 Play). 그래서 슬라이더·다이얼로 칩이 움직이는 표시도 없다(FM 격자와 같다).
// 칩을 누르면 그 선수를 고른다(09-30 개인 전술). 칩을 다른 칩 위에 놓으면 둘의 슬롯을 바꾼다(공격 탭 = 공격 슬롯, 수비 탭 = 수비 슬롯). 못 바꾸면(역할군·GK 슬롯) 잠깐 빨갛게.
// 10-08 자유 배치(끌어 놓은 자리를 미터로 저장)는 접었다. 코어의 MoveLineupSlot·PlacementRules는 ④에서 지운다
public class PitchView : MonoBehaviour
{
    private const int DefendPhase = 1;   // phaseTabs: 0 = 공격할 때, 1 = 수비할 때
    public const int NoPlayer = -1;

    [SerializeField] private PitchChip[] chips;   // 11개 고정(FormationTemplate.SlotCount). chips[i] = Lineup[i]
    [SerializeField] private TabGroup phaseTabs;

    // 칩을 눌러 선수를 고를 때(playerId). 전술 화면(TacticsBoardView)이 개인 전술 패널을 그 선수로 바꾼다
    public event Action<int> PlayerSelected;
    // 슬롯 교체 뒤. 포메이션 패널(전환 거리 머리글)이 듣는다
    public event Action Changed;

    public int SelectedPlayerId { get; private set; } = NoPlayer;

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
        for (int i = 0; i < chips.Length; i++)
        {
            chips[i].Initialize();
            chips[i].Clicked += OnChipClicked;
            chips[i].Dropped += OnChipDropped;
        }
    }

    // 놓은 자리에 다른 칩이 있으면 둘의 슬롯을 바꾼다. 빈 곳이면 제자리로. 성공이든 거부든 칩은 저장된 자리에 다시 선다
    private void OnChipDropped(PitchChip chip, Vector2 local)
    {
        PitchChip target = ChipUnder(local, chip);
        if (target != null) { TrySwap(chip, target); }
        PlaceChips();
    }

    private PitchChip ChipUnder(Vector2 local, PitchChip except)
    {
        for (int i = 0; i < chips.Length; i++)
        {
            if (chips[i] == except || !chips[i].gameObject.activeSelf) { continue; }
            if (chips[i].Covers(local)) { return chips[i]; }
        }
        return null;
    }

    private void TrySwap(PitchChip dragged, PitchChip target)
    {
        SeasonState state = State;
        int a = state.Lineup[Array.IndexOf(chips, dragged)].PlayerId;
        int b = state.Lineup[Array.IndexOf(chips, target)].PlayerId;
        bool defending = Defending();
        if (!state.CanSwapLineupSlots(a, b, defending, FormationTemplateRepository.All))
        {
            dragged.FlashRejected();
            target.FlashRejected();
            return;
        }
        GameDataManager.Instance.Season.SwapLineupSlots(a, b, defending);
        Changed?.Invoke();
    }

    // 고른 선수가 라인업에 없으면(화면을 처음 열 때, 시즌이 바뀌어 선수 id가 달라졌을 때) 첫 선수를 고른다
    private void KeepSelectionInLineup()
    {
        IReadOnlyList<LineupEntry> lineup = State.Lineup;
        for (int i = 0; i < lineup.Count; i++)
        {
            if (lineup[i].PlayerId == SelectedPlayerId) { return; }
        }
        SelectedPlayerId = lineup.Count > 0 ? lineup[0].PlayerId : NoPlayer;
    }

    // 코드가 고를 때(스쿼드 패널에서 교체로 들어온 선수). 알림은 안 쏜다: 부른 쪽이 이미 안다
    public void Select(int playerId)
    {
        SelectedPlayerId = playerId;
        KeepSelectionInLineup();   // 라인업 밖 id면 첫 선수로(Refresh와 같은 규칙)
        PlaceChips();
    }

    private void OnChipClicked(PitchChip chip)
    {
        int index = Array.IndexOf(chips, chip);
        SelectedPlayerId = State.Lineup[index].PlayerId;
        PlaceChips();
        PlayerSelected?.Invoke(SelectedPlayerId);
    }

    private void OnPhaseSelected(int _)
    {
        PlaceChips();
    }

    private bool Defending()
    {
        return phaseTabs.SelectedIndex == DefendPhase;
    }

    // 탭의 포메이션 격자를 깔고, 각 선수를 자기 슬롯 칸에 세운다
    private void PlaceChips()
    {
        SeasonState state = State;
        IReadOnlyList<LineupEntry> lineup = state.Lineup;
        bool defending = Defending();
        FormationTemplate template = FormationRules.Find(FormationTemplateRepository.All, defending ? state.DefenseFormationId : state.AttackFormationId);
        (float u, float v)[] grid = FormationGrid.Layout(template, defending);

        for (int i = 0; i < chips.Length; i++)
        {
            bool hasPlayer = i < lineup.Count;
            chips[i].gameObject.SetActive(hasPlayer);
            if (!hasPlayer) { continue; }

            LineupEntry entry = lineup[i];
            PlayerStats stats = state.FindRosterPlayer(entry.PlayerId).Stats;
            (float u, float v) cell = grid[(defending ? entry.DefenseSlot : entry.AttackSlot) - 1];
            string roleLabel = stats.RoleId;
            string nameLabel = stats.DisplayName;
            if (defending)
            {
                // 수비 탭은 선수의 공격 변형이 아니라 수비 모양의 자리와 수비 역할을 보인다(FM26 공격 역할 1 + 수비 역할 1, 10-27 유저 Play 지적)
                roleLabel = FormationRules.SlotOf(template, entry.DefenseSlot).RoleId;
                nameLabel = DefenseRoleRepository.Get(entry.DefenseRoleId).DisplayName;
            }
            chips[i].Show(roleLabel, nameLabel, new Vector2(cell.u, cell.v), entry.PlayerId == SelectedPlayerId);
        }
    }
}
