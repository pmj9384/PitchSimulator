using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 필드 그림 위의 선수 한 명. 자리(역할 약칭)와 역할 이름을 보여 주고 필드 안 비율 좌표에 선다. 누르면 그 선수를 고른다(09-30 개인 전술).
// 끌면 따라 움직이고 놓는 자리를 알린다(10-08 배치, 스펙 §8). 놓아도 되는지는 모른다: PitchView가 판정하고 안 되면 Show로 되돌린다.
// 값만 받는다. 시즌도 전술도 모른다(PitchView가 채운다)
public class PitchChip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private static readonly Color RejectedColor = new Color(0.8f, 0.2f, 0.2f, 0.95f);   // 놓을 수 없는 자리에 놓았을 때 잠깐
    private const float RejectFlashSeconds = 0.3f;
    private static readonly Color NormalColor = new Color(0.11f, 0.2f, 0.47f, 0.95f);    // 칩 바탕(짙은 파랑)
    private static readonly Color NormalTextColor = Color.white;
    private static readonly Color SelectedTextColor = new Color(0.13f, 0.13f, 0.13f);    // 강조색 바탕 위에선 짙은 글자

    [SerializeField] private RectTransform rect;
    [SerializeField] private TMP_Text roleText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image background;
    [SerializeField] private Button button;

    public event Action<PitchChip> Clicked;
    public event Action<PitchChip, Vector2> Dropped;   // 끌어서 놓은 자리(부모 필드 RectTransform의 로컬 좌표). 판정은 받는 쪽

    private float rejectUntil;
    private Color restingColor;

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

        restingColor = selected ? TacticsColors.Highlight : NormalColor;
        background.color = restingColor;
        Color textColor = selected ? SelectedTextColor : NormalTextColor;
        roleText.color = textColor;
        nameText.color = textColor;
    }

    // 놓을 수 없는 자리였다: 자리는 PitchView가 Show로 되돌리고, 여기선 잠깐 빨갛게만
    public void FlashRejected()
    {
        background.color = RejectedColor;
        rejectUntil = Time.unscaledTime + RejectFlashSeconds;
    }

    private void Update()
    {
        if (rejectUntil <= 0f) { return; }
        if (Time.unscaledTime < rejectUntil) { return; }
        rejectUntil = 0f;
        background.color = restingColor;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        rect.SetAsLastSibling();   // 끄는 동안 다른 칩 위로
    }

    // 끄는 동안은 포인터를 따라간다. 앵커는 Show가 준 그대로 두고 anchoredPosition만 움직여서, 놓을 수 없으면 Show 한 번으로 돌아간다
    public void OnDrag(PointerEventData eventData)
    {
        var field = rect.parent as RectTransform;
        if (field == null) { return; }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(field, eventData.position, eventData.pressEventCamera, out Vector2 local)) { return; }
        rect.anchoredPosition = local - AnchorLocal(field);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        var field = rect.parent as RectTransform;
        if (field == null) { return; }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(field, eventData.position, eventData.pressEventCamera, out Vector2 local)) { return; }
        Dropped?.Invoke(this, local);
    }

    // 앵커(0~1)가 가리키는 점의 필드 로컬 좌표. anchoredPosition은 이 점 기준 오프셋이다
    private Vector2 AnchorLocal(RectTransform field)
    {
        Rect r = field.rect;
        return new Vector2(r.xMin + rect.anchorMin.x * r.width, r.yMin + rect.anchorMin.y * r.height);
    }

    private void OnClicked()
    {
        Clicked?.Invoke(this);
    }
}
