using System.Collections.Generic;
using Game.Core.League;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 킥오프 전 팀 전술 설정창(09-30, 스펙 §4-2·목업 "전술 지도와 설정창" 탭 2). GameReady(선수 세팅 국면)에 뜨고 킥오프 버튼이 GamePlay로 넘긴다.
// 입구 3단: ①카드 4장(고르면 아래 전부가 그 카드 값) → ②슬라이더 3개(카드 값에 오프셋, TacticsEditing.ApplySlider) → ③세부 표(서드 3열 × 공격 4행·수비 1행 + 전환 3).
// 슬라이더는 자기 담당 칸만 기준 카드 + 오프셋으로 다시 쓴다(다른 칸에서 표로 바꾼 값은 지킨다). 표를 만지면 그 칸만 바뀐다.
// 값이 기준 카드와 다르면 카드 강조를 끄고 "사용자 설정"을 띄운다(목업 규칙). 규칙은 전부 순수 코어(TacticsEditing·SeasonState), 여기는 화면과 입력만.
// 고른 값의 진실은 시즌 한 곳(SeasonSystem.SetMyPreset·SetMyTactics·SetMyTacticsFromTable → SeasonState)이고, 경기는 킥오프 순간에 읽는다(MatchManager.StartMatch).
// 상대 이름·홈/원정·상대 전술을 보여 준다: 상성이 있어도 상대를 모르면 고르는 게 찍기가 된다.
// 내용은 Show가 아니라 첫 Update에서 채운다: GameUIManager가 MatchManager보다 먼저 등록돼 GameReady 훅도 먼저 돈다.
// Show에서 이번 경기를 읽으면 시즌이 끝난 직후엔 ResetMatch가 다음 시즌을 열기 전이라 CurrentMatch가 던진다(결과 화면과 같은 Dirty Flag 꼴)
public class TacticsPanelUIElement : UIElement
{
    private static readonly Color SelectedColor = new Color(1f, 0.835f, 0.31f);   // 결과 화면 내 행 강조와 같은 노랑(#FFD54F)
    private static readonly Color NormalColor = Color.white;

    [Header("① 카드")]
    [SerializeField] private Button[] cardButtons;
    [SerializeField] private Image[] cardFrames;
    [SerializeField] private TMP_Text[] cardTitles;
    [SerializeField] private TMP_Text[] cardDescriptions;
    [SerializeField] private TMP_Text customLabel;   // "사용자 설정". 어느 카드와도 값이 다를 때만

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

    [Header("경기")]
    [SerializeField] private TMP_Text matchupText;
    [SerializeField] private Button kickoffButton;

    private IReadOnlyList<TeamTactics> presets;
    private readonly List<TacticLevelSelector> allSelectors = new List<TacticLevelSelector>();
    private bool needsRefresh;

    public override void Initialize()
    {
        gameObject.SetActive(false);
        presets = TeamTacticsRepository.All;
        if (!CardArraysMatch())
        {
            Debug.LogError($"[TacticsPanel] 카드 배열 길이가 다르다(버튼 {cardButtons.Length}·테두리 {cardFrames.Length}·이름 {cardTitles.Length}·설명 {cardDescriptions.Length}). 씬 배선을 확인한다");
        }
        if (cardButtons.Length != presets.Count)
        {
            Debug.LogError($"[TacticsPanel] 카드 {cardButtons.Length}칸 ≠ 프리셋 {presets.Count}종(TacticPresets.csv). 남는 칸은 숨기고 넘치는 프리셋은 안 보인다");
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
        kickoffButton.onClick.AddListener(OnKickoff);
    }

    public override void Show()
    {
        gameObject.SetActive(true);
        needsRefresh = true;
    }

    public override void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!needsRefresh) { return; }
        needsRefresh = false;
        FillCards();
        FillMatchup();
        ResetSliders();
        ShowTactics();
    }

    private static SeasonSystem Season => GameDataManager.Instance.Season;

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

    private void OnKickoff()
    {
        gameManager.SetGameState(GameManager.GameState.GamePlay);
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

    private void FillMatchup()
    {
        MatchSetup setup = Season.CurrentMatch;
        string venue = setup.MyTeamIsHome ? "홈" : "원정";
        matchupText.text = $"상대 {setup.OpponentName} ({venue}) · 상대 전술: {setup.Tactics1.DisplayName}";
    }

    // 슬라이더는 카드 기준 오프셋이라 저장하지 않는다. 설정창을 열 때·카드를 고를 때 가운데(0)로
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
