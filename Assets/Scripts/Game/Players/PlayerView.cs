using System;
using Game.Core.Match;
using UnityEngine;

// 선수의 겉모습 담당. 컨트롤러는 경기만 알고, 유니폼·피부·머리는 여기서 입힌다.
// 모델 = Quaternius Universal Base Characters(11-14 교체, 설계 실험/2026-11-12-설계-효과음-모델교체.md B절): 몸은 전원 같고 머리 칸만 번호(PortraitRules.HeadOf)로 하나 켠다.
// 머리 칸 하나는 렌더러 묶음이다(헤어 + 수염처럼 여러 개를 같이 켠다, 결정 B9). 같은 렌더러가 여러 칸에 들어 있을 수 있다.
// 렌더러마다 머티리얼 슬롯의 역할(피부·머리·유니폼·신발·그대로)을 프리팹에 적어 두고, 그 역할대로 머티리얼을 끼운다
public class PlayerView : MonoBehaviour
{
    public enum SlotKind
    {
        Keep,   // 눈·장신구처럼 안 바꾸는 슬롯
        Skin,
        Hair,
        Kit,
        Boots,
    }

    [Serializable]
    public sealed class Part
    {
        public Renderer renderer;
        public SlotKind[] kinds;   // renderer.sharedMaterials와 같은 길이·순서
    }

    [Serializable]
    public sealed class HeadSlot
    {
        public Renderer[] renderers;   // 이 칸을 고르면 같이 켜는 렌더러(헤어, 헤어 + 수염)
    }

    [SerializeField] private HeadSlot[] heads;   // PortraitRules.HeadCount개, 머리 번호 순. 한 칸만 켠다
    [SerializeField] private SkinnedMeshRenderer bodyRenderer;   // 몸(서브메시 5개). 체형 메시를 갈아 끼운다
    [SerializeField] private Mesh[] bodyMeshes;                  // PortraitRules.BodyCount개, 체형 번호 순(마름·보통·원본). 뼈 순서와 서브메시 순서가 같은 구운 메시
    [SerializeField] private Part[] parts;       // 몸·눈·눈썹 + 머리 칸의 렌더러 전부

    // 프리팹과 코어 상수가 어긋나면 번호 뒤쪽 머리가 조용히 0번으로 몰린다. 몸 하나당 한 번만 확인한다
    private void Awake()
    {
        if (heads.Length != PortraitRules.HeadCount)
        {
            Debug.LogError($"[PlayerView] 머리 칸 수 {heads.Length}가 PortraitRules.HeadCount {PortraitRules.HeadCount}와 다르다");
        }
        if (bodyMeshes.Length != PortraitRules.BodyCount)
        {
            Debug.LogError($"[PlayerView] 체형 메시 수 {bodyMeshes.Length}가 PortraitRules.BodyCount {PortraitRules.BodyCount}와 다르다");
        }
        for (int i = 0; i < parts.Length; i++)
        {
            int slots = parts[i].renderer.sharedMaterials.Length;
            if (parts[i].kinds.Length == slots) { continue; }
            Debug.LogError($"[PlayerView] {parts[i].renderer.name} 슬롯 역할 수 {parts[i].kinds.Length}가 머티리얼 수 {slots}와 다르다");
        }
    }

    public void ApplyLook(int head, int body, Material kit, Material skin, Material hair, Material boots)
    {
        if (head < 0 || head >= heads.Length)
        {
            Debug.LogError($"[PlayerView] 머리 번호 범위 밖: {head} / {heads.Length}");
            head = 0;
        }
        if (body < 0 || body >= bodyMeshes.Length)
        {
            Debug.LogError($"[PlayerView] 체형 번호 범위 밖: {body} / {bodyMeshes.Length}");
            body = bodyMeshes.Length - 1;   // 마지막 = 원본 체형(서브메시·뼈가 원본과 같아 가장 안전한 값)
        }
        ShowHead(head);
        bodyRenderer.sharedMesh = bodyMeshes[body];
        for (int i = 0; i < parts.Length; i++)
        {
            ApplyPart(parts[i], kit, skin, hair, boots);
        }
    }

    // 전부 끈 뒤 고른 칸만 켠다. 한 렌더러가 두 칸에 들어 있어서(수염) 칸마다 켜고 끄면 뒤 칸이 앞 칸을 덮는다
    private void ShowHead(int head)
    {
        for (int i = 0; i < heads.Length; i++)
        {
            SetEnabled(heads[i].renderers, false);
        }
        SetEnabled(heads[head].renderers, true);
    }

    private static void SetEnabled(Renderer[] renderers, bool enabled)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = enabled;
        }
    }

    // 인스턴스 머티리얼을 만들지 않는다. 22명이 같은 몇 개를 공유한다(색마다 1개)
    private static void ApplyPart(Part part, Material kit, Material skin, Material hair, Material boots)
    {
        Material[] materials = part.renderer.sharedMaterials;
        for (int i = 0; i < materials.Length && i < part.kinds.Length; i++)
        {
            Material replacement = MaterialFor(part.kinds[i], kit, skin, hair, boots);
            if (replacement != null) { materials[i] = replacement; }
        }
        part.renderer.sharedMaterials = materials;
    }

    private static Material MaterialFor(SlotKind kind, Material kit, Material skin, Material hair, Material boots)
    {
        if (kind == SlotKind.Skin) { return skin; }
        if (kind == SlotKind.Hair) { return hair; }
        if (kind == SlotKind.Kit) { return kit; }
        if (kind == SlotKind.Boots) { return boots; }
        return null;
    }
}
