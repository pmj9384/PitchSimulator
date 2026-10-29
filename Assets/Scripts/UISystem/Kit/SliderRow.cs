using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 슬라이더 행(2026-10-29 표준안): 라벨 + 음소거 버튼 + 슬라이더(0~1) + 값(0~100). 볼륨처럼 연속값에 쓴다(Apple HIG Sliders).
// 음소거 버튼은 0과 마지막 값을 오간다. 코드가 값을 채울 때(SetValueWithoutNotify)는 ValueChanged를 쏘지 않는다.
// 마지막 값은 RememberMin 이상일 때만 기억한다: SoundManager가 0을 0.0001로 저장해, 음소거 뒤 다시 열면 그 값이 기억돼 해제가 무음이었다(리뷰)
public class SliderRow : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private Button muteButton;
    [SerializeField] private TMP_Text valueText;

    public event Action<float> ValueChanged;

    private const float RememberMin = 0.01f;

    private float lastNonZero = 1f;

    private void Awake()
    {
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(OnSliderChanged);
        if (muteButton != null) { muteButton.onClick.AddListener(OnMuteClicked); }
    }

    public void SetValueWithoutNotify(float value)
    {
        slider.SetValueWithoutNotify(value);
        Remember(value);
        ShowValue(value);
    }

    private void OnSliderChanged(float value)
    {
        Remember(value);
        ShowValue(value);
        ValueChanged?.Invoke(value);
    }

    private void OnMuteClicked()
    {
        float next = slider.value >= RememberMin ? 0f : lastNonZero;   // 0.0001(SoundManager가 0 대신 저장하는 값)도 꺼진 것으로 본다. > 0이면 한 번은 헛클릭이었다(재검증)
        slider.value = next;   // onValueChanged로 OnSliderChanged가 돈다
    }

    private void Remember(float value)
    {
        if (value >= RememberMin) { lastNonZero = value; }
    }

    private void ShowValue(float value)
    {
        if (valueText == null) { return; }
        valueText.text = Mathf.RoundToInt(value * 100f).ToString();
    }
}
