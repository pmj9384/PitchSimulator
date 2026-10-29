using System;
using System.Collections.Generic;
using Game.Core.League;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 팀 전술 패널(09-30, 스펙 §4-2·§2-1 목업 4번 보드). 전술 화면(TacticsBoardView)의 [팀 전술] 모달(10-27 ③b: 자리 단위 설정과 층위가 달라 탭이 아니라 필드 위 띠의 버튼으로 연다).
// 왼쪽 좁은 열이 입구(카드 4 + "앞으로" 슬라이더), 가운데가 국면 탭 3개(공격할 때 / 전환 / 수비할 때, FM26 팀 지시 3판), 오른쪽 미니 피치는 보드가 잇는다(10-29 ③d-2. 그 전의 표 한 장 + 순환 버튼은 유저 Play "버튼이 너무 많다, 공수 UI가 합쳐졌다"로 접음. 스펙 §4-2).
// 칸은 3칸 슬라이더(TacticLevelSelector). 속도·압박 슬라이더는 칸 좌우 조절과 중복이라 뺐고, 전진 정도만 칸이 없어 슬라이더가 남는다(TacticsEditing.ApplyForward: 카드 값에 오프셋, 자기 칸만 다시 쓴다).
// 기준 카드와 다른 칸은 바탕색으로 표시한다(어느 입력이 바꿨는지는 세이브에 없어 "다른 칸 전부", 10-21 결정).
// 카드 아래 체력 부담 막대는 지금 값이 선수를 얼마나 지치게 하는지를 실측 표로 보여 준다(StaminaLoad).
// 값이 기준 카드와 다르면 "사용자 설정" 버튼이 뜨고, 누르면 카드 값으로 돌아간다. 규칙은 전부 순수 코어(TacticsEditing·SeasonState), 여기는 화면과 입력만.
// 고른 값은 시즌 한 곳(SeasonSystem.SetMyPreset·SetMyTactics·SetMyTacticsFromTable → SeasonState)이라 로비 전술 화면과 매치데이 허브가 같은 값을 본다.
// 이 패널은 이번 경기(상대·킥오프)도 필드도 모른다. 값이 바뀌면 Changed, 탭이 바뀌면 PhaseSelected만 쏜다
public class TeamTacticsPanel : MonoBehaviour
{
    [Header("① 카드 4장 (TacticPresets 순서)")]
    [SerializeField] private Button[] cardButtons;
    [SerializeField] private Image[] cardImages;     // 기준 카드만 강조색
    [SerializeField] private TMP_Text cardDescription;
    [SerializeField] private Button customResetButton;   // "사용자 설정". 값이 기준 카드와 다를 때만 보이고, 누르면 카드 값으로
    [SerializeField] private StaminaLoadGauge loadGauge;  // 체력 부담 막대. 카드 설명 아래

    [Header("② 앞으로 슬라이더 (카드 기준 −1~+1) + 국면 탭")]
    [SerializeField] private Slider forwardSlider;
    [SerializeField] private TabGroup phaseTabs;   // 공격할 때 / 전환 / 수비할 때(TeamTacticsPhase 순서). contents = 탭별 칸 묶음 3개

    [Header("③ 공격 탭 (서드 3칸: 우리 진영·중원·상대 진영)")]
    [SerializeField] private TMP_Text[] attackRowLabels;   // 행 이름 3개. 글자는 TacticLevelNames.AttackRows
    [SerializeField] private TacticLevelSelector[] passStyle;
    [SerializeField] private TacticLevelSelector[] passRisk;
    [SerializeField] private TacticLevelSelector[] tempo;
    [SerializeField] private TacticLevelSelector[] width;

    [Header("④ 전환 탭 · ⑤ 수비 탭")]
    [SerializeField] private TacticLevelSelector counter;
    [SerializeField] private TacticLevelSelector counterPress;
    [SerializeField] private TacticLevelSelector gkDistribution;
    [SerializeField] private TacticLevelSelector[] pressStart;
    [SerializeField] private TacticLevelSelector defenseLine;   // 수비 라인 높이(10-27 ③c, FM Out of Possession의 Defensive Line). 수비 탭에 압박 시작 옆

    // 전술 값이 바뀔 때마다. 보드가 미니 피치·저장 전술 요약을 다시 그리려고 듣는다
    public event Action Changed;
    // 국면 탭을 바꿨을 때. 미니 피치가 공격/수비 자리를 따라가려고 듣는다
    public event Action<TeamTacticsPhase> PhaseSelected;

    public TeamTacticsPhase Phase => (TeamTacticsPhase)phaseTabs.SelectedIndex;

    private IReadOnlyList<TeamTactics> presets;
    private readonly List<TacticLevelSelector> allSelectors = new List<TacticLevelSelector>();
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    // 화면을 열 때마다 부른다. 시즌이 든 지금 값으로 카드·슬라이더·칸을 다시 채운다
    public void Refresh()
    {
        EnsureInitialized();
        ResetSliders();
        ShowTactics();
    }

    // 입력 연결은 한 번만. 두 화면이 각자 다른 시점(인게임 첫 Update·로비 Open)에 열어서 첫 Refresh에 맡긴다
    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        presets = TeamTacticsRepository.All;
        if (cardButtons.Length != presets.Count || cardImages.Length != presets.Count)
        {
            Debug.LogError($"[TeamTacticsPanel] 카드 버튼 {cardButtons.Length}·이미지 {cardImages.Length}개 ≠ 프리셋 {presets.Count}개. 프리팹 배선을 확인한다");
        }
        int cardCount = Mathf.Min(cardButtons.Length, presets.Count);   // 수가 어긋나도 있는 만큼만 잇는다(위 로그가 알린다)
        for (int i = 0; i < cardCount; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 카드 번호를 잡게 한다
            cardButtons[i].onClick.AddListener(() => SelectCard(index));
            cardButtons[i].GetComponentInChildren<TMP_Text>().text = presets[i].DisplayName;   // 카드 이름의 출처는 TacticPresets.csv
        }
        customResetButton.onClick.AddListener(OnResetToCard);

        SetSliderRange(forwardSlider);   // 범위의 진실은 코어 상수(프리팹 값이 아니라)
        forwardSlider.onValueChanged.AddListener(OnForwardChanged);
        phaseTabs.Initialize();
        phaseTabs.Selected += OnPhaseSelected;

        CollectSelectors();
        for (int i = 0; i < allSelectors.Count; i++)
        {
            allSelectors[i].Initialize();
            allSelectors[i].Changed += OnTableChanged;
        }
        ApplyLevelNames();
    }

    // 3단 칸 글자와 행 이름은 코드 한 곳(TacticLevelNames)에서. 결과 화면(TacticsReadoutText)과 같은 배열을 쓴다
    private void ApplyLevelNames()
    {
        for (int i = 0; i < attackRowLabels.Length; i++) { attackRowLabels[i].text = TacticLevelNames.AttackRows[i]; }
        SetRowLabels(passStyle, TacticLevelNames.PassStyle);
        SetRowLabels(passRisk, TacticLevelNames.PassRisk);
        SetRowLabels(tempo, TacticLevelNames.Tempo);
        SetRowLabels(width, TacticLevelNames.Width);
        SetRowLabels(pressStart, TacticLevelNames.PressStart);
        counter.SetLabels(TacticLevelNames.Counter);
        counterPress.SetLabels(TacticLevelNames.CounterPress);
        gkDistribution.SetLabels(TacticLevelNames.GkDistribution);
        defenseLine.SetLabels(TacticLevelNames.DefenseLine);
    }

    private static void SetRowLabels(TacticLevelSelector[] row, string[] names)
    {
        for (int i = 0; i < row.Length; i++) { row[i].SetLabels(names); }
    }

    #region 입력
    private void OnResetToCard()
    {
        SelectCard(BaseCardIndex());
    }

    // 카드를 고르면 칸 전부가 그 카드 값으로 돌아간다(SeasonState.SetMyPreset이 바꾼 값을 지운다)
    private void SelectCard(int index)
    {
        Season.SetMyPreset(presets[index].PresetId);
        ResetSliders();
        ShowTactics();
        Changed?.Invoke();
    }

    private void OnPhaseSelected(int index)
    {
        PhaseSelected?.Invoke((TeamTacticsPhase)index);
    }

    // 카드 값이 이미 끝이면 더 밀어도 같은 값이라 손잡이를 실제로 바뀐 칸까지만 둔다(유저 "더·덜이 분배가 안 된다"). 돌려놓는 건 표시용이라 이벤트를 안 낸다
    private void OnForwardChanged(float value)
    {
        TeamTactics card = SeasonRunner.FindPreset(presets, Season.State.MyPresetId);
        int offset = Mathf.RoundToInt(value);
        int effective = TacticsEditing.EffectiveForwardOffset(card, offset);
        if (effective != offset) { forwardSlider.SetValueWithoutNotify(effective); }
        TeamTactics tactics = TacticsEditing.ApplyForward(Season.State.MyTactics(presets), card, effective);
        Season.SetMyTactics(tactics);
        ShowTactics();
        Changed?.Invoke();
    }

    private static void SetSliderRange(Slider slider)
    {
        slider.minValue = TacticsEditing.SliderMin;
        slider.maxValue = TacticsEditing.SliderMax;
        slider.wholeNumbers = true;
    }

    private void OnTableChanged(TacticLevelSelector _)
    {
        Season.SetMyTacticsFromTable(ReadTable());
        // 칸으로 맞춘 값이 어떤 카드와 같아지면 그 카드를 고른 것이 된다(SeasonState). 슬라이더는 카드 기준 오프셋이라 가운데로 돌린다
        if (Season.State.MyCustomTactics == null) { ResetSliders(); }
        ShowTactics();
        Changed?.Invoke();
    }
    #endregion

    #region 화면 채우기
    // 슬라이더는 카드 기준 오프셋이라 저장하지 않는다. 화면을 열 때·카드를 고를 때 가운데(0)로
    private void ResetSliders()
    {
        forwardSlider.SetValueWithoutNotify(0f);
    }

    // 시즌이 든 지금 값(바꾼 값 또는 기준 카드)을 카드와 칸에 비춘다
    private void ShowTactics()
    {
        TeamTactics tactics = Season.State.MyTactics(presets);
        int baseIndex = BaseCardIndex();
        SetThirds(passStyle, tactics.PassStyle);
        SetThirds(passRisk, tactics.PassRisk);
        SetThirds(tempo, tactics.Tempo);
        SetThirds(width, tactics.Width);
        SetThirds(pressStart, tactics.PressStart);
        counter.SetLevel(tactics.Counter);
        counterPress.SetLevel(tactics.CounterPress);
        gkDistribution.SetLevel(tactics.GkDistribution);
        defenseLine.SetLevel(tactics.DefenseLine);
        ShowCard(baseIndex);
        ShowDiff(tactics, presets[baseIndex]);
        ShowLoad(tactics);
    }

    // 기준 카드와 다른 칸에 바탕색. 전진 정도는 칸이 없어 "사용자 설정" 버튼만 그것을 알린다
    private void ShowDiff(TeamTactics tactics, TeamTactics card)
    {
        MarkThirds(passStyle, card.PassStyle, tactics.PassStyle);
        MarkThirds(passRisk, card.PassRisk, tactics.PassRisk);
        MarkThirds(tempo, card.Tempo, tactics.Tempo);
        MarkThirds(width, card.Width, tactics.Width);
        MarkThirds(pressStart, card.PressStart, tactics.PressStart);
        counter.SetDiffersFromCard(card.Counter != tactics.Counter);
        counterPress.SetDiffersFromCard(card.CounterPress != tactics.CounterPress);
        gkDistribution.SetDiffersFromCard(card.GkDistribution != tactics.GkDistribution);
        defenseLine.SetDiffersFromCard(card.DefenseLine != tactics.DefenseLine);
    }

    // 체력 부담: 지금 값(압박 시작·전진 정도)에 해당하는 실측 표 칸을 막대로. 압박을 올리면 그 자리에서 막대가 찬다
    private void ShowLoad(TeamTactics tactics)
    {
        int filled = StaminaLoad.FilledSegments(StaminaLoadRepository.All, tactics);
        loadGauge.Show(filled, StaminaLoad.LevelOf(filled));
    }

    // 카드 4장 중 기준 카드만 강조한다(슬라이더가 어느 카드 기준인지). 값이 그 카드와 다르면 "사용자 설정"을 같이 띄운다
    private void ShowCard(int baseIndex)
    {
        for (int i = 0; i < cardImages.Length; i++)
        {
            cardImages[i].color = i == baseIndex ? TacticsColors.Highlight : TacticsColors.Unselected;
        }
        cardDescription.text = presets[baseIndex].Description;
        customResetButton.gameObject.SetActive(Season.State.MyCustomTactics != null);
    }
    #endregion

    private int BaseCardIndex()
    {
        string baseId = Season.State.MyPresetId;
        for (int i = 0; i < presets.Count; i++)
        {
            if (presets[i].PresetId == baseId) { return i; }
        }
        throw new InvalidOperationException($"[TeamTacticsPanel] TacticPresets에 없는 기준 카드: {baseId}");
    }

    // 칸의 값 + 칸에 없는 전진 정도("앞으로" 슬라이더가 맡는다, 스펙 §4-2)는 지금 값 그대로
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
        tactics.DefenseLine = defenseLine.Level;
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
        allSelectors.Add(defenseLine);
    }

    private static void SetThirds(TacticLevelSelector[] row, int[] values)
    {
        for (int i = 0; i < row.Length; i++) { row[i].SetLevel(values[i]); }
    }

    private static void ReadThirds(TacticLevelSelector[] row, int[] values)
    {
        for (int i = 0; i < row.Length; i++) { values[i] = row[i].Level; }
    }

    private static void MarkThirds(TacticLevelSelector[] row, int[] card, int[] values)
    {
        for (int i = 0; i < row.Length; i++) { row[i].SetDiffersFromCard(card[i] != values[i]); }
    }
}
