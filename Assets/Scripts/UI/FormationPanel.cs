using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 포메이션 패널(10-26 슬롯 격자 ②, FM26 In/Out of Possession 모양 2장). 전술 화면(TacticsBoardView)의 모달 2개([포메이션]·[저장 전술])의 내용을 한 컴포넌트가 든다(10-27 ③b).
// ①공격할 때 모양 6개 중 하나 ②수비할 때 모양 6개 중 하나(★ = 공격 모양에서 평균 이동이 짧은 추천 3, FM26 어시스턴트 추천에 해당)
// ③저장 전술 5칸(TacticLibrary.SlotName): [불러오기]는 그 칸의 공수 모양·슬롯별 수비 역할·팀 전술을 입히고, [저장]은 지금 세팅을 그 칸에 뜬다. 찬 칸에 저장은 두 번 눌러 확정(스쿼드 방출과 같은 꼴).
// 규칙(없는 id 거부, 추천 순위, 칸 상한)은 전부 코어(SeasonState·FormationRules·TacticLibrary), 여기는 화면과 입력만. 값이 바뀌면 Changed만 쏜다
public class FormationPanel : MonoBehaviour
{
    private const int NoSlot = -1;
    private const int RecommendCount = 3;   // [가정] FM26이 IP 모양을 고르면 OOP 추천을 3개 보여 준다
    private static readonly Color SelectedColor = TacticsColors.Highlight;
    private static readonly Color NormalColor = Color.white;

    [SerializeField] private TMP_Text headerText;   // "공격 4-3-3 → 수비 5-3-2 · 평균 전환 8.2m"

    [Header("① 공격할 때 모양 (FormationTemplates 순서)")]
    [SerializeField] private Button[] attackButtons;
    [SerializeField] private Image[] attackImages;
    [SerializeField] private TMP_Text[] attackLabels;

    [Header("② 수비할 때 모양 (★ 추천 3)")]
    [SerializeField] private Button[] defenseButtons;
    [SerializeField] private Image[] defenseImages;
    [SerializeField] private TMP_Text[] defenseLabels;

    [Header("③ 저장 전술 (TacticLibrary.MaxCount칸)")]
    [SerializeField] private TMP_Text[] slotLabels;     // "전술 1 · 4-3-3/5-3-2 · 압박" 또는 "전술 1 · 비어 있음"
    [SerializeField] private Button[] loadButtons;      // 빈 칸은 끈다
    [SerializeField] private Button[] saveButtons;
    [SerializeField] private TMP_Text[] saveLabels;     // "저장" → 찬 칸은 한 번 누르면 "덮어쓰기?"

    // 포메이션이나 저장 전술을 입힌 뒤. 필드(격자)·팀 전술 패널·자리 패널이 다시 그리려고 듣는다
    public event Action Changed;

    private int pendingOverwrite = NoSlot;   // 두 번 눌러 확정할 저장 칸
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;
    private static IReadOnlyList<FormationTemplate> Formations => FormationTemplateRepository.All;

    // 화면을 열 때와 필드에서 슬롯을 바꿨을 때 부른다
    public void Refresh()
    {
        EnsureInitialized();
        pendingOverwrite = NoSlot;
        Render();
    }

    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        int formationCount = Formations.Count;
        if (!SameLength(formationCount, attackButtons, attackImages, attackLabels) || !SameLength(formationCount, defenseButtons, defenseImages, defenseLabels))
        {
            Debug.LogError($"[FormationPanel] 모양 버튼 {attackButtons.Length}·{defenseButtons.Length}개 ≠ FormationTemplates {formationCount}종. 프리팹 배선을 확인한다");
        }
        if (!SameLength(TacticLibrary.MaxCount, slotLabels, loadButtons, saveButtons, saveLabels))
        {
            Debug.LogError($"[FormationPanel] 저장 칸 {slotLabels.Length}개 ≠ TacticLibrary.MaxCount {TacticLibrary.MaxCount}. 프리팹 배선을 확인한다");
        }

        for (int i = 0; i < attackButtons.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 버튼 번호를 잡게 한다
            attackButtons[i].onClick.AddListener(() => OnAttackClicked(index));
        }
        for (int i = 0; i < defenseButtons.Length; i++)
        {
            int index = i;
            defenseButtons[i].onClick.AddListener(() => OnDefenseClicked(index));
        }
        for (int i = 0; i < loadButtons.Length; i++)
        {
            int index = i;
            loadButtons[i].onClick.AddListener(() => OnLoadClicked(index));
            saveButtons[i].onClick.AddListener(() => OnSaveClicked(index));
        }
    }

    // 배선 배열들이 전부 같은 길이인가(버튼·바탕·글자가 한 벌)
    private static bool SameLength(int expected, params Array[] arrays)
    {
        for (int i = 0; i < arrays.Length; i++)
        {
            if (arrays[i].Length != expected) { return false; }
        }
        return true;
    }

    #region 입력
    private void OnAttackClicked(int index)
    {
        if (index >= Formations.Count) { return; }
        string attackId = Formations[index].Id;
        if (attackId == Season.State.AttackFormationId) { return; }

        Season.SetFormations(attackId, Season.State.DefenseFormationId);
        AfterChange();
    }

    private void OnDefenseClicked(int index)
    {
        if (index >= Formations.Count) { return; }
        string defenseId = Formations[index].Id;
        if (defenseId == Season.State.DefenseFormationId) { return; }

        Season.SetFormations(Season.State.AttackFormationId, defenseId);
        AfterChange();
    }

    private void OnLoadClicked(int index)
    {
        if (Season.Library.Find(TacticLibrary.SlotName(index)) == null) { return; }
        Season.LoadTacticSlot(index);
        AfterChange();
    }

    // 빈 칸은 바로 저장. 찬 칸은 두 번째 누름에 덮어쓴다(사이에 다른 칸을 누르면 그 칸의 첫 누름이 된다)
    private void OnSaveClicked(int index)
    {
        bool occupied = Season.Library.Find(TacticLibrary.SlotName(index)) != null;
        if (occupied && pendingOverwrite != index)
        {
            pendingOverwrite = index;
            Render();
            return;
        }
        Season.SaveTacticSlot(index);
        pendingOverwrite = NoSlot;
        Render();   // 저장은 세팅을 안 바꾸니 Changed 없이 이 패널만 다시 그린다
    }

    private void AfterChange()
    {
        pendingOverwrite = NoSlot;
        Render();
        Changed?.Invoke();
    }
    #endregion

    #region 화면 채우기
    private void Render()
    {
        SeasonState state = Season.State;
        FormationTemplate attack = FormationRules.Find(Formations, state.AttackFormationId);
        FormationTemplate defense = FormationRules.Find(Formations, state.DefenseFormationId);
        headerText.text = $"공격 {attack.Id} → 수비 {defense.Id} · 평균 전환 {FormationRules.MeanTransitionDistance(attack, defense):0.0}m";
        RenderFormationRow(attackImages, attackLabels, state.AttackFormationId, null);
        RenderFormationRow(defenseImages, defenseLabels, state.DefenseFormationId, Recommended(attack));
        RenderSlots();
    }

    // recommended = ★를 붙일 id 집합. 공격 줄은 null
    private void RenderFormationRow(Image[] images, TMP_Text[] labels, string selectedId, HashSet<string> recommended)
    {
        for (int i = 0; i < images.Length; i++)
        {
            bool used = i < Formations.Count;
            images[i].gameObject.SetActive(used);
            if (!used) { continue; }

            string id = Formations[i].Id;
            bool star = recommended != null && recommended.Contains(id);
            labels[i].text = star ? $"★ {id}" : id;
            images[i].color = id == selectedId ? SelectedColor : NormalColor;
        }
    }

    private static HashSet<string> Recommended(FormationTemplate attack)
    {
        List<FormationTemplate> ranked = FormationRules.RankDefenseFormations(attack, Formations);
        var set = new HashSet<string>();
        for (int i = 0; i < ranked.Count && i < RecommendCount; i++) { set.Add(ranked[i].Id); }
        return set;
    }

    private void RenderSlots()
    {
        for (int i = 0; i < slotLabels.Length; i++)
        {
            string name = TacticLibrary.SlotName(i);
            SavedTactic tactic = Season.Library.Find(name);
            slotLabels[i].text = tactic == null ? $"{name} · 비어 있음" : $"{name} · {Summary(tactic)}";
            loadButtons[i].interactable = tactic != null;
            saveLabels[i].text = pendingOverwrite == i ? "덮어쓰기?" : "저장";
        }
    }

    // "4-3-3/5-3-2 · 압박"
    private static string Summary(SavedTactic tactic)
    {
        return $"{tactic.AttackFormationId}/{tactic.DefenseFormationId} · {tactic.CardName(TeamTacticsRepository.All)}";
    }
    #endregion
}
