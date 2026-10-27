using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 자리 설정 패널(09-30 개인 전술 패널을 10-27 슬롯 중심으로 고침. 스펙 §4-2·§8). 전술 화면(TacticsBoardView)의 왼쪽.
// 필드에서 고른 칩이 선 **공격 슬롯**의 ①역할 변형(그 자리의 노출된 변형 중 하나)과 ②개인 지시(역할 기본값에서 다이얼마다 −2~+2칸)를 정한다.
// 역할·지시는 자리의 것이라 칩(선수)을 바꿔도 그대로다(FM26·EA FC). 선수가 자연 변형과 다른 변형 자리에 서면 적성 어긋남을 제목에 적는다.
// 규칙(같은 자리·노출된 변형만, 지시 범위, 자리별 해당 다이얼)은 전부 순수 코어(SeasonState·SlotTactics·PlayerInstructions), 여기는 화면과 입력만.
// ③b(10-27 전술판 배치)에서 자리 패널로 다시 짠다. 그때까지 프리팹의 지시 줄 10개 중 마지막 하나는 안 쓴다(라인 높이 다이얼 제거)
public class PlayerTacticsPanel : MonoBehaviour
{
    private static readonly Color SelectedColor = TacticsColors.Highlight;
    private static readonly Color NormalColor = Color.white;

    // PlayerDial 순서
    private static readonly string[] DialNames = { "전진 폭", "측면 쏠림", "압박 거리", "슛 성향", "패스 길이", "패스 리스크", "드리블 성향", "볼 끌기", "출격 반경" };
    // 오프셋 −2~+2 순서. 가운데가 역할 기본값 그대로
    private static readonly string[] StepWords = { "훨씬 덜", "덜", "역할 기본값", "더", "훨씬 더" };

    [Header("고른 자리")]
    [SerializeField] private TMP_Text roleTitle;         // 슬롯 · 자리 · 역할 이름
    [SerializeField] private TMP_Text roleDescription;

    [Header("① 역할 (그 자리의 노출된 변형)")]
    [SerializeField] private Button[] roleButtons;
    [SerializeField] private Image[] roleImages;
    [SerializeField] private TMP_Text[] roleLabels;

    [Header("② 개인 지시 (역할 기본값 기준 −2~+2)")]
    [SerializeField] private PlayerDialRow[] dialRows;   // PlayerDial 순서. 다이얼 수보다 많으면 뒤쪽 줄은 끈다
    [SerializeField] private Button resetButton;         // "역할 기본값으로 되돌리기". 지시가 하나라도 있을 때만 보인다

    // 역할이나 개인 지시가 바뀔 때마다. 필드(PitchView)가 자리와 역할 이름을 다시 그리려고 듣는다
    public event Action Changed;

    private readonly List<PlayerStats> roles = new List<PlayerStats>();
    private int playerId;   // 필드에서 고른 선수. 그 선수가 선 공격 슬롯을 다룬다
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    // 필드에서 선수를 고르거나 화면을 열 때 부른다
    public void Show(int selectedPlayerId)
    {
        EnsureInitialized();
        playerId = selectedPlayerId;
        ShowSlot();
    }

    // 입력 연결은 한 번만(TeamTacticsPanel과 같은 이유로 첫 Show에 맡긴다)
    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        if (dialRows.Length < PlayerInstructions.DialCount)
        {
            Debug.LogError($"[PlayerTacticsPanel] 지시 줄 {dialRows.Length}개 < 다이얼 {PlayerInstructions.DialCount}개. 프리팹 배선을 확인한다");
        }
        // 글자 배열은 PlayerDial 순서와 오프셋 범위에 묶여 있다. 다이얼이나 단계가 늘면 여기서 바로 드러나게
        int stepCount = PlayerInstructions.OffsetMax - PlayerInstructions.OffsetMin + 1;
        if (DialNames.Length != PlayerInstructions.DialCount || StepWords.Length != stepCount)
        {
            Debug.LogError($"[PlayerTacticsPanel] 다이얼 이름 {DialNames.Length}개(다이얼 {PlayerInstructions.DialCount}개)·단계 글자 {StepWords.Length}개(단계 {stepCount}개)가 코어와 안 맞는다");
        }

        for (int i = 0; i < roleButtons.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 버튼 번호를 잡게 한다
            roleButtons[i].onClick.AddListener(() => OnRoleClicked(index));
        }
        for (int i = 0; i < dialRows.Length; i++)
        {
            dialRows[i].Initialize();
            dialRows[i].Stepped += OnDialStepped;
        }
        resetButton.onClick.AddListener(OnResetClicked);
    }

    #region 입력
    private void OnRoleClicked(int index)
    {
        if (index >= roles.Count) { return; }
        if (roles[index].VariantId == CurrentVariant().VariantId) { return; }

        Season.SetSlotVariant(AttackSlot(), roles[index].VariantId);
        ShowSlot();
        Changed?.Invoke();
    }

    private void OnDialStepped(PlayerDialRow row, int direction)
    {
        int dialIndex = Array.IndexOf(dialRows, row);
        if (dialIndex >= PlayerInstructions.DialCount) { return; }
        int offset = Season.State.Slots.InstructionsOf(AttackSlot())[dialIndex] + direction;
        if (offset < PlayerInstructions.OffsetMin || offset > PlayerInstructions.OffsetMax) { return; }

        Season.SetSlotInstruction(AttackSlot(), (PlayerDial)dialIndex, offset);
        ShowSlot();
        Changed?.Invoke();
    }

    private void OnResetClicked()
    {
        Season.ClearSlotInstructions(AttackSlot());
        ShowSlot();
        Changed?.Invoke();
    }
    #endregion

    #region 화면 채우기
    private void ShowSlot()
    {
        int slot = AttackSlot();
        FormationSlot position = SlotOf(slot);
        PlayerStats variant = CurrentVariant();
        RosterPlayer player = Season.State.FindRosterPlayer(playerId);
        string aptitude = LineupComposer.IsNatural(player.Stats, variant) ? string.Empty : " (적성 어긋남)";
        roleTitle.text = $"슬롯 {slot} · {position.RoleId} · {variant.DisplayName}{aptitude}";
        roleDescription.text = variant.Description;
        ShowRoles(position, variant);
        ShowDials(position, slot);
        resetButton.gameObject.SetActive(Season.State.Slots.HasInstructions(slot));
    }

    private void ShowRoles(FormationSlot position, PlayerStats current)
    {
        roles.Clear();
        roles.AddRange(PlayerTableLookup.ExposedVariants(PlayerTableRepository.All, position.RoleId));
        if (!current.Exposed) { roles.Insert(0, current); }   // 업데이트로 잠긴 역할을 쓰고 있으면 그 역할은 보이고 돌아올 수 있다. 새로 고를 순 없다
        if (roles.Count > roleButtons.Length)
        {
            Debug.LogError($"[PlayerTacticsPanel] {position.RoleId}의 노출된 역할 {roles.Count}개 > 버튼 {roleButtons.Length}개. 뒤쪽 역할이 안 보인다");
        }

        for (int i = 0; i < roleButtons.Length; i++)
        {
            bool used = i < roles.Count;
            roleButtons[i].gameObject.SetActive(used);
            if (!used) { continue; }

            roleLabels[i].text = roles[i].DisplayName;
            roleImages[i].color = roles[i].VariantId == current.VariantId ? SelectedColor : NormalColor;
        }
    }

    private void ShowDials(FormationSlot position, int slot)
    {
        IReadOnlyList<int> offsets = Season.State.Slots.InstructionsOf(slot);
        for (int i = 0; i < dialRows.Length; i++)
        {
            bool applies = i < PlayerInstructions.DialCount && PlayerInstructions.AppliesTo((PlayerDial)i, position.RoleId);
            dialRows[i].gameObject.SetActive(applies);
            if (!applies) { continue; }

            int offset = offsets[i];
            string step = StepWords[offset - PlayerInstructions.OffsetMin];
            dialRows[i].Show(DialNames[i], step, offset > PlayerInstructions.OffsetMin, offset < PlayerInstructions.OffsetMax);
        }
    }
    #endregion

    private int AttackSlot()
    {
        IReadOnlyList<LineupEntry> lineup = Season.State.Lineup;
        for (int i = 0; i < lineup.Count; i++)
        {
            if (lineup[i].PlayerId == playerId) { return lineup[i].AttackSlot; }
        }
        throw new InvalidOperationException($"[PlayerTacticsPanel] 라인업에 없는 선수 id {playerId}");
    }

    private static FormationSlot SlotOf(int attackSlot)
    {
        FormationTemplate template = FormationRules.Find(FormationTemplateRepository.All, Season.State.AttackFormationId);
        return FormationRules.SlotOf(template, attackSlot);
    }

    private PlayerStats CurrentVariant()
    {
        string variantId = Season.State.Slots.AttackVariantOf(AttackSlot());
        PlayerStats variant = PlayerTableLookup.FindVariant(PlayerTableRepository.All, variantId);
        if (variant == null) { throw new InvalidOperationException($"[PlayerTacticsPanel] PlayerTable에 없는 변형 {variantId}"); }
        return variant;
    }
}
