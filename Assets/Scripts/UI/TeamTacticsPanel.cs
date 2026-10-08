using System;
using System.Collections.Generic;
using Game.Core.League;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 팀 전술 패널(09-30, 스펙 §4-2·목업 "전술 지도와 설정창" 탭 2). 전술 화면(TacticsBoardView)의 왼쪽.
// 배치는 FM·EA FC·FC 온라인 전술 화면을 따랐다(09-30 유저 지시): 카드는 ◀ ▶로 넘기는 한 줄, 세부는 한 번에 한 묶음만 보인다(버튼을 한꺼번에 깔지 않는다).
// 입구 3단: ①카드(고르면 아래 전부가 그 카드 값) → ②슬라이더 3개(카드 값에 오프셋, TacticsEditing.ApplySlider) → ③세부(공격할 때·수비할 때·공수가 바뀔 때).
// 슬라이더는 자기 담당 칸만 기준 카드 + 오프셋으로 다시 쓴다(다른 칸에서 세부로 바꾼 값은 지킨다). 세부를 만지면 그 칸만 바뀐다.
// 값이 기준 카드와 다르면 "사용자 설정" 버튼이 뜨고, 누르면 카드 값으로 돌아간다. 규칙은 전부 순수 코어(TacticsEditing·SeasonState), 여기는 화면과 입력만.
// 고른 값의 진실은 시즌 한 곳(SeasonSystem.SetMyPreset·SetMyTactics·SetMyTacticsFromTable → SeasonState)이라 로비와 경기 직전 화면이 같은 값을 본다.
// 이 패널은 이번 경기(상대·킥오프)도 필드도 모른다. 값이 바뀌면 Changed만 쏜다
public class TeamTacticsPanel : MonoBehaviour
{
    [Header("① 카드 (◀ ▶로 넘긴다)")]
    [SerializeField] private Button prevCardButton;
    [SerializeField] private Button nextCardButton;
    [SerializeField] private TMP_Text cardTitle;
    [SerializeField] private TMP_Text cardDescription;
    [SerializeField] private Button customResetButton;   // "사용자 설정". 값이 기준 카드와 다를 때만 보이고, 누르면 카드 값으로

    [Header("② 슬라이더 (카드 기준 −2~+2)")]
    [SerializeField] private Slider forwardSlider;
    [SerializeField] private Slider speedSlider;
    [SerializeField] private Slider pressSlider;

    [Header("③ 세부 (서드 3칸: 우리 진영·중원·상대 진영)")]
    [SerializeField] private TabGroup sections;   // 공격할 때·수비할 때·공수가 바뀔 때
    [SerializeField] private TacticLevelSelector[] passStyle;
    [SerializeField] private TacticLevelSelector[] passRisk;
    [SerializeField] private TacticLevelSelector[] tempo;
    [SerializeField] private TacticLevelSelector[] width;
    [SerializeField] private TacticLevelSelector[] pressStart;
    [SerializeField] private TacticLevelSelector counter;
    [SerializeField] private TacticLevelSelector counterPress;
    [SerializeField] private TacticLevelSelector gkDistribution;

    // 전술 값이 바뀔 때마다. 필드(PitchView)가 자리를 다시 그리려고 듣는다
    public event Action Changed;

    private IReadOnlyList<TeamTactics> presets;
    private readonly List<TacticLevelSelector> allSelectors = new List<TacticLevelSelector>();
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    // 화면을 열 때마다 부른다. 시즌이 든 지금 값으로 카드·슬라이더·세부를 다시 채운다
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
        prevCardButton.onClick.AddListener(OnPrevCard);
        nextCardButton.onClick.AddListener(OnNextCard);
        customResetButton.onClick.AddListener(OnResetToCard);

        forwardSlider.onValueChanged.AddListener(OnForwardChanged);
        speedSlider.onValueChanged.AddListener(OnSpeedChanged);
        pressSlider.onValueChanged.AddListener(OnPressChanged);

        sections.Initialize();
        CollectSelectors();
        for (int i = 0; i < allSelectors.Count; i++)
        {
            allSelectors[i].Initialize();
            allSelectors[i].Changed += OnTableChanged;
        }
    }

    #region 입력
    private void OnPrevCard()
    {
        SelectCard(BaseCardIndex() - 1);
    }

    private void OnNextCard()
    {
        SelectCard(BaseCardIndex() + 1);
    }

    private void OnResetToCard()
    {
        SelectCard(BaseCardIndex());
    }

    // 카드를 고르면 아래 전부가 그 카드 값으로 돌아간다(SeasonState.SetMyPreset이 바꾼 값을 지운다). 끝에서 넘기면 반대쪽 끝으로
    private void SelectCard(int index)
    {
        int wrapped = (index + presets.Count) % presets.Count;
        Season.SetMyPreset(presets[wrapped].PresetId);
        ResetSliders();
        ShowTactics();
        Changed?.Invoke();
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
        Changed?.Invoke();
    }

    private void OnTableChanged(TacticLevelSelector _)
    {
        Season.SetMyTacticsFromTable(ReadTable());
        // 세부로 맞춘 값이 어떤 카드와 같아지면 그 카드를 고른 것이 된다(SeasonState). 슬라이더는 카드 기준 오프셋이라 가운데로 돌린다
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
        speedSlider.SetValueWithoutNotify(0f);
        pressSlider.SetValueWithoutNotify(0f);
    }

    // 시즌이 든 지금 값(바꾼 값 또는 기준 카드)을 카드 줄과 세부에 비춘다
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
        ShowCard();
    }

    // 카드 줄은 늘 기준 카드를 보여 준다(슬라이더가 어느 카드 기준인지). 값이 그 카드와 다르면 "사용자 설정"을 같이 띄운다
    private void ShowCard()
    {
        TeamTactics card = presets[BaseCardIndex()];
        cardTitle.text = card.DisplayName;
        cardDescription.text = card.Description;
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

    // 세부의 값 + 세부에 없는 전진 정도(슬라이더 ①이 맡는다, 스펙 §4-2)는 지금 값 그대로
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
}
