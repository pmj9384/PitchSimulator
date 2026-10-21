using Game.Core.League;
using Game.Core.Match;
using UnityEngine;

// 선수 겉모습 색. 번호는 코어(KitRules·PortraitRules)가 정하고 색만 여기 둔다.
// 유니폼: 0 = 내 팀, 1~7 = 상대(10-22 유저 "색만 좀" → 원색 대신 실제 유니폼 톤으로 채도를 낮춤 [가정])
public static class PlayerPalette
{
    private static readonly Color[] Kits =
    {
        new Color(0.16f, 0.24f, 0.48f),   // 0 내 팀: 네이비
        new Color(0.55f, 0.16f, 0.2f),    // 1 와인
        new Color(0.82f, 0.68f, 0.2f),    // 2 머스터드
        new Color(0.16f, 0.42f, 0.28f),   // 3 딥그린
        new Color(0.9f, 0.88f, 0.8f),     // 4 크림
        new Color(0.4f, 0.26f, 0.55f),    // 5 퍼플
        new Color(0.85f, 0.45f, 0.15f),   // 6 오렌지
        new Color(0.22f, 0.22f, 0.25f),   // 7 차콜
    };

    private static readonly Color[] Skins =
    {
        new Color(0.96f, 0.8f, 0.68f),    // 밝음
        new Color(0.85f, 0.64f, 0.48f),   // 황갈
        new Color(0.6f, 0.4f, 0.26f),     // 갈색
        new Color(0.36f, 0.24f, 0.16f),   // 짙음
    };

    private static readonly Color[] Hairs =
    {
        new Color(0.08f, 0.07f, 0.07f),   // 검정
        new Color(0.3f, 0.18f, 0.1f),     // 짙은 갈색
        new Color(0.55f, 0.38f, 0.22f),   // 밝은 갈색
        new Color(0.85f, 0.7f, 0.4f),     // 금발
        new Color(0.6f, 0.25f, 0.12f),    // 적갈
    };

    public static readonly Color Boots = new Color(0.1f, 0.1f, 0.12f);

    public static Color Kit(int kit)
    {
        return Pick(Kits, kit, "유니폼");
    }

    public static Color Skin(int skin)
    {
        return Pick(Skins, skin, "피부");
    }

    public static Color Hair(int hair)
    {
        return Pick(Hairs, hair, "머리색");
    }

    private static Color Pick(Color[] table, int index, string what)
    {
        if (index < 0 || index >= table.Length)
        {
            Debug.LogError($"[PlayerPalette] {what} 번호 범위 밖: {index} / {table.Length}");
            return table[0];
        }
        return table[index];
    }

    // 코어 상수와 표 길이가 어긋나면 색이 돌아 다른 선수와 겹친다. 시작 때 한 번 확인(PlayerManager)
    public static bool Matches()
    {
        return Kits.Length == KitRules.KitCount && Skins.Length == PortraitRules.SkinCount && Hairs.Length == PortraitRules.HairCount;
    }
}
