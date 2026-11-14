using Game.Core.Match;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 선수 프리팹·구운 몸 메시가 PlayerPrefabBuilder·PlayerMeshBaker 결과와 같은지(11-14 모델 교체 1단계). 어긋나면 Tools/Pitch/선수 몸 메시 굽기 → 선수 프리팹 배선(UBC)을 다시 돌린다.
// PlayerView는 Assembly-CSharp라 이 테스트 asmdef가 타입을 못 본다. SerializedObject로 직렬화 필드를 읽는다. UnityEditor를 쓰므로 Unity EditMode에서만 돈다
public class PlayerPrefabTests
{
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";
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

    // 체형 메시 3종(PortraitRules.BodyOf 순서)이 몸 렌더러와 같은 뼈 수·서브메시 수다. 다르면 갈아 끼울 때 몸이 찢어진다
    [Test]
    public void 체형_메시가_BodyCount개이고_몸_렌더러와_뼈_수가_같다()
    {
        SerializedObject view = ViewOf(LoadPrefab());
        var bodyRenderer = view.FindProperty("bodyRenderer").objectReferenceValue as SkinnedMeshRenderer;
        Assert.IsNotNull(bodyRenderer, "bodyRenderer 없음");
        Assert.AreEqual(BodyRendererName, bodyRenderer.name, "bodyRenderer가 몸 렌더러");
        SerializedProperty meshes = view.FindProperty("bodyMeshes");
        Assert.AreEqual(PortraitRules.BodyCount, meshes.arraySize, "체형 메시 수");
        string[] shapes = { "Thin", "Regular", "Original" };
        for (int i = 0; i < meshes.arraySize; i++)
        {
            var mesh = meshes.GetArrayElementAtIndex(i).objectReferenceValue as Mesh;
            Assert.IsNotNull(mesh, $"체형 {i} 메시 null");
            Assert.AreEqual($"{BakedFolder}/Body_{shapes[i]}.asset", AssetDatabase.GetAssetPath(mesh), $"체형 {i} 순서");
            Assert.AreEqual(bodyRenderer.bones.Length, mesh.bindposes.Length, $"체형 {i} 바인드 포즈 수 = 뼈 수");
            Assert.AreEqual(SubmeshCount, mesh.subMeshCount, $"체형 {i} 서브메시 수");
        }
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

    // 11-14 유저 Play: Humanoid 몸에 옛 Generic 클립이 걸려 아바타가 기본 근육 자세로 떨어지고 다리가 땅에 묻혔다. 컨트롤러의 모든 클립은 Humanoid여야 한다.
    // 이동 트리 임계값은 LocomotionCadence(뷰의 재생 배율과 같은 값)와 같아야 한다. 상수를 바꾸고 빌더를 안 돌리면 여기서 깨진다
    [Test]
    public void 선수_컨트롤러의_클립은_전부_Humanoid이고_이동_트리_임계값이_LocomotionCadence와_같다()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, $"{ControllerPath} 없음");
        bool foundTree = false;
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            var tree = child.state.motion as BlendTree;
            if (tree == null)
            {
                var clip = child.state.motion as AnimationClip;
                Assert.IsNotNull(clip, $"상태 {child.state.name}에 클립이 없다");
                Assert.IsTrue(clip.humanMotion, $"상태 {child.state.name}의 클립 {clip.name}이 Humanoid가 아니다");
                continue;
            }

            foundTree = true;
            float[] expected = { 0f, LocomotionCadence.WalkAnimSpeed, LocomotionCadence.JogAnimSpeed, LocomotionCadence.SprintAnimSpeed };
            ChildMotion[] motions = tree.children;
            Assert.AreEqual(expected.Length, motions.Length, "이동 트리 칸 수");
            for (int i = 0; i < motions.Length; i++)
            {
                Assert.AreEqual(expected[i], motions[i].threshold, 1e-4f, $"이동 트리 {i}번 임계값");
                var clip = motions[i].motion as AnimationClip;
                Assert.IsNotNull(clip, $"이동 트리 {i}번 클립 없음");
                Assert.IsTrue(clip.humanMotion, $"이동 트리 클립 {clip.name}이 Humanoid가 아니다");
            }
        }
        Assert.IsTrue(foundTree, "이동 블렌드 트리가 없다");
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
