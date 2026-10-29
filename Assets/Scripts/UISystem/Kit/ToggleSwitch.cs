using System;
using UnityEngine;
using UnityEngine.UI;

// 스위치(2026-10-29 표준안): 켜기/끄기 하나. 설정 행 안에서만 쓴다(Apple HIG Toggles). 라벨은 상태가 아니라 설정 이름("진동").
// 코드가 값을 채울 때(SetWithoutNotify)는 Changed를 쏘지 않는다
public class ToggleSwitch : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image track;
    [SerializeField] private RectTransform knob;
    [SerializeField] private Image knobImage;

    public bool IsOn { get; private set; }
    public event Action<bool> Changed;

    private void Awake()
    {
        button.onClick.AddListener(OnClicked);
        Show();
    }

    public void SetWithoutNotify(bool on)
    {
        IsOn = on;
        Show();
    }

    private void OnClicked()
    {
        IsOn = !IsOn;
        Show();
        Changed?.Invoke(IsOn);
    }

    // 손잡이는 트랙 양 끝에 앵커로 붙인다(켜짐 = 오른쪽)
    private void Show()
    {
        track.color = IsOn ? UIKitColors.Accent : UIKitColors.Track;
        knobImage.color = IsOn ? UIKitColors.OnAccent : UIKitColors.SubText;
        float x = IsOn ? 1f : 0f;
        knob.anchorMin = new Vector2(x, 0.5f);
        knob.anchorMax = new Vector2(x, 0.5f);
        knob.pivot = new Vector2(x, 0.5f);
        knob.anchoredPosition = new Vector2(IsOn ? -6f : 6f, 0f);
    }
}
