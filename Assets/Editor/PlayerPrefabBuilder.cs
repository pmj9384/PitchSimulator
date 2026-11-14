using System.Collections.Generic;
using Game.Core.Match;
using UnityEditor;
using UnityEngine;

// 선수 프리팹 배선(11-14 모델 교체 1단계, 설계 실험/2026-11-12-설계-효과음-모델교체.md B-4 1단계 ④·결정 B2·B9·B12).
// Player.prefab의 Model 자식을 UBC 몸(Humanoid 아바타)으로 바꾸고, 구운 몸 메시(PlayerMeshBaker)·눈·눈썹·헤어를 붙인 뒤
// PlayerView의 머리 칸 6개와 슬롯 역할, PlayerController의 Animator를 다시 잇는다. 손 배선을 남기지 않으려고 전부 이 스크립트가 한다(결정 B7).
// 이동·동작 클립은 아직 옛 Generic 클립이라 Humanoid 몸에는 안 먹는다(2단계 이동, 4·5단계 동작에서 바꾼다). 메뉴는 몇 번 눌러도 같은 결과
public static class PlayerPrefabBuilder
{
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";
    private const string HairFolder = PlayerMeshBaker.ModelFolder + "/Hair";
    private const string TextureFolder = PlayerMeshBaker.ModelFolder + "/Textures";
    private const string MaterialFolder = PlayerMeshBaker.ModelFolder + "/Materials";
    private const string ModelName = "Model";
    private const string EyesName = "Eyes";
    private const string EyebrowsName = "Eyebrows";
    private const string LitShaderName = "Universal Render Pipeline/Lit";
    private const float FootLift = 0.01f;   // 원본 발바닥이 y −0.01(0단계 B-8 6번). 잔디에 닿게 올린다

    // 헤어 파일 하나와 그 텍스처 번호(원본 머티리얼 MI_Hair_1·MI_Hair_2와 같음)
    private readonly struct HairFile
    {
        public readonly string Name;
        public readonly int Texture;

        public HairFile(string name, int texture)
        {
            Name = name;
            Texture = texture;
        }
    }

    private static readonly HairFile Buzzed = new HairFile("Hair_Buzzed", 1);
    private static readonly HairFile SimpleParted = new HairFile("Hair_SimpleParted", 1);
    private static readonly HairFile Long = new HairFile("Hair_Long", 2);
    private static readonly HairFile Buns = new HairFile("Hair_Buns", 2);
    private static readonly HairFile Beard = new HairFile("Hair_Beard", 1);
    private static readonly HairFile[] HairFiles = { Buzzed, SimpleParted, Long, Buns, Beard };

    // 머리 칸(결정 B2 권장안): 헤어 4종 + 수염 조합 2칸. 번호 순서 = PortraitRules.HeadOf. 최종 6칸은 머리 후보 시트를 보고 유저가 확정한다
    private static readonly HairFile[][] HeadSlots =
    {
        new[] { Buzzed },
        new[] { SimpleParted },
        new[] { Long },
        new[] { Buns },
        new[] { Buzzed, Beard },
        new[] { SimpleParted, Beard },
    };

    [MenuItem("Tools/Pitch/선수 프리팹 배선(UBC)")]
    public static void Build()
    {
        if (HeadSlots.Length != PortraitRules.HeadCount)
        {
            Debug.LogError($"[PlayerPrefabBuilder] 머리 칸 {HeadSlots.Length}개가 PortraitRules.HeadCount {PortraitRules.HeadCount}와 다르다");
            return;
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerMeshBaker.ModelPath);
        var body = AssetDatabase.LoadAssetAtPath<Mesh>(PlayerMeshBaker.OriginalBodyPath());
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        Avatar avatar = LoadAvatar();
        if (!CheckInputs(model, body, controller, avatar)) { return; }

        Materials materials = EnsureMaterials();
        if (materials == null) { return; }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (Rebuild(root, model, body, controller, avatar, materials))
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[PlayerPrefabBuilder] {PrefabPath} 저장");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
    }

    private static bool CheckInputs(GameObject model, Mesh body, RuntimeAnimatorController controller, Avatar avatar)
    {
        if (model == null)
        {
            Debug.LogError($"[PlayerPrefabBuilder] {PlayerMeshBaker.ModelPath} 없음");
            return false;
        }
        if (body == null)
        {
            Debug.LogError($"[PlayerPrefabBuilder] {PlayerMeshBaker.OriginalBodyPath()} 없음. 선수 몸 메시 굽기를 먼저 돌린다");
            return false;
        }
        if (body.subMeshCount != PlayerMeshBaker.SubmeshCount)
        {
            Debug.LogError("[PlayerPrefabBuilder] 몸 메시 서브메시 수가 다르다. 선수 몸 메시 굽기를 먼저 돌린다");
            return false;
        }
        if (controller == null || avatar == null)
        {
            Debug.LogError($"[PlayerPrefabBuilder] {ControllerPath} 또는 Humanoid 아바타 없음");
            return false;
        }
        return true;
    }

    private static Avatar LoadAvatar()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(PlayerMeshBaker.ModelPath);
        for (int i = 0; i < assets.Length; i++)
        {
            var avatar = assets[i] as Avatar;
            if (avatar != null && avatar.isHuman) { return avatar; }
        }
        return null;
    }

    private static bool Rebuild(GameObject root, GameObject model, Mesh body, RuntimeAnimatorController controller, Avatar avatar, Materials materials)
    {
        AnimatorCullingMode culling = RemoveOldModel(root);
        GameObject instance = PlaceModel(root, model);
        Animator animator = SetupAnimator(instance, controller, avatar, culling);

        var parts = new List<PartSpec>();
        if (!WireBody(instance.transform, body, materials, parts)) { return false; }

        var bones = BoneMap(instance.transform);
        var hairs = new Dictionary<string, SkinnedMeshRenderer>();
        for (int i = 0; i < HairFiles.Length; i++)
        {
            SkinnedMeshRenderer hair = AttachHair(instance.transform, HairFiles[i].Name, bones, materials.Hair[HairFiles[i].Texture - 1]);
            if (hair == null) { return false; }

            hairs[HairFiles[i].Name] = hair;
            parts.Add(new PartSpec(hair, new[] { PlayerView.SlotKind.Hair }));
        }

        WireView(root, hairs, parts);
        WireController(root, animator);
        return true;
    }

    // 옛 Model을 지우고 그 Animator의 컬링 모드를 돌려준다(새 Animator도 같은 값으로)
    private static AnimatorCullingMode RemoveOldModel(GameObject root)
    {
        AnimatorCullingMode culling = AnimatorCullingMode.CullUpdateTransforms;
        Transform old = root.transform.Find(ModelName);
        if (old == null) { return culling; }

        Animator oldAnimator = old.GetComponent<Animator>();
        if (oldAnimator != null) { culling = oldAnimator.cullingMode; }
        Object.DestroyImmediate(old.gameObject);
        return culling;
    }

    private static GameObject PlaceModel(GameObject root, GameObject model)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = ModelName;
        instance.transform.SetSiblingIndex(0);
        instance.transform.localPosition = new Vector3(0f, FootLift, 0f);
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        return instance;
    }

    private static Animator SetupAnimator(GameObject instance, RuntimeAnimatorController controller, Avatar avatar, AnimatorCullingMode culling)
    {
        Animator animator = instance.GetComponent<Animator>();
        if (animator == null) { animator = instance.AddComponent<Animator>(); }
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = culling;
        return animator;
    }

    // 몸(구운 메시, 서브메시 5)·눈·눈썹
    private static bool WireBody(Transform model, Mesh body, Materials materials, List<PartSpec> parts)
    {
        SkinnedMeshRenderer bodyRenderer = FindRenderer(model, PlayerMeshBaker.BodyRendererName);
        SkinnedMeshRenderer eyes = FindRenderer(model, EyesName);
        SkinnedMeshRenderer brows = FindRenderer(model, EyebrowsName);
        if (bodyRenderer == null || eyes == null || brows == null)
        {
            Debug.LogError("[PlayerPrefabBuilder] 몸·눈·눈썹 렌더러를 못 찾았다. 모델 이름이 바뀌었으면 상수를 고친다");
            return false;
        }

        bodyRenderer.sharedMesh = body;
        var bodyMaterials = new Material[PlayerMeshBaker.SubmeshCount];
        for (int i = 0; i < bodyMaterials.Length; i++)
        {
            bodyMaterials[i] = materials.Body;
        }
        bodyRenderer.sharedMaterials = bodyMaterials;
        parts.Add(new PartSpec(bodyRenderer, BodySlotKinds()));
        eyes.sharedMaterials = new[] { materials.Eyes };
        parts.Add(new PartSpec(eyes, new[] { PlayerView.SlotKind.Keep }));
        brows.sharedMaterials = new[] { materials.Hair[0] };
        parts.Add(new PartSpec(brows, new[] { PlayerView.SlotKind.Hair }));
        return true;
    }

    // 결정 B12: 상의·하의·양말은 전부 유니폼 색, 축구화는 신발 색
    private static PlayerView.SlotKind[] BodySlotKinds()
    {
        var kinds = new PlayerView.SlotKind[PlayerMeshBaker.SubmeshCount];
        kinds[(int)PlayerMeshBaker.Region.Skin] = PlayerView.SlotKind.Skin;
        kinds[(int)PlayerMeshBaker.Region.Shirt] = PlayerView.SlotKind.Kit;
        kinds[(int)PlayerMeshBaker.Region.Shorts] = PlayerView.SlotKind.Kit;
        kinds[(int)PlayerMeshBaker.Region.Socks] = PlayerView.SlotKind.Kit;
        kinds[(int)PlayerMeshBaker.Region.Boots] = PlayerView.SlotKind.Boots;
        return kinds;
    }

    private static Dictionary<string, Transform> BoneMap(Transform model)
    {
        var map = new Dictionary<string, Transform>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            if (!map.ContainsKey(t.name)) { map[t.name] = t; }
        }
        return map;
    }

    private static SkinnedMeshRenderer FindRenderer(Transform model, string name)
    {
        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.name == name) { return r; }
        }
        return null;
    }

    // 헤어 FBX는 몸과 같은 뼈 65개에 스킨돼 있다. 렌더러만 몸 아래로 옮기고 뼈를 이름으로 몸 뼈에 다시 잇는다. 헤어 쪽 뼈 사본은 지운다
    private static SkinnedMeshRenderer AttachHair(Transform model, string file, Dictionary<string, Transform> bones, Material material)
    {
        string path = $"{HairFolder}/{file}.fbx";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null)
        {
            Debug.LogError($"[PlayerPrefabBuilder] {path} 없음");
            return null;
        }

        GameObject copy = Object.Instantiate(source, model);   // 프리팹 편집 씬 안에 바로 만든다
        SkinnedMeshRenderer hair = FindRenderer(copy.transform, file);
        if (hair == null)
        {
            Debug.LogError($"[PlayerPrefabBuilder] {path}에 렌더러 {file}이 없다");
            Object.DestroyImmediate(copy);
            return null;
        }

        Transform[] mapped = MapBones(hair.bones, bones, file);
        Transform rootBone = MapRoot(hair.rootBone, bones);
        if (mapped == null || rootBone == null)
        {
            Debug.LogError($"[PlayerPrefabBuilder] {file}의 뼈를 몸 뼈에 잇지 못했다");
            Object.DestroyImmediate(copy);
            return null;
        }

        hair.transform.SetParent(model, false);
        hair.transform.localPosition = Vector3.zero;
        hair.transform.localRotation = Quaternion.identity;
        hair.transform.localScale = Vector3.one;
        hair.bones = mapped;
        hair.rootBone = rootBone;
        hair.sharedMaterials = new[] { material };
        hair.enabled = false;   // 켜는 것은 PlayerView.ApplyLook
        Object.DestroyImmediate(copy);
        return hair;
    }

    private static Transform MapRoot(Transform root, Dictionary<string, Transform> bones)
    {
        if (root == null) { return null; }

        Transform target;
        bones.TryGetValue(root.name, out target);
        return target;
    }

    private static Transform[] MapBones(Transform[] source, Dictionary<string, Transform> bones, string file)
    {
        var mapped = new Transform[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == null)
            {
                Debug.LogError($"[PlayerPrefabBuilder] {file}의 뼈 {i}번이 비어 있다");
                return null;
            }

            Transform target;
            if (!bones.TryGetValue(source[i].name, out target))
            {
                Debug.LogError($"[PlayerPrefabBuilder] {file}의 뼈 {source[i].name}가 몸에 없다");
                return null;
            }
            mapped[i] = target;
        }
        return mapped;
    }

    private static void WireView(GameObject root, Dictionary<string, SkinnedMeshRenderer> hairs, List<PartSpec> parts)
    {
        var view = root.GetComponent<PlayerView>();
        var serialized = new SerializedObject(view);
        SerializedProperty heads = serialized.FindProperty("heads");
        heads.arraySize = HeadSlots.Length;
        for (int h = 0; h < HeadSlots.Length; h++)
        {
            SerializedProperty renderers = heads.GetArrayElementAtIndex(h).FindPropertyRelative("renderers");
            renderers.arraySize = HeadSlots[h].Length;
            for (int r = 0; r < HeadSlots[h].Length; r++)
            {
                renderers.GetArrayElementAtIndex(r).objectReferenceValue = hairs[HeadSlots[h][r].Name];
            }
        }
        SerializedProperty partList = serialized.FindProperty("parts");
        partList.arraySize = parts.Count;
        for (int p = 0; p < parts.Count; p++)
        {
            SerializedProperty part = partList.GetArrayElementAtIndex(p);
            part.FindPropertyRelative("renderer").objectReferenceValue = parts[p].Renderer;
            SerializedProperty kinds = part.FindPropertyRelative("kinds");
            kinds.arraySize = parts[p].Kinds.Length;
            for (int k = 0; k < parts[p].Kinds.Length; k++)
            {
                kinds.GetArrayElementAtIndex(k).enumValueIndex = (int)parts[p].Kinds[k];
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireController(GameObject root, Animator animator)
    {
        var controller = root.GetComponent<PlayerController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("animator").objectReferenceValue = animator;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // 프리팹 기본 머티리얼(없으면 만들고 있으면 값을 맞춘다). 경기에서는 PlayerManager가 Skin·Hair·Kit·Boots 슬롯을 단색 머티리얼로 바꿔 끼운다(색은 3단계에서 정리)
    private static Materials EnsureMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder(PlayerMeshBaker.ModelFolder, "Materials");
        }
        Material body = EnsureMaterial("M_UBC_Body", "T_Superhero_Male_Ligh", "T_Superhero_Male_Normal");
        Material eyes = EnsureMaterial("M_UBC_Eyes", "T_Eye_Brown", "T_Eye_Normal");
        Material hair1 = EnsureMaterial("M_UBC_Hair1", "T_Hair_1_BaseColor", "T_Hair_1_Normal");
        Material hair2 = EnsureMaterial("M_UBC_Hair2", "T_Hair_2_BaseColor", "T_Hair_2_Normal");
        if (body == null || eyes == null || hair1 == null || hair2 == null) { return null; }

        return new Materials(body, eyes, new[] { hair1, hair2 });
    }

    private static Material EnsureMaterial(string name, string baseTexture, string normalTexture)
    {
        Shader shader = Shader.Find(LitShaderName);
        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{baseTexture}.png");
        var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{normalTexture}.png");
        if (shader == null || baseMap == null || normalMap == null)
        {
            Debug.LogError($"[PlayerPrefabBuilder] {name}: 셰이더 또는 텍스처({baseTexture}·{normalTexture}) 없음");
            return null;
        }

        string path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", baseMap);
        material.SetTexture("_BumpMap", normalMap);
        material.EnableKeyword("_NORMALMAP");
        material.SetFloat("_Smoothness", 0.2f);
        material.enableInstancing = false;
        EditorUtility.SetDirty(material);
        return material;
    }

    private sealed class Materials
    {
        public readonly Material Body;
        public readonly Material Eyes;
        public readonly Material[] Hair;

        public Materials(Material body, Material eyes, Material[] hair)
        {
            Body = body;
            Eyes = eyes;
            Hair = hair;
        }
    }

    private readonly struct PartSpec
    {
        public readonly Renderer Renderer;
        public readonly PlayerView.SlotKind[] Kinds;

        public PartSpec(Renderer renderer, PlayerView.SlotKind[] kinds)
        {
            Renderer = renderer;
            Kinds = kinds;
        }
    }
}
