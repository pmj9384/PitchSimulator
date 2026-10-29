using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 분절 버튼(2026-10-29 표준안): 2~4칸 중 하나를 고른다(Apple HIG Segmented controls, 5칸 넘으면 목록). 프레임 상한·화질 등.
// 칸 글자는 코드가 SetLabels로 넣는다(값의 SSOT는 각 설정 클래스). 코드가 고를 때(SetSelectedWithoutNotify)는 Selected를 쏘지 않는다
public class SegmentedControl : MonoBehaviour
{
    [SerializeField] private Button[] buttons;
    [SerializeField] private Image[] backgrounds;   // buttons와 같은 순서
    [SerializeField] private TMP_Text[] labels;     // buttons와 같은 순서

    public int SelectedIndex { get; private set; }
    public event Action<int> Selected;

    private void Awake()
    {
        if (backgrounds.Length != buttons.Length || labels.Length != buttons.Length)
        {
            Debug.LogError($"[SegmentedControl] {name}: 버튼 {buttons.Length}·바탕 {backgrounds.Length}·글자 {labels.Length}개가 안 맞는다");
        }
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 칸 번호를 잡게 한다
            buttons[i].onClick.AddListener(() => OnClicked(index));
        }
        Show();
    }

    // 칸 수보다 이름이 적으면 남는 칸을 숨긴다(2칸만 쓰는 설정)
    public void SetLabels(string[] names)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            bool used = i < names.Length;
            buttons[i].gameObject.SetActive(used);
            if (used) { labels[i].text = names[i]; }
        }
    }

    public void SetSelectedWithoutNotify(int index)
    {
        SelectedIndex = index;
        Show();
    }

    private void OnClicked(int index)
    {
        if (index == SelectedIndex) { return; }
        SelectedIndex = index;
        Show();
        Selected?.Invoke(index);
    }

    private void Show()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            bool on = i == SelectedIndex;
            backgrounds[i].color = on ? UIKitColors.Accent : Color.clear;
            labels[i].color = on ? UIKitColors.OnAccent : UIKitColors.SubText;
        }
    }
}
