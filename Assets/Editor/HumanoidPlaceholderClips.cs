using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 동작 상태(킥·슛·세이브·세리머니)의 임시 Humanoid 클립(11-14 모델 교체, 4·5단계 전까지만 쓴다).
// 옛 동작 클립 9개는 Generic 뼈 경로 커브라 Humanoid 몸에 안 먹는다. 그 상태에 들어가면 아바타가 기본 근육 자세로 떨어져 다리가 땅에 묻혔다(11-14 유저 Play).
// UAL 서기 클립의 첫 자세를 옛 클립과 같은 길이만큼 유지하는 클립을 만들어 끼운다. 길이를 맞추는 이유: 상태 전이(exitTime 0.9)와 PlayerController의 시점 상수가 클립 길이에 묶여 있다(설계 구멍 6).
// 4·5단계에서 PlayerClipBuilder Humanoid판이 진짜 동작 클립으로 바꾸면 이 파일과 만든 클립을 지운다. 길이를 옛 클립 파일에서 읽으므로 옛 클립은 그때까지 지우지 않는다. 메뉴는 몇 번 눌러도 같은 결과
public static class HumanoidPlaceholderClips
{
    private const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";
    private const string OldClipFolder = "Assets/Animations/Clips";
    private const string OutputFolder = "Assets/Animations/Clips/HumanoidPlaceholder";
    private const string LoopingState = "Celebrate";   // bool 동안 반복하는 상태(PlayerClipBuilder.Held)
    private static readonly string[] ActionStates = { "Kick", "Shoot", "Catch", "Parry", "DiveLeft", "DiveRight", "LowSaveLeft", "LowSaveRight", LoopingState };

    [MenuItem("Tools/Animation/Humanoid 임시 동작 클립(4·5단계 전)")]
    public static void Build()
    {
        AnimationClip idle = PlayerLocomotionBuilder.LoadModelClip(PlayerLocomotionBuilder.IdleClipName);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (idle == null) { return; }
        if (controller == null)
        {
            Debug.LogError($"[HumanoidPlaceholderClips] {ControllerPath} 없음");
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder(OldClipFolder, "HumanoidPlaceholder");
        }
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        for (int i = 0; i < ActionStates.Length; i++)
        {
            AssignPlaceholder(machine, idle, ActionStates[i]);
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static void AssignPlaceholder(AnimatorStateMachine machine, AnimationClip idle, string name)
    {
        AnimatorState state = FindState(machine, name);
        var old = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{OldClipFolder}/{name}.anim");
        if (state == null || old == null)
        {
            Debug.LogError($"[HumanoidPlaceholderClips] 상태 {name} 또는 옛 클립 {OldClipFolder}/{name}.anim 없음");
            return;
        }

        AnimationClip placeholder = HoldFirstPose(idle, old.length, name == LoopingState);
        placeholder.name = $"Placeholder_{name}";
        state.motion = SaveClip(placeholder, $"{OutputFolder}/{placeholder.name}.anim");
        EditorUtility.SetDirty(state);
        Debug.Log($"[HumanoidPlaceholderClips] {name}: 서기 첫 자세 {old.length:F2}초");
    }

    // 서기 클립의 모든 Humanoid 커브(근육·루트)를 0초 값으로 고정해 length 동안 유지한다.
    // 루트 설정(Bake Into Pose·기준)은 서기 클립 것을 그대로 쓴다. 기본값이면 Locomotion에서 들어올 때 몸 기준점이 달라 돌거나 밀릴 수 있다(11-14 재검증)
    private static AnimationClip HoldFirstPose(AnimationClip idle, float length, bool loop)
    {
        var clip = new AnimationClip();
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(idle);
        for (int i = 0; i < bindings.Length; i++)
        {
            AnimationCurve source = AnimationUtility.GetEditorCurve(idle, bindings[i]);
            float value = source.Evaluate(0f);
            AnimationUtility.SetEditorCurve(clip, bindings[i], AnimationCurve.Constant(0f, length, value));
        }
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(idle);
        settings.loopTime = loop;
        settings.startTime = 0f;
        settings.stopTime = length;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    // 이미 있으면 내용만 덮어써서 GUID(컨트롤러 참조)를 유지한다
    private static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        EditorUtility.CopySerialized(clip, existing);
        Object.DestroyImmediate(clip);
        return existing;
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        ChildAnimatorState[] states = machine.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state.name == name) { return states[i].state; }
        }
        return null;
    }
}
