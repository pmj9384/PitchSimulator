using UnityEngine;

// 경기 화면 HUD·보기 패널·일시정지 창의 디자인 토큰(11-19 유저 Play "UI 자체가 너무 구리다, 폰트·크기를 디자인 패턴에 맞게, FM 참고").
// 빌더(Editor/MatchHudBuilder)가 배치할 때와 런타임(ViewingControlUIElement가 선택 상태를 칠할 때)이 같은 값을 읽는다.
// 단위는 캔버스 단위다. 캔버스 기준 1080×1920·Match 0.5라 가로에서 실제 배율이 다르다:
// 2400×1080 폰은 캔버스 높이 약 966(1단위 ≈ 1.12px), 에디터 게임 뷰 1787×1069는 높이 약 979.
// 색 근거 = FM26 Touch 경기 화면(레퍼런스/FM26-UI/frames/hk_t477.png)과 데스크톱 Preferences(desk_pref_t18.png)에서 3×3 픽셀 평균으로 뽑은 값.
// 크기 근거 = 실험/2026-10-29-설정창-기본UI-표준-조사.md(48dp, 8dp 간격)와 Material 3 글자 단계를 이 캔버스로 환산한 값 [가정]
public static class UiTokens
{
    // 색
    public static readonly Color Panel = Hex(0x21, 0x21, 0x36, 0.92f);     // 팝오버 바탕. FM26 Preferences 모달 #212136, 경기 화면 위라 92% 불투명
    public static readonly Color Chip = Hex(0x23, 0x25, 0x33, 0.92f);      // 상단 칩(시계·톱니). FM26 Touch 상단 칩 #232533
    public static readonly Color Surface = Hex(0x27, 0x27, 0x3C, 1f);     // 보조 버튼 바탕. FM26 Preferences 고르지 않은 줄 #27273C
    public static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);        // 모달 뒤 딤(10-29 조사 모달 규칙: 딤 배경 + 제목 + 버튼)
    public static readonly Color ScoreBox = Hex(0xE7, 0xE2, 0xFB, 1f);     // 점수 칸. FM26 Touch 스코어 칸 밝은 바탕 #E7E2FB(칩 중 이 칸만 대비)
    public static readonly Color ScoreText = Hex(0x25, 0x25, 0x3A, 1f);    // 점수 칸 글자. FM26 스코어 가운데 칸 #25253A
    public static readonly Color Accent = Hex(0x75, 0x09, 0xDA, 1f);       // 강조 하나: 일시정지 바탕·고른 라디오·슬라이더. FM26 Touch 일시정지 #7509DA
    public static readonly Color Text = Color.white;                       // 본문 글자
    public static readonly Color SubText = Hex(0xA6, 0xA3, 0xB8, 1f);      // 보조 글자(고르지 않은 줄·눈금). FM26 보조 글자 회보라를 눈으로 맞춤 [가정]
    public static readonly Color Line = Hex(0x5C, 0x5A, 0x66, 1f);         // 고르지 않은 라디오 테두리·슬라이더 트랙. FM26 Preferences 꺼진 라디오 #5C5A66
    public static readonly Color Divider = Hex(0x5C, 0x5A, 0x66, 0.35f);   // 줄 사이 구분선

    // 모양
    public const float Radius = 16f;          // 모서리 반경 하나. FM26 칩 반경이 칩 높이의 약 20% → 칩 72 × 0.2 ≈ 14, 8 배수로 16

    // 글자(폰트는 Assets/Font/Kostar SDF 2 하나). Material 3 기준을 이 캔버스(1단위 ≈ 1.1px, 밀도 2.625 폰)로 환산
    public const float TitleSize = 48f;       // 패널 제목·자막. M3 Title Large 22sp ≈ 53, Title Medium 16sp ≈ 38 사이
    public const float BodySize = 40f;        // 라디오 글자·시계·점수. M3 Body Large 16sp ≈ 38
    public const float CaptionSize = 32f;     // 배속 눈금. M3 Body Small 12sp ≈ 29

    // 간격·크기
    public const float Space = 8f;            // 간격 단위(8dp 그리드)
    public const float Padding = 16f;         // 패널·칩 안쪽 여백 = Space × 2
    public const float TouchMin = 112f;       // 누름 영역 최소 48dp: 2400×1080 폰(밀도 2.625)에서 126px ÷ 1.12 ≈ 113 → 8 배수 112
    public const float ChipHeight = 72f;      // 보이는 칩 높이(누름 영역 112 안 가운데). FM26 상단 칩은 화면 높이의 약 4%로 더 작다
    public const float IconSize = 40f;        // 칩 안 아이콘

    private static Color Hex(int r, int g, int b, float alpha)
    {
        return new Color(r / 255f, g / 255f, b / 255f, alpha);
    }
}
