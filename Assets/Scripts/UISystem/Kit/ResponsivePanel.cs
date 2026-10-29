using UnityEngine;

// 팝업 패널이 화면 방향에 맞는 크기를 갖게 한다(2026-10-29 표준안). 템플릿은 가로·세로 자동 회전이라 앵커 하나로는 한쪽이 납작해진다.
// 가로: 폭 60%·높이 80% / 세로: 폭 90%·높이 70%(조사 문서 부품 표).
// 다시 계산하는 조건은 "부모 크기가 직전과 다를 때"뿐이다. 앵커·오프셋 대입 자체가 OnRectTransformDimensionsChange를 다시 부르고
// Unity는 값이 같아도 알림을 보내서, 조건 없이 다시 대입하면 무한 재귀로 스택이 넘쳐 에디터가 꺼졌다(10-29 PitchSimulator Play 크래시)
[RequireComponent(typeof(RectTransform))]
public class ResponsivePanel : MonoBehaviour
{
    [SerializeField] private Vector2 landscapeSize = new Vector2(0.6f, 0.8f);
    [SerializeField] private Vector2 portraitSize = new Vector2(0.9f, 0.7f);

    private RectTransform rect;
    private Vector2 appliedParentSize = new Vector2(-1f, -1f);   // 처음엔 어떤 부모 크기와도 다르게
    private bool applying;

    private void Awake()
    {
        rect = (RectTransform)transform;
    }

    private void OnEnable()
    {
        Apply();
    }

    private void OnRectTransformDimensionsChange()
    {
        Apply();
    }

    private void Apply()
    {
        if (applying) { return; }   // 아래 대입이 부른 알림
        if (rect == null) { return; }
        var parent = rect.parent as RectTransform;
        if (parent == null) { return; }

        Vector2 parentSize = parent.rect.size;
        if (parentSize == appliedParentSize) { return; }   // 부모가 그대로면 할 일이 없다
        appliedParentSize = parentSize;

        bool landscape = parentSize.x >= parentSize.y;
        Vector2 size = landscape ? landscapeSize : portraitSize;
        applying = true;
        rect.anchorMin = new Vector2(0.5f - size.x / 2f, 0.5f - size.y / 2f);
        rect.anchorMax = new Vector2(0.5f + size.x / 2f, 0.5f + size.y / 2f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        applying = false;
    }
}
