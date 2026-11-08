using System;
using Game.Core.Data;

namespace Game.Core.Tactics
{
    // 수비 시 역할(10-25 슬롯 격자, FM26의 Out of Possession 역할을 우리 수비 다이얼로 옮긴 것: 실험/2026-10-25-FM26-공수-포메이션-레퍼런스.md).
    // 공격 역할은 PlayerTable 변형(전진 폭·측면 쏠림·슛 성향·패스 길이…)이 그대로 맡고, 수비 역할은 그 위에 압박 거리·라인 높이를 덮고 행동 2개를 얹는다.
    // 자리 이탈 반경 배율은 10-29에 뺐다: 이탈 반경은 원래 폴스 9·프리 롤처럼 공격에서 자리를 떠나는 역할용이고(전술-기획 3-2, FM Roam From Position),
    // 경기 판정이 읽지 않는 값이었다(재검증 D1). 수비에서 "얼마나 나가나"는 압박 거리가 이미 맡는다
    // 행동 2개(11-06 수비 구조 D1, 설계 실험/2026-11-06-설계-수비-구조.md 3-1): 역할 이름을 코드가 보지 않도록 CSV 열로 둔다(스펙 §6 "역할 = 다이얼 프리셋").
    // 값은 DefenseRoles.csv. [가정] 배율·오프셋과 생성자 범위(배율 0.3~2, 오프셋 ±10m)는 FM 역할 이름의 뜻에서 옮긴 추정이라 출처가 없다. 러너로 맞춘다
    public sealed class DefenseRole
    {
        public const string DefaultId = "balanced";   // 역할 값 그대로(배율 1, 오프셋 0). 옛 세이브·새 시즌의 기본
        public const int ScreensNone = 0;             // 스크린(⑧c, D4) 안 함
        public const int ScreensBallSide = 1;         // 공 쪽 1명만
        public const int ScreensAlways = 2;           // 항상

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public float PressRangeScale { get; }    // 압박 거리 배율
        public float LineHeightOffset { get; }   // 라인 높이에 더하는 m(+면 앞으로)
        public bool MarksAsSecond { get; }       // 뒷줄이 아닌데도 리시버 마크(⑧b)의 2차 후보가 되나(뒷줄이 모자랄 때만). FM Tracking "바로 내려와 수비 숫자를 더한다"
        public int Screens { get; }              // 스크린(⑧c) 참여: 0 없음 · 1 공 쪽 1명 · 2 항상. D4에서 읽는다(D1에선 파싱·검증만)

        public DefenseRole(string id, string displayName, string description, float pressRangeScale, float lineHeightOffset, bool marksAsSecond, int screens)
        {
            if (string.IsNullOrEmpty(id)) { throw new ArgumentException("[DefenseRole] id가 비었다"); }
            if (pressRangeScale < 0.3f || pressRangeScale > 2f) { throw new ArgumentException($"[DefenseRole] {id}: 압박 거리 배율은 0.3~2 ({pressRangeScale})"); }
            if (lineHeightOffset < -10f || lineHeightOffset > 10f) { throw new ArgumentException($"[DefenseRole] {id}: 라인 높이 오프셋은 ±10m ({lineHeightOffset})"); }
            if (screens < ScreensNone || screens > ScreensAlways) { throw new ArgumentException($"[DefenseRole] {id}: screens는 0~2 ({screens})"); }
            Id = id;
            DisplayName = displayName;
            Description = description;
            PressRangeScale = pressRangeScale;
            LineHeightOffset = lineHeightOffset;
            MarksAsSecond = marksAsSecond;
            Screens = screens;
        }

        // 경기에 들어가는 값: 역할 값(+개인 지시) 위에 수비 다이얼 2개와 마크 2차 후보 플래그를 덮은 복사본. 원본은 안 바뀐다.
        // 라인 높이는 0에서 자른다: PlayerTable 불변식이 lineHeight ≥ 0이고(라인 높이 0인 변형이 23개), 음수면 DefendHome이 자리를 뒤로 밀어 "앞으로 서는 폭"이라는 뜻이 깨진다
        public PlayerStats Apply(PlayerStats stats)
        {
            PlayerStats copy = stats.Clone();
            copy.PressRange = stats.PressRange * PressRangeScale;
            copy.LineHeight = Math.Max(0f, stats.LineHeight + LineHeightOffset);
            copy.MarksAsSecond = MarksAsSecond;
            return copy;
        }
    }
}
