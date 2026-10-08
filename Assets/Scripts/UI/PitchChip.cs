using TMPro;
using UnityEngine;

// 필드 그림 위의 선수 한 명. 자리(역할 약칭)와 역할 이름을 보여 주고 필드 안 비율 좌표에 선다.
// 값만 받는다. 시즌도 전술도 모른다(PitchView가 채운다)
public class PitchChip : MonoBehaviour
{
    [SerializeField] private RectTransform rect;
    [SerializeField] private TMP_Text roleText;
    [SerializeField] private TMP_Text nameText;

    // anchor = 필드 그림 안 0~1 좌표. 칩의 중심이 그 점에 온다
    public void Show(string roleId, string displayName, Vector2 anchor)
    {
        roleText.text = roleId;
        nameText.text = displayName;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = Vector2.zero;
    }
}
