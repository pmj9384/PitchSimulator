using System;
using Game.Core.Match;
using UnityEngine;

// 선수의 겉모습 담당. 컨트롤러는 경기만 알고, 유니폼·피부·머리는 여기서 입힌다.
// 모델 = Quaternius Ultimate Modular Males(10-21): 몸·다리·발은 전원 같고 머리 메시만 번호(PortraitRules.HeadOf)로 하나 켠다.
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

    [SerializeField] private Renderer[] headRenderers;   // PortraitRules.HeadCount개, 머리 번호 순. 하나만 켠다
    [SerializeField] private Part[] parts;               // 몸·다리·발 + 머리 전부

    // 프리팹과 코어 상수가 어긋나면 번호 뒤쪽 머리가 조용히 0번으로 몰린다. 몸 하나당 한 번만 확인한다
    private void Awake()
    {
        if (headRenderers.Length != PortraitRules.HeadCount)
        {
            Debug.LogError($"[PlayerView] 머리 렌더러 수 {headRenderers.Length}가 PortraitRules.HeadCount {PortraitRules.HeadCount}와 다르다");
        }
        for (int i = 0; i < parts.Length; i++)
        {
            int slots = parts[i].renderer.sharedMaterials.Length;
            if (parts[i].kinds.Length == slots) { continue; }
            Debug.LogError($"[PlayerView] {parts[i].renderer.name} 슬롯 역할 수 {parts[i].kinds.Length}가 머티리얼 수 {slots}와 다르다");
        }
    }

    public void ApplyLook(int head, Material kit, Material skin, Material hair, Material boots)
    {
        if (head < 0 || head >= headRenderers.Length)
        {
            Debug.LogError($"[PlayerView] 머리 번호 범위 밖: {head} / {headRenderers.Length}");
            head = 0;
        }
        for (int i = 0; i < headRenderers.Length; i++)
        {
            headRenderers[i].enabled = i == head;
        }
        for (int i = 0; i < parts.Length; i++)
        {
            ApplyPart(parts[i], kit, skin, hair, boots);
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
