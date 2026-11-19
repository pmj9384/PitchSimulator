using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 경기 HUD용 흰색 UI 스프라이트를 만든다(11-19): 둥근 사각(9-slice)과 원 테두리(라디오). 색은 Image.color로 칠한다.
// 프로젝트에 둥근 사각 스프라이트가 없어서 만들었다. 1회용이어도 다시 만들 수 있게 남긴다(CLAUDE.md 관례).
// 같은 픽셀이면 파일을 다시 쓰지 않아 여러 번 돌려도 결과가 같다. MatchHudBuilder가 배치 전에 부른다
public static class UiSpriteGenerator
{
    public const string RoundedPath = "Assets/Sprites/UI/RoundedRect.png";
    public const string RingPath = "Assets/Sprites/UI/Ring.png";
    public const string CirclePath = "Assets/Sprites/Circle.png";   // 이미 있던 채운 원(256px)

    private const int RoundedSize = 64;
    private const int RingSize = 128;
    private const float RingStroke = 10f;   // 128px 기준. 40단위로 그리면 약 3단위 선

    [MenuItem("Tools/UI/경기 HUD 스프라이트 만들기")]
    public static void Generate()
    {
        Directory.CreateDirectory("Assets/Sprites/UI");
        // 반경 픽셀 = UiTokens.Radius. PPU 100(캔버스 기준 100)이라 9-slice 모서리가 캔버스에서 같은 단위로 그려진다
        int radius = Mathf.RoundToInt(UiTokens.Radius);
        Write(RoundedPath, RoundedRect(RoundedSize, radius), radius);
        Write(RingPath, Ring(RingSize, RingStroke), 0);
    }

    private static Texture2D RoundedRect(int size, int radius)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 가장 가까운 모서리 원 중심까지의 거리로 가장자리를 1px 부드럽게
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        return texture;
    }

    private static Texture2D Ring(int size, float stroke)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float outer = size * 0.5f;
        float inner = outer - stroke;
        var center = new Vector2(outer, outer);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float outside = Mathf.Clamp01(outer - distance + 0.5f);
                float hole = Mathf.Clamp01(distance - inner + 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Min(outside, hole)));
            }
        }
        texture.Apply();
        return texture;
    }

    private static void Write(string path, Texture2D texture, int border)
    {
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        bool same = File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(png);
        if (!same)
        {
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var wanted = new Vector4(border, border, border, border);
        if (HasSettings(importer, wanted)) { return; }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = wanted;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
    }

    private static bool HasSettings(TextureImporter importer, Vector4 border)
    {
        if (importer.textureType != TextureImporterType.Sprite) { return false; }
        if (importer.spriteImportMode != SpriteImportMode.Single) { return false; }
        if (importer.spriteBorder != border) { return false; }
        return !importer.mipmapEnabled;
    }
}
