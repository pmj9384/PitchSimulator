using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum AnchorPresets
{
    TopLeft,
    TopCenter,
    TopRight,

    MiddleLeft,
    MiddleCenter,
    MiddleRight,

    BottomLeft,
    BottonCenter,
    BottomRight,
    BottomStretch,

    VertStretchLeft,
    VertStretchRight,
    VertStretchCenter,

    HorStretchTop,
    HorStretchMiddle,
    HorStretchBottom,

    StretchAll,
    None
}

[RequireComponent(typeof(RectTransform))]
public class SafeAreaCanvas : MonoBehaviour
{
    [HideInInspector]
    public RectTransform rectTransform;

    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        ApplySafeAreaCanvasAnchor();
    }

    // 값이 바뀌면 다시 적용한다 — Device Simulator는 첫 프레임에 Screen.safeArea와 Screen.width가 다른 기준으로 잡혀
    // 앵커가 1을 넘었고(09-14 실측: Max 1.29·1.08), 회전이나 해상도 변경에도 같은 경로로 대응한다 (유니티 공식 SafeArea 샘플 방식)
    private void Update()
    {
        if (Screen.safeArea == lastSafeArea && Screen.width == lastScreenSize.x && Screen.height == lastScreenSize.y) { return; }
        ApplySafeAreaCanvasAnchor();
    }

    public void ApplySafeAreaCanvasAnchor()
    {
        Rect safeArea = Screen.safeArea;
        lastSafeArea = safeArea;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);

        Vector2 minAnchor = safeArea.position;
        Vector2 maxAnchor = safeArea.position + safeArea.size;

        minAnchor.x /= Screen.width;
        minAnchor.y /= Screen.height;
        maxAnchor.x /= Screen.width;
        maxAnchor.y /= Screen.height;

        // 앵커는 0~1이어야 한다 — 기준이 어긋난 프레임에 1을 넘으면 패널이 화면 밖으로 나간다
        rectTransform.anchorMin = new Vector2(Mathf.Clamp01(minAnchor.x), Mathf.Clamp01(minAnchor.y));
        rectTransform.anchorMax = new Vector2(Mathf.Clamp01(maxAnchor.x), Mathf.Clamp01(maxAnchor.y));
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = Vector2.zero;
    }
}
