using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.Placement;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 경기장 프리팹 4종(11-06 경기장 4종 2단계, 설계 실험/2026-11-05-설계-경기장-4종.md §2).
// Stadiums.csv의 부별 1행 → 잔디 평면 + CommonPitch 중첩 + 단 큐브·기단·지붕·조명탑·전광판·광고판·벤치를 큐브로 조립해 Resources/Stadiums/{id}.prefab으로 저장한다.
// 단 큐브 자리는 StadiumLayout.Rows가 낸다(관중 4단계가 같은 Seats를 쓰므로 좌석 오브젝트는 만들지 않는다). StadiumPrefabTests가 저장본을 다시 대조한다.
// 모바일 성능(11-06 관중석 최적화 레퍼런스 2단계 적용): 스탠드 재질 2개(URP Lit, 색만 다름, SRP Batcher 호환, GPU Instancing 끔), 콜라이더 없음,
// 정적 배칭 안 씀(URP 권고: SRP Batcher 켬·정적 배칭 끔), 스탠드·지붕·광고판 등은 그림자 드리움·받음 둘 다 끔(잔디만 받음 설정. 지금 씬 조명은 그림자를 안 켜서 잔디도 실제로는 받지 않는다), 단 큐브는 면별 부모 아래
public static class StadiumPrefabBuilder
{
    private const string CsvPath = "Assets/Resources/Tables/Stadiums.csv";
    private const string OutputFolder = "Assets/Resources/Stadiums";
    private const string StandsName = "Stands";

    // 치수 근거 없는 값은 전부 [가정]
    private const float BaseFrontMargin = 0.2f;     // [가정] 기단이 첫 단 앞으로 내미는 폭(m)
    private const float RoofClearance = 3f;         // [가정] 지붕 아래면과 맨 뒤 단 윗면 사이 높이(m)
    private const float RoofThickness = 0.3f;       // [가정]
    private const float LightStripHeight = 0.5f;    // [가정] 1부 지붕 가장자리 조명 띠 높이
    private const float TowerHeight = 18f;          // [가정] 조명탑 기둥 높이(m)
    private const float TowerWidth = 0.6f;          // [가정]
    private const float TowerInset = 3f;            // [가정] 코너 관중석 영역 바깥쪽 여유(m)
    private const float TowerHeadWidth = 4f;        // [가정]
    private const float TowerHeadHeight = 1.5f;     // [가정]
    private const float ScoreboardWidth = 8f;       // [가정] 폭(Z, m)
    private const float ScoreboardHeight = 3f;      // [가정]
    private const float ScoreboardThickness = 0.5f; // [가정]
    private const float ScoreboardLift = 3f;        // [가정] 놓인 바닥(스탠드 윗면 또는 지면) 위 높이
    private const float AdBoardHeight = 1f;         // [가정]
    private const float AdBoardThickness = 0.2f;    // [가정]
    private const float AdBoardGap = 0.3f;          // [가정] 광고판이 런오프 바깥 끝에서 안쪽으로 들어온 거리
    private const float BenchLength = 4f;           // [가정]
    private const float BenchSeatHeight = 0.45f;    // [가정]
    private const float BenchDepth = 0.6f;          // [가정]
    private const float BenchCenterX = 12f;         // [가정] 중앙선에서 벤치 중심까지(m)
    private const float DugoutRoofHeight = 2.2f;    // [가정] 1·2부 더그아웃 지붕 높이
    private const float DugoutRoofThickness = 0.1f; // [가정]
    private const float DugoutOverhang = 1f;        // [가정] 지붕이 벤치보다 긴 길이(m)
    private const float DugoutDepthScale = 2.5f;    // [가정] 지붕 깊이 = 벤치 깊이 × 이 값
    private const float RoofLightDepth = 0.3f;      // [가정] 1부 조명 띠 깊이(m)
    private const float ScoreboardGroundGap = 1f;   // [가정] 골대 뒤 관중석이 없을 때 런오프 끝에서 전광판까지(m)
    private const int DugoutMaxTier = 2;

    private enum Paint { Light, Dark }

    [MenuItem("Tools/Pitch/경기장 프리팹 만들기")]
    public static void Build()
    {
        GameObject pitch = AssetDatabase.LoadAssetAtPath<GameObject>(PitchPrefabBuilder.PrefabPath);
        if (pitch == null)
        {
            Debug.LogError($"[StadiumPrefabBuilder] {PitchPrefabBuilder.PrefabPath} 없음");
            return;
        }

        List<StadiumSpec> specs = StadiumSpecParser.Parse(File.ReadAllText(CsvPath));
        GrassWearBuilder.Build(specs);   // 잔디 머티리얼 4개(부별 _Wear)를 먼저 최신으로
        Dictionary<Paint, Material> paints = LoadPaints();
        Directory.CreateDirectory(OutputFolder);
        for (int i = 0; i < specs.Count; i++)
        {
            var grass = AssetDatabase.LoadAssetAtPath<Material>(GrassWearBuilder.MaterialPathOf(specs[i]));
            BuildOne(specs[i], pitch, grass, paints);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static string PrefabPathOf(string id)
    {
        return $"{OutputFolder}/{id}.prefab";
    }

    private static void BuildOne(StadiumSpec spec, GameObject pitch, Material grass, Dictionary<Paint, Material> paints)
    {
        var root = new GameObject(spec.Id);
        BuildGrass(root.transform, spec, grass);
        var pitchInstance = (GameObject)PrefabUtility.InstantiatePrefab(pitch);
        pitchInstance.transform.SetParent(root.transform, false);
        BuildStands(root.transform, spec, paints);
        BuildRoofs(root.transform, spec, paints);
        BuildTowers(root.transform, spec, paints);
        BuildScoreboards(root.transform, spec, paints);
        BuildAdBoards(root.transform, spec, paints);
        BuildBenches(root.transform, spec, paints);

        string path = PrefabPathOf(spec.Id);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log($"[StadiumPrefabBuilder] {path} 저장");
    }

    // 잔디 평면(런오프 포함). 크기는 GrassWearBuilder가 낸다(마스크 UV 매핑과 같은 식)
    private static void BuildGrass(Transform root, StadiumSpec spec, Material grass)
    {
        GameObject go = Primitive(PrimitiveType.Plane, "Grass", root, grass);
        go.transform.localScale = GrassWearBuilder.PlaneScale(spec);
        go.GetComponent<MeshRenderer>().receiveShadows = true;
    }

    private static void BuildStands(Transform root, StadiumSpec spec, Dictionary<Paint, Material> paints)
    {
        Transform parent = NewChild(root, StandsName);
        List<StandRow> rows = StadiumLayout.Rows(spec);
        for (int i = 0; i < rows.Count; i++)
        {
            StandRow row = rows[i];
            Transform sideParent = SideParent(parent, row.Side);
            GameObject go = Primitive(PrimitiveType.Cube, RowName(row.Side, row.Index), sideParent, paints[Paint.Light]);
            go.transform.localPosition = new Vector3((row.FromX + row.ToX) * 0.5f, row.Height * 0.5f, (row.FromZ + row.ToZ) * 0.5f);
            float length = Mathf.Abs(row.ToX - row.FromX) + Mathf.Abs(row.ToZ - row.FromZ);   // 한 축으로만 달린다
            go.transform.localScale = StadiumLayout.IsAlongX(row.Side) ? new Vector3(length, row.Height, spec.RowDepth) : new Vector3(spec.RowDepth, row.Height, length);
        }
        for (int s = 0; s < spec.Stands.Count; s++)
        {
            BuildBase(parent.Find(spec.Stands[s].ToString()), spec, spec.Stands[s], paints[Paint.Dark]);
        }
    }

    // 기단: 첫 단 앞으로 살짝 내민 바닥~기단 높이 슬래브(앞면이 어두운 벽으로 보인다)
    private static void BuildBase(Transform parent, StadiumSpec spec, StandSide side, Material material)
    {
        float front = spec.Runoff - BaseFrontMargin;
        float back = spec.Runoff + spec.Rows * spec.RowDepth;
        Slab(parent, $"Base_{side}", spec, side, front, back, 0f, spec.BaseHeight, material);
    }

    private static void BuildRoofs(Transform root, StadiumSpec spec, Dictionary<Paint, Material> paints)
    {
        if (spec.Roof.Count == 0) { return; }

        Transform parent = NewChild(root, "Roofs");
        float top = spec.BaseHeight + spec.Rows * spec.RowHeight;
        float front = spec.Runoff;
        float back = spec.Runoff + spec.Rows * spec.RowDepth;
        for (int i = 0; i < spec.Roof.Count; i++)
        {
            StandSide side = spec.Roof[i];
            Slab(parent, $"Roof_{side}", spec, side, front, back, top + RoofClearance, top + RoofClearance + RoofThickness, paints[Paint.Light]);
            if (spec.LightTowers == 0)
            {
                Slab(parent, $"RoofLight_{side}", spec, side, front - RoofLightDepth, front, top + RoofClearance - LightStripHeight, top + RoofClearance, paints[Paint.Dark]);   // 조명탑 대신 지붕 앞 가장자리 띠
            }
        }
    }

    // 코너 조명탑. 코너 네모(관중석 면이 안 가는 구석)에 선다. 기둥 큐브 1 + 등 큐브 1
    private static void BuildTowers(Transform root, StadiumSpec spec, Dictionary<Paint, Material> paints)
    {
        if (spec.LightTowers == 0) { return; }

        Transform parent = NewChild(root, "Towers");
        float x = FieldBounds.HalfLength + spec.Runoff + TowerInset;
        float z = FieldBounds.HalfWidth + spec.Runoff + TowerInset;
        for (int i = 0; i < spec.LightTowers && i < 4; i++)
        {
            float sx = (i & 1) == 0 ? 1f : -1f;
            float sz = (i & 2) == 0 ? 1f : -1f;
            Transform tower = NewChild(parent, $"Tower{i}");
            tower.localPosition = new Vector3(sx * x, 0f, sz * z);
            Cube(tower, "Pole", new Vector3(0f, TowerHeight * 0.5f, 0f), new Vector3(TowerWidth, TowerHeight, TowerWidth), paints[Paint.Dark]);
            Cube(tower, "Head", new Vector3(0f, TowerHeight + TowerHeadHeight * 0.5f, 0f), new Vector3(TowerHeadWidth, TowerHeadHeight, TowerHeadWidth), paints[Paint.Dark]);
        }
    }

    // 골대 뒤 전광판. 번호 0은 오른쪽(+X), 1은 왼쪽. 골대 뒤 관중석이 있으면 그 맨 뒤 단 위에, 없으면 지면에 선다
    private static void BuildScoreboards(Transform root, StadiumSpec spec, Dictionary<Paint, Material> paints)
    {
        if (spec.Scoreboards == 0) { return; }

        Transform parent = NewChild(root, "Scoreboards");
        for (int i = 0; i < spec.Scoreboards && i < 2; i++)
        {
            float sign = i == 0 ? 1f : -1f;
            StandSide end = i == 0 ? StandSide.EndRight : StandSide.EndLeft;
            bool hasStand = HasStand(spec, end);
            float distance = FieldBounds.HalfLength + spec.Runoff + (hasStand ? spec.Rows * spec.RowDepth : ScoreboardGroundGap);
            float floor = hasStand ? spec.BaseHeight + spec.Rows * spec.RowHeight : 0f;
            float y = floor + ScoreboardLift + ScoreboardHeight * 0.5f;
            Cube(parent, $"Scoreboard{i}", new Vector3(sign * distance, y, 0f), new Vector3(ScoreboardThickness, ScoreboardHeight, ScoreboardWidth), paints[Paint.Dark]);
        }
    }

    // 광고판: 런오프 바깥 끝 안쪽에 길게 1개씩
    private static void BuildAdBoards(Transform root, StadiumSpec spec, Dictionary<Paint, Material> paints)
    {
        if (spec.AdBoards.Count == 0) { return; }

        Transform parent = NewChild(root, "AdBoards");
        float offset = spec.Runoff - AdBoardGap;
        for (int i = 0; i < spec.AdBoards.Count; i++)
        {
            StandSide side = spec.AdBoards[i];
            Slab(parent, $"AdBoard_{side}", spec, side, offset - AdBoardThickness * 0.5f, offset + AdBoardThickness * 0.5f, 0f, AdBoardHeight, paints[Paint.Dark]);
        }
    }

    // 벤치 2개(가까운 쪽 터치라인 바깥, 중앙선 양쪽). 1·2부는 더그아웃 지붕을 얹는다
    private static void BuildBenches(Transform root, StadiumSpec spec, Dictionary<Paint, Material> paints)
    {
        Transform parent = NewChild(root, "Benches");
        float z = -FieldBounds.HalfWidth - spec.Runoff * 0.5f;
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -BenchCenterX : BenchCenterX;
            Cube(parent, $"Bench{i}", new Vector3(x, BenchSeatHeight * 0.5f, z), new Vector3(BenchLength, BenchSeatHeight, BenchDepth), paints[Paint.Dark]);
            if (spec.Tier <= DugoutMaxTier)
            {
                Cube(parent, $"Dugout{i}", new Vector3(x, DugoutRoofHeight, z), new Vector3(BenchLength + DugoutOverhang, DugoutRoofThickness, BenchDepth * DugoutDepthScale), paints[Paint.Light]);
            }
        }
    }

    // 면마다 부모 하나(면 단위 컬링·분리 유지). 없으면 만든다
    private static Transform SideParent(Transform stands, StandSide side)
    {
        Transform found = stands.Find(side.ToString());
        if (found != null) { return found; }

        return NewChild(stands, side.ToString());
    }

    private static bool HasStand(StadiumSpec spec, StandSide side)
    {
        for (int i = 0; i < spec.Stands.Count; i++)
        {
            if (spec.Stands[i] == side) { return true; }
        }
        return false;
    }

    private static string RowName(StandSide side, int index)
    {
        return $"Stand_{side}_{index:00}";
    }

    // 면 하나를 따라 앞뒤 거리(선에서 front~back)와 높이 y0~y1을 채우는 슬래브. 길이는 단과 같다(선 길이 + 양쪽 런오프)
    private static GameObject Slab(Transform parent, string name, StadiumSpec spec, StandSide side, float front, float back, float y0, float y1, Material material)
    {
        float mid = (front + back) * 0.5f;
        float depth = back - front;
        float halfAlongX = FieldBounds.HalfLength + spec.Runoff;
        float halfAlongZ = FieldBounds.HalfWidth + spec.Runoff;
        Vector3 center;
        Vector3 size;
        switch (side)
        {
            case StandSide.Far:
                center = new Vector3(0f, (y0 + y1) * 0.5f, FieldBounds.HalfWidth + mid);
                size = new Vector3(halfAlongX * 2f, y1 - y0, depth);
                break;
            case StandSide.Near:
                center = new Vector3(0f, (y0 + y1) * 0.5f, -FieldBounds.HalfWidth - mid);
                size = new Vector3(halfAlongX * 2f, y1 - y0, depth);
                break;
            case StandSide.EndLeft:
                center = new Vector3(-FieldBounds.HalfLength - mid, (y0 + y1) * 0.5f, 0f);
                size = new Vector3(depth, y1 - y0, halfAlongZ * 2f);
                break;
            default:
                center = new Vector3(FieldBounds.HalfLength + mid, (y0 + y1) * 0.5f, 0f);
                size = new Vector3(depth, y1 - y0, halfAlongZ * 2f);
                break;
        }
        return Cube(parent, name, center, size, material);
    }

    private static GameObject Cube(Transform parent, string name, Vector3 center, Vector3 size, Material material)
    {
        GameObject go = Primitive(PrimitiveType.Cube, name, parent, material);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        return go;
    }

    private static Transform NewChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    // 프리미티브는 콜라이더를 달고 나온다. 화면 전용이라 지운다. 그림자는 드리우지도 받지도 않는다(경기 읽기 방해 + 그림자 패스 비용, Unity 모바일 가이드)
    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    // 재질 2개: URP Lit 하나에 색만 다르게(SRP Batcher 호환)
    private static Dictionary<Paint, Material> LoadPaints()
    {
        Directory.CreateDirectory(GrassWearBuilder.MaterialFolder);
        return new Dictionary<Paint, Material>
        {
            { Paint.Light, LoadPaint("StadiumLight", new Color(0.72f, 0.73f, 0.75f)) },   // 단·지붕
            { Paint.Dark, LoadPaint("StadiumDark", new Color(0.18f, 0.22f, 0.32f)) },     // 기단·탑·전광판·광고판·벤치·조명 띠
        };
    }

    private static Material LoadPaint(string name, Color color)
    {
        string path = $"{GrassWearBuilder.MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.1f);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = false;   // URP 권고(변이 증가 방지). 관중 4단계는 RenderMeshInstanced 쪽 재질을 따로 쓴다
        EditorUtility.SetDirty(material);
        return material;
    }
}
