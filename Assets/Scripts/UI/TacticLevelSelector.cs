using System;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 3단 값(0·1·2) 하나를 고르는 칸. 팀 전술 모달 국면 탭의 한 칸이다. 3칸 슬라이더를 좌우로 옮기고 아래 단 이름 3개 중 지금 단이 밝다(10-29 ③d-2).
// 전엔 버튼 하나를 누르면 다음 단으로 돌았는데(10-21), 유저 Play "버튼 눌러서 바꾸는 것보다 좌우로 조절하는 게 낫다": 순환은 되돌리려면 두 번 눌러야 하고 방향이 없었다.
// 값의 뜻(짧게·직접·롱볼 등)은 모른다: 글자는 패널이 TacticLevelNames에서 SetLabels로 넣는다.
// 코드가 값을 채울 때(SetLevel)는 Changed를 쏘지 않는다: 카드·슬라이더가 칸을 다시 채울 때마다 "사용자가 바꿨다"로 저장되는 순환을 막는다.
// 바탕색은 기준 카드와 다른 칸 표시(SetDiffersFromCard). 어느 입력이 바꿨는지는 세이브에 없어 "다른 칸 전부"가 기준이다(10-21 결정).
// 바탕 위에 흰 안쪽 판(4px 안)이 덮여 있어 바탕은 테두리로만 보인다: 칸 전체가 노랗면 노란 슬라이더 채움이 묻혔다(10-29 Play).
// 채움·손잡이 색은 단마다 초록 → 주황 → 빨강(10-29 유저 "빡셀수록 빨강, 아니면 초록"). 단 2가 대체로 더 공격적·더 많이 뛰는 쪽이다(모험·빠르게·넓게·적극·높게). 패스 방식·GK 배급(롱볼·길게)은 강도라기보다 성향이지만 같은 색 규칙을 쓴다
public class TacticLevelSelector : MonoBehaviour
{
    private static readonly Color DiffersColor = TacticsColors.Highlight;
    private static readonly Color NormalColor = Color.white;
    private static readonly Color CurrentLabelColor = TacticsColors.DarkText;          // 지금 단
    private static readonly Color OtherLabelColor = new Color(0.55f, 0.58f, 0.64f);     // 나머지 단
    private static readonly Color[] LevelColors =
    {
        new Color(0.40f, 0.73f, 0.42f),   // 단 0: 초록(#66BB6A)
        new Color(1f, 0.65f, 0.15f),      // 단 1: 주황(#FFA726). 테두리 강조색(노랑)과 안 겹치게
        new Color(0.94f, 0.33f, 0.31f),   // 단 2: 빨강(#EF5350)
    };

    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text[] levelLabels;   // 왼쪽부터 단 0·1·2
    [SerializeField] private Image background;   // 테두리로 보이는 바탕(위에 흰 안쪽 판)

    private Image fillImage;
    private Image handleImage;

    public int Level { get; private set; }
    public event Action<TacticLevelSelector> Changed;

    public void Initialize()
    {
        if (levelLabels.Length != TeamTactics.Levels)
        {
            Debug.LogError($"[TacticLevelSelector] {name}: 단 글자 {levelLabels.Length}개 ≠ 단 {TeamTactics.Levels}개. 프리팹 배선을 확인한다");
        }
        slider.minValue = 0f;   // 범위의 진실은 코어 상수(프리팹 값이 아니라)
        slider.maxValue = TeamTactics.Levels - 1;
        slider.wholeNumbers = true;
        fillImage = slider.fillRect.GetComponent<Image>();
        handleImage = slider.handleRect.GetComponent<Image>();
        slider.onValueChanged.AddListener(OnSliderChanged);
    }

    // 단마다 글자
    public void SetLabels(string[] names)
    {
        if (names.Length != levelLabels.Length)
        {
            Debug.LogError($"[TacticLevelSelector] {name}: 이름 {names.Length}개 ≠ 단 {levelLabels.Length}개");
            return;
        }
        for (int i = 0; i < levelLabels.Length; i++) { levelLabels[i].text = names[i]; }
        ShowLevel();
    }

    public void SetLevel(int level)
    {
        Level = level;
        slider.SetValueWithoutNotify(level);
        ShowLevel();
    }

    public void SetDiffersFromCard(bool differs)
    {
        background.color = differs ? DiffersColor : NormalColor;
    }

    private void OnSliderChanged(float value)
    {
        Level = Mathf.RoundToInt(value);
        ShowLevel();
        Changed?.Invoke(this);
    }

    private void ShowLevel()
    {
        for (int i = 0; i < levelLabels.Length; i++)
        {
            levelLabels[i].color = i == Level ? CurrentLabelColor : OtherLabelColor;
        }
        if (fillImage == null) { return; }   // Initialize 전(SetLabels가 먼저 불릴 때)
        fillImage.color = LevelColors[Level];
        handleImage.color = LevelColors[Level];
    }
}
