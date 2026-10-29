using UnityEngine;

// UI 키트 부품이 코드로 칠하는 색(2026-10-29, 설정창·기본 UI 표준안): 선택·켜짐·위험 표시처럼 상태에 따라 바뀌는 색만 여기 둔다.
// 팝업 바탕·딤·본문 글자처럼 안 바뀌는 색은 프리팹(Assets/Prefabs/UI/Kit, SafeAreaPanel)의 Image·TMP 값이다. 테마를 바꾸면 둘 다 고친다
public static class UIKitColors
{
    public static readonly Color Surface = new Color(0.18f, 0.22f, 0.31f);       // 컨트롤 바탕·닫기 버튼(#2E3850)
    public static readonly Color Track = new Color(0.23f, 0.27f, 0.38f);         // 슬라이더 트랙·꺼진 스위치(#3A4560)
    public static readonly Color Accent = new Color(1f, 0.835f, 0.31f);          // 고른 것·켜진 것(#FFD54F)
    public static readonly Color Danger = new Color(0.90f, 0.28f, 0.30f);        // 되돌릴 수 없는 행동(#E5484D)
    public static readonly Color SubText = new Color(0.75f, 0.78f, 0.85f);       // 값·보조 글자(#BFC7D9)
    public static readonly Color OnAccent = new Color(0.10f, 0.13f, 0.20f);      // 강조색 위 글자(팝업 바탕 #1A2133과 같은 색)
}
