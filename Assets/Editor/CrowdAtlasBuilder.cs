using System.IO;
using UnityEditor;
using UnityEngine;

// 관중 실루엣 아틀라스(11-06 경기장 4종 4단계, 설계 실험/2026-11-05-설계-경기장-4종.md §3).
// 4열(셔츠색) × 2행(자세: 0 = 손 내림, 1 = 두 팔 든 응원) = 8종을 거리 함수로 직접 그려 PNG 하나로 저장한다. 외부 에셋 없음.
// 난수를 쓰지 않아 다시 만들어도 같은 PNG다. 임포트 설정(ASTC·알파 투명·밉맵 커버리지 유지)과 관중 머티리얼도 여기서 맞춘다.
// 셀 안에서 v ≥ HeadV는 머리·손(피부·머리색), 그 아래는 몸통(셔츠색). Crowd.shader가 HeadV 아래를 홈 유니폼 색으로 바꾸므로 같은 값을 쓴다.
public static class CrowdAtlasBuilder
{
    public const string AtlasPath = "Assets/Textures/Crowd/CrowdAtlas.png";
    public const string MaterialPath = GrassWearBuilder.MaterialFolder + "/Crowd.mat";
    public const string ShaderName = "PitchSimulator/Crowd";
    public const int Columns = 4;
    public const int Rows = 2;
    // 아래 HeadV·CellPixels·ShirtShadeLow는 셰이더 기본값·CrowdPrefabTests와 같게 유지한다(머티리얼이 값을 들고 있어 어긋나도 동작은 안전하다)
    public const float HeadV = 0.7f;              // 셀 높이 중 머리가 시작하는 곳. Crowd.shader _HeadV 기본값과 같다
    private const int CellPixels = 128;           // [가정] 셀 한 변. 아틀라스 512×256, ASTC 6x6이면 약 0.05MB
    private const float ShirtShadeLow = 0.8f;     // Crowd.shader SHIRT_SHADE_LOW와 같다(홈 색 몸통과 아틀라스 몸통의 명암을 맞춘다)
    private const TextureImporterFormat AndroidFormat = TextureImporterFormat.ASTC_6x6;

    // [가정] 도형(셀 좌표 0~1, 가장자리 0.05는 비운다)
    private static readonly Vector2 HeadCenter = new Vector2(0.5f, 0.8f);
    private const float HeadRadius = 0.1f;
    private const float HairBottomV = 0.83f;      // 머리 원 안에서 이 위는 머리카락
    private static readonly Vector2 TorsoCenter = new Vector2(0.5f, 0.39f);
    private static readonly Vector2 TorsoHalf = new Vector2(0.19f, 0.31f);
    private const float TorsoCorner = 0.07f;
    private const float ArmRadius = 0.042f;
    private const float ArmDownTopV = 0.64f;      // 손 내린 자세: 어깨에서 허리까지
    private const float ArmDownBottomV = 0.25f;
    private const float ArmDownTopX = 0.22f;
    private const float ArmDownBottomX = 0.25f;
    private const float ArmUpShoulderV = 0.6f;    // 응원 자세: 어깨에서 위로 벌려 든다
    private const float ArmUpHandV = 0.9f;
    private const float ArmUpShoulderX = 0.2f;
    private const float ArmUpHandX = 0.31f;

    private static readonly Color[] Shirts =
    {
        new Color(0.9f, 0.88f, 0.8f),     // 크림
        new Color(0.82f, 0.68f, 0.2f),    // 머스터드
        new Color(0.7f, 0.2f, 0.22f),     // 붉은색
        new Color(0.2f, 0.5f, 0.5f),      // 청록
    };

    private static readonly Color[] Skins =
    {
        new Color(0.96f, 0.8f, 0.68f),
        new Color(0.85f, 0.64f, 0.48f),
        new Color(0.6f, 0.4f, 0.26f),
        new Color(0.9f, 0.72f, 0.58f),
    };

    private static readonly Color[] Hairs =
    {
        new Color(0.08f, 0.07f, 0.07f),
        new Color(0.3f, 0.18f, 0.1f),
        new Color(0.85f, 0.7f, 0.4f),
        new Color(0.15f, 0.12f, 0.1f),
    };

    [MenuItem("Tools/Pitch/관중 아틀라스 만들기")]
    public static void BuildMenu()
    {
        Build();
    }

    // PNG → 임포트 설정 → 머티리얼. 몇 번 불러도 같은 결과다
    public static void Build()
    {
        WriteAtlas();
        ImportAtlas();
        BuildMaterial();
        AssetDatabase.SaveAssets();
    }

    private static void WriteAtlas()
    {
        int width = Columns * CellPixels;
        int height = Rows * CellPixels;
        var colors = new Color32[width * height];
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                DrawCell(colors, width, column, row);
            }
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(colors);
        Directory.CreateDirectory(Path.GetDirectoryName(AtlasPath));
        File.WriteAllBytes(AtlasPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
    }

    // 셀 하나. 행 0 = 손 내림, 행 1 = 응원. 픽셀 원점은 왼쪽 아래
    private static void DrawCell(Color32[] colors, int width, int column, int pose)
    {
        for (int y = 0; y < CellPixels; y++)
        {
            for (int x = 0; x < CellPixels; x++)
            {
                var p = new Vector2((x + 0.5f) / CellPixels, (y + 0.5f) / CellPixels);
                Color pixel = Shade(p, column, pose);
                colors[(pose * CellPixels + y) * width + column * CellPixels + x] = pixel;
            }
        }
    }

    // 도형 합집합의 알파(경계 한 픽셀 안티앨리어싱)와 색. 모양은 머리·몸통·팔의 합집합이고, 색은 머리 → 든 손 → 몸통 순으로 고른다
    private static Color Shade(Vector2 p, int column, int pose)
    {
        float head = Length(p - HeadCenter) - HeadRadius;
        float torso = RoundBox(p - TorsoCenter, TorsoHalf, TorsoCorner);
        float armLeft = pose == 0 ? ArmDown(p, -1f) : ArmUp(p, -1f);
        float armRight = pose == 0 ? ArmDown(p, 1f) : ArmUp(p, 1f);
        float body = Mathf.Min(torso, Mathf.Min(armLeft, armRight));
        float alpha = Coverage(Mathf.Min(head, body));
        if (alpha <= 0f) { return new Color(0f, 0f, 0f, 0f); }

        Color color;
        if (head <= 0f)
        {
            color = p.y >= HairBottomV ? Hairs[column] : Skins[column];
        }
        else if (p.y >= HeadV)
        {
            color = Skins[column];   // 든 손
        }
        else
        {
            color = Shirts[column] * Mathf.Lerp(ShirtShadeLow, 1f, p.y / HeadV);
        }
        color.a = alpha;
        return color;
    }

    private static float ArmDown(Vector2 p, float side)
    {
        return Capsule(p, new Vector2(0.5f + side * ArmDownTopX, ArmDownTopV), new Vector2(0.5f + side * ArmDownBottomX, ArmDownBottomV), ArmRadius);
    }

    private static float ArmUp(Vector2 p, float side)
    {
        return Capsule(p, new Vector2(0.5f + side * ArmUpShoulderX, ArmUpShoulderV), new Vector2(0.5f + side * ArmUpHandX, ArmUpHandV), ArmRadius);
    }

    private static float Coverage(float distance)
    {
        return Mathf.Clamp01(0.5f - distance * CellPixels);
    }

    private static float Length(Vector2 v)
    {
        return Mathf.Sqrt(v.x * v.x + v.y * v.y);
    }

    private static float RoundBox(Vector2 p, Vector2 half, float corner)
    {
        var q = new Vector2(Mathf.Abs(p.x) - half.x + corner, Mathf.Abs(p.y) - half.y + corner);
        var outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return Length(outside) + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - corner;
    }

    private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return Length(pa - ba * h) - radius;
    }

    // 모바일: ASTC 6x6, 알파 투명(경계 색 번짐 방지), 밉맵은 알파 문턱 기준으로 면적 유지(멀리서 실루엣이 안 마른다)
    private static void ImportAtlas()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.5f;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = Columns * CellPixels;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.streamingMipmaps = false;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Android",
            overridden = true,
            maxTextureSize = Columns * CellPixels,
            format = AndroidFormat,
            textureCompression = TextureImporterCompression.Compressed,
        });
        importer.SaveAndReimport();
    }

    private static void BuildMaterial()
    {
        Directory.CreateDirectory(GrassWearBuilder.MaterialFolder);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find(ShaderName));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = Shader.Find(ShaderName);
        material.SetTexture("_Atlas", AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));
        material.SetFloat("_HeadV", HeadV);
        material.enableInstancing = true;   // RenderMeshInstanced 요건. 스탠드 재질과 달리 켠다
        EditorUtility.SetDirty(material);
    }
}
