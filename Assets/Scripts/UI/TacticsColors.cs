using UnityEngine;

// 전술 화면이 같이 쓰는 색. 탭·3단 칸·체력 부담 막대·선수 칩이 같은 강조색을 각자 적어 두던 것을 한 곳으로 모았다
public static class TacticsColors
{
    public static readonly Color Highlight = new Color(1f, 0.835f, 0.31f);          // 고른 것·강조(#FFD54F)
    public static readonly Color Unselected = new Color(0.82f, 0.84f, 0.88f);      // 안 고른 탭·카드. 글자가 짙은 색이라 밝은 회색
    public static readonly Color DarkText = new Color(0.13f, 0.13f, 0.13f);        // 밝은 바탕(강조색·흰 칸) 위의 글자. 칩·스카우트 행·3단 칸이 같이 쓴다(10-29)
}
