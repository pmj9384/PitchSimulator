using UnityEditor;
using Game.Core.Match;
using UnityEngine;

// 이동 클립 기준 속도 재기(11-17 재검증 D1b). LocomotionCadence의 WalkAnimSpeed·JogAnimSpeed·SprintAnimSpeed를 다시 잴 수 있게 남긴다.
// 11-14에는 에디터에서 손으로 재고 숫자만 상수에 적어서, UAL 버전이 바뀌면 다시 잴 방법이 주석 한 줄뿐이었다.
// 값 = 루트 모션판 FBX(UAL1_Standard_RM.fbx) 클립의 AnimationClip.averageSpeed 앞 방향 성분. 경기에 쓰는 제자리판(Assets/Models/UAL)은 루트가 안 움직여 0이 나온다.
// 루트 모션판은 측정에만 쓰고 23MB라 리포에 넣지 않는다(.gitignore의 _Probe). 없으면 Quaternius Universal Animation Library(Standard)의 Root Motion판을
// RootMotionModelPath에 둔다. 11-14에 잰 파일: 23,767,852바이트, sha256 5a5f5de6f18e9c91cdbc40eb26319ec85cc45070bc00965bc2737beb4b2984ea.
// .meta는 11-14 측정 때 것을 같이 둔다(Vault 실험/모델교체-0단계-2026-11-13/UAL1_Standard_RM.fbx.meta.txt를 이름만 바꿔 복사).
// 11-17 확인: .meta 없이 새로 임포트하면 이 스크립트가 아래 설정을 맞춰도 조깅 4.830·전력 7.730으로 11-14 값(5.161·8.013)과 다르다.
// 뼈 매핑을 제자리판에서 복사하거나 Loop Time을 맞춰도 같았고, 원인은 아직 모른다(아바타 뼈대 자세 차이로 추정). 그래서 .meta를 원본으로 삼는다.
// 이 스크립트는 그 .meta 위에서 설정이 어긋나지 않았는지 맞추고 잰다. hasMotionCurves는 False(모션 노드가 아니라 클립 루트 설정이 속도를 정함). 메뉴는 몇 번 눌러도 같은 결과
public static class LocomotionSpeedProbe
{
    public const string RootMotionModelPath = "Assets/_Probe/UAL/UAL1_Standard_RM.fbx";
    private const string MotionNode = "root";
    private const string LoopSuffix = "_Loop";
    private const float Tolerance = 0.05f;   // 상수와 측정값 차이가 이보다 크면 경고(m/s). 상수는 측정값을 이 안쪽으로 다듬은 값이다

    [MenuItem("Tools/Pitch/이동 클립 기준 속도 재기")]
    public static void Measure()
    {
        var importer = AssetImporter.GetAtPath(RootMotionModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[LocomotionSpeedProbe] {RootMotionModelPath} 없음. UAL Standard의 Root Motion판 FBX를 이 경로에 둔다");
            return;
        }

        if (ApplyImportSettings(importer))
        {
            importer.SaveAndReimport();
        }

        // 블렌드 트리에 실제로 걸린 클립 이름을 빌더에서 읽는다. 트리 클립을 바꾸면 여기서도 그 클립을 잰다
        LogClip(PlayerLocomotionBuilder.WalkClipName, LocomotionCadence.WalkAnimSpeed, nameof(LocomotionCadence.WalkAnimSpeed));
        LogClip(PlayerLocomotionBuilder.JogClipName, LocomotionCadence.JogAnimSpeed, nameof(LocomotionCadence.JogAnimSpeed));
        LogClip(PlayerLocomotionBuilder.SprintClipName, LocomotionCadence.SprintAnimSpeed, nameof(LocomotionCadence.SprintAnimSpeed));
    }

    // 바뀐 것이 있으면 true. 같은 값이면 다시 임포트하지 않는다
    private static bool ApplyImportSettings(ModelImporter importer)
    {
        bool changed = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;   // Human은 아바타가 있어야 휴머노이드 클립이 만들어진다
            changed = true;
        }
        if (importer.bakeAxisConversion)
        {
            importer.bakeAxisConversion = false;   // 켜면 몸이 누웠다(11-13 0단계)
            changed = true;
        }
        if (importer.motionNodeName != MotionNode)
        {
            importer.motionNodeName = MotionNode;
            changed = true;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
            changed = true;
        }
        for (int i = 0; i < clips.Length; i++)
        {
            if (SetRootMotion(clips[i]))
            {
                changed = true;
            }
        }
        if (changed)
        {
            importer.clipAnimations = clips;
        }
        return changed;
    }

    // 루트 회전 = Body Orientation, 루트 XZ = Center of Mass(원본 루트 안 씀), 루트 Y = 원본. 11-14 .meta의 43개 클립이 모두 이 값이었다.
    // 이름이 _Loop로 끝나는 클립은 Loop Time을 켠다(11-14 .meta와 같음)
    private static bool SetRootMotion(ModelImporterClipAnimation clip)
    {
        bool loop = clip.name.EndsWith(LoopSuffix);
        bool changed = clip.keepOriginalOrientation || clip.keepOriginalPositionXZ || !clip.keepOriginalPositionY || clip.loopTime != loop;
        clip.keepOriginalOrientation = false;
        clip.keepOriginalPositionXZ = false;
        clip.keepOriginalPositionY = true;
        clip.loopTime = loop;
        return changed;
    }

    private static void LogClip(string clipName, float constant, string constantName)
    {
        AnimationClip clip = FindClip(clipName);
        if (clip == null)
        {
            Debug.LogError($"[LocomotionSpeedProbe] {RootMotionModelPath}에 {clipName} 클립 없음");
            return;
        }

        Vector3 speed = clip.averageSpeed;
        string line = $"[LocomotionSpeedProbe] {clipName}: 앞 {speed.z:F3} · 옆 {speed.x:F3} · 위 {speed.y:F3} m/s (hasMotionCurves {clip.hasMotionCurves}), {constantName} = {constant:F3}";
        if (Mathf.Abs(speed.z - constant) > Tolerance)
        {
            Debug.LogWarning($"{line} → 차이 {speed.z - constant:+0.000;-0.000}. LocomotionCadence 상수를 다시 맞추고 이동 트리 빌더를 다시 돌린다");
            return;
        }
        Debug.Log(line);
    }

    private static AnimationClip FindClip(string clipName)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(RootMotionModelPath);
        for (int i = 0; i < assets.Length; i++)
        {
            var clip = assets[i] as AnimationClip;
            if (clip == null) { continue; }
            if (clip.name == clipName) { return clip; }
        }
        return null;
    }
}
