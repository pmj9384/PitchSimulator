using System.Collections.Generic;
using Game.Core.Placement;
using UnityEditor;
using UnityEngine;

// 공통 피치 프리팹(11-05): 선·마크·골대를 PitchLayout(= FieldBounds 상수)으로 한 번 만들어 저장한다. 경기장 프리팹 4종(부별)이 이 프리팹을 넣는다.
// 손으로 숫자를 적지 않는 이유: 화면의 박스가 시뮬이 판정하는 박스와 어긋나지 않게(PitchPrefabTests가 저장된 프리팹을 다시 대조한다)
public static class PitchPrefabBuilder
{
    public const string PrefabPath = "Assets/Prefabs/Pitch/CommonPitch.prefab";
    public const string LineMaterialPath = "Assets/Materials/FieldLine.mat";
    public const float LineHeight = 0.04f;    // 기존 손 배치 선과 같은 두께(잔디 위로 살짝)
    private const float SpotDiameter = 0.22f;  // 마크 지름 22cm(IFAB 1조 "지름 0.22m 원")

    [MenuItem("Tools/Pitch/공통 피치 프리팹 만들기")]
    public static void Build()
    {
        Material lineMaterial = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);
        if (lineMaterial == null)
        {
            Debug.LogError($"[PitchPrefabBuilder] {LineMaterialPath} 없음");
            return;
        }

        var root = new GameObject("CommonPitch");
        BuildLines(root.transform, lineMaterial);
        BuildSpots(root.transform, lineMaterial);
        BuildGoals(root.transform, lineMaterial);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[PitchPrefabBuilder] {PrefabPath} 저장");
    }

    private static void BuildLines(Transform root, Material material)
    {
        Transform parent = NewChild(root, "Lines");
        List<PitchLine> lines = PitchLayout.Lines();
        for (int i = 0; i < lines.Count; i++)
        {
            PitchLine line = lines[i];
            GameObject go = Primitive(PrimitiveType.Cube, $"Line{i:00}", parent, material);
            go.transform.localPosition = new Vector3((line.FromX + line.ToX) * 0.5f, LineHeight * 0.5f, (line.FromZ + line.ToZ) * 0.5f);
            float yaw = -Mathf.Atan2(line.ToZ - line.FromZ, line.ToX - line.FromX) * Mathf.Rad2Deg;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(line.Length + FieldBounds.LineWidth, LineHeight, FieldBounds.LineWidth);   // 폭만큼 늘려 모서리를 닫는다
        }
    }

    private static void BuildSpots(Transform root, Material material)
    {
        Transform parent = NewChild(root, "Spots");
        List<(float x, float z)> spots = PitchLayout.Spots();
        for (int i = 0; i < spots.Count; i++)
        {
            GameObject go = Primitive(PrimitiveType.Cylinder, $"Spot{i}", parent, material);
            go.transform.localPosition = new Vector3(spots[i].x, LineHeight * 0.5f, spots[i].z);
            go.transform.localScale = new Vector3(SpotDiameter, LineHeight * 0.5f, SpotDiameter);   // 원기둥 높이는 2라 반을 곱한다
        }
    }

    // 포스트 2개 + 크로스바. 원기둥은 높이 2·축 Y라 높이는 반, 크로스바는 X로 눕혀 Z축을 따르게 한다
    private static void BuildGoals(Transform root, Material material)
    {
        Transform parent = NewChild(root, "Goals");
        List<(float x, float z)> posts = PitchLayout.GoalPosts();
        for (int i = 0; i < posts.Count; i++)
        {
            GameObject post = Primitive(PrimitiveType.Cylinder, $"Post{i}", parent, material);
            post.transform.localPosition = new Vector3(posts[i].x, FieldBounds.GoalHeight * 0.5f, posts[i].z);
            post.transform.localScale = new Vector3(FieldBounds.GoalPostDiameter, FieldBounds.GoalHeight * 0.5f, FieldBounds.GoalPostDiameter);
        }
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? -FieldBounds.HalfLength : FieldBounds.HalfLength;
            GameObject bar = Primitive(PrimitiveType.Cylinder, $"Crossbar{side}", parent, material);
            bar.transform.localPosition = new Vector3(x, FieldBounds.GoalHeight + FieldBounds.GoalPostDiameter * 0.5f, 0f);
            bar.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bar.transform.localScale = new Vector3(FieldBounds.GoalPostDiameter, FieldBounds.GoalHalfWidth + FieldBounds.GoalPostDiameter, FieldBounds.GoalPostDiameter);   // 포스트 바깥 면까지(반길이)
        }
    }

    private static Transform NewChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    // 프리미티브는 콜라이더를 달고 나온다. 화면 전용이라 지운다(공·선수는 물리를 안 쓴다)
    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
}
