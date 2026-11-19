using UnityEngine;

// SafeArea 안에 있는 창의 딤을 화면 전체(루트 캔버스)로 넓힌다(11-19). 부모(SafeAreaPanel 아래 창)는 노치·둥근 모서리를 피해 줄어들지만,
// 딤은 뒤 화면을 가리는 바탕이라 가장자리까지 덮어야 한다(10-29 조사 모달 규칙: 딤 배경). 창 본문(카드·버튼)은 그대로 SafeArea 안에 둔다.
// SafeAreaCanvas가 Update에서 앵커를 바꾸므로 그 뒤 LateUpdate에서, 화면 크기나 SafeArea가 바뀐 프레임에만 다시 맞춘다
[RequireComponent(typeof(RectTransform))]
public class FullScreenBackdrop : MonoBehaviour
{
    private readonly Vector3[] corners = new Vector3[4];
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;

    private void OnEnable()
    {
        Fit();
    }

    private void LateUpdate()
    {
        if (Screen.safeArea == lastSafeArea && Screen.width == lastScreenSize.x && Screen.height == lastScreenSize.y) { return; }
        Fit();
    }

    // 루트 캔버스의 네 귀퉁이를 부모 좌표로 옮겨 그대로 덮는다. 앵커는 부모 왼쪽 아래 한 점에 두고 오프셋으로 크기를 준다
    private void Fit()
    {
        lastSafeArea = Screen.safeArea;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { return; }
        var parent = transform.parent as RectTransform;
        if (parent == null) { return; }

        ((RectTransform)canvas.rootCanvas.transform).GetWorldCorners(corners);
        Vector2 min = parent.InverseTransformPoint(corners[0]);
        Vector2 max = parent.InverseTransformPoint(corners[2]);
        var rect = (RectTransform)transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.offsetMin = min - parent.rect.min;
        rect.offsetMax = max - parent.rect.min;
    }
}
