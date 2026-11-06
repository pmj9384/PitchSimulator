using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.Placement;
using UnityEditor;
using UnityEngine;

// 잔디 마모 에셋(11-06 경기장 4종 3단계, 설계 실험/2026-11-05-설계-경기장-4종.md §4).
// ① 마모 마스크 1장을 FieldBounds 상수로 그린다(센터서클·페널티 박스·골 에어리어·페널티 마크가 진하고 노이즈가 섞인다).
// ② 텍스처 임포트 설정(ASTC·1K 이하) ③ 부별 머티리얼 4개(같은 셰이더, _Wear·_Stripe·_PlaneSize만 다름, 값은 Stadiums.csv). StadiumPrefabBuilder가 먼저 부른다.
// 손 숫자 금지: 규격은 FieldBounds, _Wear·_Stripe·런오프는 Stadiums.csv. 이 파일의 [가정] 값은 눈으로 보고 조정할 자리다.
public static class GrassWearBuilder
{
    public const string MaterialFolder = "Assets/Materials/Stadium";   // 경기장 재질 폴더. StadiumPrefabBuilder도 쓴다
    public const string ShaderName = "PitchSimulator/GrassWear";
    private const string TextureFolder = "Assets/Textures/Grass";
    private const string MaskPath = TextureFolder + "/GrassWearMask.png";
    private const string GoodPath = TextureFolder + "/Grass001_Color_1K.jpg";
    private const string DryPath = TextureFolder + "/Ground037_Color_1K.jpg";
    private const string MaterialPrefix = "Grass_t";
    private const float UnityPlaneSize = 10f;
    private const int MaxTextureSize = 1024;          // 모바일: 1K 이하
    private const TextureImporterFormat AndroidFormat = TextureImporterFormat.ASTC_6x6;

    private const int MaskPixels = 512;               // [가정] 마스크 한 변(px). 117×80m를 덮어 한 칸 약 0.2m
    private const float NoiseCellMeters = 3f;         // [가정] 가장 큰 노이즈 덩어리 크기(m)
    private const int NoiseOctaves = 3;               // [가정]
    private const float CenterCoreScale = 0.6f;       // [가정] 센터서클 반지름의 이 비율 안쪽이 최대치
    private const float CenterFadeScale = 1.3f;       // [가정] 이 비율에서 0으로 떨어진다
    private const float CenterWeight = 0.85f;         // [가정]
    private const float BoxWeight = 0.55f;            // [가정] 페널티 박스 안
    private const float BoxFadeMeters = 5f;           // [가정] 박스 밖으로 번지는 거리
    private const float GoalAreaWeight = 1f;          // [가정] 골 에어리어(골문 앞 최대 마모)
    private const float SpotWeight = 1f;              // [가정] 페널티 마크 둘레
    private const float GoalAreaFadeMeters = 3f;      // [가정]
    private const float SpotRadius = 2.5f;            // [가정] 페널티 마크 둘레 마모 반지름(m)
    private const float SpotFadeMeters = 1.5f;        // [가정]
    private const float FieldBaseWeight = 0.25f;      // [가정] 터치라인 안 전체의 바닥 마모
    private const float NoiseBase = 0.6f;             // [가정] 노이즈 곱의 최저
    private const float NoiseRange = 0.8f;            // [가정] 노이즈 곱의 폭(최저 + 폭 = 1.4)
    private const float RunoffWeight = 0.08f;         // [가정] 터치라인 밖 런오프 바닥 마모(거의 없음)

    private const float TileMeters = 4f;              // [가정] 텍스처 한 장이 덮는 길이(m)
    private const float StripeWidth = 5f;             // [가정] 줄무늬 한 줄 폭(m)
    private static readonly Color GoodTint = new Color(1f, 1f, 1f, 1f);     // [가정]
    private static readonly Color DryTint = new Color(1.3f, 1.1f, 0.7f, 1f); // [가정] 이끼 낀 땅을 마른 누런 쪽으로

    [MenuItem("Tools/Pitch/잔디 마모 에셋 만들기")]
    public static void BuildMenu()
    {
        Build(StadiumSpecParser.Parse(File.ReadAllText("Assets/Resources/Tables/Stadiums.csv")));
    }

    public static string MaterialPathOf(StadiumSpec spec)
    {
        return $"{MaterialFolder}/{MaterialPrefix}{spec.Tier}.mat";
    }

    // 마스크 → 임포트 설정 → 머티리얼. 몇 번 불러도 같은 결과다
    public static void Build(List<StadiumSpec> specs)
    {
        Vector2 maskSize = MaskSizeMeters(specs);
        WriteMask(maskSize);
        ImportColor(GoodPath);
        ImportColor(DryPath);
        ImportMask(MaskPath);
        Directory.CreateDirectory(MaterialFolder);
        for (int i = 0; i < specs.Count; i++)
        {
            BuildMaterial(specs[i], maskSize);
        }
        AssetDatabase.SaveAssets();
    }

    private static float MaxRunoff(List<StadiumSpec> specs)
    {
        float max = 0f;
        for (int i = 0; i < specs.Count; i++)
        {
            max = Mathf.Max(max, specs[i].Runoff);
        }
        return max;
    }

    // 런오프까지 포함한 잔디 평면 크기(m). 마스크는 가장 큰 런오프(1부) 평면 하나를 덮고, 부별 평면은 셰이더가 _PlaneSize·_MaskSize로 맞춘다
    public static Vector2 PlaneSizeMeters(float runoff)
    {
        return new Vector2(FieldBounds.HalfLength * 2f + runoff * 2f, FieldBounds.HalfWidth * 2f + runoff * 2f);
    }

    public static Vector2 MaskSizeMeters(List<StadiumSpec> specs)
    {
        return PlaneSizeMeters(MaxRunoff(specs));
    }

    private static void WriteMask(Vector2 maskSize)
    {
        var texture = new Texture2D(MaskPixels, MaskPixels, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[MaskPixels * MaskPixels];
        for (int j = 0; j < MaskPixels; j++)
        {
            float z = ((j + 0.5f) / MaskPixels - 0.5f) * maskSize.y;
            for (int i = 0; i < MaskPixels; i++)
            {
                float x = ((i + 0.5f) / MaskPixels - 0.5f) * maskSize.x;
                byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(WearAt(x, z)) * 255f);
                pixels[j * MaskPixels + i] = new Color32(v, v, v, 255);
            }
        }
        texture.SetPixels32(pixels);
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        Directory.CreateDirectory(TextureFolder);
        if (File.Exists(MaskPath) && BytesEqual(File.ReadAllBytes(MaskPath), png)) { return; }

        File.WriteAllBytes(MaskPath, png);
        AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) { return false; }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) { return false; }
        }
        return true;
    }

    // 경기장 좌표(x = 진영 축, z = 폭) 한 점의 마모 0~1. 노이즈 전 값(Features) × 노이즈
    private static float WearAt(float x, float z)
    {
        float features = Features(x, z);
        float noise = Fbm(x / NoiseCellMeters, z / NoiseCellMeters);
        return features * (NoiseBase + NoiseRange * noise);
    }

    private static float Features(float x, float z)
    {
        float ax = Mathf.Abs(x);
        float az = Mathf.Abs(z);
        bool inside = ax <= FieldBounds.HalfLength && az <= FieldBounds.HalfWidth;
        float value = inside ? FieldBaseWeight : RunoffWeight;

        float centerRadius = Mathf.Sqrt(x * x + z * z);
        float center = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FieldBounds.CenterCircleRadius * CenterCoreScale, FieldBounds.CenterCircleRadius * CenterFadeScale, centerRadius));
        value = Mathf.Max(value, center * CenterWeight);

        float box = FadeOutsideRect(ax, az, FieldBounds.HalfLength - FieldBounds.PenaltyBoxDepth, FieldBounds.HalfLength, FieldBounds.PenaltyBoxHalfWidth, BoxFadeMeters);
        value = Mathf.Max(value, box * BoxWeight);

        float goalArea = FadeOutsideRect(ax, az, FieldBounds.HalfLength - FieldBounds.GoalAreaDepth, FieldBounds.HalfLength, FieldBounds.GoalAreaHalfWidth, GoalAreaFadeMeters);
        value = Mathf.Max(value, goalArea * GoalAreaWeight);

        float spotDx = ax - (FieldBounds.HalfLength - FieldBounds.PenaltySpotDistance);
        float spotDistance = Mathf.Sqrt(spotDx * spotDx + az * az);
        float spot = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SpotRadius, SpotRadius + SpotFadeMeters, spotDistance));
        value = Mathf.Max(value, spot * SpotWeight);
        return value;
    }

    // 대칭 사분면 좌표(ax, az ≥ 0)에서 직사각형(x0~x1, |z| ≤ halfZ) 안은 1, 밖으로 fade 미터에 걸쳐 0
    private static float FadeOutsideRect(float ax, float az, float x0, float x1, float halfZ, float fade)
    {
        float dx = Mathf.Max(x0 - ax, ax - x1, 0f);
        float dz = Mathf.Max(az - halfZ, 0f);
        float distance = Mathf.Sqrt(dx * dx + dz * dz);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / fade));
    }

    // 값 노이즈 여러 겹. 시드 고정 해시라 다시 만들어도 같은 마스크가 나온다
    private static float Fbm(float x, float z)
    {
        float sum = 0f;
        float amplitude = 0.5f;
        float total = 0f;
        for (int o = 0; o < NoiseOctaves; o++)
        {
            sum += ValueNoise(x, z) * amplitude;
            total += amplitude;
            x *= 2f;
            z *= 2f;
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    private static float ValueNoise(float x, float z)
    {
        int xi = Mathf.FloorToInt(x);
        int zi = Mathf.FloorToInt(z);
        float tx = Mathf.SmoothStep(0f, 1f, x - xi);
        float tz = Mathf.SmoothStep(0f, 1f, z - zi);
        float a = Hash(xi, zi);
        float b = Hash(xi + 1, zi);
        float c = Hash(xi, zi + 1);
        float d = Hash(xi + 1, zi + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    private static float Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static void ImportColor(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            Debug.LogError($"[GrassWearBuilder] {path} 없음");
            return;
        }
        ApplyImport(importer, true, TextureWrapMode.Repeat, 4);
    }

    private static void ImportMask(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) { return; }

        ApplyImport(importer, false, TextureWrapMode.Clamp, 1);   // 마스크는 위에서 비스듬히 볼 일이 텍스처만큼 중요하지 않아 이방성을 끈다
    }

    // 모바일: 1K 이하, 밉맵, ASTC 6x6(Android). 값이 이미 같으면 다시 임포트하지 않는다
    private static void ApplyImport(TextureImporter importer, bool srgb, TextureWrapMode wrap, int aniso)
    {
        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        if (IsImportedAs(importer, android, srgb, wrap, aniso)) { return; }

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.mipmapEnabled = true;
        importer.wrapMode = wrap;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = aniso;
        importer.maxTextureSize = MaxTextureSize;
        android.name = "Android";
        android.overridden = true;
        android.maxTextureSize = MaxTextureSize;
        android.format = AndroidFormat;
        importer.SetPlatformTextureSettings(android);
        importer.SaveAndReimport();
    }

    private static bool IsImportedAs(TextureImporter importer, TextureImporterPlatformSettings android, bool srgb, TextureWrapMode wrap, int aniso)
    {
        if (importer.textureType != TextureImporterType.Default) { return false; }
        if (importer.filterMode != FilterMode.Bilinear) { return false; }
        if (importer.sRGBTexture != srgb) { return false; }
        if (!importer.mipmapEnabled) { return false; }
        if (importer.wrapMode != wrap) { return false; }
        if (importer.anisoLevel != aniso) { return false; }
        if (importer.maxTextureSize > MaxTextureSize) { return false; }
        if (!android.overridden) { return false; }
        return android.format == AndroidFormat;
    }

    private static void BuildMaterial(StadiumSpec spec, Vector2 maskSize)
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[GrassWearBuilder] 셰이더 {ShaderName} 없음");
            return;
        }

        string path = MaterialPathOf(spec);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        Vector2 plane = PlaneSizeMeters(spec.Runoff);
        material.SetTexture("_GoodTex", AssetDatabase.LoadAssetAtPath<Texture2D>(GoodPath));
        material.SetTexture("_DryTex", AssetDatabase.LoadAssetAtPath<Texture2D>(DryPath));
        material.SetTexture("_WearMask", AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath));
        material.SetColor("_GoodTint", GoodTint);
        material.SetColor("_DryTint", DryTint);
        material.SetFloat("_Wear", spec.Wear);
        material.SetVector("_PlaneSize", new Vector4(plane.x, plane.y, 0f, 0f));
        material.SetVector("_MaskSize", new Vector4(maskSize.x, maskSize.y, 0f, 0f));
        material.SetFloat("_TileMeters", TileMeters);
        material.SetFloat("_StripeWidth", StripeWidth);
        material.SetFloat("_Stripe", spec.Stripe);
        material.enableInstancing = false;   // SRP Batcher와 겹치지 않게(스탠드 재질과 같은 이유)
        EditorUtility.SetDirty(material);
    }

    // 평면 크기 계산을 StadiumPrefabBuilder와 공유한다(유니티 기본 Plane은 10m)
    public static Vector3 PlaneScale(StadiumSpec spec)
    {
        Vector2 plane = PlaneSizeMeters(spec.Runoff);
        return new Vector3(plane.x / UnityPlaneSize, 1f, plane.y / UnityPlaneSize);
    }
}
