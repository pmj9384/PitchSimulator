using System.Collections.Generic;
using System.IO;
using Game.Core.Match;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 선수 초상 120장 렌더(11-14 모델 교체 6단계, 설계 실험/2026-11-12-설계-효과음-모델교체.md B-4). 번호 = 머리 칸 × 피부 × 머리색(PortraitRules), 체형도 같은 번호에서 풀린다.
// 경기 몸과 같은 프리팹·같은 PlayerView.ApplyLook·같은 팔레트로 찍어서 초상과 경기 겉모습이 번호대로 맞는다.
// 10-21판 초상 스크립트를 "1회성"으로 리포에 안 남겨 모델 교체 때 새로 짜야 했다(CLAUDE.md 결과물 스크립트 규칙의 계기). 이 파일은 다시 돌릴 수 있게 남긴다.
// 구도는 옛 초상과 같게: 96 × 96, 투명 배경, 머리와 어깨, 내 팀 유니폼(0번). 기존 .meta(스프라이트 임포트 설정)는 그대로 두고 PNG만 덮어쓴다
public static class PortraitRenderer
{
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string OutputFolder = "Assets/Resources/Sprites/Portrait";
    private const string LookTemplatePath = "Assets/Materials/PlayerHome.mat";        // PlayerManager.lookTemplate와 같은 원본(InGameScene)
    private const string SkinTemplatePath = "Assets/Models/UBC/Materials/M_UBC_Skin.mat";   // PlayerManager.skinTemplate와 같은 원본
    private const int Size = 96;
    private const int Supersample = 3;   // 3배 크기로 찍어 줄인다. 렌더 텍스처 MSAA는 URP 설정(4배)과 다르면 렌더가 실패해서(11-14 "Attachment 0 was created with 4 samples but 8") 쓰지 않는다
    private const int MyKit = 0;
    private const float CameraDistance = 1.15f;   // [가정] 머리에서 카메라까지(m). 어깨까지 들어오는 거리
    private const float CameraLift = 0.02f;       // [가정] 눈높이보다 살짝 위에서
    private const float LookDrop = 0.02f;         // [가정] 머리 뼈(턱 높이)보다 조금 아래를 화면 가운데로. 머리끝부터 어깨까지 들어온다(11-14 렌더로 맞춤)
    private const float FieldOfView = 30f;        // [가정]

    [MenuItem("Tools/Pitch/선수 초상 120장 렌더")]
    public static void RenderAll()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var lookTemplate = AssetDatabase.LoadAssetAtPath<Material>(LookTemplatePath);
        var skinTemplate = AssetDatabase.LoadAssetAtPath<Material>(SkinTemplatePath);
        AnimationClip idle = PlayerLocomotionBuilder.LoadModelClip(PlayerLocomotionBuilder.IdleClipName);
        if (prefab == null || lookTemplate == null || skinTemplate == null)
        {
            Debug.LogError($"[PortraitRenderer] {PrefabPath}·{LookTemplatePath}·{SkinTemplatePath} 중 없는 것이 있다");
            return;
        }
        if (idle == null) { return; }

        Scene scene = EditorSceneManager.NewPreviewScene();
        var large = new RenderTexture(Size * Supersample, Size * Supersample, 24, RenderTextureFormat.ARGB32);
        var target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32);
        try
        {
            Light key = AddLight(scene, "Key", 1.3f);
            Light fill = AddLight(scene, "Fill", 0.5f);
            Camera camera = AddCamera(scene, large);
            var rig = new Rig(camera, key, fill);
            for (int portrait = 1; portrait <= PortraitRules.VariantCount; portrait++)
            {
                RenderOne(scene, prefab, idle, rig, large, target, portrait, lookTemplate, skinTemplate);
            }
        }
        finally
        {
            RenderTexture.active = null;
            large.Release();
            target.Release();
            EditorSceneManager.ClosePreviewScene(scene);
        }
        AssetDatabase.Refresh();
        Debug.Log($"[PortraitRenderer] 초상 {PortraitRules.VariantCount}장을 {OutputFolder}에 저장");
    }

    private static void RenderOne(Scene scene, GameObject prefab, AnimationClip idle, Rig rig, RenderTexture large, RenderTexture target, int portrait, Material lookTemplate, Material skinTemplate)
    {
        var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        var materials = new Material[4];
        var baked = new List<GameObject>();
        try
        {
            Animator animator = body.transform.Find("Model").GetComponent<Animator>();
            materials[0] = Tinted(lookTemplate, PlayerPalette.Kit(MyKit));
            materials[1] = Tinted(skinTemplate, PlayerPalette.Skin(PortraitRules.SkinOf(portrait)));
            materials[2] = Tinted(lookTemplate, PlayerPalette.Hair(PortraitRules.HairOf(portrait)));
            materials[3] = Tinted(lookTemplate, PlayerPalette.Boots);
            body.GetComponent<PlayerView>().ApplyLook(PortraitRules.HeadOf(portrait), PortraitRules.BodyOf(portrait), materials[0], materials[1], materials[2], materials[3]);
            idle.SampleAnimation(animator.gameObject, 0f);
            BakeSkins(scene, body, baked);
            Aim(rig, animator);
            rig.Camera.Render();
            Graphics.Blit(large, target);   // 쌍선형으로 줄인다
            Save(target, portrait);
        }
        finally
        {
            Object.DestroyImmediate(body);
            for (int i = 0; i < baked.Count; i++)
            {
                Object.DestroyImmediate(baked[i].GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(baked[i]);
            }
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null) { Object.DestroyImmediate(materials[i]); }
            }
        }
    }

    // 미리보기 씬에서는 SkinnedMeshRenderer가 샘플한 자세로 갱신되지 않는다(0단계 렌더에서 확인). 켜진 렌더러를 메시로 구워 대신 그린다
    private static void BakeSkins(Scene scene, GameObject body, List<GameObject> baked)
    {
        foreach (SkinnedMeshRenderer skin in body.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled) { continue; }

            var mesh = new Mesh();
            skin.BakeMesh(mesh, true);
            var go = new GameObject($"Baked_{skin.name}");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            skin.enabled = false;
            baked.Add(go);
        }
    }

    // PlayerManager.LookMaterial과 같은 방식(원본 복사 + 색)
    private static Material Tinted(Material template, Color color)
    {
        var material = new Material(template);
        material.color = color;
        return material;
    }

    // 몸 정면(오른 허벅지 방향과 월드 위의 외적)에서 머리를 본다. 바인드 포즈 방향과 상관없다. 조명도 몸 정면 기준으로 둔다
    private static void Aim(Rig rig, Animator animator)
    {
        Vector3 side = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
        Vector3 right = Vector3.ProjectOnPlane(side, Vector3.up).normalized;
        Vector3 forward = Vector3.Cross(right, Vector3.up);
        Vector3 head = animator.GetBoneTransform(HumanBodyBones.Head).position;
        rig.Camera.transform.position = head + forward * CameraDistance + Vector3.up * CameraLift;
        rig.Camera.transform.LookAt(head + Vector3.down * LookDrop);
        rig.Key.transform.rotation = Quaternion.LookRotation(-forward + Vector3.down * 0.45f + right * 0.35f);    // 앞 위 오른쪽에서 [가정]
        rig.Fill.transform.rotation = Quaternion.LookRotation(-forward + Vector3.down * 0.2f - right * 0.6f);     // 앞 왼쪽 보조 [가정]
    }

    private static void Save(RenderTexture target, int portrait)
    {
        RenderTexture.active = target;
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        texture.Apply();
        File.WriteAllBytes($"{OutputFolder}/portrait_{portrait:000}.png", texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    // 키 라이트 + 보조. 경기 씬 조명과 같을 필요는 없고 얼굴이 읽히면 된다 [가정]
    private static Light AddLight(Scene scene, string name, float intensity)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        return light;
    }

    private sealed class Rig
    {
        public readonly Camera Camera;
        public readonly Light Key;
        public readonly Light Fill;

        public Rig(Camera camera, Light key, Light fill)
        {
            Camera = camera;
            Key = key;
            Fill = fill;
        }
    }

    private static Camera AddCamera(Scene scene, RenderTexture target)
    {
        var go = new GameObject("PortraitCamera");
        SceneManager.MoveGameObjectToScene(go, scene);
        var camera = go.AddComponent<Camera>();
        camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 투명 배경(UI가 뒤 패널 색 위에 얹는다)
        camera.fieldOfView = FieldOfView;
        camera.nearClipPlane = 0.05f;
        camera.targetTexture = target;
        return camera;
    }
}
