using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 개인 전술 패널(09-30, 스펙 §4-2·전술-기획 3-2). 전술 화면(TacticsBoardView)의 왼쪽, [개인 전술] 탭.
// 필드에서 고른 선수 한 명의 ①역할(같은 자리의 노출된 역할 중 하나)과 ②개인 지시(역할 값에서 다이얼마다 −2~+2칸)를 정한다.
// FM·EA FC·FC 온라인이 같은 구조다: 필드에서 선수를 누르면 옆 패널에 그 선수의 역할과 지시가 뜬다(09-30 유저 지시).
// 규칙(같은 자리·노출된 역할만, 지시 범위, 골키퍼는 출격 반경만)은 전부 순수 코어(SeasonState·PlayerInstructions), 여기는 화면과 입력만.
// 능력치(빌드)는 여기서 못 바꾼다. 역할을 바꾸면 그 역할의 지시 값으로 바뀌고 개인 지시는 지워진다(SeasonState.SetPlayerRole).
// 이 패널은 필드도 팀 전술도 모른다. 값이 바뀌면 Changed만 쏜다
public class PlayerTacticsPanel : MonoBehaviour
{
    private static readonly Color SelectedColor = TacticsColors.Highlight;
    private static readonly Color NormalColor = Color.white;

    // PlayerDial 순서
    private static readonly string[] DialNames = { "전진 폭", "측면 쏠림", "압박 거리", "라인 높이", "슛 성향", "패스 길이", "패스 리스크", "드리블 성향", "볼 끌기", "출격 반경" };
    // 오프셋 −2~+2 순서. 가운데가 역할 값 그대로
    private static readonly string[] StepWords = { "훨씬 덜", "덜", "역할 값", "더", "훨씬 더" };

    [Header("고른 선수")]
    [SerializeField] private TMP_Text roleTitle;         // 자리 · 역할 이름
    [SerializeField] private TMP_Text roleDescription;

    [Header("① 역할 (같은 자리의 노출된 역할)")]
    [SerializeField] private Button[] roleButtons;
    [SerializeField] private Image[] roleImages;
    [SerializeField] private TMP_Text[] roleLabels;

    [Header("② 개인 지시 (역할 값 기준 −2~+2)")]
    [SerializeField] private PlayerDialRow[] dialRows;   // PlayerDial 순서 10줄. 선수에게 해당하는 줄만 보인다
    [SerializeField] private Button resetButton;         // "역할 값으로 되돌리기". 지시가 하나라도 있을 때만 보인다

    // 역할이나 개인 지시가 바뀔 때마다. 필드(PitchView)가 자리와 역할 이름을 다시 그리려고 듣는다
    public event Action Changed;

    private readonly List<PlayerStats> roles = new List<PlayerStats>();
    private int playerId;   // 필드에서 고른 선수. Show가 채운다
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    // 필드에서 선수를 고르거나 화면을 열 때 부른다
    public void Show(int selectedPlayerId)
    {
        EnsureInitialized();
        playerId = selectedPlayerId;
        ShowPlayer();
    }

    // 입력 연결은 한 번만(TeamTacticsPanel과 같은 이유로 첫 Show에 맡긴다)
    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        if (dialRows.Length != PlayerInstructions.DialCount)
        {
            Debug.LogError($"[PlayerTacticsPanel] 지시 줄 {dialRows.Length}개 ≠ 다이얼 {PlayerInstructions.DialCount}개. 프리팹 배선을 확인한다");
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
        if (roles[index].VariantId == Player().Stats.VariantId) { return; }

        Season.SetPlayerRole(playerId, roles[index].VariantId);
        ShowPlayer();
        Changed?.Invoke();
    }

    private void OnDialStepped(PlayerDialRow row, int direction)
    {
        int dialIndex = Array.IndexOf(dialRows, row);
        int offset = Player().Instructions[dialIndex] + direction;
        if (offset < PlayerInstructions.OffsetMin || offset > PlayerInstructions.OffsetMax) { return; }

        Season.SetPlayerInstruction(playerId, (PlayerDial)dialIndex, offset);
        ShowPlayer();
        Changed?.Invoke();
    }

    private void OnResetClicked()
    {
        Season.ClearPlayerInstructions(playerId);
        ShowPlayer();
        Changed?.Invoke();
    }
    #endregion

    #region 화면 채우기
    private void ShowPlayer()
    {
        RosterPlayer player = Player();
        PlayerStats role = player.Stats;
        roleTitle.text = $"{role.RoleId} · {role.DisplayName}";
        roleDescription.text = role.Description;
        ShowRoles(role);
        ShowDials(player);
        resetButton.gameObject.SetActive(player.HasInstructions);
    }

    private void ShowRoles(PlayerStats current)
    {
        roles.Clear();
        roles.AddRange(PlayerTableLookup.ExposedVariants(PlayerTableRepository.All, current.RoleId));
        if (roles.Count > roleButtons.Length)
        {
            Debug.LogError($"[PlayerTacticsPanel] {current.RoleId}의 노출된 역할 {roles.Count}개 > 버튼 {roleButtons.Length}개. 뒤쪽 역할이 안 보인다");
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

    private void ShowDials(RosterPlayer player)
    {
        for (int i = 0; i < dialRows.Length; i++)
        {
            bool applies = PlayerInstructions.AppliesTo((PlayerDial)i, player.Stats);
            dialRows[i].gameObject.SetActive(applies);
            if (!applies) { continue; }

            int offset = player.Instructions[i];
            string step = StepWords[offset - PlayerInstructions.OffsetMin];
            dialRows[i].Show(DialNames[i], step, offset > PlayerInstructions.OffsetMin, offset < PlayerInstructions.OffsetMax);
        }
    }
    #endregion

    private RosterPlayer Player()
    {
        return Season.State.FindRosterPlayer(playerId);
    }
}
