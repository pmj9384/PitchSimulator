using System.Collections.Generic;
using Game.Core.League;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 팀 전술 편집 부품(09-30, 스펙 §4-2·목업 "전술 지도와 설정창" 탭 2). 프리팹 하나를 로비 전술 화면(TacticsScreen)과 경기 직전 세팅 화면(TacticsPanelUIElement)이 같이 쓴다.
// 입구 3단: ①카드 4장(고르면 아래 전부가 그 카드 값) → ②슬라이더 3개(카드 값에 오프셋, TacticsEditing.ApplySlider) → ③세부 표(서드 3열 × 공격 4행·수비 1행 + 전환 3).
// 슬라이더는 자기 담당 칸만 기준 카드 + 오프셋으로 다시 쓴다(다른 칸에서 표로 바꾼 값은 지킨다). 표를 만지면 그 칸만 바뀐다.
// 값이 기준 카드와 다르면 카드 강조를 끄고 "사용자 설정"을 띄운다(목업 규칙). 규칙은 전부 순수 코어(TacticsEditing·SeasonState), 여기는 화면과 입력만.
// 고른 값의 진실은 시즌 한 곳(SeasonSystem.SetMyPreset·SetMyTactics·SetMyTacticsFromTable → SeasonState)이라 두 화면이 같은 값을 본다.
// 이 부품은 이번 경기(상대·킥오프)를 모른다. 그건 경기 직전 화면 몫
public class TacticsEditorView : MonoBehaviour
{
    private static readonly Color SelectedColor = new Color(1f, 0.835f, 0.31f);   // 결과 화면 내 행 강조와 같은 노랑(#FFD54F)
    private static readonly Color NormalColor = Color.white;

    [Header("① 카드")]
    [SerializeField] private Button[] cardButtons;
    [SerializeField] private Image[] cardFrames;
    [SerializeField] private TMP_Text[] cardTitles;
    [SerializeField] private TMP_Text[] cardDescriptions;
    [SerializeField] private TMP_Text customLabel;   // "사용자 설정 (기준: 카드 이름)". 값이 기준 카드와 다를 때만

    [Header("② 슬라이더 (카드 기준 −2~+2)")]
    [SerializeField] private Slider forwardSlider;
    [SerializeField] private Slider speedSlider;
    [SerializeField] private Slider pressSlider;

    [Header("③ 세부 표 (서드 3칸: 우리 진영·중원·상대 진영)")]
    [SerializeField] private TacticLevelSelector[] passStyle;
    [SerializeField] private TacticLevelSelector[] passRisk;
    [SerializeField] private TacticLevelSelector[] tempo;
    [SerializeField] private TacticLevelSelector[] width;
    [SerializeField] private TacticLevelSelector[] pressStart;
    [SerializeField] private TacticLevelSelector counter;
    [SerializeField] private TacticLevelSelector counterPress;
    [SerializeField] private TacticLevelSelector gkDistribution;

    private IReadOnlyList<TeamTactics> presets;
    private readonly List<TacticLevelSelector> allSelectors = new List<TacticLevelSelector>();
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    // 화면을 열 때마다 부른다. 시즌이 든 지금 값으로 카드·슬라이더·표를 다시 채운다
    public void Refresh()
    {
        EnsureInitialized();
        FillCards();
        ResetSliders();
        ShowTactics();
    }

    // 입력 연결은 한 번만. 두 화면이 각자 다른 시점(인게임 Initialize·로비 Open)에 열어서 첫 Refresh에 맡긴다
    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        presets = TeamTacticsRepository.All;
        if (!CardArraysMatch())
        {
            Debug.LogError($"[TacticsEditor] 카드 배열 길이가 다르다(버튼 {cardButtons.Length}·테두리 {cardFrames.Length}·이름 {cardTitles.Length}·설명 {cardDescriptions.Length}). 프리팹 배선을 확인한다");
        }
        if (cardButtons.Length != presets.Count)
        {
            Debug.LogError($"[TacticsEditor] 카드 {cardButtons.Length}칸 ≠ 프리셋 {presets.Count}종(TacticPresets.csv). 남는 칸은 숨기고 넘치는 프리셋은 안 보인다");
        }
        for (int i = 0; i < cardButtons.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 칸 번호를 잡게 한다
            cardButtons[i].onClick.AddListener(() => OnCardSelected(index));
        }

        forwardSlider.onValueChanged.AddListener(OnForwardChanged);
        speedSlider.onValueChanged.AddListener(OnSpeedChanged);
        pressSlider.onValueChanged.AddListener(OnPressChanged);

        CollectSelectors();
        for (int i = 0; i < allSelectors.Count; i++)
        {
            allSelectors[i].Initialize();
            allSelectors[i].Changed += OnTableChanged;
        }
    }

    #region 입력
    private void OnCardSelected(int index)
    {
        Season.SetMyPreset(presets[index].PresetId);
        ResetSliders();
        ShowTactics();
    }

    private void OnForwardChanged(float value)
    {
        ApplySlider(TacticSlider.Forward, value);
    }

    private void OnSpeedChanged(float value)
    {
        ApplySlider(TacticSlider.Speed, value);
    }

    private void OnPressChanged(float value)
    {
        ApplySlider(TacticSlider.Press, value);
    }

    private void ApplySlider(TacticSlider slider, float value)
    {
        TeamTactics card = SeasonRunner.FindPreset(presets, Season.State.MyPresetId);
        TeamTactics tactics = TacticsEditing.ApplySlider(Season.State.MyTactics(presets), card, slider, Mathf.RoundToInt(value));
        Season.SetMyTactics(tactics);
        ShowTactics();
    }

    private void OnTableChanged(TacticLevelSelector _)
    {
        Season.SetMyTacticsFromTable(ReadTable());
        // 표로 맞춘 값이 어떤 카드와 같아지면 그 카드를 고른 것이 된다(SeasonState). 슬라이더는 카드 기준 오프셋이라 가운데로 돌린다
        if (Season.State.MyCustomTactics == null) { ResetSliders(); }
        ShowTactics();
    }
    #endregion

    #region 화면 채우기
    private void FillCards()
    {
        for (int i = 0; i < cardButtons.Length; i++)
        {
            bool hasPreset = i < presets.Count;
            cardButtons[i].gameObject.SetActive(hasPreset);
            if (!hasPreset) { continue; }
            cardTitles[i].text = presets[i].DisplayName;
            cardDescriptions[i].text = presets[i].Description;
        }
    }

    // 슬라이더는 카드 기준 오프셋이라 저장하지 않는다. 화면을 열 때·카드를 고를 때 가운데(0)로
    private void ResetSliders()
    {
        forwardSlider.SetValueWithoutNotify(0f);
        speedSlider.SetValueWithoutNotify(0f);
        pressSlider.SetValueWithoutNotify(0f);
    }

    // 시즌이 든 지금 값(바꾼 값 또는 기준 카드)을 표와 카드 강조에 비춘다
    private void ShowTactics()
    {
        TeamTactics tactics = Season.State.MyTactics(presets);
        SetThirds(passStyle, tactics.PassStyle);
        SetThirds(passRisk, tactics.PassRisk);
        SetThirds(tempo, tactics.Tempo);
        SetThirds(width, tactics.Width);
        SetThirds(pressStart, tactics.PressStart);
        counter.SetLevel(tactics.Counter);
        counterPress.SetLevel(tactics.CounterPress);
        gkDistribution.SetLevel(tactics.GkDistribution);
        HighlightCard();
    }

    // 바꾼 값이 없을 때만 기준 카드를 강조한다. 바꾼 값이 있으면 "사용자 설정"과 기준 카드 이름(슬라이더가 어느 카드 기준인지)
    private void HighlightCard()
    {
        bool isCustom = Season.State.MyCustomTactics != null;
        string baseId = Season.State.MyPresetId;
        for (int i = 0; i < cardFrames.Length; i++)
        {
            bool isBase = i < presets.Count && presets[i].PresetId == baseId;
            cardFrames[i].color = isBase && !isCustom ? SelectedColor : NormalColor;
        }

        customLabel.gameObject.SetActive(isCustom);
        if (isCustom) { customLabel.text = $"사용자 설정 (기준: {SeasonRunner.FindPreset(presets, baseId).DisplayName})"; }
    }
    #endregion

    // 표의 값 + 표에 없는 전진 정도(슬라이더 ①이 맡는다, 스펙 §4-2)는 지금 값 그대로
    private TeamTactics ReadTable()
    {
        TeamTactics tactics = TacticsEditing.Copy(Season.State.MyTactics(presets));
        ReadThirds(passStyle, tactics.PassStyle);
        ReadThirds(passRisk, tactics.PassRisk);
        ReadThirds(tempo, tactics.Tempo);
        ReadThirds(width, tactics.Width);
        ReadThirds(pressStart, tactics.PressStart);
        tactics.Counter = counter.Level;
        tactics.CounterPress = counterPress.Level;
        tactics.GkDistribution = gkDistribution.Level;
        return tactics;
    }

    private void CollectSelectors()
    {
        allSelectors.Clear();
        allSelectors.AddRange(passStyle);
        allSelectors.AddRange(passRisk);
        allSelectors.AddRange(tempo);
        allSelectors.AddRange(width);
        allSelectors.AddRange(pressStart);
        allSelectors.Add(counter);
        allSelectors.Add(counterPress);
        allSelectors.Add(gkDistribution);
    }

    private static void SetThirds(TacticLevelSelector[] row, int[] values)
    {
        for (int i = 0; i < row.Length; i++) { row[i].SetLevel(values[i]); }
    }

    private static void ReadThirds(TacticLevelSelector[] row, int[] values)
    {
        for (int i = 0; i < row.Length; i++) { values[i] = row[i].Level; }
    }

    // 칸마다 버튼·테두리·이름·설명이 한 벌이다. 길이가 다르면 FillCards가 인덱스 밖을 읽는다(09-30 리뷰)
    private bool CardArraysMatch()
    {
        int n = cardButtons.Length;
        if (cardFrames.Length != n) { return false; }
        if (cardTitles.Length != n) { return false; }
        return cardDescriptions.Length == n;
    }
}
