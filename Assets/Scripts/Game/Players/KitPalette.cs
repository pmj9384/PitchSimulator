using Game.Core.League;
using UnityEngine;

// 유니폼 번호(KitRules) → 색. 0 = 내 팀(파랑, 10-22 전 PlayerHome.mat과 같은 색). 1~7 = 상대. 번호는 코어가 정하고 색만 여기
public static class KitPalette
{
    private static readonly Color[] Colors =
    {
        new Color(0.2f, 0.4f, 1f),      // 0 내 팀: 파랑
        new Color(0.9f, 0.2f, 0.2f),    // 1 빨강
        new Color(0.95f, 0.85f, 0.2f),  // 2 노랑
        new Color(0.15f, 0.7f, 0.35f),  // 3 초록
        new Color(0.95f, 0.95f, 0.95f), // 4 흰색
        new Color(0.6f, 0.3f, 0.8f),    // 5 보라
        new Color(1f, 0.55f, 0.1f),     // 6 주황
        new Color(0.15f, 0.15f, 0.2f),  // 7 검정
    };

    public static Color Of(int kit)
    {
        if (!KitRules.IsValid(kit))
        {
            Debug.LogError($"[KitPalette] 유니폼 번호 범위 밖: {kit}");
            return Colors[KitRules.MyKit];
        }
        return Colors[kit];
    }
}
