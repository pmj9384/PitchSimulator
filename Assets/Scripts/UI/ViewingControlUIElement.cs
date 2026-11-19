using System;
using Game.Core.Viewing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 경기 중 우측 상단 보기 패널(11-19 유저 Play 피드백 "경기 중에 우측 상단에서 바꾸고 싶다, 배속은 FM처럼 좌우 슬라이더"). HUD 톱니로 켜고 끈다(꺼진 채로 킥오프).
// 위: 보기 모드 세로 라디오 목록(FM26 Preferences > Match Day처럼 위에서부터 전체 경기·하이라이트·주요 장면, 줄 ↔ 모드는 ViewingOptions.ModeRows).
// 아래: 배속 슬라이더(칸 번호 = ViewingOptions.Speeds 순서).
// 화면만 맡는다. 바꾸는 일은 MatchManager.SetViewingMode·SetSpeed가 한다(UI가 시뮬·timeScale을 직접 만지지 않는다). 배치는 Editor/ViewingControlBuilder
public class ViewingControlUIElement : UIElement
{
    [SerializeField] private Toggle[] modeToggles;    // 줄 순서(ViewingOptions.ModeRows). 같은 ToggleGroup이라 하나만 켜진다
    [SerializeField] private TMP_Text[] modeLabels;   // modeToggles와 같은 순서
    [SerializeField] private Slider speedSlider;      // 0 ~ Speeds.Length-1 정수 칸
    [SerializeField] private TMP_Text speedText;      // "×2"

    public override void Initialize()
    {
        gameObject.SetActive(false);
        for (int row = 0; row < modeToggles.Length; row++)
        {
            modeLabels[row].text = LabelOf(ViewingOptions.ModeAtRow(row));
            modeToggles[row].onValueChanged.AddListener(OnModeToggled);
        }
        speedSlider.wholeNumbers = true;
        speedSlider.minValue = 0;
        speedSlider.maxValue = ViewingOptions.Speeds.Length - 1;
        speedSlider.onValueChanged.AddListener(OnSpeedChanged);
    }

    // 톱니로 켤 때마다 지금 값을 그린다. 코드가 채우는 값은 알림을 쏘지 않는다
    public override void Show()
    {
        gameObject.SetActive(true);
        MatchManager match = gameManager.Match;
        int selectedRow = ViewingOptions.RowOf(match.ViewingMode);
        for (int row = 0; row < modeToggles.Length; row++)
        {
            modeToggles[row].SetIsOnWithoutNotify(row == selectedRow);
        }
        int index = Array.IndexOf(ViewingOptions.Speeds, match.Speed);
        speedSlider.SetValueWithoutNotify(index);
        ShowSpeed(match.Speed);
    }

    public override void Hide()
    {
        gameObject.SetActive(false);
    }

    private static string LabelOf(ViewingMode mode)
    {
        switch (mode)
        {
            case ViewingMode.Full:
                return "전체 경기";
            case ViewingMode.Extended:
                return "하이라이트";
            default:
                return "주요 장면";
        }
    }

    // 줄마다 같은 콜백을 건다(줄 번호를 잡는 람다 대신). 켜진 줄 하나만 보고, 그룹이 끄는 다른 줄의 알림은 넘긴다
    private void OnModeToggled(bool isOn)
    {
        if (!isOn) { return; }
        for (int row = 0; row < modeToggles.Length; row++)
        {
            if (!modeToggles[row].isOn) { continue; }
            gameManager.Match.SetViewingMode(ViewingOptions.ModeAtRow(row));
            return;
        }
    }

    private void OnSpeedChanged(float value)
    {
        int speed = ViewingOptions.Speeds[Mathf.RoundToInt(value)];
        gameManager.Match.SetSpeed(speed);
        ShowSpeed(speed);
    }

    private void ShowSpeed(int speed)
    {
        speedText.text = $"×{speed}";
    }
}
