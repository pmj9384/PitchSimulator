using System.Collections.Generic;
using Game.Core.Placement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 저장된 공통 피치 프리팹이 PitchLayout과 같은지(11-05). 프리팹을 손으로 고치면 시뮬 판정과 화면이 어긋나므로 여기서 잡는다.
// 어긋나면 Tools/Pitch/공통 피치 프리팹 만들기로 다시 만든다. UnityEditor를 쓰므로 Unity EditMode에서만 돈다(dotnet 러너 제외)
public class PitchPrefabTests
{
    private const string PrefabPath = "Assets/Prefabs/Pitch/CommonPitch.prefab";
    private const float Eps = 1e-3f;

    private static Transform Child(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        Assert.IsNotNull(t, $"{parent.name}/{name} 없음");
        return t;
    }

    private static GameObject LoadPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, $"{PrefabPath} 없음");
        return prefab;
    }

    [Test]
    public void 선마다_자리_길이_방향이_PitchLayout과_같다()
    {
        Transform linesRoot = Child(LoadPrefab().transform, "Lines");
        List<PitchLine> lines = PitchLayout.Lines();
        Assert.AreEqual(lines.Count, linesRoot.childCount, "선 수");
        for (int i = 0; i < lines.Count; i++)
        {
            PitchLine line = lines[i];
            Transform t = Child(linesRoot, $"Line{i:00}");
            Assert.AreEqual((line.FromX + line.ToX) * 0.5f, t.localPosition.x, Eps, $"Line{i:00} x");
            Assert.AreEqual((line.FromZ + line.ToZ) * 0.5f, t.localPosition.z, Eps, $"Line{i:00} z");
            Assert.AreEqual(line.Length + FieldBounds.LineWidth, t.localScale.x, Eps, $"Line{i:00} 길이");
            Assert.AreEqual(FieldBounds.LineWidth, t.localScale.z, Eps, $"Line{i:00} 폭");
            Vector3 dir = t.localRotation * Vector3.right;   // 선의 길이 방향
            float cross = dir.x * (line.ToZ - line.FromZ) - dir.z * (line.ToX - line.FromX);
            Assert.AreEqual(0f, cross / line.Length, Eps, $"Line{i:00} 방향");
        }
    }

    [Test]
    public void 골대_포스트와_크로스바와_마크가_규격_자리다()
    {
        Transform root = LoadPrefab().transform;
        Transform goals = Child(root, "Goals");
        List<(float x, float z)> posts = PitchLayout.GoalPosts();
        for (int i = 0; i < posts.Count; i++)
        {
            Transform post = Child(goals, $"Post{i}");
            Assert.AreEqual(posts[i].x, post.localPosition.x, Eps, $"Post{i} x");
            Assert.AreEqual(posts[i].z, post.localPosition.z, Eps, $"Post{i} z");
            Assert.AreEqual(FieldBounds.GoalHeight, post.localScale.y * 2f, Eps, $"Post{i} 높이(원기둥 높이 2)");
        }
        for (int side = 0; side < 2; side++)
        {
            Transform bar = Child(goals, $"Crossbar{side}");
            Assert.AreEqual(FieldBounds.GoalHeight + FieldBounds.GoalPostDiameter * 0.5f, bar.localPosition.y, Eps, $"Crossbar{side} 높이");
            Assert.AreEqual(2f * (FieldBounds.GoalHalfWidth + FieldBounds.GoalPostDiameter), bar.localScale.y * 2f, Eps, $"Crossbar{side} 길이(포스트 바깥 면까지)");
        }

        Transform spotsRoot = Child(root, "Spots");
        List<(float x, float z)> spots = PitchLayout.Spots();
        for (int i = 0; i < spots.Count; i++)
        {
            Transform spot = Child(spotsRoot, $"Spot{i}");
            Assert.AreEqual(spots[i].x, spot.localPosition.x, Eps, $"Spot{i} x");
            Assert.AreEqual(spots[i].z, spot.localPosition.z, Eps, $"Spot{i} z");
        }
    }
}
