using UnityEditor;
using UnityEngine;

// 매치데이 화면에 보기 모드·배속 분절 버튼을 놓고 MatchdayScreen 필드에 잇는다(S4-4). 다시 돌려도 같은 결과(이미 있으면 자리·연결만 다시 맞춘다).
// 오른쪽 열: 작은 순위표 아래를 줄이고 [경기 시작] 위 한 줄에 모드(왼쪽)·배속(오른쪽)을 둔다
public static class MatchdayViewingModeBuilder
{
    private const string PanelPath = "Assets/Prefabs/UI/SafeAreaPanel.prefab";
    private const string SegmentPath = "Assets/Prefabs/UI/Kit/SegmentedControl.prefab";
    private const string ModeName = "ViewingModeControl";
    private const string SpeedName = "SpeedControl";
    private const float RowMinY = 0.14f;
    private const float RowMaxY = 0.22f;
    private const float TableMinY = 0.24f;

    [MenuItem("Tools/UI/매치데이 보기 모드 배치")]
    public static void Build()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
        try
        {
            MatchdayScreen screen = root.GetComponentInChildren<MatchdayScreen>(true);
            if (screen == null)
            {
                Debug.LogError("[MatchdayViewingModeBuilder] SafeAreaPanel에 MatchdayScreen이 없다");
                return;
            }
            SegmentedControl mode = EnsureSegment(screen.transform, ModeName, 0.50f, 0.76f);
            SegmentedControl speed = EnsureSegment(screen.transform, SpeedName, 0.78f, 0.96f);
            ShrinkTable(screen.transform);

            var serialized = new SerializedObject(screen);
            serialized.FindProperty("viewingModeControl").objectReferenceValue = mode;
            serialized.FindProperty("speedControl").objectReferenceValue = speed;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
            Debug.Log("[MatchdayViewingModeBuilder] 보기 모드·배속 버튼을 배치했다");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static SegmentedControl EnsureSegment(Transform parent, string name, float minX, float maxX)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SegmentPath);
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
        }
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(minX, RowMinY);
        rect.anchorMax = new Vector2(maxX, RowMaxY);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return go.GetComponent<SegmentedControl>();
    }

    private static void ShrinkTable(Transform screen)
    {
        Transform table = screen.Find("TableText");
        if (table == null) { return; }
        var rect = (RectTransform)table;
        rect.anchorMin = new Vector2(rect.anchorMin.x, TableMinY);
    }
}
