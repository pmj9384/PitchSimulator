using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 선수 몸 메시 굽기(11-14 모델 교체 1단계, 설계 실험/2026-11-12-설계-효과음-모델교체.md B-4 1단계 ③·결정 B3·B8).
// UBC 무료판 몸(Superhero_Male)은 서브메시 1개라 유니폼 슬롯이 없다. 버텍스의 뼈 가중치로 삼각형을 피부·상의·하의·양말·축구화 5개 서브메시로 나눠
// 지금 슬롯 방식(PlayerView.SlotKind)을 그대로 쓴다. 체형은 버텍스를 뼈 축 쪽으로 당긴 메시 3종(마름·보통·원본)으로 굽는다.
// 0단계 시험 스크립트(Vault 모델교체-0단계-2026-11-13/bodymesh.cs·uniform_split.cs)를 리포 문체로 옮기고, 삼각형 단위라 생기던 톱니 경계를 정리한다.
// 메뉴는 몇 번 눌러도 같은 결과다. 이미 있는 메시 에셋은 내용만 덮어써서 GUID(프리팹 참조)가 유지된다
public static class PlayerMeshBaker
{
    public const string ModelFolder = "Assets/Models/UBC";
    public const string ModelPath = ModelFolder + "/Superhero_Male_FullBody.fbx";
    public const string BodyRendererName = "SuperHero_Male";
    public const string OutputFolder = ModelFolder + "/Baked";
    public const int SubmeshCount = 5;   // Region 값 순서 = 서브메시 순서
    public const string SkinPatternPath = ModelFolder + "/Textures/T_Skin_Pattern.png";
    private const string SkinSourcePath = ModelFolder + "/Textures/T_Superhero_Male_Ligh.png";
    // 원본 피부 대표색(0단계 B-8: 가장 많은 색 169·109·75). 원본을 이 색으로 나누면 밝기 무늬만 남고, 팔레트 색을 곱해 피부 4종을 만든다(결정 B10)
    private static readonly Color SkinReference = new Color(169f / 255f, 109f / 255f, 75f / 255f);
    private const float SkinPatternGain = 0.85f;   // [가정] 대표색이 무늬에서 0.85가 되게 한다. 대표색(최빈색)이 피부 평균보다 붉어서 피부 픽셀의 약 19%는 G·B가 1로 잘리고(R은 안 잘림) 화면 색이 팔레트보다 약 15% 어둡다(11-14 재검증 측정)

    // 서브메시 순서. PlayerPrefabBuilder가 슬롯 역할(Skin·Kit·Kit·Kit·Boots)을 이 순서로 적는다
    public enum Region
    {
        Skin = 0,
        Shirt = 1,
        Shorts = 2,
        Socks = 3,
        Boots = 4,
    }

    // 뼈 이름 접두(UE 마네킹식). 분류·체형·경계 평면이 전부 이 상수를 읽는다(설계 B-3 「굽기 스크립트가 기대는 이름」)
    private const string Pelvis = "pelvis";
    private const string Spine = "spine";
    private const string SpineUpper = "spine_03";
    private const string SpineMiddle = "spine_02";
    private const string SpineLower = "spine_01";
    private const string Neck = "neck";
    private const string HeadBone = "head";   // 대소문자 무시(원본은 "Head")
    private const string Clavicle = "clavicle";
    private const string UpperArm = "upperarm";
    private const string LowerArm = "lowerarm";
    private const string Thigh = "thigh";
    private const string Calf = "calf";
    private const string Foot = "foot";
    private const string Ball = "ball";
    private static readonly string[] RequiredBonePrefixes = { Pelvis, Spine, Neck, Clavicle, UpperArm, LowerArm, Thigh, Calf, Foot, Ball };
    private static readonly string[] Sides = { "_l", "_r" };

    // 경계 기준 [가정]: 0단계 렌더 시트(유니폼-*.png)에서 반팔·반바지·무릎 아래 양말로 보이던 값
    private const float SleeveT = 0.45f;   // 위팔 구간에서 이 비율보다 어깨 쪽 = 상의
    private const float ShortsT = 0.55f;   // 허벅지 구간에서 이 비율보다 골반 쪽 = 하의
    private const float SockT = 0.25f;     // 종아리 구간에서 이 비율보다 발 쪽 = 양말
    private const float MaxSnap = 0.03f;   // [가정] 경계 버텍스를 경계 평면으로 옮기는 최대 거리(m, 평면까지의 실제 거리). 이보다 멀면 안 옮긴다(몸 모양을 지키려고)
    private const float ReclassifyBand = 0.06f;   // [가정] 목·발목에서 삼각형을 평면 기준으로 다시 나누는 띠의 반폭(m). 뼈 가중치 분류가 평면 하나로 안 모이는 두 곳(11-14 재검증)

    private readonly struct BodyShape
    {
        public readonly string Name;
        public readonly float Torso;
        public readonly float Arm;
        public readonly float Leg;
        public readonly float Neck;

        public BodyShape(string name, float torso, float arm, float leg, float neck)
        {
            Name = name;
            Torso = torso;
            Arm = arm;
            Leg = leg;
            Neck = neck;
        }
    }

    // 뼈 축으로 당기는 비율(1 = 그대로). 0단계 메시 변형 시험값(최대 이동 4.5cm·2.5cm, 이음매 없음). 머리뼈는 안 건드린다
    private static readonly BodyShape[] Shapes =
    {
        new BodyShape("Thin", 0.80f, 0.72f, 0.84f, 0.88f),
        new BodyShape("Regular", 0.89f, 0.84f, 0.92f, 0.94f),
        new BodyShape("Original", 1f, 1f, 1f, 1f),
    };

    // 색이 갈리는 경계 하나. 두 영역 사이의 평면(점 + 뼈 축 방향 법선). 법선 쪽이 Positive 영역
    private readonly struct BoundaryPlane
    {
        public readonly Region Negative;
        public readonly Region Positive;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;

        public BoundaryPlane(Region negative, Region positive, Vector3 point, Vector3 normal)
        {
            Negative = negative;
            Positive = positive;
            Point = point;
            Normal = normal;
        }

        public int Mask
        {
            get { return (1 << (int)Negative) | (1 << (int)Positive); }
        }

        public float Distance(Vector3 p)
        {
            return Vector3.Dot(p - Point, Normal);
        }
    }

    public static string BodyPathOf(string shape)
    {
        return $"{OutputFolder}/Body_{shape}.asset";
    }

    public static string OriginalBodyPath()
    {
        return BodyPathOf(Shapes[Shapes.Length - 1].Name);
    }

    // 체형 번호(PortraitRules.BodyOf) 순서의 메시 경로. 0 마름 · 1 보통 · 2 원본
    public static string BodyPathOfIndex(int body)
    {
        return BodyPathOf(Shapes[body].Name);
    }

    public static int ShapeCount
    {
        get { return Shapes.Length; }
    }

    [MenuItem("Tools/Pitch/선수 몸 메시 굽기")]
    public static void Bake()
    {
        SkinnedMeshRenderer body = FindBodyRenderer();
        if (body == null) { return; }
        if (!HasRequiredBones(body.bones)) { return; }

        var skeleton = new Skeleton(body);
        List<BoundaryPlane> planes = BoundaryPlanes(skeleton);
        if (planes == null) { return; }

        Mesh source = body.sharedMesh;
        int[] regions = ClassifyTriangles(source, skeleton);
        LogRegionCounts("뼈 가중치 분류", regions);
        ReclassifyNeckAndAnkles(source, skeleton, planes, regions);
        LogRegionCounts("목·발목 평면 재분류 뒤", regions);

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder(ModelFolder, "Baked");
        }
        for (int s = 0; s < Shapes.Length; s++)
        {
            BakeShape(source, skeleton, planes, regions, Shapes[s]);
        }
        BakeSkinPattern();
        AssetDatabase.SaveAssets();
    }

    // 피부 밝기 무늬: 원본 피부 텍스처를 대표색으로 나눈다. 텍스처가 Read/Write 꺼짐이라 PNG 파일을 직접 읽는다
    private static void BakeSkinPattern()
    {
        if (!File.Exists(SkinSourcePath))
        {
            Debug.LogError($"[PlayerMeshBaker] {SkinSourcePath} 없음");
            return;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(SkinSourcePath));
        Color[] pixels = texture.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = PatternOf(pixels[i]);
        }
        texture.SetPixels(pixels);
        File.WriteAllBytes(SkinPatternPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(SkinPatternPath);
        SetPatternImport();
        Debug.Log($"[PlayerMeshBaker] {SkinPatternPath} 저장");
    }

    private static Color PatternOf(Color pixel)
    {
        float r = Mathf.Clamp01(pixel.r / SkinReference.r * SkinPatternGain);
        float g = Mathf.Clamp01(pixel.g / SkinReference.g * SkinPatternGain);
        float b = Mathf.Clamp01(pixel.b / SkinReference.b * SkinPatternGain);
        return new Color(r, g, b, 1f);
    }

    // 다른 몸 텍스처와 같은 모바일 설정(Android 최대 512, ASTC 6x6)
    private static void SetPatternImport()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SkinPatternPath);
        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = 512;
        android.format = TextureImporterFormat.ASTC_6x6;
        importer.SetPlatformTextureSettings(android);
        importer.SaveAndReimport();
    }

    private static SkinnedMeshRenderer FindBodyRenderer()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[PlayerMeshBaker] {ModelPath} 없음");
            return null;
        }

        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.name == BodyRendererName) { return r; }
        }
        Debug.LogError($"[PlayerMeshBaker] {ModelPath}에 렌더러 {BodyRendererName}이 없다");
        return null;
    }

    // 뼈 이름(UE 마네킹식 접두)에 기대는 분류라, 이름이 다르면 전부 피부로 조용히 분류된다(설계 구멍 25). 하나라도 없으면 아무것도 안 굽는다
    private static bool HasRequiredBones(Transform[] bones)
    {
        for (int p = 0; p < RequiredBonePrefixes.Length; p++)
        {
            if (FindBone(bones, RequiredBonePrefixes[p], string.Empty) < 0)
            {
                Debug.LogError($"[PlayerMeshBaker] 뼈 이름 접두 {RequiredBonePrefixes[p]}가 없다. 모델이 바뀌었으면 분류 규칙을 다시 본다");
                return false;
            }
        }
        return true;
    }

    private static int FindBone(Transform[] bones, string prefix, string suffix)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            string name = bones[i].name;
            if (name.StartsWith(prefix) && name.EndsWith(suffix)) { return i; }
        }
        return -1;
    }

    // 경계마다 평면 하나. 소매·바지 밑단·양말 위는 기준 비율, 발목(양말·축구화)은 종아리 끝 = 발 머리, 목 둘레는 목뼈 머리
    private static List<BoundaryPlane> BoundaryPlanes(Skeleton skeleton)
    {
        var planes = new List<BoundaryPlane>();
        for (int s = 0; s < Sides.Length; s++)
        {
            int arm = skeleton.Find(UpperArm, Sides[s]);
            int thigh = skeleton.Find(Thigh, Sides[s]);
            int calf = skeleton.Find(Calf, Sides[s]);
            if (arm < 0 || thigh < 0 || calf < 0)
            {
                Debug.LogError($"[PlayerMeshBaker] {Sides[s]} 쪽 위팔·허벅지·종아리 뼈를 못 찾았다");
                return null;
            }
            planes.Add(PlaneOf(skeleton, arm, SleeveT, Region.Shirt, Region.Skin));
            planes.Add(PlaneOf(skeleton, thigh, ShortsT, Region.Shorts, Region.Skin));
            planes.Add(PlaneOf(skeleton, calf, SockT, Region.Skin, Region.Socks));
            planes.Add(PlaneOf(skeleton, calf, 1f, Region.Socks, Region.Boots));
        }
        int neck = skeleton.Find(Neck, string.Empty);
        planes.Add(PlaneOf(skeleton, neck, 0f, Region.Shirt, Region.Skin));
        return planes;
    }

    private static BoundaryPlane PlaneOf(Skeleton skeleton, int bone, float t, Region negative, Region positive)
    {
        return new BoundaryPlane(negative, positive, skeleton.PointAt(bone, t), skeleton.Direction(bone));
    }

    // 삼각형마다 세 버텍스 가중치를 합해 가장 큰 뼈를 고르고, 그 뼈 구간에서 무게중심의 위치(t)로 영역을 정한다
    private static int[] ClassifyTriangles(Mesh mesh, Skeleton skeleton)
    {
        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        int[] triangles = mesh.triangles;
        int[] regions = new int[triangles.Length / 3];
        var sums = new Dictionary<int, float>();
        for (int f = 0; f < regions.Length; f++)
        {
            int bone = HeaviestBone(triangles, f, weights, sums);
            Vector3 center = Centroid(triangles, f, vertices);
            regions[f] = (int)RegionOf(skeleton.Name(bone), skeleton.T(bone, center));
        }
        return regions;
    }

    private static Vector3 Centroid(int[] triangles, int face, Vector3[] vertices)
    {
        return (vertices[triangles[face * 3]] + vertices[triangles[face * 3 + 1]] + vertices[triangles[face * 3 + 2]]) / 3f;
    }

    private static int HeaviestBone(int[] triangles, int face, BoneWeight[] weights, Dictionary<int, float> sums)
    {
        sums.Clear();
        for (int j = 0; j < 3; j++)
        {
            BoneWeight w = weights[triangles[face * 3 + j]];
            AddWeight(sums, w.boneIndex0, w.weight0);
            AddWeight(sums, w.boneIndex1, w.weight1);
            AddWeight(sums, w.boneIndex2, w.weight2);
            AddWeight(sums, w.boneIndex3, w.weight3);
        }
        int best = 0;
        float bestWeight = -1f;
        foreach (KeyValuePair<int, float> pair in sums)
        {
            if (pair.Value <= bestWeight) { continue; }

            best = pair.Key;
            bestWeight = pair.Value;
        }
        return best;
    }

    private static void AddWeight(Dictionary<int, float> sums, int bone, float weight)
    {
        if (weight <= 0f) { return; }

        float current;
        sums.TryGetValue(bone, out current);
        sums[bone] = current + weight;
    }

    private static Region RegionOf(string bone, float t)
    {
        if (bone.StartsWith(Spine) || bone.StartsWith(Clavicle)) { return Region.Shirt; }
        if (bone.StartsWith(UpperArm)) { return t < SleeveT ? Region.Shirt : Region.Skin; }
        if (bone.StartsWith(Pelvis)) { return Region.Shorts; }
        if (bone.StartsWith(Thigh)) { return t < ShortsT ? Region.Shorts : Region.Skin; }
        if (bone.StartsWith(Calf)) { return t > SockT ? Region.Socks : Region.Skin; }
        if (bone.StartsWith(Foot) || bone.StartsWith(Ball)) { return Region.Boots; }
        return Region.Skin;
    }

    // 목 둘레와 발목은 가장 무거운 뼈가 경계 양쪽에서 섞여(목: spine_03·neck·Head, 발목: calf·foot) 버텍스 스냅만으로는 3cm 안에 못 모인다(11-14 재검증).
    // 이 두 경계에 한해 평면 띠 안의 삼각형을 무게중심이 평면 어느 쪽인지로 다시 나눈다. 띠 밖과 다른 뼈의 삼각형은 그대로 둔다
    private static void ReclassifyNeckAndAnkles(Mesh mesh, Skeleton skeleton, List<BoundaryPlane> planes, int[] regions)
    {
        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        int[] triangles = mesh.triangles;
        var sums = new Dictionary<int, float>();
        for (int f = 0; f < regions.Length; f++)
        {
            string bone = skeleton.Name(HeaviestBone(triangles, f, weights, sums));
            if (!IsNeckOrAnkleBone(bone)) { continue; }

            Vector3 center = Centroid(triangles, f, vertices);
            for (int p = 0; p < planes.Count; p++)
            {
                if (!IsNeckOrAnklePlane(planes[p])) { continue; }
                if (!InPlanePair(planes[p], (Region)regions[f])) { continue; }

                float distance = planes[p].Distance(center);
                if (Mathf.Abs(distance) > ReclassifyBand) { continue; }

                regions[f] = (int)(distance > 0f ? planes[p].Positive : planes[p].Negative);
                break;
            }
        }
    }

    private static bool IsNeckOrAnkleBone(string bone)
    {
        if (bone.StartsWith(Neck) || bone.StartsWith(SpineUpper)) { return true; }   // 쇄골은 뺀다. 넣으면 목 평면 위의 어깨 윗면까지 피부가 되어 어깨가 드러난 옷이 됐다(11-14 렌더)
        if (bone.ToLowerInvariant().StartsWith(HeadBone)) { return true; }

        return bone.StartsWith(Calf) || bone.StartsWith(Foot) || bone.StartsWith(Ball);
    }

    private static bool IsNeckOrAnklePlane(BoundaryPlane plane)
    {
        if (plane.Negative == Region.Shirt && plane.Positive == Region.Skin) { return true; }

        return plane.Negative == Region.Socks && plane.Positive == Region.Boots;
    }

    private static bool InPlanePair(BoundaryPlane plane, Region region)
    {
        return region == plane.Negative || region == plane.Positive;
    }

    private static void BakeShape(Mesh source, Skeleton skeleton, List<BoundaryPlane> planes, int[] regions, BodyShape shape)
    {
        Vector3[] vertices = Morph(source, skeleton, shape);
        int snapped = SnapBoundaries(source, planes, regions, vertices);

        Mesh baked = Object.Instantiate(source);
        baked.name = $"Body_{shape.Name}";
        baked.vertices = vertices;
        baked.subMeshCount = SubmeshCount;
        List<int>[] lists = SplitTriangles(source.triangles, regions);
        for (int i = 0; i < SubmeshCount; i++)
        {
            baked.SetTriangles(lists[i], i);
        }
        baked.RecalculateBounds();
        SaveMesh(baked, BodyPathOf(shape.Name));
        Debug.Log($"[PlayerMeshBaker] {BodyPathOf(shape.Name)} 저장(경계 버텍스 {snapped}개 정리)");
    }

    // 버텍스마다 붙은 뼈 4개의 가중치로, 각 뼈 구간의 가장 가까운 점 쪽으로 비율만큼 당긴다
    private static Vector3[] Morph(Mesh mesh, Skeleton skeleton, BodyShape shape)
    {
        Vector3[] source = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        float[] pull = new float[skeleton.Count];
        for (int i = 0; i < pull.Length; i++)
        {
            pull[i] = PullOf(skeleton.Name(i), shape);
        }
        var result = new Vector3[source.Length];
        for (int v = 0; v < source.Length; v++)
        {
            result[v] = MorphVertex(source[v], weights[v], skeleton, pull);
        }
        return result;
    }

    private static Vector3 MorphVertex(Vector3 p, BoneWeight w, Skeleton skeleton, float[] pull)
    {
        Vector3 sum = Vector3.zero;
        sum += PulledBy(p, w.boneIndex0, w.weight0, skeleton, pull);
        sum += PulledBy(p, w.boneIndex1, w.weight1, skeleton, pull);
        sum += PulledBy(p, w.boneIndex2, w.weight2, skeleton, pull);
        sum += PulledBy(p, w.boneIndex3, w.weight3, skeleton, pull);
        float total = Mathf.Max(0f, w.weight0) + Mathf.Max(0f, w.weight1) + Mathf.Max(0f, w.weight2) + Mathf.Max(0f, w.weight3);
        if (total <= 0f) { return p; }

        return sum / total;
    }

    private static Vector3 PulledBy(Vector3 p, int bone, float weight, Skeleton skeleton, float[] pull)
    {
        if (weight <= 0f) { return Vector3.zero; }

        Vector3 axis = skeleton.ClosestOnSegment(bone, p);
        return weight * (axis + (p - axis) * pull[bone]);
    }

    private static float PullOf(string bone, BodyShape shape)
    {
        if (bone.StartsWith(SpineUpper) || bone.StartsWith(SpineMiddle)) { return shape.Torso; }
        if (bone.StartsWith(SpineLower) || bone.StartsWith(Pelvis)) { return (shape.Torso + 1f) * 0.5f; }
        if (bone.StartsWith(Clavicle)) { return (shape.Torso + 1f) * 0.5f; }
        if (bone.StartsWith(UpperArm)) { return shape.Arm; }
        if (bone.StartsWith(LowerArm)) { return (shape.Arm + 1f) * 0.5f; }
        if (bone.StartsWith(Thigh)) { return shape.Leg; }
        if (bone.StartsWith(Calf)) { return (shape.Leg + 1f) * 0.5f; }
        if (bone.StartsWith(Neck)) { return shape.Neck; }
        return 1f;
    }

    // 톱니 경계 정리: 정확히 두 영역에 걸친 버텍스를 그 두 영역의 경계 평면으로 옮겨 경계선을 곧게 만든다.
    // 같은 영역 쌍의 평면이 여럿이면(왼쪽·오른쪽) 원본 위치에서 가장 가까운 평면을 쓴다. 평면까지 실제 거리가 MaxSnap을 넘으면 옮기지 않는다.
    // 판정은 원본 위치로 해서 체형 3종의 경계가 같은 버텍스에서 같은 평면으로 정리된다
    private static int SnapBoundaries(Mesh source, List<BoundaryPlane> planes, int[] regions, Vector3[] vertices)
    {
        int[] masks = RegionMasks(source.triangles, regions, vertices.Length);
        Vector3[] original = source.vertices;
        int snapped = 0;
        for (int v = 0; v < vertices.Length; v++)
        {
            int plane = NearestPlane(planes, masks[v], original[v]);
            if (plane < 0) { continue; }

            vertices[v] -= planes[plane].Normal * planes[plane].Distance(vertices[v]);
            snapped++;
        }
        return snapped;
    }

    private static int NearestPlane(List<BoundaryPlane> planes, int mask, Vector3 p)
    {
        int best = -1;
        float bestDistance = MaxSnap;
        for (int i = 0; i < planes.Count; i++)
        {
            if (planes[i].Mask != mask) { continue; }

            float distance = Mathf.Abs(planes[i].Distance(p));
            if (distance > bestDistance) { continue; }

            best = i;
            bestDistance = distance;
        }
        return best;
    }

    private static int[] RegionMasks(int[] triangles, int[] regions, int vertexCount)
    {
        int[] masks = new int[vertexCount];
        for (int f = 0; f < regions.Length; f++)
        {
            int bit = 1 << regions[f];
            masks[triangles[f * 3]] |= bit;
            masks[triangles[f * 3 + 1]] |= bit;
            masks[triangles[f * 3 + 2]] |= bit;
        }
        return masks;
    }

    private static List<int>[] SplitTriangles(int[] triangles, int[] regions)
    {
        var lists = new List<int>[SubmeshCount];
        for (int i = 0; i < SubmeshCount; i++)
        {
            lists[i] = new List<int>();
        }
        for (int f = 0; f < regions.Length; f++)
        {
            List<int> list = lists[regions[f]];
            list.Add(triangles[f * 3]);
            list.Add(triangles[f * 3 + 1]);
            list.Add(triangles[f * 3 + 2]);
        }
        return lists;
    }

    // 뼈 가중치 분류 값은 0단계(피부 7,868·상의 1,730·하의 900·양말 740·축구화 1,328)와 대조할 수 있게 남긴다
    private static void LogRegionCounts(string stage, int[] regions)
    {
        int[] counts = new int[SubmeshCount];
        for (int f = 0; f < regions.Length; f++)
        {
            counts[regions[f]]++;
        }
        Debug.Log($"[PlayerMeshBaker] {stage} 삼각형: 피부 {counts[0]} · 상의 {counts[1]} · 하의 {counts[2]} · 양말 {counts[3]} · 축구화 {counts[4]}");
    }

    private static void SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return;
        }

        EditorUtility.CopySerialized(mesh, existing);
        existing.name = mesh.name;
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
    }

    // 바인드 포즈 기준 뼈 구간(메시 공간). 뼈 머리 → 꼬리(자식 뼈 머리). 자식이 여럿이면 좌우가 없는 가운데 자식을 꼬리로 삼는다
    // (spine_03의 자식은 clavicle_l·clavicle_r·neck_01이라 첫 자식을 쓰면 축이 왼쪽으로 기울어 체형 메시가 좌우 비대칭이 됐다, 11-14 재검증)
    private sealed class Skeleton
    {
        private readonly Transform[] bones;
        private readonly Vector3[] heads;
        private readonly Vector3[] tails;

        public Skeleton(SkinnedMeshRenderer renderer)
        {
            bones = renderer.bones;
            Matrix4x4[] bindposes = renderer.sharedMesh.bindposes;
            heads = new Vector3[bones.Length];
            tails = new Vector3[bones.Length];
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < bones.Length; i++)
            {
                index[bones[i]] = i;
                heads[i] = bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero);
            }
            for (int i = 0; i < bones.Length; i++)
            {
                int tail = TailOf(i, index);
                tails[i] = tail < 0 ? heads[i] : heads[tail];
            }
        }

        public int Count
        {
            get { return bones.Length; }
        }

        public string Name(int bone)
        {
            return bones[bone].name;
        }

        public int Find(string prefix, string suffix)
        {
            return FindBone(bones, prefix, suffix);
        }

        public Vector3 Direction(int bone)
        {
            return (tails[bone] - heads[bone]).normalized;
        }

        public Vector3 PointAt(int bone, float t)
        {
            return heads[bone] + (tails[bone] - heads[bone]) * t;
        }

        // 구간 안의 비율(0~1로 자름). 길이 0인 뼈(손끝·Head 같은 끝 뼈)는 0
        public float T(int bone, Vector3 p)
        {
            Vector3 segment = tails[bone] - heads[bone];
            if (segment.sqrMagnitude < 1e-8f) { return 0f; }

            return Mathf.Clamp01(Vector3.Dot(p - heads[bone], segment) / segment.sqrMagnitude);
        }

        public Vector3 ClosestOnSegment(int bone, Vector3 p)
        {
            return PointAt(bone, T(bone, p));
        }

        private int TailOf(int bone, Dictionary<Transform, int> index)
        {
            Transform t = bones[bone];
            int first = -1;
            for (int c = 0; c < t.childCount; c++)
            {
                int child;
                if (!index.TryGetValue(t.GetChild(c), out child)) { continue; }
                if (first < 0) { first = child; }
                if (!IsSided(bones[child].name)) { return child; }
            }
            return first;
        }

        private static bool IsSided(string name)
        {
            for (int s = 0; s < Sides.Length; s++)
            {
                if (name.EndsWith(Sides[s])) { return true; }
            }
            return false;
        }
    }
}
