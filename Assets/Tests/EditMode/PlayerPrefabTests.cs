using Game.Core.Match;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 선수 프리팹·구운 몸 메시가 PlayerPrefabBuilder·PlayerMeshBaker 결과와 같은지(11-14 모델 교체 1단계). 어긋나면 Tools/Pitch/선수 몸 메시 굽기 → 선수 프리팹 배선(UBC)을 다시 돌린다.
// PlayerView는 Assembly-CSharp라 이 테스트 asmdef가 타입을 못 본다. SerializedObject로 직렬화 필드를 읽는다. UnityEditor를 쓰므로 Unity EditMode에서만 돈다
public class PlayerPrefabTests
{
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string ModelPath = "Assets/Models/UBC/Superhero_Male_FullBody.fbx";
    private const string BodyRendererName = "SuperHero_Male";
    private const string BakedFolder = "Assets/Models/UBC/Baked";
    private const int SubmeshCount = 5;
    private const float MaxSnap = 0.03f;   // PlayerMeshBaker.MaxSnap과 같은 값
    private const float MaxMorph = 0.05f;  // 0단계 체형 시험의 최대 이동 4.5cm + 경계 정리 여유

    // PlayerView.SlotKind 값(Keep 0·Skin 1·Hair 2·Kit 3·Boots 4). 몸 서브메시 순서는 피부·상의·하의·양말·축구화
    private static readonly int[] BodyKinds = { 1, 3, 3, 3, 4 };

    private static GameObject LoadPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, $"{PrefabPath} 없음");
        return prefab;
    }

    private static SerializedObject ViewOf(GameObject prefab)
    {
        Component view = prefab.GetComponent("PlayerView");
        Assert.IsNotNull(view, "PlayerView 없음");
        return new SerializedObject(view);
    }

    [Test]
    public void 머리_칸_수가_PortraitRules와_같고_칸마다_렌더러가_비어_있지_않다()
    {
        SerializedProperty heads = ViewOf(LoadPrefab()).FindProperty("heads");
        Assert.AreEqual(PortraitRules.HeadCount, heads.arraySize, "머리 칸 수");
        for (int h = 0; h < heads.arraySize; h++)
        {
            SerializedProperty renderers = heads.GetArrayElementAtIndex(h).FindPropertyRelative("renderers");
            Assert.Greater(renderers.arraySize, 0, $"머리 칸 {h} 비어 있음");
            for (int r = 0; r < renderers.arraySize; r++)
            {
                Assert.IsNotNull(renderers.GetArrayElementAtIndex(r).objectReferenceValue, $"머리 칸 {h}의 렌더러 {r}이 null");
            }
        }
    }

    [Test]
    public void 파츠마다_슬롯_역할_수가_머티리얼_수와_같고_몸은_피부_유니폼_3_축구화_순서다()
    {
        SerializedProperty parts = ViewOf(LoadPrefab()).FindProperty("parts");
        bool foundBody = false;
        for (int p = 0; p < parts.arraySize; p++)
        {
            SerializedProperty part = parts.GetArrayElementAtIndex(p);
            var renderer = part.FindPropertyRelative("renderer").objectReferenceValue as Renderer;
            Assert.IsNotNull(renderer, $"파츠 {p} 렌더러 null");
            SerializedProperty kinds = part.FindPropertyRelative("kinds");
            Assert.AreEqual(renderer.sharedMaterials.Length, kinds.arraySize, $"{renderer.name} 슬롯 역할 수");
            if (renderer.name != BodyRendererName) { continue; }

            foundBody = true;
            Assert.AreEqual(SubmeshCount, kinds.arraySize, "몸 서브메시 수");
            for (int k = 0; k < SubmeshCount; k++)
            {
                Assert.AreEqual(BodyKinds[k], kinds.GetArrayElementAtIndex(k).enumValueIndex, $"몸 슬롯 {k} 역할");
            }
        }
        Assert.IsTrue(foundBody, "몸 렌더러가 파츠에 없다");
    }

    [Test]
    public void Model은_Humanoid_아바타와_컨트롤러를_쓰고_루트_모션을_끈다()
    {
        GameObject prefab = LoadPrefab();
        Transform model = prefab.transform.Find("Model");
        Assert.IsNotNull(model, "Model 없음");
        Animator animator = model.GetComponent<Animator>();
        Assert.IsNotNull(animator, "Animator 없음");
        Assert.IsNotNull(animator.avatar, "아바타 없음");
        Assert.IsTrue(animator.avatar.isHuman, "Humanoid 아바타");
        Assert.IsNotNull(animator.runtimeAnimatorController, "컨트롤러 없음");
        Assert.IsFalse(animator.applyRootMotion, "루트 모션");
        Component controller = prefab.GetComponent("PlayerController");
        var serialized = new SerializedObject(controller);
        Assert.AreSame(animator, serialized.FindProperty("animator").objectReferenceValue, "PlayerController가 Model의 Animator를 본다");
    }

    // 헤어는 빌더가 뼈를 이름으로 몸 뼈에 다시 잇는다. 하나라도 비면 그 렌더러가 원점에 붙어 그려진다. 몸은 구운 원본 체형 메시를 쓴다
    [Test]
    public void 스킨_렌더러마다_뼈가_비어_있지_않고_몸은_구운_원본_메시를_쓴다()
    {
        Transform model = LoadPrefab().transform.Find("Model");
        Assert.IsNotNull(model, "Model 없음");
        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Assert.IsNotNull(r.rootBone, $"{r.name} rootBone");
            Assert.IsTrue(r.rootBone.IsChildOf(model), $"{r.name} rootBone이 Model 아래 뼈");
            Transform[] bones = r.bones;
            Assert.Greater(bones.Length, 0, $"{r.name} 뼈 수");
            for (int i = 0; i < bones.Length; i++)
            {
                Assert.IsNotNull(bones[i], $"{r.name} 뼈 {i} null");
            }
            if (r.name != BodyRendererName) { continue; }

            Assert.AreEqual($"{BakedFolder}/Body_Original.asset", AssetDatabase.GetAssetPath(r.sharedMesh), "몸 메시");
        }
    }

    // 경계 정리(MaxSnap)와 체형 변형이 몸 모양을 크게 바꾸지 않는다(11-14 재검증: 클램프된 t로 판정해 7cm 옮겨진 버텍스가 있었다)
    [Test]
    public void 구운_몸_메시는_서브메시_5개이고_원본에서_멀리_움직인_버텍스가_없다()
    {
        Mesh source = SourceBody();
        Vector3[] original = source.vertices;
        foreach (string shape in new[] { "Original", "Regular", "Thin" })
        {
            var baked = AssetDatabase.LoadAssetAtPath<Mesh>($"{BakedFolder}/Body_{shape}.asset");
            Assert.IsNotNull(baked, $"Body_{shape} 없음");
            Assert.AreEqual(SubmeshCount, baked.subMeshCount, $"{shape} 서브메시 수");
            Assert.AreEqual(original.Length, baked.vertexCount, $"{shape} 버텍스 수");
            Assert.AreEqual(source.triangles.Length, baked.triangles.Length, $"{shape} 삼각형 수(나누기만 하고 버리지 않는다)");
            float limit = shape == "Original" ? MaxSnap : MaxMorph;
            Vector3[] vertices = baked.vertices;
            for (int v = 0; v < vertices.Length; v++)
            {
                float moved = (vertices[v] - original[v]).magnitude;
                Assert.LessOrEqual(moved, limit + 1e-4f, $"{shape} 버텍스 {v}가 {moved * 100f:F1}cm 움직였다");
            }
        }
    }

    private static Mesh SourceBody()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Assert.IsNotNull(model, $"{ModelPath} 없음");
        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.name == BodyRendererName) { return r.sharedMesh; }
        }
        Assert.Fail($"{BodyRendererName} 렌더러 없음");
        return null;
    }
}
