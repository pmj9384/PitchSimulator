using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 선수 연출 클립 빌더(10-23, 11-14 Humanoid판). 모캡·외부 클립 없이, 서기 첫 프레임 위에 "이름 붙은 포즈"(관절별 회전 오프셋)를 얹고
// 그 자세를 샘플링해 커브로 굽는다. 유저 Play "차는 동작이 현실적이지 않다"(범용 팩 격투 킥)에 대한 1차 답.
// 11-14 모델 교체 4·5단계(설계 실험/2026-11-12-설계-효과음-모델교체.md 결정 B4): 몸이 UBC Humanoid가 되어 뼈 경로 커브가 안 먹는다.
// 포즈표(각도·시점)는 그대로 두고, 뼈를 HumanBodyBones로 찾고, 자세를 HumanPoseHandler로 읽어 근육 커브(Humanoid 클립)로 굽는다.
// 시점(킥 접촉 0.18·0.24초, 다이빙 수평 0.32초)과 길이가 그대로라 PlayerController 상수와 상태 전이가 바뀌지 않는다.
// 회전 축은 서기 자세의 몸 기준(오른쪽 = 오른 허벅지 쪽, 위 = 월드 위, 앞 = 둘의 외적)이라 모델의 바인드 포즈 방향과 상관없다.
// 메뉴 한 번이면 클립 9개(Kick·Shoot·Catch·Parry·DiveLeft·DiveRight·LowSaveLeft·LowSaveRight·Celebrate)와 컨트롤러 상태·트리거를 다시 만든다. 카메라가 멀어 실루엣이면 충분.
// 각도는 전부 출처 없는 눈대중 [가정]. Play로 조정한다
public static class PlayerClipBuilder
{
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";
    private const string ClipFolder = "Assets/Animations/Clips";
    private const float Fps = 30f;
    private const float MinBoneHeight = 0.05f;   // [가정] 손·발·머리 뼈가 이 높이(m)보다 내려가면 엉덩이를 올린다. 뼈는 살 속에 있어 0이면 살이 땅을 뚫는다.
                                                  // 서 있을 때 이미 이보다 낮은 뼈(발끝 0.015·발 0.086 중 발끝)는 서기 높이를 바닥으로 쓴다. 안 그러면 서기 자세부터 몸이 4~5cm 떴다(11-14 재검증)
    private static readonly HumanBodyBones[] GroundBones =
    {
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head, HumanBodyBones.Hips,
    };

    // 포즈표가 쓰는 뼈(Humanoid). 옛 Generic 경로 이름을 그대로 두어 포즈표를 바꾸지 않았다
    private const HumanBodyBones Body = HumanBodyBones.Hips;
    private const HumanBodyBones Abdomen = HumanBodyBones.Spine;
    private const HumanBodyBones Head = HumanBodyBones.Head;
    private const HumanBodyBones UpperArmL = HumanBodyBones.LeftUpperArm;
    private const HumanBodyBones UpperArmR = HumanBodyBones.RightUpperArm;
    private const HumanBodyBones LowerArmL = HumanBodyBones.LeftLowerArm;
    private const HumanBodyBones LowerArmR = HumanBodyBones.RightLowerArm;
    private const HumanBodyBones UpperLegR = HumanBodyBones.RightUpperLeg;
    private const HumanBodyBones LowerLegR = HumanBodyBones.RightLowerLeg;
    private const HumanBodyBones FootR = HumanBodyBones.RightFoot;
    private const HumanBodyBones UpperLegL = HumanBodyBones.LeftUpperLeg;
    private const HumanBodyBones LowerLegL = HumanBodyBones.LeftLowerLeg;

    // 회전 축은 서기 자세의 몸 기준. Right 축 양수 = 뒤로(다리·팔이 뒤로 감), 음수 = 앞으로. Forward 축 양수 = 오른쪽 위로 벌림
    private enum Axis { Right, Forward, Up }

    private readonly struct Joint
    {
        public readonly HumanBodyBones Bone;
        public readonly Axis Axis;
        public readonly float Degrees;

        public Joint(HumanBodyBones bone, Axis axis, float degrees)
        {
            Bone = bone;
            Axis = axis;
            Degrees = degrees;
        }
    }

    private sealed class Pose
    {
        public readonly float Time;
        public readonly List<Joint> Joints = new List<Joint>();
        public float FootDegrees;   // 오른발끝 추가 회전(Right 축). 양수 = 발끝이 아래로(킥 접촉 때 펴기)
        public float BodyDrop;      // 엉덩이를 내리는 높이(m). 다이빙처럼 몸이 눕는 자세

        public Pose(float time)
        {
            Time = time;
        }

        public Pose Add(HumanBodyBones bone, Axis axis, float degrees)
        {
            Joints.Add(new Joint(bone, axis, degrees));
            return this;
        }

        public Pose Foot(float degrees)
        {
            FootDegrees = degrees;
            return this;
        }

        public Pose Drop(float meters)
        {
            BodyDrop = meters;
            return this;
        }
    }

    // 서기 자세의 몸 기준 축(굽는 동안 한 번 정한다)
    private readonly struct BodyAxes
    {
        public readonly Vector3 Right;
        public readonly Vector3 Up;
        public readonly Vector3 Forward;

        public BodyAxes(Vector3 right, Vector3 up, Vector3 forward)
        {
            Right = right;
            Up = up;
            Forward = forward;
        }
    }

    [MenuItem("Tools/Animation/Build Player Clips")]
    public static void Build()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip idle = PlayerLocomotionBuilder.LoadModelClip(PlayerLocomotionBuilder.IdleClipName);   // 기준 자세(UAL 서기 첫 프레임)
        if (prefab == null || controller == null)
        {
            Debug.LogError($"[PlayerClipBuilder] {PrefabPath} 또는 {ControllerPath} 없음");
            return;
        }
        if (idle == null) { return; }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            Transform model = instance.transform.Find("Model");
            Animator animator = model == null ? null : model.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[PlayerClipBuilder] 프리팹 Model에 Humanoid 아바타가 없다. 선수 프리팹 배선(UBC)을 먼저 돌린다");
                return;
            }
            if (!AssetDatabase.IsValidFolder(ClipFolder)) { AssetDatabase.CreateFolder("Assets/Animations", "Clips"); }

            // HumanPose의 몸 위치·회전은 월드 공간 값이다(HumanPoseHandler.GetHumanPose 문서). 인스턴스를 원점·무회전에 두고,
            // 프리팹 Model의 발 올림(로컬 y 0.01)도 굽는 동안 0으로 둔다. 안 그러면 그만큼 클립 몸 높이가 서기 클립보다 높아진다(11-14 측정 0.9552 대 0.9456)
            model.localPosition = Vector3.zero;
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            try
            {
                BakeAll(controller, animator, handler, idle);
            }
            finally
            {
                handler.Dispose();
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static void BakeAll(AnimatorController controller, Animator animator, HumanPoseHandler handler, AnimationClip idle)
    {
        AnimationClip kick = Bake(animator, handler, idle, "Kick", KickPoses(), false);
        AnimationClip shoot = Bake(animator, handler, idle, "Shoot", ShootPoses(), false);
        AnimationClip catchClip = Bake(animator, handler, idle, "Catch", CatchPoses(), false);
        AnimationClip parry = Bake(animator, handler, idle, "Parry", ParryPoses(), false);
        AnimationClip diveLeft = Bake(animator, handler, idle, "DiveLeft", DivePoses(-1f), false);
        AnimationClip diveRight = Bake(animator, handler, idle, "DiveRight", DivePoses(1f), false);
        AnimationClip lowSaveLeft = Bake(animator, handler, idle, "LowSaveLeft", LowSavePoses(-1f), false);
        AnimationClip lowSaveRight = Bake(animator, handler, idle, "LowSaveRight", LowSavePoses(1f), false);
        AnimationClip celebrate = Bake(animator, handler, idle, "Celebrate", CelebratePoses(), true);
        Wire(controller, kick, shoot, catchClip, parry, diveLeft, diveRight, lowSaveLeft, lowSaveRight, celebrate);
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerClipBuilder] Humanoid 클립 9개를 굽고 컨트롤러를 갱신했다");
    }

    // ── 포즈 (시간은 초)

    // 오른발 인사이드 킥: 백스윙 → 접촉(0.18) → 팔로스루 → 복귀. 공은 트리거 순간에 떠나므로 접촉을 앞쪽에 둔다
    private static List<Pose> KickPoses()
    {
        return new List<Pose>
        {
            new Pose(0f),
            // 각은 발 궤적을 에디터에서 재서 맞췄다: 접촉 때 발이 앞 0.55~0.6m·높이 0.2m 근처(= BallView 오프셋 0.55 + 공 반지름)
            // 11-14 UBC 몸에서 다시 쟀다: 허벅지 −26°면 발끝이 엉덩이 앞 0.29m라 −42°로(발끝 앞 0.54m·높이 0.13m, 몸 기준 측정. 공 반지름 0.11 근처). 슛 접촉도 같은 값
            new Pose(0.10f).Add(UpperLegR, Axis.Right, 30f).Add(LowerLegR, Axis.Right, 55f).Add(Abdomen, Axis.Right, -10f).Add(UpperArmL, Axis.Right, -30f).Add(UpperArmR, Axis.Right, 20f),
            new Pose(0.18f).Add(UpperLegR, Axis.Right, -42f).Add(LowerLegR, Axis.Right, 5f).Add(Abdomen, Axis.Right, -2f).Add(UpperArmL, Axis.Right, 15f).Add(UpperArmR, Axis.Right, -25f).Foot(25f),
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
            new Pose(0.24f).Add(UpperLegR, Axis.Right, -42f).Add(LowerLegR, Axis.Right, 5f).Add(Abdomen, Axis.Right, 5f).Add(UpperArmL, Axis.Right, 20f).Add(UpperArmR, Axis.Right, -30f).Foot(30f),
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
            // 팔 -90은 손이 어깨 위로 올라갔다(에디터 측정 y 1.55). -60 + 아래팔로 손이 가슴 앞(y 1.2, z 0.5)에 오게
            new Pose(0.12f).Add(UpperArmL, Axis.Right, -60f).Add(UpperArmR, Axis.Right, -60f).Add(LowerArmL, Axis.Right, -40f).Add(LowerArmR, Axis.Right, -40f).Add(Abdomen, Axis.Right, -12f),
            new Pose(0.30f).Add(UpperArmL, Axis.Right, -45f).Add(UpperArmR, Axis.Right, -45f).Add(LowerArmL, Axis.Right, -80f).Add(LowerArmR, Axis.Right, -80f).Add(Abdomen, Axis.Right, -8f),
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

    // GK 다이빙(side +1 = 오른쪽, -1 = 왼쪽. Forward 축 음수 = 오른쪽으로 기울기). 10-25 2판: 10-23 1판은 포즈 2개(기울기 → 눕기)라 "뛰어드는" 느낌이 없었다(유저).
    // 스포츠 과학 단계(실험/2026-10-25-GK-다이빙-애니-조사.md: 준비 무릎 굽힘 → 옆 스텝·반대 다리 밀기 → 이륙 0.2초 → 공중 수평·팔 뻗음 → 옆구리 착지, 전체 약 1초)를
    // 5포즈로. 수평 자세 시각·클립 길이는 PlayerController 상수를 그대로 쓴다(뷰가 공 도착 시각에 수평 시점을 맞춘다). 각도·높이는 출처 없는 눈대중 [가정]
    private static List<Pose> DivePoses(float side)
    {
        HumanBodyBones nearArm = side > 0f ? UpperArmR : UpperArmL;
        HumanBodyBones farArm = side > 0f ? UpperArmL : UpperArmR;
        HumanBodyBones nearLowerArm = side > 0f ? LowerArmR : LowerArmL;
        HumanBodyBones farLowerArm = side > 0f ? LowerArmL : LowerArmR;
        HumanBodyBones nearUpperLeg = side > 0f ? UpperLegR : UpperLegL;
        HumanBodyBones nearLowerLeg = side > 0f ? LowerLegR : LowerLegL;
        HumanBodyBones farUpperLeg = side > 0f ? UpperLegL : UpperLegR;
        HumanBodyBones farLowerLeg = side > 0f ? LowerLegL : LowerLegR;
        return new List<Pose>
        {
            new Pose(0f),
            // 준비: 무릎을 굽혀 몸을 낮추고 상체를 조금 숙인다
            new Pose(0.08f).Add(UpperLegL, Axis.Right, -30f).Add(UpperLegR, Axis.Right, -30f).Add(LowerLegL, Axis.Right, 55f).Add(LowerLegR, Axis.Right, 55f)
                .Add(Abdomen, Axis.Right, -15f).Add(nearArm, Axis.Forward, 40f * side).Add(farArm, Axis.Forward, 20f * side).Drop(0.15f),
            // 옆 스텝·밀기: 공 쪽 다리는 굽혀 디디고 반대 다리는 펴서 밀며 몸통이 기운다
            new Pose(0.18f).Add(Body, Axis.Forward, -30f * side).Add(Abdomen, Axis.Forward, -10f * side)
                .Add(nearUpperLeg, Axis.Right, -20f).Add(nearLowerLeg, Axis.Right, 40f).Add(farUpperLeg, Axis.Right, 10f).Add(farLowerLeg, Axis.Right, 0f)
                .Add(nearArm, Axis.Forward, 110f * side).Add(farArm, Axis.Forward, 70f * side).Drop(0.20f),
            // 공중: 몸이 수평, 두 팔을 공 쪽으로 완전히 뻗고 다리는 곧게
            new Pose(PlayerController.DiveHorizontalSeconds).Add(Body, Axis.Forward, -80f * side).Add(Abdomen, Axis.Forward, -10f * side).Add(Head, Axis.Forward, 15f * side)
                .Add(nearArm, Axis.Forward, 165f * side).Add(farArm, Axis.Forward, 115f * side).Add(nearLowerArm, Axis.Right, 0f).Add(farLowerArm, Axis.Right, 0f).Drop(0.55f),
            // 착지: 옆구리가 닿고 팔을 접으며 무릎을 굽힌다
            new Pose(0.58f).Add(Body, Axis.Forward, -88f * side).Add(Abdomen, Axis.Forward, -5f * side).Add(Head, Axis.Forward, 10f * side)
                .Add(nearArm, Axis.Forward, 120f * side).Add(farArm, Axis.Forward, 90f * side).Add(nearLowerArm, Axis.Right, -70f).Add(farLowerArm, Axis.Right, -70f)
                .Add(UpperLegL, Axis.Right, -25f).Add(UpperLegR, Axis.Right, -25f).Add(LowerLegL, Axis.Right, 45f).Add(LowerLegR, Axis.Right, 45f).Drop(0.50f),   // 0.60이면 접은 손이 땅 아래(y −0.04, 에디터 샘플)
            new Pose(PlayerController.DiveSeconds),
        };
    }

    // GK 낮은 세이브(11-09 B4-a, side +1 = 오른쪽): 교차점이 0.4~1.2m인 공. 다이빙처럼 눕지 않고 무릎을 굽혀 몸을 45° 기울이고 공 쪽 다리를 디디며 두 팔을 낮게 뻗는다.
    // 10-25 조사(실험/2026-10-25-GK-다이빙-애니-조사.md)의 "가까운 공 = 스텝" 권고. 길이는 PlayerController.LowSaveSeconds. 각도·높이는 눈대중 [가정]
    private static List<Pose> LowSavePoses(float side)
    {
        HumanBodyBones nearArm = side > 0f ? UpperArmR : UpperArmL;
        HumanBodyBones farArm = side > 0f ? UpperArmL : UpperArmR;
        HumanBodyBones nearUpperLeg = side > 0f ? UpperLegR : UpperLegL;
        HumanBodyBones nearLowerLeg = side > 0f ? LowerLegR : LowerLegL;
        return new List<Pose>
        {
            new Pose(0f),
            // 준비: 무릎을 굽혀 낮춘다
            new Pose(0.06f).Add(UpperLegL, Axis.Right, -25f).Add(UpperLegR, Axis.Right, -25f).Add(LowerLegL, Axis.Right, 45f).Add(LowerLegR, Axis.Right, 45f)
                .Add(Abdomen, Axis.Right, -15f).Drop(0.12f),
            // 막기: 몸통 45° 기울임, 공 쪽 다리를 굽혀 디디고 두 팔을 공 쪽 아래로
            new Pose(0.20f).Add(Body, Axis.Forward, -45f * side).Add(Abdomen, Axis.Forward, -10f * side)
                .Add(nearUpperLeg, Axis.Right, -30f).Add(nearLowerLeg, Axis.Right, 50f)
                .Add(nearArm, Axis.Forward, 70f * side).Add(farArm, Axis.Forward, 45f * side).Drop(0.25f),
            new Pose(0.32f).Add(Body, Axis.Forward, -40f * side).Add(Abdomen, Axis.Forward, -8f * side)
                .Add(nearUpperLeg, Axis.Right, -25f).Add(nearLowerLeg, Axis.Right, 45f)
                .Add(nearArm, Axis.Forward, 60f * side).Add(farArm, Axis.Forward, 40f * side).Drop(0.20f),
            new Pose(PlayerController.LowSaveSeconds),
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

    // 포즈마다 서기 첫 프레임 위에 관절 회전을 얹고 HumanPose(몸 위치·회전 + 근육 값)를 읽어 키로 넣는다
    private static AnimationClip Bake(Animator animator, HumanPoseHandler handler, AnimationClip idle, string name, List<Pose> poses, bool loop)
    {
        GameObject root = animator.gameObject;
        idle.SampleAnimation(root, 0f);
        BodyAxes axes = AxesOf(animator);
        float[] floors = GroundFloors(animator);
        var curves = new HumanCurves();
        var human = new HumanPose();
        for (int p = 0; p < poses.Count; p++)
        {
            idle.SampleAnimation(root, 0f);   // 서기 첫 프레임이 기준 자세. 모든 근육에 키가 생겨 다른 상태의 값이 새어 들어오지 않는다
            ApplyPose(animator, axes, floors, poses[p]);
            handler.GetHumanPose(ref human);
            curves.Add(poses[p].Time, human);
        }

        var clip = new AnimationClip();
        clip.name = name;   // 비어 있으면 CopySerialized가 빈 이름까지 덮어 임포트 경고(Main Object Name)가 난다
        clip.frameRate = Fps;
        curves.WriteTo(clip);
        clip.EnsureQuaternionContinuity();
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(idle);   // 루트 설정(Bake Into Pose·기준)은 서기 클립과 같게. 다르면 이동 상태에서 들어올 때 몸이 돈다
        settings.loopTime = loop;
        settings.startTime = 0f;
        settings.stopTime = poses[poses.Count - 1].Time;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return SaveClip(clip, ClipFolder + "/" + name + ".anim");
    }

    private static BodyAxes AxesOf(Animator animator)
    {
        Vector3 hipsSide = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
        Vector3 right = Vector3.ProjectOnPlane(hipsSide, Vector3.up).normalized;
        Vector3 forward = Vector3.Cross(right, Vector3.up);
        return new BodyAxes(right, Vector3.up, forward);
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

    // Humanoid 클립 커브: 몸 위치(RootT)·몸 회전(RootQ)·근육(HumanTrait.MuscleName). Animator에 붙는 이름 커브다
    private sealed class HumanCurves
    {
        private static readonly string[] RootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        private readonly AnimationCurve[] root = NewCurves(RootNames.Length);
        private readonly AnimationCurve[] muscles = NewCurves(HumanTrait.MuscleCount);

        public void Add(float time, HumanPose pose)
        {
            root[0].AddKey(time, pose.bodyPosition.x);
            root[1].AddKey(time, pose.bodyPosition.y);
            root[2].AddKey(time, pose.bodyPosition.z);
            root[3].AddKey(time, pose.bodyRotation.x);
            root[4].AddKey(time, pose.bodyRotation.y);
            root[5].AddKey(time, pose.bodyRotation.z);
            root[6].AddKey(time, pose.bodyRotation.w);
            for (int i = 0; i < muscles.Length; i++)
            {
                muscles[i].AddKey(time, pose.muscles[i]);
            }
        }

        public void WriteTo(AnimationClip clip)
        {
            for (int i = 0; i < root.Length; i++)
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), RootNames[i]), root[i]);
            }
            for (int i = 0; i < muscles.Length; i++)
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), HumanTrait.MuscleName[i]), muscles[i]);
            }
        }

        private static AnimationCurve[] NewCurves(int count)
        {
            var curves = new AnimationCurve[count];
            for (int i = 0; i < count; i++)
            {
                curves[i] = new AnimationCurve();
            }
            return curves;
        }
    }

    // Humanoid는 발이 정강이의 자식이라 옛 판의 LegFollow(IK형 리그의 발 따라붙이기)가 필요 없다
    private static void ApplyPose(Animator animator, BodyAxes axes, float[] floors, Pose pose)
    {
        for (int i = 0; i < pose.Joints.Count; i++)
        {
            Joint joint = pose.Joints[i];
            animator.GetBoneTransform(joint.Bone).Rotate(AxisOf(axes, joint.Axis), joint.Degrees, Space.World);
        }
        animator.GetBoneTransform(Body).position += Vector3.down * pose.BodyDrop;
        animator.GetBoneTransform(FootR).Rotate(axes.Right, pose.FootDegrees, Space.World);
        KeepAboveGround(animator, floors);
    }

    // 뼈마다 바닥 높이 = min(MinBoneHeight, 서기 자세의 그 뼈 높이). 서기 자세에서는 어느 뼈도 바닥 아래가 아니라 포즈 0이 서기 그대로다
    private static float[] GroundFloors(Animator animator)
    {
        var floors = new float[GroundBones.Length];
        for (int i = 0; i < GroundBones.Length; i++)
        {
            Transform bone = animator.GetBoneTransform(GroundBones[i]);
            floors[i] = bone == null ? MinBoneHeight : Mathf.Min(MinBoneHeight, bone.position.y - animator.transform.position.y);
        }
        return floors;
    }

    // 엉덩이 내림값(BodyDrop)은 옛 리그 키에 맞춘 눈대중이라 새 몸에서는 다이빙 몸이 땅에 묻혔다(11-14 렌더). 바닥보다 가장 많이 내려간 뼈만큼 올린다
    private static void KeepAboveGround(Animator animator, float[] floors)
    {
        float deficit = 0f;
        for (int i = 0; i < GroundBones.Length; i++)
        {
            Transform bone = animator.GetBoneTransform(GroundBones[i]);
            if (bone == null) { continue; }

            float height = bone.position.y - animator.transform.position.y;
            deficit = Mathf.Max(deficit, floors[i] - height);
        }
        if (deficit <= 0f) { return; }

        animator.GetBoneTransform(Body).position += Vector3.up * deficit;
    }

    private static Vector3 AxisOf(BodyAxes axes, Axis axis)
    {
        if (axis == Axis.Right) { return axes.Right; }
        if (axis == Axis.Forward) { return axes.Forward; }
        return axes.Up;
    }

    // ── 컨트롤러

    private static void Wire(AnimatorController controller, AnimationClip kick, AnimationClip shoot, AnimationClip catchClip, AnimationClip parry, AnimationClip diveLeft, AnimationClip diveRight, AnimationClip lowSaveLeft, AnimationClip lowSaveRight, AnimationClip celebrate)
    {
        EnsureParameter(controller, "Kick", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Shoot", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Catch", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Parry", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "DiveLeft", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "DiveRight", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "LowSaveLeft", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "LowSaveRight", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "DiveSpeed", AnimatorControllerParameterType.Float);   // 다이빙 재생 배율. 공이 GK 라인에 닿는 시각에 수평 자세(0.32초)가 오도록 뷰가 넣는다(10-25)
        SetFloatDefault(controller, "DiveSpeed", 1f);   // 기본 0이면 SetFloat 전에 다이빙 상태에 들어갈 때 재생이 멈춘다
        EnsureParameter(controller, "Celebrate", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = FindState(machine, PlayerLocomotionBuilder.StateName);   // 연출이 끝나면 돌아갈 이동 상태(10-29, 옛 Idle)
        if (idle == null)
        {
            Debug.LogError("[PlayerClipBuilder] Locomotion 상태가 없다. Tools/Animation/Build Player Locomotion을 먼저 누른다");
            return;
        }
        OneShot(machine, idle, "Kick", kick);
        OneShot(machine, idle, "Shoot", shoot);
        OneShot(machine, idle, "Catch", catchClip);
        OneShot(machine, idle, "Parry", parry);
        OneShot(machine, idle, "DiveLeft", diveLeft, "DiveSpeed");
        OneShot(machine, idle, "DiveRight", diveRight, "DiveSpeed");
        OneShot(machine, idle, "LowSaveLeft", lowSaveLeft);
        OneShot(machine, idle, "LowSaveRight", lowSaveRight);
        Held(machine, idle, "Celebrate", celebrate);
        EditorUtility.SetDirty(controller);
    }

    // 트리거 한 번에 끝까지 재생하고 이동 상태(Locomotion)로. Any State에서 들어오고 자기 자신으로는 재진입 안 함
    // speedParameter: 주면 그 float 파라미터가 재생 배율을 곱한다(다이빙: 공 도착 시각에 맞춤). 없으면 1배
    private static void OneShot(AnimatorStateMachine machine, AnimatorState idle, string name, AnimationClip clip, string speedParameter = null)
    {
        AnimatorState state = FindOrAddState(machine, name);
        state.motion = clip;
        state.speed = 1f;
        state.speedParameterActive = !string.IsNullOrEmpty(speedParameter);
        state.speedParameter = speedParameter ?? string.Empty;
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

    // bool이 참인 동안 루프, 거짓이 되면 이동 상태(Locomotion)로
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

    private static void SetFloatDefault(AnimatorController controller, string name, float value)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name != name) { continue; }
            parameters[i].defaultFloat = value;
        }
        controller.parameters = parameters;   // 배열 복사본이라 다시 넣어야 저장된다
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
