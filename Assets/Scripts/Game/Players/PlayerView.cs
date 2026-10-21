using System;
using UnityEngine;

// 선수의 겉모습 담당. 컨트롤러는 경기만 알고, 유니폼·피부·머리는 여기서 입힌다.
// 모델 = Quaternius Ultimate Modular Males(10-22): 몸·다리·발은 전원 같고 머리 메시만 번호(PortraitRules.HeadOf)로 하나 켠다.
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
            Material replacement = Pick(part.kinds[i], kit, skin, hair, boots);
            if (replacement != null) { materials[i] = replacement; }
        }
        part.renderer.sharedMaterials = materials;
    }

    private static Material Pick(SlotKind kind, Material kit, Material skin, Material hair, Material boots)
    {
        if (kind == SlotKind.Skin) { return skin; }
        if (kind == SlotKind.Hair) { return hair; }
        if (kind == SlotKind.Kit) { return kit; }
        if (kind == SlotKind.Boots) { return boots; }
        return null;
    }
}
