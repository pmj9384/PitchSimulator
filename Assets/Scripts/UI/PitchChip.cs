using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 필드 그림 위의 선수 한 명. 자리(역할 약칭)와 역할 이름을 보여 주고 필드 안 비율 좌표에 선다. 누르면 그 선수를 고른다(09-30 개인 전술).
// 값만 받는다. 시즌도 전술도 모른다(PitchView가 채운다)
public class PitchChip : MonoBehaviour
{
    private static readonly Color NormalColor = new Color(0.11f, 0.2f, 0.47f, 0.95f);    // 칩 바탕(짙은 파랑)
    private static readonly Color NormalTextColor = Color.white;
    private static readonly Color SelectedTextColor = new Color(0.13f, 0.13f, 0.13f);    // 강조색 바탕 위에선 짙은 글자

    [SerializeField] private RectTransform rect;
    [SerializeField] private TMP_Text roleText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image background;
    [SerializeField] private Button button;

    public event Action<PitchChip> Clicked;

    public void Initialize()
    {
        button.onClick.AddListener(OnClicked);
    }

    // anchor = 필드 그림 안 0~1 좌표. 칩의 중심이 그 점에 온다. 고른 선수는 강조색으로 칠한다
    public void Show(string roleId, string displayName, Vector2 anchor, bool selected)
    {
        roleText.text = roleId;
        nameText.text = displayName;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = Vector2.zero;

        background.color = selected ? TacticsColors.Highlight : NormalColor;
        Color textColor = selected ? SelectedTextColor : NormalTextColor;
        roleText.color = textColor;
        nameText.color = textColor;
    }

    private void OnClicked()
    {
        Clicked?.Invoke(this);
    }
}
