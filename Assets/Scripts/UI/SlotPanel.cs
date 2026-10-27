using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 자리 패널(10-27 ③b, 스펙 §4-2·§8. 09-30 개인 전술 패널을 슬롯 중심으로 다시 짬). 전술 화면 왼쪽, 필드에서 칩을 골랐을 때 보인다.
// 위: 슬롯 번호·자리 + 선 선수(자연 포지션·자연 변형·★) + [교체] + 경고 한 줄(적성·역할군·전환 거리).
// 아래는 셋 중 하나: 공격 탭이면 ①공격 역할 변형(그 자리의 노출된 변형) + ②개인 지시 다이얼(자리별 해당 칸만), 수비 탭이면 수비 역할 5종, [교체]를 누르면 같은 군 후보 목록(벤치는 같은 자리, 선발은 역할군이 맞는 슬롯).
// 역할·지시는 자리(슬롯)의 것이라 선수를 바꿔도 그대로다(FM26·EA FC). 규칙은 전부 코어(SeasonState·SlotTactics·PlayerInstructions·FormationRules), 여기는 화면과 입력만.
// 이 패널은 필드도 다른 패널도 모른다. 설정이 바뀌면 Changed, 선수가 바뀌면 LineupChanged(고를 선수 id)만 쏜다
public class SlotPanel : MonoBehaviour
{
    private static readonly Color SelectedColor = TacticsColors.Highlight;
    private static readonly Color NormalColor = Color.white;

    // PlayerDial 순서
    private static readonly string[] DialNames = { "전진 폭", "측면 쏠림", "압박 거리", "슛 성향", "패스 길이", "패스 리스크", "드리블 성향", "볼 끌기", "출격 반경" };
    // 오프셋 −2~+2 순서. 가운데가 역할 기본값 그대로
    private static readonly string[] StepWords = { "훨씬 덜", "덜", "역할 기본값", "더", "훨씬 더" };

    private enum Mode { Attack, Defense, Candidates }

    [Header("머리: 자리와 선 선수")]
    [SerializeField] private TMP_Text titleText;        // "슬롯 10 · ST"
    [SerializeField] private TMP_Text playerText;       // "ST 포처 ★"
    [SerializeField] private Button swapButton;         // [교체] → 후보 목록
    [SerializeField] private TMP_Text warningText;      // 경고 한 줄. 없으면 숨김

    [Header("공격 탭: ① 역할 변형 (그 자리의 노출된 변형)")]
    [SerializeField] private GameObject attackRoot;
    [SerializeField] private Button[] roleButtons;
    [SerializeField] private Image[] roleImages;
    [SerializeField] private TMP_Text[] roleLabels;
    [SerializeField] private TMP_Text roleDescription;

    [Header("공격 탭: ② 개인 지시 (역할 기본값 기준 −2~+2, PlayerDial 순서)")]
    [SerializeField] private PlayerDialRow[] dialRows;
    [SerializeField] private Button resetButton;        // "역할 기본값으로 되돌리기". 지시가 하나라도 있을 때만 보인다

    [Header("수비 탭: 수비 역할 (DefenseRoles.csv 순서)")]
    [SerializeField] private GameObject defenseRoot;
    [SerializeField] private Button[] defenseButtons;
    [SerializeField] private Image[] defenseImages;
    [SerializeField] private TMP_Text[] defenseLabels;
    [SerializeField] private TMP_Text defenseDescription;

    [Header("교체 후보 (같은 군)")]
    [SerializeField] private GameObject candidateRoot;
    [SerializeField] private Button[] candidateButtons;
    [SerializeField] private TMP_Text[] candidateLabels;
    [SerializeField] private TMP_Text candidateHint;    // "후보 없음"·"n명 더 있음"
    [SerializeField] private Button cancelButton;

    // 슬롯 설정(변형·지시·수비 역할)이 바뀔 때마다. 필드가 칩 글자·어긋남을 다시 그리려고 듣는다
    public event Action Changed;
    // 교체로 선수 배치가 바뀐 뒤. 인자 = 이제 고를 선수 id(필드·명단이 같이 고른다)
    public event Action<int> LineupChanged;

    private readonly List<PlayerStats> roles = new List<PlayerStats>();
    private readonly List<int> candidateIds = new List<int>();
    private readonly List<bool> candidateIsBench = new List<bool>();
    private int playerId = PitchView.NoPlayer;   // 필드에서 고른 선수. 그 선수가 선 슬롯을 다룬다
    private bool defending;
    private Mode mode = Mode.Attack;
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;
    private static IReadOnlyList<FormationTemplate> Formations => FormationTemplateRepository.All;

    // 필드에서 선수를 고르거나, 공격/수비 탭을 바꾸거나, 세팅이 바뀌었을 때 부른다
    public void Show(int selectedPlayerId, bool defendingPhase)
    {
        EnsureInitialized();
        playerId = selectedPlayerId;
        defending = defendingPhase;
        mode = defending ? Mode.Defense : Mode.Attack;
        Render();
    }

    // 입력 연결은 한 번만(TeamTacticsPanel과 같은 이유로 첫 Show에 맡긴다)
    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        if (dialRows.Length != PlayerInstructions.DialCount)
        {
            Debug.LogError($"[SlotPanel] 지시 줄 {dialRows.Length}개 ≠ 다이얼 {PlayerInstructions.DialCount}개. 프리팹 배선을 확인한다");
        }
        int stepCount = PlayerInstructions.OffsetMax - PlayerInstructions.OffsetMin + 1;
        if (DialNames.Length != PlayerInstructions.DialCount || StepWords.Length != stepCount)
        {
            Debug.LogError($"[SlotPanel] 다이얼 이름 {DialNames.Length}개(다이얼 {PlayerInstructions.DialCount}개)·단계 글자 {StepWords.Length}개(단계 {stepCount}개)가 코어와 안 맞는다");
        }
        if (defenseButtons.Length != DefenseRoleRepository.All.Count)
        {
            Debug.LogError($"[SlotPanel] 수비 역할 버튼 {defenseButtons.Length}개 ≠ DefenseRoles {DefenseRoleRepository.All.Count}종. 프리팹 배선을 확인한다");
        }

        swapButton.onClick.AddListener(OnSwapClicked);
        cancelButton.onClick.AddListener(OnCancelClicked);
        resetButton.onClick.AddListener(OnResetClicked);
        for (int i = 0; i < roleButtons.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 버튼 번호를 잡게 한다
            roleButtons[i].onClick.AddListener(() => OnRoleClicked(index));
        }
        for (int i = 0; i < defenseButtons.Length; i++)
        {
            int index = i;
            defenseButtons[i].onClick.AddListener(() => OnDefenseRoleClicked(index));
        }
        for (int i = 0; i < candidateButtons.Length; i++)
        {
            int index = i;
            candidateButtons[i].onClick.AddListener(() => OnCandidateClicked(index));
        }
        for (int i = 0; i < dialRows.Length; i++)
        {
            dialRows[i].Initialize();
            dialRows[i].Stepped += OnDialStepped;
        }
    }

    #region 입력
    private void OnSwapClicked()
    {
        mode = Mode.Candidates;
        Render();
    }

    private void OnCancelClicked()
    {
        mode = defending ? Mode.Defense : Mode.Attack;
        Render();
    }

    // 벤치 후보면 선발 교체(같은 자리), 선발 후보면 슬롯 맞바꾸기(같은 군). 교체 뒤엔 그 자리에 새로 선 선수를 고른다
    private void OnCandidateClicked(int index)
    {
        if (index >= candidateIds.Count) { return; }
        int other = candidateIds[index];
        int nextSelected;
        if (candidateIsBench[index])
        {
            Season.SwapStarter(playerId, other);
            nextSelected = other;
        }
        else
        {
            Season.SwapLineupSlots(playerId, other, defending);
            nextSelected = other;
        }
        playerId = nextSelected;
        mode = defending ? Mode.Defense : Mode.Attack;
        Render();
        LineupChanged?.Invoke(nextSelected);
    }

    private void OnRoleClicked(int index)
    {
        if (index >= roles.Count) { return; }
        if (roles[index].VariantId == CurrentVariant().VariantId) { return; }

        Season.SetSlotVariant(Entry().AttackSlot, roles[index].VariantId);
        Render();
        Changed?.Invoke();
    }

    private void OnDefenseRoleClicked(int index)
    {
        IReadOnlyList<DefenseRole> all = DefenseRoleRepository.All;
        if (index >= all.Count) { return; }
        int defenseSlot = Entry().DefenseSlot;
        if (all[index].Id == Season.State.Slots.DefenseRoleOf(defenseSlot)) { return; }

        Season.SetDefenseRole(defenseSlot, all[index].Id);
        Render();
        Changed?.Invoke();
    }

    private void OnDialStepped(PlayerDialRow row, int direction)
    {
        int dialIndex = Array.IndexOf(dialRows, row);
        int attackSlot = Entry().AttackSlot;
        int offset = Season.State.Slots.InstructionsOf(attackSlot)[dialIndex] + direction;
        if (offset < PlayerInstructions.OffsetMin || offset > PlayerInstructions.OffsetMax) { return; }

        Season.SetSlotInstruction(attackSlot, (PlayerDial)dialIndex, offset);
        Render();
        Changed?.Invoke();
    }

    private void OnResetClicked()
    {
        Season.ClearSlotInstructions(Entry().AttackSlot);
        Render();
        Changed?.Invoke();
    }
    #endregion

    #region 화면 채우기
    private void Render()
    {
        SeasonState state = Season.State;
        LineupEntry entry = Entry();
        RosterPlayer player = state.FindRosterPlayer(playerId);
        FormationSlot attackSlot = FormationRules.SlotOf(AttackTemplate(state), entry.AttackSlot);
        FormationSlot shownSlot = defending ? FormationRules.SlotOf(DefenseTemplate(state), entry.DefenseSlot) : attackSlot;
        PlayerStats variant = PitchView.SlotVariant(state, entry.AttackSlot);

        titleText.text = $"슬롯 {shownSlot.Slot} · {shownSlot.RoleId}{(defending ? " (수비)" : " (공격)")}";
        playerText.text = $"{player.Stats.RoleId} {player.Stats.DisplayName} {new string('★', player.Stars)}";
        ShowWarning(state, entry, player.Stats, attackSlot, variant);

        attackRoot.SetActive(mode == Mode.Attack);
        defenseRoot.SetActive(mode == Mode.Defense);
        candidateRoot.SetActive(mode == Mode.Candidates);
        swapButton.gameObject.SetActive(mode != Mode.Candidates);
        switch (mode)
        {
            case Mode.Attack:
                ShowRoles(attackSlot, variant);
                ShowDials(attackSlot, entry.AttackSlot);
                break;
            case Mode.Defense:
                ShowDefenseRoles(state.Slots.DefenseRoleOf(entry.DefenseSlot));
                break;
            default:
                ShowCandidates(state, player.Stats, entry);
                break;
        }
    }

    // 셋 다 막지 않는다(FM26 "둘 나란히" 경고처럼 표시만). 칩의 "!"(PitchView.IsMismatched)와 같은 판정
    private void ShowWarning(SeasonState state, LineupEntry entry, PlayerStats player, FormationSlot attackSlot, PlayerStats variant)
    {
        var reasons = new List<string>();
        if (!LineupComposer.IsNatural(player, variant)) { reasons.Add($"적성: 자연 변형은 {player.DisplayName}(다이얼 절반만)"); }
        if (!RoleGroups.SameGroup(player.RoleId, attackSlot.RoleId)) { reasons.Add($"역할군: {player.RoleId} 선수가 {attackSlot.RoleId} 자리"); }
        float distance = state.TransitionDistanceOf(entry.PlayerId);
        if (FormationRules.IsFarTransition(distance)) { reasons.Add($"공수 자리 {distance:0}m(전환에 늦음)"); }
        warningText.gameObject.SetActive(reasons.Count > 0);
        warningText.text = string.Join(" · ", reasons);
    }

    private void ShowRoles(FormationSlot slot, PlayerStats current)
    {
        roles.Clear();
        roles.AddRange(PlayerTableLookup.ExposedVariants(PlayerTableRepository.All, slot.RoleId));
        if (!current.Exposed) { roles.Insert(0, current); }   // 업데이트로 잠긴 역할을 쓰고 있으면 그 역할은 보이고 돌아올 수 있다. 새로 고를 순 없다
        if (roles.Count > roleButtons.Length)
        {
            Debug.LogError($"[SlotPanel] {slot.RoleId}의 노출된 역할 {roles.Count}개 > 버튼 {roleButtons.Length}개. 뒤쪽 역할이 안 보인다");
        }
        roleDescription.text = current.Description;
        for (int i = 0; i < roleButtons.Length; i++)
        {
            bool used = i < roles.Count;
            roleButtons[i].gameObject.SetActive(used);
            if (!used) { continue; }
            roleLabels[i].text = roles[i].DisplayName;
            roleImages[i].color = roles[i].VariantId == current.VariantId ? SelectedColor : NormalColor;
        }
    }

    private void ShowDials(FormationSlot slot, int attackSlot)
    {
        IReadOnlyList<int> offsets = Season.State.Slots.InstructionsOf(attackSlot);
        resetButton.gameObject.SetActive(Season.State.Slots.HasInstructions(attackSlot));
        for (int i = 0; i < dialRows.Length; i++)
        {
            bool applies = PlayerInstructions.AppliesTo((PlayerDial)i, slot.RoleId);
            dialRows[i].gameObject.SetActive(applies);
            if (!applies) { continue; }
            int offset = offsets[i];
            string step = StepWords[offset - PlayerInstructions.OffsetMin];
            dialRows[i].Show(DialNames[i], step, offset > PlayerInstructions.OffsetMin, offset < PlayerInstructions.OffsetMax);
        }
    }

    private void ShowDefenseRoles(string currentId)
    {
        IReadOnlyList<DefenseRole> all = DefenseRoleRepository.All;
        for (int i = 0; i < defenseButtons.Length; i++)
        {
            bool used = i < all.Count;
            defenseButtons[i].gameObject.SetActive(used);
            if (!used) { continue; }
            defenseLabels[i].text = all[i].DisplayName;
            bool selected = all[i].Id == currentId;
            defenseImages[i].color = selected ? SelectedColor : NormalColor;
            if (selected) { defenseDescription.text = all[i].Description; }
        }
    }

    // 벤치 = 선 선수와 같은 자리(SeasonState.SwapStarter 규칙), 선발 = 이 탭에서 슬롯을 맞바꿀 수 있는 선수(CanSwapLineupSlots)
    private void ShowCandidates(SeasonState state, PlayerStats starter, LineupEntry entry)
    {
        candidateIds.Clear();
        candidateIsBench.Clear();
        var labels = new List<string>();
        List<RosterPlayer> bench = state.Bench();
        for (int i = 0; i < bench.Count; i++)
        {
            if (!PlayerTableLookup.IsSamePosition(bench[i].Stats, starter.RoleId)) { continue; }
            candidateIds.Add(bench[i].PlayerId);
            candidateIsBench.Add(true);
            labels.Add($"{bench[i].Stats.RoleId} {bench[i].Stats.DisplayName} {new string('★', bench[i].Stars)} (벤치)");
        }
        IReadOnlyList<LineupEntry> lineup = state.Lineup;
        for (int i = 0; i < lineup.Count; i++)
        {
            int other = lineup[i].PlayerId;
            if (other == playerId) { continue; }
            if (!state.CanSwapLineupSlots(playerId, other, defending, Formations)) { continue; }
            PlayerStats stats = state.FindRosterPlayer(other).Stats;
            int slot = defending ? lineup[i].DefenseSlot : lineup[i].AttackSlot;
            candidateIds.Add(other);
            candidateIsBench.Add(false);
            labels.Add($"{stats.RoleId} {stats.DisplayName} (슬롯 {slot})");
        }

        for (int i = 0; i < candidateButtons.Length; i++)
        {
            bool used = i < labels.Count;
            candidateButtons[i].gameObject.SetActive(used);
            if (used) { candidateLabels[i].text = labels[i]; }
        }
        int hidden = labels.Count - candidateButtons.Length;
        if (labels.Count == 0) { candidateHint.text = "같은 군 후보가 없다"; }
        else if (hidden > 0) { candidateHint.text = $"{hidden}명 더 있음(명단에서 방출해 줄인다)"; }
        else { candidateHint.text = defending ? "수비 슬롯을 맞바꾼다(벤치는 같은 자리만)" : "공격 슬롯을 맞바꾼다(같은 역할군)"; }
    }
    #endregion

    private LineupEntry Entry()
    {
        IReadOnlyList<LineupEntry> lineup = Season.State.Lineup;
        for (int i = 0; i < lineup.Count; i++)
        {
            if (lineup[i].PlayerId == playerId) { return lineup[i]; }
        }
        throw new InvalidOperationException($"[SlotPanel] 라인업에 없는 선수 id {playerId}");
    }

    private static FormationTemplate AttackTemplate(SeasonState state)
    {
        return FormationRules.Find(Formations, state.AttackFormationId);
    }

    private static FormationTemplate DefenseTemplate(SeasonState state)
    {
        return FormationRules.Find(Formations, state.DefenseFormationId);
    }

    private PlayerStats CurrentVariant()
    {
        return PitchView.SlotVariant(Season.State, Entry().AttackSlot);
    }
}
