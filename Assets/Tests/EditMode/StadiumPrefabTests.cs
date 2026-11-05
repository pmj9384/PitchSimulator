using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.Placement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 저장된 경기장 프리팹 4종이 Stadiums.csv·StadiumLayout과 같은지(11-06 경기장 4종 2단계). 어긋나면 Tools/Pitch/경기장 프리팹 만들기로 다시 만든다.
// UnityEditor를 쓰므로 Unity EditMode에서만 돈다(dotnet 러너 제외)
public class StadiumPrefabTests
{
    private const float Eps = 1e-3f;
    private const string CsvPath = "Assets/Resources/Tables/Stadiums.csv";
    private const string CommonPitchPath = "Assets/Prefabs/Pitch/CommonPitch.prefab";
    private const string StandsName = "Stands";
    private const string MaterialFolder = "Assets/Materials/Stadium/";
    private const float UnityPlaneSize = 10f;

    private static List<StadiumSpec> Specs()
    {
        return StadiumSpecParser.Parse(File.ReadAllText(CsvPath));
    }

    private static GameObject Load(StadiumSpec spec)
    {
        string path = $"Assets/Resources/Stadiums/{spec.Id}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, $"{path} 없음");
        return prefab;
    }

    [Test]
    public void 단_큐브_수와_자리와_크기가_StadiumLayout과_같다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform stands = Load(spec).transform.Find(StandsName);
            Assert.IsNotNull(stands, $"{spec.Id} Stands 없음");
            List<StandRow> rows = StadiumLayout.Rows(spec);
            int cubes = 0;
            foreach (Transform t in stands.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Stand_")) { cubes++; }
            }
            Assert.AreEqual(rows.Count, cubes, $"{spec.Id} 단 수");
            for (int i = 0; i < rows.Count; i++)
            {
                StandRow row = rows[i];
                Transform t = stands.Find($"{row.Side}/Stand_{row.Side}_{row.Index:00}");
                Assert.IsNotNull(t, $"{spec.Id} {row.Side} {row.Index}단 없음");
                Assert.AreEqual((row.FromX + row.ToX) * 0.5f, t.localPosition.x, Eps, $"{spec.Id} {row.Side}{row.Index} x");
                Assert.AreEqual((row.FromZ + row.ToZ) * 0.5f, t.localPosition.z, Eps, $"{spec.Id} {row.Side}{row.Index} z");
                Assert.AreEqual(row.Height, t.localScale.y, Eps, $"{spec.Id} {row.Side}{row.Index} 높이");
                Assert.AreEqual(row.Height * 0.5f, t.localPosition.y, Eps, $"{spec.Id} {row.Side}{row.Index} 바닥이 지면");
                float length = Mathf.Abs(row.ToX - row.FromX) + Mathf.Abs(row.ToZ - row.FromZ);
                float along = StadiumLayout.IsAlongX(row.Side) ? t.localScale.x : t.localScale.z;
                float depth = StadiumLayout.IsAlongX(row.Side) ? t.localScale.z : t.localScale.x;
                Assert.AreEqual(length, along, Eps, $"{spec.Id} {row.Side}{row.Index} 길이");
                Assert.AreEqual(spec.RowDepth, depth, Eps, $"{spec.Id} {row.Side}{row.Index} 깊이");
            }
        }
    }

    [Test]
    public void CommonPitch가_중첩_프리팹으로_들어_있다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform pitch = Load(spec).transform.Find("CommonPitch");
            Assert.IsNotNull(pitch, $"{spec.Id} CommonPitch 없음");
            Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(pitch.gameObject), $"{spec.Id} 중첩 프리팹이 아니다");
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(pitch.gameObject);
            Assert.AreEqual(CommonPitchPath, AssetDatabase.GetAssetPath(source), $"{spec.Id} 원본 경로");
        }
    }

    [Test]
    public void 지붕_수와_조명탑_전광판_광고판_수가_스펙과_같다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform root = Load(spec).transform;
            Assert.AreEqual(spec.Roof.Count, CountPrefix(root, "Roofs", "Roof_"), $"{spec.Id} 지붕");
            Assert.AreEqual(spec.LightTowers, CountPrefix(root, "Towers", "Tower"), $"{spec.Id} 조명탑");
            Assert.AreEqual(spec.Scoreboards, CountPrefix(root, "Scoreboards", "Scoreboard"), $"{spec.Id} 전광판");
            Assert.AreEqual(spec.AdBoards.Count, CountPrefix(root, "AdBoards", "AdBoard_"), $"{spec.Id} 광고판");
            Assert.AreEqual(2, CountPrefix(root, "Benches", "Bench"), $"{spec.Id} 벤치");
        }
    }

    [Test]
    public void 콜라이더가_없고_그림자를_드리우지_않는다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            GameObject prefab = Load(spec);
            Assert.AreEqual(0, prefab.GetComponentsInChildren<Collider>(true).Length, $"{spec.Id} 콜라이더");
            Transform stands = prefab.transform.Find(StandsName);
            foreach (MeshRenderer r in stands.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{spec.Id} {r.name} 그림자 드리움");
                Assert.IsFalse(r.receiveShadows, $"{spec.Id} {r.name} 그림자 받음");
                Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(r.gameObject), $"{spec.Id} {r.name} 정적 배칭 안 씀");
            }
            Transform roofs = prefab.transform.Find("Roofs");
            if (roofs == null) { continue; }
            foreach (MeshRenderer r in roofs.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{spec.Id} {r.name} 그림자 드리움");
            }
        }
    }

    [Test]
    public void 잔디_평면이_선_밖_런오프까지_덮는다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform grass = Load(spec).transform.Find("Grass");
            Assert.IsNotNull(grass, $"{spec.Id} Grass 없음");
            float width = FieldBounds.HalfLength * 2f + spec.Runoff * 2f;
            float depth = FieldBounds.HalfWidth * 2f + spec.Runoff * 2f;
            Assert.AreEqual(width / UnityPlaneSize, grass.localScale.x, Eps, $"{spec.Id} 잔디 길이");
            Assert.AreEqual(depth / UnityPlaneSize, grass.localScale.z, Eps, $"{spec.Id} 잔디 폭");
        }
    }

    // 잔디·CommonPitch 밖의 렌더러는 경기장 재질 2개만 쓰고, 그 재질은 GPU Instancing을 끈다(SRP Batcher와 겹치지 않게)
    [Test]
    public void 경기장_구조물은_재질_2개만_쓰고_인스턴싱을_끈다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform root = Load(spec).transform;
            var materials = new HashSet<Material>();
            foreach (Transform child in root)
            {
                if (child.name == "Grass") { continue; }
                if (child.name == "CommonPitch") { continue; }

                foreach (MeshRenderer r in child.GetComponentsInChildren<MeshRenderer>(true))
                {
                    materials.Add(r.sharedMaterial);
                }
            }
            Assert.AreEqual(2, materials.Count, $"{spec.Id} 재질 수");
            foreach (Material m in materials)
            {
                StringAssert.StartsWith(MaterialFolder, AssetDatabase.GetAssetPath(m), $"{spec.Id} {m.name} 경로");
                Assert.IsFalse(m.enableInstancing, $"{spec.Id} {m.name} 인스턴싱");
            }
        }
    }

    private static int CountPrefix(Transform root, string groupName, string prefix)
    {
        Transform group = root.Find(groupName);
        if (group == null) { return 0; }

        int count = 0;
        foreach (Transform child in group)
        {
            if (child.name.StartsWith(prefix)) { count++; }
        }
        return count;
    }
}
