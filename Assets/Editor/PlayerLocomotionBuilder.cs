using UnityEditor;
using UnityEditor.Animations;
using Game.Core.Match;
using UnityEngine;

// 선수 이동 애니 빌더(10-29). 서기·걷기·달리기 3상태(속도 0.5·4m/s에서 뚝 끊어 바꿈)를 1D 블렌드 트리 한 상태(Locomotion)로 합친다.
// 10-29 유저 Play "가속이랑 조깅 차이가 안 느껴짐, 전력질주 폼이랑 조깅 폼이 같다": 조깅 상한 4.2m/s와 전력 7m/s가 둘 다 달리기 클립 1배라 똑같아 보였다.
// 표준 방식(Unity 매뉴얼 Blend Trees: 이동 속도를 파라미터로 클립을 섞는다) + 달리기 기준 속도를 넘으면 재생 배율을 실제 속도에 비례(Cadence, PlayerController가 넣는다).
// 다른 상태(킥·세이브·세리머니)가 돌아오던 Idle 전이는 Locomotion으로 옮긴다. 메뉴는 몇 번 눌러도 같은 결과.
// 11-14 모델 교체 2단계(설계 실험/2026-11-12-설계-효과음-모델교체.md B-4·결정 B11): 클립을 Quaternius UAL(Humanoid, 제자리판)의 서기·걷기·조깅·전력 4점으로 바꿨다.
// 옛 Generic 클립은 Humanoid 몸에 안 먹어 아바타가 기본 근육 자세로 떨어지고 다리가 땅에 묻혔다(11-14 유저 Play 화면 기록)
public static class PlayerLocomotionBuilder
{
    public const string StateName = "Locomotion";
    public const string ClipModelPath = "Assets/Models/UAL/UAL1_Standard.fbx";
    public const string IdleClipName = "Armature|Idle_Loop";
    private const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";
    private const string WalkClipName = "Armature|Walk_Loop";
    private const string JogClipName = "Armature|Jog_Fwd_Loop";
    private const string SprintClipName = "Armature|Sprint_Loop";
    private static readonly string[] OldStates = { "Idle", "Walk", "Run" };

    [MenuItem("Tools/Animation/Build Player Locomotion")]
    public static void Build()
    {
        // 클립 4개를 먼저 읽는다. 하나라도 없으면 아무것도 안 바꾼다(10-29 리뷰: 옛 트리를 지운 뒤 빈 트리가 저장될 수 있었다)
        AnimationClip idle = LoadModelClip(IdleClipName);
        AnimationClip walk = LoadModelClip(WalkClipName);
        AnimationClip jog = LoadModelClip(JogClipName);
        AnimationClip sprint = LoadModelClip(SprintClipName);
        if (idle == null || walk == null) { return; }
        if (jog == null || sprint == null) { return; }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[PlayerLocomotionBuilder] {ControllerPath} 없음");
            return;
        }
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        EnsureFloat(controller, PlayerController.CadenceParamName, 1f);   // 기본 0이면 SetFloat 전에 재생이 멈춘다

        AnimatorState locomotion = FindState(machine, StateName);
        if (locomotion == null) { locomotion = machine.AddState(StateName); }
        RemoveOldTree(locomotion);
        locomotion.motion = MakeTree(controller, idle, walk, jog, sprint);
        locomotion.speed = 1f;
        locomotion.speedParameterActive = true;
        locomotion.speedParameter = PlayerController.CadenceParamName;
        EditorUtility.SetDirty(locomotion);

        RetargetToLocomotion(machine, locomotion);
        RemoveOldStates(machine);
        machine.defaultState = locomotion;
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerLocomotionBuilder] Locomotion 블렌드 트리(서기·걷기·조깅·전력)를 만들었다");
    }

    public static AnimationClip LoadModelClip(string name)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ClipModelPath);
        for (int i = 0; i < assets.Length; i++)
        {
            var clip = assets[i] as AnimationClip;
            if (clip == null) { continue; }
            if (clip.name == name) { return clip; }
        }
        Debug.LogError($"[PlayerLocomotionBuilder] {ClipModelPath}에 {name} 클립이 없다");
        return null;
    }

    // 서기 0 · 걷기 · 조깅 · 전력(m/s, 클립의 자연 속도 = LocomotionCadence). 사이는 섞는다
    private static BlendTree MakeTree(AnimatorController controller, AnimationClip idle, AnimationClip walk, AnimationClip jog, AnimationClip sprint)
    {
        var tree = new BlendTree
        {
            name = StateName,
            blendType = BlendTreeType.Simple1D,
            blendParameter = PlayerController.SpeedParamName,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy,
        };
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, LocomotionCadence.WalkAnimSpeed);
        tree.AddChild(jog, LocomotionCadence.JogAnimSpeed);
        tree.AddChild(sprint, LocomotionCadence.SprintAnimSpeed);
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    private static void RemoveOldTree(AnimatorState locomotion)
    {
        var old = locomotion.motion as BlendTree;
        if (old == null) { return; }
        Object.DestroyImmediate(old, true);
    }

    // 모든 상태·Any State의 전이 중 목적지가 옛 3상태인 것을 Locomotion으로
    private static void RetargetToLocomotion(AnimatorStateMachine machine, AnimatorState locomotion)
    {
        ChildAnimatorState[] states = machine.states;
        for (int i = 0; i < states.Length; i++)
        {
            AnimatorStateTransition[] transitions = states[i].state.transitions;
            for (int k = 0; k < transitions.Length; k++)
            {
                Retarget(transitions[k], locomotion);
            }
        }
        AnimatorStateTransition[] fromAny = machine.anyStateTransitions;
        for (int k = 0; k < fromAny.Length; k++)
        {
            Retarget(fromAny[k], locomotion);
        }
    }

    // 전이는 컨트롤러의 서브에셋이라 따로 저장 표시를 한다(10-29 리뷰)
    private static void Retarget(AnimatorStateTransition transition, AnimatorState locomotion)
    {
        if (!IsOldState(transition.destinationState)) { return; }
        transition.destinationState = locomotion;
        EditorUtility.SetDirty(transition);
    }

    private static void RemoveOldStates(AnimatorStateMachine machine)
    {
        for (int i = 0; i < OldStates.Length; i++)
        {
            AnimatorState old = FindState(machine, OldStates[i]);
            if (old != null) { machine.RemoveState(old); }
        }
    }

    private static bool IsOldState(AnimatorState state)
    {
        if (state == null) { return false; }
        return System.Array.IndexOf(OldStates, state.name) >= 0;
    }

    private static void EnsureFloat(AnimatorController controller, string name, float defaultValue)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name != name) { continue; }
            parameters[i].defaultFloat = defaultValue;
            controller.parameters = parameters;
            return;
        }
        controller.AddParameter(new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Float, defaultFloat = defaultValue });
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
