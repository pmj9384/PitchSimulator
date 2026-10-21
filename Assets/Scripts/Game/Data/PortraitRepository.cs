using Game.Core.Match;
using UnityEngine;

// 초상 스프라이트 접근층. 다른 리포지토리와 같은 꼴(정적·지연 로드·캐시). 번호 규칙은 순수 코어(PortraitRules), 여기는 Resources에서 꺼내기만 한다.
// 그림 = Kenney Sports Pack(CC0) 머리 10종, Resources/Sprites/Portrait/portrait_01~10. 장수가 PortraitRules.VariantCount와 같은지는 PortraitTests가 본다
public static class PortraitRepository
{
    private const string ResourceFolder = "Sprites/Portrait";
    private static Sprite[] cache;   // [portrait - 1]. 없는 파일은 null로 남겨 호출마다 다시 찾지 않는다

    public static Sprite Get(int portrait)
    {
        if (!PortraitRules.IsValid(portrait))
        {
            Debug.LogError($"[PortraitRepository] 초상 번호 범위 밖: {portrait}");
            return null;
        }
        EnsureLoaded();
        return cache[portrait - 1];
    }

    private static void EnsureLoaded()
    {
        if (cache != null) { return; }

        cache = new Sprite[PortraitRules.VariantCount];
        for (int portrait = 1; portrait <= PortraitRules.VariantCount; portrait++)
        {
            Sprite sprite = Resources.Load<Sprite>($"{ResourceFolder}/portrait_{portrait:00}");
            if (sprite == null) { Debug.LogError($"[PortraitRepository] {ResourceFolder}/portrait_{portrait:00} 없음"); }
            cache[portrait - 1] = sprite;
        }
    }
}
