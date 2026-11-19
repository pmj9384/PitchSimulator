using System;

namespace Game.Core.Viewing
{
    // 보기 모드·배속의 기본값과 고를 수 있는 값(11-05 유저 확정, 11-19 유저 Play 피드백으로 배속을 ×1~×4 정수 4단·모든 모드로 넓혔다 = 설계 문서 결정 6 변경, 9절. 기본 하이라이트·×1).
    // 저장(PlayerPrefs)은 Unity 쪽 ViewingModeSetting이 하고, 값 검사는 EditMode로 잠그려고 여기 둔다
    public static class ViewingOptions
    {
        public const ViewingMode DefaultMode = ViewingMode.Extended;
        public const int DefaultSpeed = 1;

        public static readonly int[] Speeds = { 1, 2, 3, 4 };   // 슬라이더 칸 순서. 배속은 모든 모드에 적용된다

        // 보기 패널 세로 목록의 줄 순서(11-19, FM26 Preferences 하이라이트 라디오처럼 많이 보여 주는 모드가 위).
        // 줄 번호와 ViewingMode 값이 다르다(0번 줄 = Full = 2). 줄 ↔ 모드 변환은 여기 한 곳에서만 한다
        public static readonly ViewingMode[] ModeRows = { ViewingMode.Full, ViewingMode.Extended, ViewingMode.Key };

        public static ViewingMode ModeAtRow(int row)
        {
            if (row < 0 || row >= ModeRows.Length) { return DefaultMode; }
            return ModeRows[row];
        }

        public static int RowOf(ViewingMode mode)
        {
            return Array.IndexOf(ModeRows, mode);
        }

        // 범위 밖 값(손상·옛 값)은 기본값으로
        public static ViewingMode SanitizeMode(int value)
        {
            if (value < (int)ViewingMode.Key || value > (int)ViewingMode.Full) { return DefaultMode; }
            return (ViewingMode)value;
        }

        public static int SanitizeSpeed(int value)
        {
            if (Array.IndexOf(Speeds, value) < 0) { return DefaultSpeed; }
            return value;
        }
    }
}
