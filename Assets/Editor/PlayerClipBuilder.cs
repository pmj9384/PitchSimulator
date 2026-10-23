using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 선수 연출 클립 빌더(10-23). 모캡·외부 클립 없이, Idle 첫 프레임 위에 "이름 붙은 포즈"(관절별 회전 오프셋)를 얹고
// 그 자세를 샘플링해 커브로 굽는다. 유저 Play "차는 동작이 현실적이지 않다"(범용 팩 격투 킥)에 대한 1차 답.
// 리그가 IK형(Foot.R이 다리의 자식이 아니라 Root 직속)이라 발은 정강이 끝을 따라가게 따로 놓는다.
// 메뉴 한 번이면 클립 5개(Kick·Shoot·Catch·Parry·Celebrate)와 컨트롤러 상태·트리거를 다시 만든다. 카메라가 멀어 실루엣이면 충분.
// 각도는 전부 출처 없는 눈대중 [가정]. Play로 조정한다
public static class PlayerClipBuilder
{
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";
    private const string ClipFolder = "Assets/Animations/Clips";
    private const float Fps = 30f;

    // 본 경로(Animator가 붙은 Model 기준)
    private const string Body = "CharacterArmature/Root/Body";
    private const string Abdomen = Body + "/Hips/Abdomen";
    private const string Chest = Abdomen + "/Torso/Chest";
    private const string Head = Chest + "/Neck/Head";
    private const string UpperArmL = Chest + "/Shoulder.L/UpperArm.L";
    private const string UpperArmR = Chest + "/Shoulder.R/UpperArm.R";
    private const string LowerArmL = UpperArmL + "/LowerArm.L";
    private const string LowerArmR = UpperArmR + "/LowerArm.R";
    private const string UpperLegR = Body + "/UpperLeg.R";
    private const string LowerLegR = UpperLegR + "/LowerLeg.R";
    private const string ShinEndR = LowerLegR + "/LowerLeg.R_end";
    private const string FootR = "CharacterArmature/Root/Foot.R";

    // 회전 축은 모델 기준 월드 축. Right 축 양수 = 뒤로(다리·팔이 뒤로 감), 음수 = 앞으로. Forward 축 양수 = 오른쪽 위로 벌림
    private enum Axis { Right, Forward, Up }

    private readonly struct Joint
    {
        public readonly string Path;
        public readonly Axis Axis;
        public readonly float Degrees;

        public Joint(string path, Axis axis, float degrees)
        {
            Path = path;
            Axis = axis;
            Degrees = degrees;
        }
    }

    private sealed class Pose
    {
        public readonly float Time;
        public readonly List<Joint> Joints = new List<Joint>();
        public float FootDegrees;   // 발끝 추가 회전(Right 축). 양수 = 발끝이 아래로(킥 접촉 때 펴기)

        public Pose(float time)
        {
            Time = time;
        }

        public Pose Add(string path, Axis axis, float degrees)
        {
            Joints.Add(new Joint(path, axis, degrees));
            return this;
        }

        public Pose Foot(float degrees)
        {
            FootDegrees = degrees;
            return this;
        }
    }

    [MenuItem("Tools/Animation/Build Player Clips")]
    public static void Build()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            Transform model = instance.transform.Find("Model");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimationClip idle = FindState(controller.layers[0].stateMachine, "Idle").motion as AnimationClip;
            if (!AssetDatabase.IsValidFolder(ClipFolder)) { AssetDatabase.CreateFolder("Assets/Animations", "Clips"); }

            AnimationClip kick = Bake(model, idle, "Kick", KickPoses(), false);
            AnimationClip shoot = Bake(model, idle, "Shoot", ShootPoses(), false);
            AnimationClip catchClip = Bake(model, idle, "Catch", CatchPoses(), false);
            AnimationClip parry = Bake(model, idle, "Parry", ParryPoses(), false);
            AnimationClip celebrate = Bake(model, idle, "Celebrate", CelebratePoses(), true);
            Wire(controller, kick, shoot, catchClip, parry, celebrate);
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerClipBuilder] 클립 5개를 굽고 컨트롤러를 갱신했다");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // ── 포즈 (시간은 초)

    // 오른발 인사이드 킥: 백스윙 → 접촉(0.18) → 팔로스루 → 복귀. 공은 트리거 순간에 떠나므로 접촉을 앞쪽에 둔다
    private static List<Pose> KickPoses()
    {
        return new List<Pose>
        {
            new Pose(0f),
            // 각은 발 궤적을 에디터에서 재서 맞췄다: 접촉 때 발이 앞 0.55~0.6m·높이 0.2m 근처(= BallView 오프셋 0.55 + 공 반지름)
            new Pose(0.10f).Add(UpperLegR, Axis.Right, 30f).Add(LowerLegR, Axis.Right, 55f).Add(Abdomen, Axis.Right, -10f).Add(UpperArmL, Axis.Right, -30f).Add(UpperArmR, Axis.Right, 20f),
            new Pose(0.18f).Add(UpperLegR, Axis.Right, -26f).Add(LowerLegR, Axis.Right, 5f).Add(Abdomen, Axis.Right, -2f).Add(UpperArmL, Axis.Right, 15f).Add(UpperArmR, Axis.Right, -25f).Foot(25f),
            new Pose(0.32f).Add(UpperLegR, Axis.Right, -50f).Add(LowerLegR, Axis.Right, 15f).Add(Abdomen, Axis.Right, 8f).Add(UpperArmL, Axis.Right, 25f).Add(UpperArmR, Axis.Right, -35f).Foot(15f),
            new Pose(0.60f),
        };
    }

    // 슛: 패스보다 백스윙이 크고(무릎 90°) 상체가 앞으로 숙였다 뒤로 젖혀지며 팔로스루가 길다. 접촉 0.24초
    private static List<Pose> ShootPoses()
    {
        return new List<Pose>
        {
            new Pose(0f),
            new Pose(0.14f).Add(UpperLegR, Axis.Right, 45f).Add(LowerLegR, Axis.Right, 90f).Add(Abdomen, Axis.Right, -15f).Add(UpperArmL, Axis.Right, -45f).Add(UpperArmR, Axis.Right, 30f),
            new Pose(0.24f).Add(UpperLegR, Axis.Right, -26f).Add(LowerLegR, Axis.Right, 5f).Add(Abdomen, Axis.Right, 5f).Add(UpperArmL, Axis.Right, 20f).Add(UpperArmR, Axis.Right, -30f).Foot(30f),
            new Pose(0.42f).Add(UpperLegR, Axis.Right, -85f).Add(LowerLegR, Axis.Right, 25f).Add(Abdomen, Axis.Right, 18f).Add(UpperArmL, Axis.Right, 35f).Add(UpperArmR, Axis.Right, -45f).Foot(20f),
            new Pose(0.70f),
        };
    }

    // GK 캐치: 두 팔을 앞으로 뻗었다가 가슴으로 당긴다
    private static List<Pose> CatchPoses()
    {
        return new List<Pose>
        {
            new Pose(0f),
            new Pose(0.12f).Add(UpperArmL, Axis.Right, -90f).Add(UpperArmR, Axis.Right, -90f).Add(LowerArmL, Axis.Right, -30f).Add(LowerArmR, Axis.Right, -30f).Add(Abdomen, Axis.Right, -8f),
            new Pose(0.30f).Add(UpperArmL, Axis.Right, -70f).Add(UpperArmR, Axis.Right, -70f).Add(LowerArmL, Axis.Right, -90f).Add(LowerArmR, Axis.Right, -90f).Add(Abdomen, Axis.Right, -5f),
            new Pose(0.50f),
        };
    }

    // GK 펀칭: 오른팔을 위·옆으로 뻗고 상체를 그쪽으로 기울인다
    private static List<Pose> ParryPoses()
    {
        return new List<Pose>
        {
            new Pose(0f),
            new Pose(0.12f).Add(UpperArmR, Axis.Forward, 140f).Add(LowerArmR, Axis.Forward, 20f).Add(UpperArmL, Axis.Forward, -30f).Add(Abdomen, Axis.Forward, -15f),
            new Pose(0.30f).Add(UpperArmR, Axis.Forward, 140f).Add(LowerArmR, Axis.Forward, 20f).Add(UpperArmL, Axis.Forward, -30f).Add(Abdomen, Axis.Forward, -15f),
            new Pose(0.50f),
        };
    }

    // 세리머니: 두 팔을 들고 위아래로 흔든다(루프). 끝은 시작과 같은 자세
    private static List<Pose> CelebratePoses()
    {
        return new List<Pose>
        {
            new Pose(0f).Add(UpperArmL, Axis.Forward, -150f).Add(UpperArmR, Axis.Forward, 150f).Add(Head, Axis.Right, -15f),
            new Pose(0.40f).Add(UpperArmL, Axis.Forward, -120f).Add(UpperArmR, Axis.Forward, 120f).Add(Head, Axis.Right, -10f),
            new Pose(0.80f).Add(UpperArmL, Axis.Forward, -150f).Add(UpperArmR, Axis.Forward, 150f).Add(Head, Axis.Right, -15f),
        };
    }

    // ── 굽기

    private static AnimationClip Bake(Transform model, AnimationClip idle, string name, List<Pose> poses, bool loop)
    {
        GameObject root = model.gameObject;
        EditorCurveBinding[] bindings = Bindings(idle);
        var curves = new AnimationCurve[bindings.Length];
        for (int i = 0; i < bindings.Length; i++)
        {
            curves[i] = new AnimationCurve();
        }

        for (int p = 0; p < poses.Count; p++)
        {
            idle.SampleAnimation(root, 0f);   // Idle 첫 프레임이 기준 자세. 모든 본에 키가 생겨 다른 상태의 값이 새어 들어오지 않는다
            ApplyPose(model, poses[p]);
            for (int i = 0; i < bindings.Length; i++)
            {
                float value;
                if (!AnimationUtility.GetFloatValue(root, bindings[i], out value)) { continue; }
                curves[i].AddKey(poses[p].Time, value);
            }
        }

        var clip = new AnimationClip();
        clip.frameRate = Fps;
        AnimationUtility.SetEditorCurves(clip, bindings, curves);
        clip.EnsureQuaternionContinuity();
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return SaveClip(clip, ClipFolder + "/" + name + ".anim");
    }

    // 같은 경로에 있으면 내용만 덮어 GUID를 지킨다(컨트롤러 참조가 안 깨진다)
    private static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(clip, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    // Idle이 키를 가진 본 전부 + 발 위치·회전(IK형 리그라 발은 따로 움직인다)
    private static EditorCurveBinding[] Bindings(AnimationClip idle)
    {
        var list = new List<EditorCurveBinding>(AnimationUtility.GetCurveBindings(idle));
        AddIfMissing(list, FootR, "m_LocalPosition.x");
        AddIfMissing(list, FootR, "m_LocalPosition.y");
        AddIfMissing(list, FootR, "m_LocalPosition.z");
        AddIfMissing(list, FootR, "m_LocalRotation.x");
        AddIfMissing(list, FootR, "m_LocalRotation.y");
        AddIfMissing(list, FootR, "m_LocalRotation.z");
        AddIfMissing(list, FootR, "m_LocalRotation.w");
        return list.ToArray();
    }

    private static void AddIfMissing(List<EditorCurveBinding> list, string path, string property)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].path == path && list[i].propertyName == property) { return; }
        }
        list.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), property));
    }

    private static void ApplyPose(Transform model, Pose pose)
    {
        Transform shin = model.Find(LowerLegR);
        Transform shinEnd = model.Find(ShinEndR);
        Transform foot = model.Find(FootR);
        Vector3 footFromShin = foot.position - shinEnd.position;
        Quaternion shinRest = shin.rotation;
        Quaternion footRest = foot.rotation;

        for (int i = 0; i < pose.Joints.Count; i++)
        {
            Joint joint = pose.Joints[i];
            model.Find(joint.Path).Rotate(AxisOf(model, joint.Axis), joint.Degrees, Space.World);
        }

        // 발은 정강이가 돈 만큼 같이 돌려 정강이 끝에 붙인다
        Quaternion delta = shin.rotation * Quaternion.Inverse(shinRest);
        foot.position = shinEnd.position + delta * footFromShin;
        foot.rotation = delta * footRest;
        foot.Rotate(AxisOf(model, Axis.Right), pose.FootDegrees, Space.World);
    }

    private static Vector3 AxisOf(Transform model, Axis axis)
    {
        if (axis == Axis.Right) { return model.right; }
        if (axis == Axis.Forward) { return model.forward; }
        return model.up;
    }

    // ── 컨트롤러

    private static void Wire(AnimatorController controller, AnimationClip kick, AnimationClip shoot, AnimationClip catchClip, AnimationClip parry, AnimationClip celebrate)
    {
        EnsureParameter(controller, "Kick", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Shoot", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Catch", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Parry", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Celebrate", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = FindState(machine, "Idle");
        OneShot(machine, idle, "Kick", kick);
        OneShot(machine, idle, "Shoot", shoot);
        OneShot(machine, idle, "Catch", catchClip);
        OneShot(machine, idle, "Parry", parry);
        Held(machine, idle, "Celebrate", celebrate);
        EditorUtility.SetDirty(controller);
    }

    // 트리거 한 번에 끝까지 재생하고 Idle로. Any State에서 들어오고 자기 자신으로는 재진입 안 함
    private static void OneShot(AnimatorStateMachine machine, AnimatorState idle, string name, AnimationClip clip)
    {
        AnimatorState state = FindOrAddState(machine, name);
        state.motion = clip;
        state.speed = 1f;
        ClearTransitions(machine, state);

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.hasExitTime = false;
        enter.hasFixedDuration = true;
        enter.duration = 0.05f;
        enter.canTransitionToSelf = false;
        enter.AddCondition(AnimatorConditionMode.If, 0f, name);

        AnimatorStateTransition exit = state.AddTransition(idle);
        exit.hasExitTime = true;
        exit.exitTime = 0.9f;
        exit.hasFixedDuration = true;
        exit.duration = 0.15f;
    }

    // bool이 참인 동안 루프, 거짓이 되면 Idle로
    private static void Held(AnimatorStateMachine machine, AnimatorState idle, string name, AnimationClip clip)
    {
        AnimatorState state = FindOrAddState(machine, name);
        state.motion = clip;
        state.speed = 1f;
        ClearTransitions(machine, state);

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.hasExitTime = false;
        enter.hasFixedDuration = true;
        enter.duration = 0.2f;
        enter.canTransitionToSelf = false;
        enter.AddCondition(AnimatorConditionMode.If, 0f, name);

        AnimatorStateTransition exit = state.AddTransition(idle);
        exit.hasExitTime = false;
        exit.hasFixedDuration = true;
        exit.duration = 0.2f;
        exit.AddCondition(AnimatorConditionMode.IfNot, 0f, name);
    }

    private static void ClearTransitions(AnimatorStateMachine machine, AnimatorState state)
    {
        AnimatorStateTransition[] fromAny = machine.anyStateTransitions;
        for (int i = 0; i < fromAny.Length; i++)
        {
            if (fromAny[i].destinationState == state) { machine.RemoveAnyStateTransition(fromAny[i]); }
        }
        AnimatorStateTransition[] outgoing = state.transitions;
        for (int i = 0; i < outgoing.Length; i++)
        {
            state.RemoveTransition(outgoing[i]);
        }
    }

    private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == name) { return; }
        }
        controller.AddParameter(name, type);
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

    private static AnimatorState FindOrAddState(AnimatorStateMachine machine, string name)
    {
        AnimatorState found = FindState(machine, name);
        if (found != null) { return found; }
        return machine.AddState(name);
    }
}
