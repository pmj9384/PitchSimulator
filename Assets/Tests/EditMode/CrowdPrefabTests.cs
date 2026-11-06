using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Placement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 관중 에셋·컴포넌트(11-06 경기장 4종 4단계): 아틀라스 PNG 존재·임포트 설정·머티리얼 설정, 프리팹의 CrowdRenderer, Init이 만든 인스턴스·호출 수.
// 어긋나면 Tools/Pitch/경기장 프리팹 만들기로 다시 만든다. UnityEditor를 쓰므로 Unity EditMode에서만 돈다(dotnet 러너 제외)
public class CrowdPrefabTests
{
    // 리플렉션을 쓰는 이유: 테스트 asmdef가 Assembly-CSharp를 못 봐서 CrowdRenderer 타입을 직접 못 쓴다. 근본 해법은 뷰 코드를 별도 asmdef로 분리하는 것(장부)
    // 테스트 어셈블리는 Assembly-CSharp(CrowdRenderer)·Editor 어셈블리(CrowdAtlasBuilder)를 못 본다. 경로·값은 StadiumPrefabTests처럼 글자로, 컴포넌트는 이름으로 찾는다
    private const string CsvPath = "Assets/Resources/Tables/Stadiums.csv";
    private const string AtlasPath = "Assets/Textures/Crowd/CrowdAtlas.png";
    private const string MaterialPath = "Assets/Materials/Stadium/Crowd.mat";
    private const string ShaderName = "PitchSimulator/Crowd";
    private const int CellPixels = 128;
    private const float HeadV = 0.7f;   // HeadV와 같은 값

    private static List<StadiumSpec> Specs()
    {
        return StadiumSpecParser.Parse(File.ReadAllText(CsvPath));
    }

    private static object Call(Component target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method);
        Assert.IsNotNull(info, $"CrowdRenderer.{method} 없음");
        return info.Invoke(target, args);
    }

    private static int Get(Component target, string property)
    {
        PropertyInfo info = target.GetType().GetProperty(property);
        Assert.IsNotNull(info, $"CrowdRenderer.{property} 없음");
        return (int)info.GetValue(target);
    }

    [Test]
    public void 아틀라스_PNG가_있고_모바일_임포트_설정이다()
    {
        Assert.IsTrue(File.Exists(AtlasPath), "아틀라스 PNG 없음. Tools/Pitch/관중 아틀라스 만들기");
        var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        Assert.IsTrue(importer.alphaIsTransparency, "알파 투명");
        Assert.IsFalse(importer.isReadable, "읽기 가능은 메모리 두 배");
        Assert.IsTrue(importer.mipmapEnabled, "멀리서 반짝임 방지");
        Assert.IsTrue(importer.mipMapsPreserveCoverage, "밉맵에서 실루엣 면적 유지");
        Assert.LessOrEqual(importer.maxTextureSize, 512, "작게 256~512px");
        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        Assert.IsTrue(android.overridden, "Android 설정을 따로 둔다");
        Assert.AreEqual(TextureImporterFormat.ASTC_6x6, android.format, "Android ASTC");
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        Assert.AreEqual(4 * CellPixels, texture.width, "4열");
        Assert.AreEqual(2 * CellPixels, texture.height, "2행");
    }

    [Test]
    public void 관중_머티리얼은_인스턴싱을_켜고_아틀라스를_쓴다()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(material, "머티리얼 없음");
        Assert.AreEqual(ShaderName, material.shader.name, "셰이더");
        Assert.IsTrue(material.enableInstancing, "RenderMeshInstanced 요건");
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath), material.GetTexture("_Atlas"), "아틀라스 연결");
        Assert.AreEqual(HeadV, material.GetFloat("_HeadV"), 1e-4f, "아틀라스 머리 높이와 셰이더가 같은 값");
    }

    [Test]
    public void 경기장_프리팹마다_CrowdRenderer가_머티리얼과_함께_붙어_있다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Stadiums/{spec.Id}.prefab");
            Assert.IsNotNull(prefab, $"{spec.Id} 프리팹 없음");
            Component crowd = prefab.GetComponent("CrowdRenderer");
            Assert.IsNotNull(crowd, $"{spec.Id} CrowdRenderer 없음");
            var serialized = new SerializedObject(crowd);
            Assert.IsNotNull(serialized.FindProperty("sourceMaterial").objectReferenceValue, $"{spec.Id} 머티리얼 연결");
        }
    }

    [Test]
    public void Init이_면별_행렬을_채운_좌석_수만큼_만든다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            var prefab = Resources.Load<GameObject>("Stadiums/" + spec.Id);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                Component crowd = instance.GetComponent("CrowdRenderer");
                Assert.IsNotNull(crowd, $"{spec.Id} CrowdRenderer 없음");
                uint seed = StadiumRules.FillSeed(7, spec.Tier, 3);
                Call(crowd, "Init", spec, seed, Color.blue);
                List<SeatSlot> filled = CrowdLayout.FilledSeats(spec, seed);
                Assert.AreEqual(filled.Count, Get(crowd, "TotalInstances"), $"{spec.Id} 관중 수");
                Assert.AreEqual(spec.Stands.Count, Get(crowd, "SideCount"), $"{spec.Id} 면 수");
                int calls = 0;
                for (int s = 0; s < Get(crowd, "SideCount"); s++)
                {
                    int onSide = 0;
                    foreach (SeatSlot seat in filled)
                    {
                        if (seat.Side == spec.Stands[s]) { onSide++; }
                    }
                    Assert.AreEqual(onSide, (int)Call(crowd, "InstancesOf", s), $"{spec.Id} {spec.Stands[s]} 면 행렬 수");
                    calls += CrowdLayout.CallCount(onSide);
                }
                Assert.AreEqual(calls, Get(crowd, "DrawCalls"), $"{spec.Id} 호출 수");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
